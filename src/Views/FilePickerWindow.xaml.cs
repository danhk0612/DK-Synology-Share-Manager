using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using SynologyShareManager.Models;
using SynologyShareManager.Services;

namespace SynologyShareManager.Views;

public partial class FilePickerWindow : Window
{
    private static readonly object LoadingMarker = new();
    private readonly DsmApiClient _client;
    private readonly Stack<string?> _backHistory = new();
    private readonly HashSet<TreeViewItem> _loadingNodes = new();
    private TreeViewItem? _rootNode;
    private string? _currentPath;
    private bool _syncingTree;
    private int _browseRequest;
    private readonly SemaphoreSlim _thumbnailRequests = new(3);
    private readonly HashSet<string> _thumbnailAttempted = new(StringComparer.Ordinal);
    private IReadOnlyList<NasFile> _visibleFiles = Array.Empty<NasFile>();
    private IReadOnlyList<NasFile> _folderFiles = Array.Empty<NasFile>();
    private string _sortField = "Name";
    private bool _sortAscending = true;
    public string SelectedPath { get; private set; } = "";

    public FilePickerWindow(DsmApiClient client)
    {
        InitializeComponent();
        _client = client;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _rootNode = new TreeViewItem { Header = "NAS 공유 폴더", Tag = "" };
        FolderTree.Items.Add(_rootNode);
        _syncingTree = true;
        _rootNode.IsSelected = true;
        _rootNode.IsExpanded = true;
        _syncingTree = false;
        await BrowseAsync(null);
    }

    private TreeViewItem CreateFolderNode(NasFile folder)
    {
        var node = new TreeViewItem { Header = folder.Name, Tag = folder.Path };
        node.Items.Add(new TreeViewItem { Header = "…", Tag = LoadingMarker });
        return node;
    }

    private bool NeedsChildren(TreeViewItem node) =>
        node.Items.Count == 1 && node.Items[0] is TreeViewItem placeholder &&
        ReferenceEquals(placeholder.Tag, LoadingMarker);

    private async Task PopulateChildrenAsync(TreeViewItem node)
    {
        if (!NeedsChildren(node) || node.Tag is not string path || path.Length == 0 || !_loadingNodes.Add(node)) return;
        try
        {
            var children = await _client.BrowseAsync(path);
            node.Items.Clear();
            foreach (var folder in children.Where(f => f.IsDirectory)) node.Items.Add(CreateFolderNode(folder));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NAS 폴더 탐색 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _loadingNodes.Remove(node); }
    }

    private async void FolderTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem node && node != _rootNode)
            await PopulateChildrenAsync(node);
    }

    private async void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingTree || e.NewValue is not TreeViewItem node || node.Tag is not string path) return;
        await BrowseAsync(path.Length == 0 ? null : path);
    }

    private TreeViewItem? FindLoadedNode(ItemCollection nodes, string path)
    {
        foreach (var item in nodes.OfType<TreeViewItem>())
        {
            if (item.Tag is string candidate && candidate == path) return item;
            var child = FindLoadedNode(item.Items, path);
            if (child != null) return child;
        }
        return null;
    }

    private void SelectTreePath(string? path)
    {
        var selected = FindLoadedNode(FolderTree.Items, path ?? "");
        if (selected == null || selected.IsSelected) return;
        _syncingTree = true;
        selected.IsSelected = true;
        _syncingTree = false;
    }

    private async Task BrowseAsync(string? path, bool recordHistory = true)
    {
        var request = ++_browseRequest;
        try
        {
            DetailsList.IsEnabled = IconsList.IsEnabled = false;
            var files = await _client.BrowseAsync(path);
            if (request != _browseRequest) return;
            if (recordHistory && _currentPath != path) _backHistory.Push(_currentPath);
            _currentPath = path;
            _thumbnailAttempted.Clear();
            _folderFiles = files;
            ApplySort();
            DetailsList.SelectedItem = IconsList.SelectedItem = null;
            CurrentPathBox.Text = path ?? "NAS 공유 폴더 목록";
            UpButton.IsEnabled = path != null;
            BackButton.IsEnabled = _backHistory.Count > 0;
            if (path == null && _rootNode != null && _rootNode.Items.Count == 0)
            {
                foreach (var folder in files.Where(f => f.IsDirectory))
                    _rootNode.Items.Add(CreateFolderNode(folder));
            }
            SelectTreePath(path);
            ListStatus.Text = $"{files.Count}개 항목";
            UpdateSelectionHint();
            if (IconsList.Visibility == Visibility.Visible) QueueVisibleThumbnails();
        }
        catch (Exception ex)
        {
            if (request == _browseRequest) MessageBox.Show(ex.Message, "NAS 탐색 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (request == _browseRequest) DetailsList.IsEnabled = IconsList.IsEnabled = true;
        }
    }

    private void ApplySort()
    {
        var selectedPath = CurrentSelection?.Path;
        // Folders remain before files as in Windows Explorer; the selected sort
        // order is applied independently within each group in both view modes.
        var groups = _folderFiles.GroupBy(file => file.IsDirectory).OrderByDescending(group => group.Key);
        var sorted = new List<NasFile>(_folderFiles.Count);
        foreach (var group in groups)
        {
            IOrderedEnumerable<NasFile> ordered = _sortField switch
            {
                "Modified" => _sortAscending ? group.OrderBy(f => f.Modified) : group.OrderByDescending(f => f.Modified),
                "Created" => _sortAscending ? group.OrderBy(f => f.Created) : group.OrderByDescending(f => f.Created),
                "Size" => _sortAscending ? group.OrderBy(f => f.Size) : group.OrderByDescending(f => f.Size),
                "Type" => _sortAscending ? group.OrderBy(f => f.TypeLabel, StringComparer.CurrentCultureIgnoreCase) :
                    group.OrderByDescending(f => f.TypeLabel, StringComparer.CurrentCultureIgnoreCase),
                "Path" => _sortAscending ? group.OrderBy(f => f.Path, StringComparer.CurrentCultureIgnoreCase) :
                    group.OrderByDescending(f => f.Path, StringComparer.CurrentCultureIgnoreCase),
                _ => _sortAscending ? group.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase) :
                    group.OrderByDescending(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            };
            sorted.AddRange(ordered.ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase));
        }
        _visibleFiles = sorted;
        DetailsList.ItemsSource = sorted;
        IconsList.ItemsSource = sorted;
        if (selectedPath != null)
        {
            var selected = sorted.FirstOrDefault(f => f.Path == selectedPath);
            if (DetailsList.Visibility == Visibility.Visible) DetailsList.SelectedItem = selected;
            else IconsList.SelectedItem = selected;
        }
        QueueVisibleThumbnails();
    }

    private void SortField_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SortField?.SelectedItem is not ComboBoxItem item || item.Tag is not string key ||
            DetailsList == null || IconsList == null) return;
        _sortField = key;
        _sortAscending = true;
        SortDirectionButton.Content = "오름차순 ↑";
        ApplySort();
    }

    private void SortDirection_Click(object sender, RoutedEventArgs e)
    {
        _sortAscending = !_sortAscending;
        SortDirectionButton.Content = _sortAscending ? "오름차순 ↑" : "내림차순 ↓";
        ApplySort();
    }

    private void DetailsHeader_Click(object sender, RoutedEventArgs e)
    {
        // Each GridViewColumnHeader handles its own Click; the ListView-level
        // bubbling handler could be consumed before reaching the ListView.
        if (sender is not GridViewColumnHeader header || header.Tag is not string field) return;
        if (_sortField == field)
        {
            SortDirection_Click(sender, e);
            return;
        }
        _sortField = field;
        _sortAscending = true;
        SortDirectionButton.Content = "오름차순 ↑";
        var dropdownItem = SortField.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == field);
        if (dropdownItem != null) SortField.SelectedItem = dropdownItem;
        else ApplySort();
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_backHistory.Count == 0) return;
        var previous = _backHistory.Peek();
        var old = _currentPath;
        await BrowseAsync(previous, false);
        if (_currentPath == previous && old != previous) _backHistory.Pop();
        BackButton.IsEnabled = _backHistory.Count > 0;
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null) return;
        var clean = _currentPath.TrimEnd('/');
        int last = clean.LastIndexOf('/');
        await BrowseAsync(last <= 0 ? null : clean[..last]);
    }

    private async void Root_Click(object sender, RoutedEventArgs e) => await BrowseAsync(null);

    private NasFile? CurrentSelection => DetailsList.Visibility == Visibility.Visible
        ? DetailsList.SelectedItem as NasFile : IconsList.SelectedItem as NasFile;

    private async void FilesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement((ItemsControl)sender, source) is not ListBoxItem) return;
        if (CurrentSelection is not NasFile file) return;
        if (!file.IsDirectory)
        {
            SelectedPath = file.Path;
            DialogResult = true;
            return;
        }
        // Expanding the corresponding tree node keeps the folder pane in sync with navigation.
        if (FolderTree.SelectedItem is TreeViewItem parent && parent.Tag is string)
        {
            await PopulateChildrenAsync(parent);
        }
        await BrowseAsync(file.Path);
    }

    private void FilesSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelectionHint();

    private void UpdateSelectionHint()
    {
        if (TargetHint == null || SelectButton == null) return;
        string? target = CurrentSelection?.Path ?? _currentPath;
        TargetHint.Text = target == null ? "공유 폴더 또는 하위 파일·폴더를 선택하세요." : $"선택 대상: {target}";
        SelectButton.IsEnabled = target != null;
    }

    private void IconView_Click(object sender, RoutedEventArgs e)
    {
        IconsList.SelectedItem = DetailsList.SelectedItem;
        DetailsList.Visibility = Visibility.Collapsed;
        IconsList.Visibility = Visibility.Visible;
        IconViewButton.Style = (Style)FindResource("PrimaryButtonStyle");
        DetailViewButton.Style = (Style)FindResource("StandardButtonStyle");
        UpdateSelectionHint();
        QueueVisibleThumbnails();
    }

    private void DetailView_Click(object sender, RoutedEventArgs e)
    {
        DetailsList.SelectedItem = IconsList.SelectedItem;
        IconsList.Visibility = Visibility.Collapsed;
        DetailsList.Visibility = Visibility.Visible;
        IconViewButton.Style = (Style)FindResource("StandardButtonStyle");
        DetailViewButton.Style = (Style)FindResource("PrimaryButtonStyle");
        UpdateSelectionHint();
    }

    private void IconsList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IconsList.Visibility == Visibility.Visible) QueueVisibleThumbnails();
    }

    private void IconsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (IconsList.Visibility == Visibility.Visible &&
            (e.VerticalChange != 0 || e.ViewportHeightChange != 0)) QueueVisibleThumbnails();
    }

    private void QueueVisibleThumbnails()
    {
        if (!_client.SupportsThumbnails || IconsList.Visibility != Visibility.Visible ||
            _visibleFiles.Count == 0) return;
        // Fixed tile size plus WrapPanel columns determine the visible row range.
        var viewer = FindScrollViewer(IconsList);
        double width = Math.Max(155, (viewer?.ViewportWidth ?? IconsList.ActualWidth) - 5);
        int columns = Math.Max(1, (int)(width / 153));
        int firstRow = Math.Max(0, (int)Math.Floor((viewer?.VerticalOffset ?? 0) / 126));
        int rows = Math.Max(2, (int)Math.Ceiling((viewer?.ViewportHeight ?? IconsList.ActualHeight) / 126) + 2);
        int request = _browseRequest;
        foreach (var file in _visibleFiles.Skip(firstRow * columns).Take(rows * columns))
        {
            if (!file.SupportsThumbnail || !_thumbnailAttempted.Add(file.Path)) continue;
            _ = LoadThumbnailAsync(file, request);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private async Task LoadThumbnailAsync(NasFile file, int request)
    {
        await _thumbnailRequests.WaitAsync();
        try
        {
            if (request != _browseRequest) return;
            var bytes = await _client.GetThumbnailAsync(file.Path);
            if (bytes == null || bytes.Length == 0 || request != _browseRequest) return;
            using var ms = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            if (request == _browseRequest) file.Thumbnail = bitmap;
        }
        catch (Exception)
        {
            // Thumbnails are optional: unsupported formats keep their file icons.
        }
        finally { _thumbnailRequests.Release(); }
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        var target = CurrentSelection?.Path ?? _currentPath;
        if (target == null) return;
        SelectedPath = target;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
