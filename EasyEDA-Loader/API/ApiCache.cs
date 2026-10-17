using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEDA_Loader
{
    // Every EasyEDA request goes through here so the API is hit as little as possible:
    //
    //  1. Memory: anything fetched this Altium session is answered without touching disk.
    //  2. Disk:   %LOCALAPPDATA%\EasyEDA-Loader\cache\<kind>\<hash>, with a lifetime per kind
    //             (3D models never expire, parts 90 days, browse pages 12 hours...).
    //  3. Shared: two callers asking for the same thing at once share one request.
    //  4. Paced:  requests to the same host are spaced out instead of fired in a burst.
    //  5. Backoff: after a 403/429 the host is left alone for a cool-down, and any copy we
    //             have - however old - is served instead of an error.
    public static class ApiCache
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyEDA-Loader", "cache");

        private static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan CoolDown = TimeSpan.FromMinutes(10);
        private const int MemoryLimitBytes = 64 * 1024 * 1024;

        private static readonly ConcurrentDictionary<string, byte[]> _memory = new();
        private static long _memoryBytes;
        private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _inFlight = new();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _hostGates = new();
        private static readonly ConcurrentDictionary<string, DateTime> _hostLastRequest = new();
        private static readonly ConcurrentDictionary<string, DateTime> _hostBlockedUntil = new();

        public class RateLimitedException : Exception
        {
            public RateLimitedException()
                : base("EasyEDA is temporarily blocking requests from this network (too many in a short time). " +
                       "Wait a few minutes and try again; parts you already opened still work from the cache.") { }
        }

        public static async Task<string> GetTextAsync(string kind, string key, TimeSpan lifetime,
            HttpClient client, Func<HttpRequestMessage> build, Func<string, bool> isValid, CancellationToken ct)
        {
            var bytes = await GetBytesAsync(kind, key, lifetime, client, build,
                b => isValid == null || isValid(Encoding.UTF8.GetString(b)), ct);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        public static async Task<byte[]> GetBytesAsync(string kind, string key, TimeSpan lifetime,
            HttpClient client, Func<HttpRequestMessage> build, Func<byte[], bool> isValid, CancellationToken ct)
        {
            string id = kind + "/" + Hash(key);
            if (_memory.TryGetValue(id, out var hot))
                return hot;

            string path = Path.Combine(Root, kind, Hash(key));
            var fresh = ReadDisk(path, lifetime);
            if (fresh != null)
            {
                Remember(id, fresh);
                return fresh;
            }

            // Share one download between everyone asking for this right now. The download
            // itself isn't tied to any one caller's token, so a cancelled preview doesn't
            // waste a request someone else (or the next click) will want.
            var lazy = _inFlight.GetOrAdd(id, _ => new Lazy<Task<byte[]>>(() => DownloadAsync(id, path, client, build, isValid)));
            try
            {
                var task = lazy.Value;
                if (ct.CanBeCanceled)
                {
                    var done = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, ct));
                    if (done != task)
                        throw new OperationCanceledException(ct);
                }
                var data = await task;
                Remember(id, data);
                return data;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Network trouble or a block: an old copy beats an error.
                var stale = ReadDisk(path, TimeSpan.MaxValue);
                if (stale != null)
                {
                    Debug.WriteLine($"[Cache] {kind}: serving stale copy ({ex.Message})");
                    Remember(id, stale);
                    return stale;
                }
                throw;
            }
        }

        private static async Task<byte[]> DownloadAsync(string id, string path,
            HttpClient client, Func<HttpRequestMessage> build, Func<byte[], bool> isValid)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var response = await SendPacedAsync(client, build(), timeout.Token);
                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadAsByteArrayAsync();
                if (isValid != null && !isValid(data))
                    throw new InvalidDataException("EasyEDA returned an unexpected response.");
                WriteDisk(path, data);
                return data;
            }
            finally
            {
                // Done either way: the next request for this key starts fresh (or hits the disk).
                _inFlight.TryRemove(id, out _);
            }
        }

        // Sends `request` once this host's previous request is at least MinSpacing old, and
        // refuses outright while the host is cooling down after a block.
        public static async Task<HttpResponseMessage> SendPacedAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
        {
            string host = request.RequestUri.Host;
            if (_hostBlockedUntil.TryGetValue(host, out var until) && DateTime.UtcNow < until)
                throw new RateLimitedException();

            var gate = _hostGates.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                if (_hostLastRequest.TryGetValue(host, out var last))
                {
                    var wait = last + MinSpacing - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, ct);
                }
                Debug.WriteLine($"[API] {request.Method} {request.RequestUri}");
                var response = await client.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                {
                    _hostBlockedUntil[host] = DateTime.UtcNow + CoolDown;
                    throw new RateLimitedException();
                }
                return response;
            }
            finally
            {
                _hostLastRequest[host] = DateTime.UtcNow;
                gate.Release();
            }
        }

        private static byte[] ReadDisk(string path, TimeSpan lifetime)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                if (lifetime != TimeSpan.MaxValue && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > lifetime)
                    return null;
                return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Cache] read failed: {ex.Message}");
                return null;
            }
        }

        private static void WriteDisk(string path, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Cache] write failed: {ex.Message}");
            }
        }

        private static void Remember(string id, byte[] data)
        {
            if (data == null || data.Length > MemoryLimitBytes / 8)
                return;
            if (Interlocked.Read(ref _memoryBytes) + data.Length > MemoryLimitBytes)
            {
                _memory.Clear();
                Interlocked.Exchange(ref _memoryBytes, 0);
            }
            if (_memory.TryAdd(id, data))
                Interlocked.Add(ref _memoryBytes, data.Length);
        }

        private static string Hash(string key)
        {
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static long DiskBytes()
        {
            try
            {
                if (!Directory.Exists(Root))
                    return 0;
                long total = 0;
                foreach (var f in new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories))
                    total += f.Length;
                return total;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
