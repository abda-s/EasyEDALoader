using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static System.Math;

namespace EasyEDA_Loader
{
    // Converts an EasyEDA (std) symbol into geometry in Altium schematic space.
    //
    // EasyEDA symbol coordinates:
    //   1 unit = 10 mil, X to the right, Y DOWN (SVG style), origin = head.x / head.y.
    //   Pins:  P~show~type~spiceNo~x~y~rot~id~locked ^^ dotX~dotY ^^ "M x y h -10"~color ^^ name ^^ number ^^ dot ^^ clock
    //          (x, y) is the electrical end; the path runs from there toward the body.
    //   R  rect (x, y = top-left corner), E ellipse, C circle, A arc (SVG "M .. A .."),
    //   PL polyline, PG polygon ("x y x y ..."), PT free SVG path (M L H V C Q A Z).
    //
    // Altium schematic coordinates:
    //   mils, Y UP. A pin's Location is its body end and Orientation points from there to the
    //   electrical end (eRotate0 = +X, 90 = +Y, 180 = -X, 270 = -Y).
    //
    // So: X_alt = (x - ox) * 10, Y_alt = -(y - oy) * 10, and every direction's Y flips sign.
    public static class SymbolConverter
    {
        public const double MilPerUnit = 10.0;

        public enum FillKind { None, Body, Solid }

        public class Pin
        {
            public double X, Y;            // body end (Altium Location), mils
            public double Length;          // mils
            public int Orientation;        // 0, 90, 180, 270
            public string Designator, Name;
            public bool ShowName, ShowDesignator, Dot, Clock;
            public EeSymbolShape.EasyedaPinType Type;
        }

        public class Rect { public double X1, Y1, X2, Y2; public FillKind Fill; }
        public class Poly { public List<(double X, double Y)> Points = new(); public bool Closed; public FillKind Fill; }
        public class Ellipse { public double Cx, Cy, Rx, Ry; public FillKind Fill; }
        // Altium arcs run counter-clockwise from StartAngle to EndAngle (degrees, 0 = +X).
        public class Arc { public double Cx, Cy, Rx, Ry, StartAngle, EndAngle; }

        public class Geometry
        {
            public List<Pin> Pins = new();
            public List<Rect> Rects = new();
            public List<Poly> Polys = new();
            public List<Ellipse> Ellipses = new();
            public List<Arc> Arcs = new();

            public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
            {
                var pts = new List<(double X, double Y)>();
                foreach (var r in Rects) { pts.Add((r.X1, r.Y1)); pts.Add((r.X2, r.Y2)); }
                foreach (var p in Polys) pts.AddRange(p.Points);
                foreach (var e in Ellipses) { pts.Add((e.Cx - e.Rx, e.Cy - e.Ry)); pts.Add((e.Cx + e.Rx, e.Cy + e.Ry)); }
                foreach (var a in Arcs) { pts.Add((a.Cx - a.Rx, a.Cy - a.Ry)); pts.Add((a.Cx + a.Rx, a.Cy + a.Ry)); }
                foreach (var p in Pins)
                {
                    pts.Add((p.X, p.Y));
                    var (dx, dy) = Direction(p.Orientation);
                    pts.Add((p.X + dx * p.Length, p.Y + dy * p.Length));
                }
                if (pts.Count == 0)
                    return (0, 0, 0, 0);
                return (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
            }
        }

        public static (double X, double Y) Direction(int orientation) => orientation switch
        {
            0 => (1, 0),
            90 => (0, 1),
            180 => (-1, 0),
            _ => (0, -1),
        };

        public static Geometry Convert(SymbolData symbol)
        {
            var g = new Geometry();
            var shapes = symbol?.Shapes ?? new List<EeSymbolShape>();

            // Origin: the symbol's own origin, nudged so the pins' electrical ends land on Altium's
            // 100 mil grid (EasyEDA's grid is 50 mil, so the head can sit half a grid off).
            double ox = symbol?.Head?.X ?? 0, oy = symbol?.Head?.Y ?? 0;
            var firstPin = shapes.OfType<EeSymbolPin>().FirstOrDefault();
            if (firstPin != null)
            {
                ox += GridRemainder(firstPin.Settings.PosX - ox);
                oy += GridRemainder(firstPin.Settings.PosY - oy);
            }

            (double X, double Y) T(double x, double y) => ((x - ox) * MilPerUnit, -(y - oy) * MilPerUnit);

            foreach (var shape in shapes)
            {
                try
                {
                    switch (shape)
                    {
                        case EeSymbolPin pin:
                            g.Pins.Add(ConvertPin(pin, T));
                            break;
                        case EeSymbolRectangle r:
                        {
                            var a = T(r.PosX, r.PosY);
                            var b = T(r.PosX + r.Width, r.PosY + r.Height);
                            g.Rects.Add(new Rect
                            {
                                X1 = Min(a.X, b.X), Y1 = Min(a.Y, b.Y), X2 = Max(a.X, b.X), Y2 = Max(a.Y, b.Y),
                                // A rectangle is the IC body: give it Altium's usual yellow fill
                                // even when EasyEDA leaves it hollow (white on white there).
                                Fill = Classify(r.FillColor, r.StrokeColor) == FillKind.Solid ? FillKind.Solid : FillKind.Body,
                            });
                            break;
                        }
                        case EeSymbolCircle c:
                        {
                            var p = T(c.CenterX, c.CenterY);
                            g.Ellipses.Add(new Ellipse { Cx = p.X, Cy = p.Y, Rx = c.Radius * MilPerUnit, Ry = c.Radius * MilPerUnit, Fill = Classify(c.FillColor, c.StrokeColor) });
                            break;
                        }
                        case EeSymbolEllipse e:
                        {
                            var p = T(e.CenterX, e.CenterY);
                            g.Ellipses.Add(new Ellipse { Cx = p.X, Cy = p.Y, Rx = e.RadiusX * MilPerUnit, Ry = e.RadiusY * MilPerUnit, Fill = Classify(e.FillColor, e.StrokeColor) });
                            break;
                        }
                        case EeSymbolPolygon pg: // before EeSymbolPolyline: it derives from it
                        {
                            var pts = ParsePoints(pg.Points).Select(p => T(p.X, p.Y)).ToList();
                            if (pts.Count >= 2)
                                g.Polys.Add(new Poly { Points = pts, Closed = true, Fill = Classify(pg.FillColor, pg.StrokeColor) });
                            break;
                        }
                        case EeSymbolPolyline pl:
                        {
                            var pts = ParsePoints(pl.Points).Select(p => T(p.X, p.Y)).ToList();
                            if (pts.Count >= 2)
                                g.Polys.Add(new Poly { Points = pts, Closed = false, Fill = FillKind.None });
                            break;
                        }
                        case EeSymbolArc a:
                            ConvertArc(a, T, g);
                            break;
                        case EeSymbolPath pt:
                        {
                            var fill = Classify(pt.FillColor, pt.StrokeColor);
                            foreach (var sub in SvgPath.Flatten(pt.Paths))
                            {
                                var pts = sub.Points.Select(p => T(p.X, p.Y)).ToList();
                                if (pts.Count >= 2)
                                    g.Polys.Add(new Poly { Points = pts, Closed = sub.Closed, Fill = sub.Closed ? fill : FillKind.None });
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // One odd shape shouldn't lose the whole symbol.
                    System.Diagnostics.Debug.WriteLine($"[Symbol] skipped {shape.GetType().Name}: {ex.Message}");
                }
            }
            return g;
        }

        // Signed distance from v to the nearest multiple of 10 units (100 mil).
        private static double GridRemainder(double v) => v - 10.0 * Round(v / 10.0);

        private static Pin ConvertPin(EeSymbolPin pin, Func<double, double, (double X, double Y)> T)
        {
            double cx = pin.Settings.PosX, cy = pin.Settings.PosY;

            // Vector from the electrical end toward the body, in EasyEDA units (Y down).
            double dx = 0, dy = 0;
            var sub = SvgPath.Flatten(pin.PinPath?.Path).FirstOrDefault();
            if (sub != null && sub.Points.Count >= 2)
            {
                var start = sub.Points[0];
                var end = sub.Points[sub.Points.Count - 1];
                // Most symbols draw the pin line from the electrical end toward the body, but
                // some draw it the other way (e.g. LCSC C44669: "M 415 295 h 10" with the pin at
                // 425). The pin's own position is the electrical end, so start from whichever
                // end of the line is closer to it.
                double dStart = Pow(start.X - pin.Settings.PosX, 2) + Pow(start.Y - pin.Settings.PosY, 2);
                double dEnd = Pow(end.X - pin.Settings.PosX, 2) + Pow(end.Y - pin.Settings.PosY, 2);
                if (dEnd < dStart)
                    (start, end) = (end, start);
                dx = end.X - start.X;
                dy = end.Y - start.Y;
                cx = start.X;
                cy = start.Y;
            }
            if (Abs(dx) < 1e-6 && Abs(dy) < 1e-6)
            {
                // rotation is the direction the pin points out of the body: 0 = right side pin
                (dx, dy) = ((int)pin.Settings.Rotation % 360) switch
                {
                    0 => (-10.0, 0.0),
                    90 => (0.0, 10.0),
                    180 => (10.0, 0.0),
                    _ => (0.0, -10.0),
                };
            }

            double len = Sqrt(dx * dx + dy * dy);

            // EasyEDA shortens the pin line by the inversion bubble; Altium draws the bubble
            // at the body end itself, so the pin runs all the way to the body.
            bool dot = pin.Dot?.IsDisplayed == true;
            if (dot)
            {
                double bubble = Sqrt(Pow(pin.Dot.CircleX - cx, 2) + Pow(pin.Dot.CircleY - cy, 2)) + 3.0;
                if (bubble > len && bubble < len + 10)
                    len = bubble;
            }

            double ux = dx / len, uy = dy / len;
            double lenRounded = Round(len * MilPerUnit / 10.0) * 10.0; // Altium pin lengths on 10 mil
            if (lenRounded <= 0) lenRounded = 100;

            var conn = T(cx, cy);
            // body→electrical direction in Altium space: EasyEDA (-ux, -uy), then flip Y → (-ux, +uy)
            double ax = -ux, ay = uy;
            int orientation = Abs(ax) >= Abs(ay) ? (ax > 0 ? 0 : 180) : (ay > 0 ? 90 : 270);
            var (odx, ody) = Direction(orientation);

            string number = pin.Designator?.Text;
            if (string.IsNullOrWhiteSpace(number))
                number = pin.Settings.SpicePinNumber;

            return new Pin
            {
                X = Round(conn.X - odx * lenRounded),
                Y = Round(conn.Y - ody * lenRounded),
                Length = lenRounded,
                Orientation = orientation,
                Designator = number,
                Name = pin.Name?.Text ?? "",
                ShowName = pin.Name?.IsDisplayed ?? true,
                ShowDesignator = pin.Designator?.IsDisplayed ?? true,
                Dot = dot,
                Clock = pin.Clock?.IsDisplayed == true && !string.IsNullOrWhiteSpace(pin.Clock.Path),
                Type = pin.Settings.Type,
            };
        }

        private static void ConvertArc(EeSymbolArc a, Func<double, double, (double X, double Y)> T, Geometry g)
        {
            var arc = SvgPath.FirstArc(a.Path);
            if (arc == null)
            {
                foreach (var sub in SvgPath.Flatten(a.Path))
                    g.Polys.Add(new Poly { Points = sub.Points.Select(p => T(p.X, p.Y)).ToList() });
                return;
            }

            var (sx, sy, rx, ry, rot, large, sweep, ex, ey) = arc.Value;
            if (Abs(rx - ry) > 1e-3 && Abs(rot % 180) > 1e-3)
            {
                // Rotated ellipse arc: Altium can't rotate an elliptical arc, flatten it.
                var pts = SvgPath.SampleArc(sx, sy, rx, ry, rot, large, sweep, ex, ey).Select(p => T(p.X, p.Y)).ToList();
                g.Polys.Add(new Poly { Points = pts });
                return;
            }

            // Do the centre maths in Altium space. Flipping Y mirrors the arc, which reverses
            // its turning direction, so the sweep flag flips too.
            var s = T(sx, sy);
            var e = T(ex, ey);
            double rxm = rx * MilPerUnit, rym = ry * MilPerUnit;
            if (Abs(rot % 180 - 90) < 1e-3 || Abs(rot % 180 + 90) < 1e-3)
                (rxm, rym) = (rym, rxm);
            bool ccw = !sweep;
            var (cx, cy, _, _, _) = SvgArcUtils.ComputeArc(s.X, s.Y, rxm, rym, 0, large, ccw, e.X, e.Y);

            // ComputeArc scales the radii up when they're too small to span the endpoints.
            double scale = 1;
            if (rxm > 0 && rym > 0)
            {
                double nx = (s.X - cx) / rxm, ny = (s.Y - cy) / rym;
                scale = Max(1, Sqrt(nx * nx + ny * ny));
            }
            double Rx = rxm * scale, Ry = rym * scale;
            double startAngle = Atan2((s.Y - cy) / Ry, (s.X - cx) / Rx) * 180 / PI;
            double endAngle = Atan2((e.Y - cy) / Ry, (e.X - cx) / Rx) * 180 / PI;

            g.Arcs.Add(new Arc
            {
                Cx = cx,
                Cy = cy,
                Rx = Rx,
                Ry = Ry,
                // ccw: start→end already counter-clockwise; cw: swap so Altium draws the same path
                StartAngle = Normalize(ccw ? startAngle : endAngle),
                EndAngle = Normalize(ccw ? endAngle : startAngle),
            });
        }

        private static double Normalize(double deg)
        {
            deg %= 360;
            if (deg < 0) deg += 360;
            return deg;
        }

        private static List<(double X, double Y)> ParsePoints(string data)
        {
            var nums = SvgPath.Numbers(data);
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i + 1 < nums.Count; i += 2)
                pts.Add((nums[i], nums[i + 1]));
            return pts;
        }

        // EasyEDA fills: "none"/"" = hollow; the stroke colour (or a dark colour) = solid, like
        // arrow heads and diode triangles; anything light (usually white) = the body fill.
        private static FillKind Classify(string fill, string stroke)
        {
            if (string.IsNullOrWhiteSpace(fill) || fill.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                return FillKind.None;
            if (string.Equals(fill.Trim(), stroke?.Trim(), StringComparison.OrdinalIgnoreCase))
                return FillKind.Solid;
            try
            {
                var c = ColorHelper.FromHex(fill.Trim());
                double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                return luminance < 128 ? FillKind.Solid : FillKind.Body;
            }
            catch (Exception)
            {
                return FillKind.Body;
            }
        }
    }

    // Minimal SVG path reader for EasyEDA symbol paths.
    public static class SvgPath
    {
        public class SubPath { public List<(double X, double Y)> Points = new(); public bool Closed; }

        public static List<double> Numbers(string data)
        {
            var list = new List<double>();
            foreach (var tok in Tokenize(data))
                if (tok.IsNumber)
                    list.Add(tok.Value);
            return list;
        }

        private struct Token { public bool IsNumber; public char Cmd; public double Value; }

        private static IEnumerable<Token> Tokenize(string d)
        {
            if (string.IsNullOrEmpty(d))
                yield break;
            int i = 0;
            while (i < d.Length)
            {
                char ch = d[i];
                if (char.IsWhiteSpace(ch) || ch == ',')
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(ch) && ch != 'e' && ch != 'E')
                {
                    yield return new Token { Cmd = ch };
                    i++;
                    continue;
                }
                int start = i;
                if (ch == '+' || ch == '-') i++;
                bool dotSeen = false;
                while (i < d.Length && (char.IsDigit(d[i]) || (d[i] == '.' && !dotSeen)))
                {
                    if (d[i] == '.') dotSeen = true;
                    i++;
                }
                if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
                {
                    i++;
                    if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
                    while (i < d.Length && char.IsDigit(d[i])) i++;
                }
                if (i == start)
                {
                    i++; // unknown character
                    continue;
                }
                if (double.TryParse(d.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    yield return new Token { IsNumber = true, Value = v };
            }
        }

        // Returns the first "M sx sy A rx ry rot large sweep ex ey" in the path.
        public static (double sx, double sy, double rx, double ry, double rot, bool large, bool sweep, double ex, double ey)? FirstArc(string d)
        {
            var toks = Tokenize(d).ToList();
            double x = 0, y = 0;
            for (int i = 0; i < toks.Count; i++)
            {
                if (toks[i].IsNumber) continue;
                char c = toks[i].Cmd;
                var n = toks.Skip(i + 1).TakeWhile(t => t.IsNumber).Select(t => t.Value).ToList();
                if ((c == 'M' || c == 'm') && n.Count >= 2)
                {
                    x = c == 'm' ? x + n[0] : n[0];
                    y = c == 'm' ? y + n[1] : n[1];
                }
                else if ((c == 'A' || c == 'a') && n.Count >= 7)
                {
                    double ex = c == 'a' ? x + n[5] : n[5];
                    double ey = c == 'a' ? y + n[6] : n[6];
                    return (x, y, Abs(n[0]), Abs(n[1]), n[2], n[3] != 0, n[4] != 0, ex, ey);
                }
            }
            return null;
        }

        public static List<SubPath> Flatten(string d)
        {
            var result = new List<SubPath>();
            var toks = Tokenize(d).ToList();
            SubPath cur = null;
            double x = 0, y = 0, startX = 0, startY = 0;
            char cmd = 'M';
            int i = 0;

            void Add(double px, double py)
            {
                if (cur == null)
                {
                    cur = new SubPath();
                    cur.Points.Add((x, y));
                    result.Add(cur);
                }
                cur.Points.Add((px, py));
                x = px;
                y = py;
            }

            double Next() => toks[i++].Value;
            bool Has(int count) => i + count <= toks.Count && toks.Skip(i).Take(count).All(t => t.IsNumber);
            // Drop one stray number (never a command), so malformed data can't stall the loop.
            void SkipNumber() { if (i < toks.Count && toks[i].IsNumber) i++; }

            while (i < toks.Count)
            {
                if (!toks[i].IsNumber)
                    cmd = toks[i++].Cmd;
                bool rel = char.IsLower(cmd);
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        if (!Has(2)) { SkipNumber(); break; }
                        x = (rel ? x : 0) + Next();
                        y = (rel ? y : 0) + Next();
                        startX = x; startY = y;
                        cur = null;
                        cmd = rel ? 'l' : 'L'; // extra pairs after M are line-tos
                        break;
                    case 'L':
                        if (!Has(2)) { SkipNumber(); break; }
                        { double nx = (rel ? x : 0) + Next(), ny = (rel ? y : 0) + Next(); Add(nx, ny); }
                        break;
                    case 'H':
                        if (!Has(1)) { SkipNumber(); break; }
                        Add((rel ? x : 0) + Next(), y);
                        break;
                    case 'V':
                        if (!Has(1)) { SkipNumber(); break; }
                        Add(x, (rel ? y : 0) + Next());
                        break;
                    case 'C':
                    {
                        if (!Has(6)) { SkipNumber(); break; }
                        double bx = rel ? x : 0, by = rel ? y : 0;
                        double x1 = bx + Next(), y1 = by + Next(), x2 = bx + Next(), y2 = by + Next(), x3 = bx + Next(), y3 = by + Next();
                        double x0 = x, y0 = y;
                        for (int s = 1; s <= 12; s++)
                        {
                            double t = s / 12.0, u = 1 - t;
                            Add(u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
                                u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3);
                        }
                        break;
                    }
                    case 'Q':
                    {
                        if (!Has(4)) { SkipNumber(); break; }
                        double bx = rel ? x : 0, by = rel ? y : 0;
                        double x1 = bx + Next(), y1 = by + Next(), x2 = bx + Next(), y2 = by + Next();
                        double x0 = x, y0 = y;
                        for (int s = 1; s <= 8; s++)
                        {
                            double t = s / 8.0, u = 1 - t;
                            Add(u * u * x0 + 2 * u * t * x1 + t * t * x2, u * u * y0 + 2 * u * t * y1 + t * t * y2);
                        }
                        break;
                    }
                    case 'A':
                    {
                        if (!Has(7)) { SkipNumber(); break; }
                        double rx = Next(), ry = Next(), rot = Next();
                        bool large = Next() != 0, sweep = Next() != 0;
                        double ex = (rel ? x : 0) + Next(), ey = (rel ? y : 0) + Next();
                        foreach (var p in SampleArc(x, y, rx, ry, rot, large, sweep, ex, ey).Skip(1))
                            Add(p.X, p.Y);
                        break;
                    }
                    case 'Z':
                        if (cur != null)
                        {
                            cur.Closed = true;
                            if (cur.Points.Count > 1 && cur.Points[cur.Points.Count - 1] == (startX, startY))
                                cur.Points.RemoveAt(cur.Points.Count - 1);
                        }
                        x = startX; y = startY;
                        cur = null;
                        SkipNumber();
                        break;
                    default:
                        SkipNumber(); // unsupported command (S, T)
                        break;
                }
            }
            return result;
        }

        // Points along an SVG arc, start and end included, in the path's own coordinates.
        public static List<(double X, double Y)> SampleArc(double sx, double sy, double rx, double ry, double rotDeg, bool large, bool sweep, double ex, double ey)
        {
            var pts = new List<(double X, double Y)> { (sx, sy) };
            rx = Abs(rx); ry = Abs(ry);
            if (rx < 1e-9 || ry < 1e-9)
            {
                pts.Add((ex, ey));
                return pts;
            }
            double phi = rotDeg * PI / 180, cosP = Cos(phi), sinP = Sin(phi);
            double dx2 = (sx - ex) / 2, dy2 = (sy - ey) / 2;
            double x1 = cosP * dx2 + sinP * dy2, y1 = -sinP * dx2 + cosP * dy2;
            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1) { rx *= Sqrt(lambda); ry *= Sqrt(lambda); }
            double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double coef = (large == sweep ? -1 : 1) * Sqrt(Max(0, den == 0 ? 0 : num / den));
            double cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx;
            double cx = cosP * cx1 - sinP * cy1 + (sx + ex) / 2;
            double cy = sinP * cx1 + cosP * cy1 + (sy + ey) / 2;

            double Angle(double ux, double uy, double vx, double vy)
            {
                double a = Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
                return a;
            }
            double t1 = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
            double dt = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
            if (!sweep && dt > 0) dt -= 2 * PI;
            else if (sweep && dt < 0) dt += 2 * PI;

            int steps = Max(4, (int)Ceiling(Abs(dt) / (PI / 16)));
            for (int s = 1; s < steps; s++)
            {
                double t = t1 + dt * s / steps;
                double px = rx * Cos(t), py = ry * Sin(t);
                pts.Add((cosP * px - sinP * py + cx, sinP * px + cosP * py + cy));
            }
            pts.Add((ex, ey));
            return pts;
        }
    }
}
