using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace EasyEDA_Loader
{
    // LCSC-style browsing: category tree + filter columns + paged results.
    // Fills the same results grid as the keyword search, so preview and
    // "Add to Library" work unchanged.
    public partial class DialogWindow
    {
        private const int BrowsePageSize = 50;

        private CatalogNodeViewModel _browseCategory;
        private int _browsePage = 1;
        private long _browseTotal;
        private readonly Dictionary<string, List<string>> _browseFilters = new();
        // Full value list of each filter column before that column itself was filtered, so
        // ticking 0402 still shows every other package (like lcsc.com) instead of only 0402.
        private readonly Dictionary<string, List<string>> _facetBaseValues = new();
        private CancellationTokenSource _browseCts;

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var nodes = await Api.GetCatalogTreeAsync(cts.Token);
                categoryTree.ItemsSource = nodes.Select(CatalogNodeViewModel.From).ToList();
                categoryStatusText.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                categoryStatusText.Text = $"Could not load categories: {ex.Message}";
            }
        }

        private async void CategoryTree_CurrentItemChanged(object sender, DevExpress.Xpf.Grid.CurrentItemChangedEventArgs e)
        {
            if (e.NewItem is not CatalogNodeViewModel node || node == _browseCategory)
                return;
            _browseCategory = node;
            _browseFilters.Clear();
            _facetBaseValues.Clear();
            withinTextBox.Text = "";
            _browsePage = 1;
            await RunBrowseAsync();
        }

        private async void FacetValue_EditValueChanged(object sender, DevExpress.Xpf.Editors.EditValueChangedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not FacetValueViewModel value)
                return;

            if (!_browseFilters.TryGetValue(value.Facet, out var selected))
                _browseFilters[value.Facet] = selected = new List<string>();

            // The binding also raises this when the list is (re)built; only react to a real change.
            bool isChecked = e.NewValue is bool b && b;
            if (isChecked == selected.Contains(value.Value))
                return;

            if (isChecked)
            {
                // Manufacturer is sent as a keyword, so only one can be active at a time.
                if (value.Facet == EasyedaApi.ManufacturerFacet)
                    selected.Clear();
                if (!selected.Contains(value.Value))
                    selected.Add(value.Value);
            }
            else
            {
                selected.Remove(value.Value);
            }

            _browsePage = 1;
            await RunBrowseAsync();
        }

        private async void WithinTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            if (_browseCategory == null)
            {
                browseStatusText.Text = "Pick a category first, or use the search box at the top.";
                return;
            }
            _browsePage = 1;
            await RunBrowseAsync();
        }

        private async void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            _browseFilters.Clear();
            _facetBaseValues.Clear();
            withinTextBox.Text = "";
            _browsePage = 1;
            await RunBrowseAsync();
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_browsePage <= 1)
                return;
            _browsePage--;
            await RunBrowseAsync();
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_browsePage >= PageCount())
                return;
            _browsePage++;
            await RunBrowseAsync();
        }

        private int PageCount() => (int)Math.Max(1, (_browseTotal + BrowsePageSize - 1) / BrowsePageSize);

        private async Task RunBrowseAsync()
        {
            if (_browseCategory == null)
                return;

            _browseCts?.Cancel();
            _browseCts = new CancellationTokenSource();
            var token = _browseCts.Token;

            browseStatusText.Text = $"Loading {_browseCategory.Name}...";
            prevPageButton.IsEnabled = nextPageButton.IsEnabled = false;
            resultsGrid.ShowLoadingPanel = true;

            EasyedaApi.BrowseResult result;
            try
            {
                result = await Api.BrowseAsync(_browseCategory.Id, _browsePage, BrowsePageSize,
                    _browseFilters, withinTextBox.Text, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    browseStatusText.Text = $"Search failed: {ex.Message}";
                return;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    resultsGrid.ShowLoadingPanel = false;
            }

            if (token.IsCancellationRequested)
                return;

            searchResults.Clear();
            foreach (var part in result.Parts)
                searchResults.Add(new PartInfoViewModel(part, this));
            UpdateAddButtonState();

            var facets = new List<FacetViewModel>();
            foreach (var f in result.Facets)
            {
                var selected = _browseFilters.TryGetValue(f.Name, out var sel) ? sel : new List<string>();
                List<string> values;
                if (selected.Count == 0)
                    _facetBaseValues[f.Name] = values = f.Values;
                else
                    values = _facetBaseValues.TryGetValue(f.Name, out var baseValues) ? baseValues : f.Values;

                if (values.Count > 0 || selected.Count > 0)
                    facets.Add(new FacetViewModel(f.Name, values, selected));
            }
            facetsPanel.ItemsSource = facets;
            facetsEmptyText.Visibility = facets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            _browseTotal = result.Total;
            int pages = PageCount();
            if (_browsePage > pages)
                _browsePage = pages;

            var active = _browseFilters.Where(kv => kv.Value.Count > 0)
                .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}").ToList();
            if (!string.IsNullOrWhiteSpace(withinTextBox.Text))
                active.Add($"\"{withinTextBox.Text.Trim()}\"");
            browseStatusText.Text = $"{_browseCategory.Name}: {result.Total:N0} parts" +
                                    (active.Count > 0 ? "  |  " + string.Join("  ·  ", active) : "");
            pageText.Text = result.Total > 0
                ? $"Page {_browsePage:N0} of {pages:N0}  ·  {result.Total:N0} parts, most stock first"
                : "No parts match these filters. Untick a filter or press Clear filters.";
            prevPageButton.IsEnabled = _browsePage > 1;
            nextPageButton.IsEnabled = _browsePage < pages;
        }

        private void UpdateResultsEmptyState()
        {
            resultsEmptyText.Visibility = searchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Called by the keyword search: its results aren't paged.
        private void ShowKeywordSearchPaging(int count)
        {
            _browseCts?.Cancel();
            pageText.Text = count > 0 ? $"{count} matches (keyword search shows the first page only; browse a category for more)" : "";
            prevPageButton.IsEnabled = nextPageButton.IsEnabled = false;
        }
    }

    public class CatalogNodeViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<CatalogNodeViewModel> Children { get; set; } = new();

        public static CatalogNodeViewModel From(EasyedaApi.CatalogNode node) => new()
        {
            Id = node.Id,
            Name = node.Name,
            Children = node.Children.Select(From).ToList(),
        };
    }

    public class FacetValueViewModel
    {
        public string Facet { get; set; }
        public string Value { get; set; }
        public bool IsSelected { get; set; }
    }

    public class FacetViewModel : INotifyPropertyChanged
    {
        private string _search = "";

        public string Name { get; }
        public string Header { get; }
        public ObservableCollection<FacetValueViewModel> Values { get; }
        public ICollectionView VisibleValues { get; }

        public string Search
        {
            get => _search;
            set
            {
                _search = value ?? "";
                VisibleValues.Refresh();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Search)));
            }
        }

        public FacetViewModel(string name, IEnumerable<string> values, ICollection<string> selected)
        {
            Name = name;
            Header = selected.Count > 0 ? $"{name} ({selected.Count})" : name;

            // Selected values first, so an active filter is always visible.
            var all = selected.Concat(values.Where(v => !selected.Contains(v)));
            Values = new ObservableCollection<FacetValueViewModel>(all.Select(v => new FacetValueViewModel
            {
                Facet = name,
                Value = v,
                IsSelected = selected.Contains(v),
            }));

            VisibleValues = new CollectionViewSource { Source = Values }.View;
            VisibleValues.Filter = o => _search.Length == 0 ||
                ((FacetValueViewModel)o).Value.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
