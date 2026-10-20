using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SynologyShareManager.Models;
using SynologyShareManager.Services;
using SynologyShareManager.Views;

namespace SynologyShareManager;

public partial class MainWindow : Window
{
    private readonly DsmApiClient _client = new();
    private readonly ObservableCollection<SharingLink> _links = new();
    private readonly ObservableCollection<SharingLink> _pageLinks = new();
    private const int PageSize = 25;
    private int _pageIndex;
    private readonly ICollectionView _view;
    private bool _busy;
    private bool _rebuildingOwnerFilter;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_links);
        _view.Filter = MatchesFilter;
        LinksGrid.ItemsSource = _pageLinks;
        OwnerFilter.Items.Add("전체 계정");
        OwnerFilter.SelectedIndex = 0;
        RefreshUi();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        (string Address, string Account, string Password, string DeviceId, string DeviceName)? saved = null;
        try { saved = SettingsStore.Load(); }
        catch { /* The connection dialog will report a settings error. */ }
        var renewDevice = false;
        if (saved is { } profile && !string.IsNullOrWhiteSpace(profile.DeviceId))
        {
            // A successful Auth.login response alone does not prove the File Station SID
            // is usable. ConnectAsync validates it with SYNO.FileStation.Info.get.
            // Do not show an unhandled API 119 dialog before requesting fresh OTP.
            _busy = true;
            Mouse.OverrideCursor = Cursors.Wait;
            RefreshUi();
            try
            {
                await _client.ConnectAsync(profile.Address, profile.Account, profile.Password, "",
                    profile.DeviceId, true, profile.DeviceName);
                ConnectionLabel.Text = $"{_client.Address}  |  {_client.Account} (관리자)";
                await LoadAsync();
                return;
            }
            catch (Exception ex)
            {
                try { if (_client.IsConnected) await _client.LogoutAsync(); }
                catch { /* The manual login can still proceed. */ }
                _links.Clear();
                RebuildOwnerFilter();
                ConnectionLabel.Text = "NAS 연결 필요";
                // 119 means the trusted-device login did not yield a usable
                // File Station SID. Do not retry a rejected token on startup.
                renewDevice = ex.Message.Contains("오류 코드 119", StringComparison.Ordinal) ||
                    ex.Message.Contains("오류 코드 403", StringComparison.Ordinal);
                if (renewDevice)
                {
                    SettingsStore.Save(profile.Address, profile.Account, profile.Password);
                    StatusText.Text = "저장된 신뢰 장치 인증이 유효하지 않습니다. OTP로 다시 등록하세요.";
                }
                else
                    StatusText.Text = "자동 연결 실패 — NAS 상태 또는 로그인 정보를 확인하세요.";
            }
            finally
            {
                _busy = false;
                Mouse.OverrideCursor = null;
                RefreshUi();
            }
        }
        await ConnectAsync(renewDevice);
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();
    private async Task ConnectAsync(bool renewDevice = false)
    {
        if (_busy) return;
        var dialog = new ConnectionWindow(renewDevice) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await BusyAsync(async () =>
        {
            // Never leave links from a previous NAS visible under a new connection.
            _links.Clear();
            RebuildOwnerFilter();
            ConnectionLabel.Text = "NAS 연결 중";
            string? deviceId = !renewDevice && dialog.RememberDevice && string.IsNullOrWhiteSpace(dialog.Otp) &&
                string.Equals(dialog.SavedDeviceName, "", StringComparison.Ordinal) == false
                ? dialog.SavedDeviceId : null;
            if (deviceId != null)
            {
                var previous = SettingsStore.Load();
                if (previous is null || previous.Value.Address.TrimEnd('/') != dialog.DsmAddress ||
                    previous.Value.Account != dialog.Account) deviceId = null;
            }
            var deviceName = string.IsNullOrWhiteSpace(dialog.SavedDeviceName)
                ? "DK Synology Share Manager (Windows)" : dialog.SavedDeviceName;
            await _client.ConnectAsync(dialog.DsmAddress, dialog.Account, dialog.Password, dialog.Otp,
                deviceId, dialog.RememberDevice, deviceName);
            if (dialog.SaveCredentials)
                SettingsStore.Save(dialog.DsmAddress, dialog.Account, dialog.Password,
                    dialog.RememberDevice ? _client.TrustedDeviceId : null,
                    dialog.RememberDevice ? deviceName : null);
            else SettingsStore.Remove();
            ConnectionLabel.Text = $"{_client.Address}  |  {_client.Account} (관리자)";
            await LoadAsync();
            if (dialog.RememberDevice && string.IsNullOrWhiteSpace(_client.TrustedDeviceId))
                MessageBox.Show("연결은 성공했지만 DSM에서 신뢰 장치 ID를 반환하지 않았습니다. 다음 실행에서 OTP가 다시 필요할 수 있습니다.",
                    "신뢰 장치 등록 확인", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await BusyAsync(() => LoadAsync());
    }
    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (!_client.IsConnected) return;
        if (MessageBox.Show("NAS 전체 공유 링크 상태를 재동기화합니다. 일반 조회보다 오래 걸릴 수 있습니다.",
            "상태 재동기화", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        await BusyAsync(() => LoadAsync(true));
    }

    private async Task LoadAsync(bool forceClean = false)
    {
        if (!_client.IsConnected) return;
        var currentSelectedId = LinksGrid.SelectedItems.Count == 1 ? (LinksGrid.SelectedItem as SharingLink)?.Id : null;
        var result = await _client.GetLinksAsync(forceClean);
        _links.Clear();
        foreach (var link in result) _links.Add(link);
        RebuildOwnerFilter();
        _view.Refresh();
        RefreshPage();
        if (currentSelectedId != null)
        {
            var selected = _pageLinks.FirstOrDefault(x => x.Id == currentSelectedId);
            if (selected != null) LinksGrid.SelectedItem = selected;
        }
        StatusText.Text = $"공유 링크 {result.Count}개를 NAS에서 조회했습니다.";
        RefreshUi();
    }

    private void RebuildOwnerFilter()
    {
        _rebuildingOwnerFilter = true;
        var selected = OwnerFilter.SelectedItem as string ?? "전체 계정";
        OwnerFilter.Items.Clear();
        OwnerFilter.Items.Add("전체 계정");
        foreach (var owner in _links.Select(x => x.Owner).Where(x => x.Length > 0).Distinct().OrderBy(x => x))
            OwnerFilter.Items.Add(owner);
        OwnerFilter.SelectedItem = OwnerFilter.Items.Contains(selected) ? selected : "전체 계정";
        _rebuildingOwnerFilter = false;
    }

    private bool MatchesFilter(object candidate)
    {
        if (candidate is not SharingLink link) return false;
        var owner = OwnerFilter?.SelectedItem as string;
        if (!string.IsNullOrEmpty(owner) && owner != "전체 계정" && link.Owner != owner) return false;
        var status = (StatusFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(status) && status != "전체 상태" && link.StatusLabel != status) return false;
        var rangeStart = DateFromFilter?.SelectedDate?.Date;
        var rangeEnd = DateToFilter?.SelectedDate?.Date;
        if (rangeStart.HasValue || rangeEnd.HasValue)
        {
            var byStartDate = (DateFieldFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() == "시작일";
            var raw = byStartDate ? link.AvailableRaw : link.ExpiredRaw;
            // "0" / "" means immediately available or unlimited, not a concrete date.
            if (SharingLink.IsUnset(raw)) return false;
            var date = SharingLink.DatePart(raw)!.Value.Date;
            if ((rangeStart.HasValue && date < rangeStart.Value) ||
                (rangeEnd.HasValue && date > rangeEnd.Value)) return false;
        }
        var search = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(search) ||
            new[] { link.Name, link.Path, link.Url, link.Owner }.Any(s => s.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    private void Filter_Changed(object sender, EventArgs e)
    {
        if (_rebuildingOwnerFilter || _view == null) return;
        _view.Refresh();
        RefreshPage(resetPage: true);
        RefreshUi();
    }

    private void ClearDateFilter_Click(object sender, RoutedEventArgs e)
    {
        DateFromFilter.SelectedDate = null;
        DateToFilter.SelectedDate = null;
    }

    private void RefreshPage(bool resetPage = false)
    {
        if (resetPage) _pageIndex = 0;
        var filtered = _view.Cast<SharingLink>().ToList();
        int pages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
        _pageIndex = Math.Clamp(_pageIndex, 0, pages - 1);
        _pageLinks.Clear();
        foreach (var link in filtered.Skip(_pageIndex * PageSize).Take(PageSize)) _pageLinks.Add(link);
        if (PageText == null) return;
        PageText.Text = $"{_pageIndex + 1} / {pages}";
        PreviousPageButton.IsEnabled = !_busy && _pageIndex > 0;
        NextPageButton.IsEnabled = !_busy && _pageIndex < pages - 1;
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex == 0) return;
        --_pageIndex;
        RefreshPage();
        RefreshUi();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_pageIndex + 1) * PageSize >= _view.Cast<SharingLink>().Count()) return;
        ++_pageIndex;
        RefreshPage();
        RefreshUi();
    }

    private void LinksGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        string property = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(property)) return;
        e.Handled = true;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in LinksGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        _view.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(property, direction));
        RefreshPage(resetPage: true);
        RefreshUi();
    }

    private void LinksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshUi();

    private void LinksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy || LinksGrid.SelectedItems.Count != 1 ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(LinksGrid, source) is not DataGridRow) return;
        e.Handled = true;
        Edit_Click(sender, e);
    }

    private void RefreshUi()
    {
        if (LinksGrid == null) return;
        var single = LinksGrid.SelectedItems.Count == 1 ? LinksGrid.SelectedItem as SharingLink : null;
        var count = LinksGrid.SelectedItems.Count;
        RefreshButton.IsEnabled = SyncButton.IsEnabled = CreateButton.IsEnabled = _client.IsConnected && !_busy;
        EditButton.IsEnabled = CopyButton.IsEnabled = OpenButton.IsEnabled = single != null && !_busy;
        DeleteButton.IsEnabled = count > 0 && !_busy;
        ConnectButton.IsEnabled = !_busy;
        int matchingCount = _view?.Cast<SharingLink>().Count() ?? 0;
        CountText.Text = $"표시 {_pageLinks.Count} / 검색 {matchingCount} / 전체 {_links.Count} | 선택 {count}";
        if (PreviousPageButton != null)
        {
            PreviousPageButton.IsEnabled = !_busy && _pageIndex > 0;
            NextPageButton.IsEnabled = !_busy && (_pageIndex + 1) * PageSize < matchingCount;
        }
        // Do not reserve vertical space for the empty hint when a single link is selected.
        DetailHint.Visibility = count == 1 ? Visibility.Collapsed : Visibility.Visible;
        DetailHint.Text = count == 1 ? "" : count > 1 ? $"{count}개 선택됨 — 일괄 삭제만 가능합니다." : "목록에서 링크 하나를 선택하세요.";
        NameBox.Text = single?.Name ?? "";
        OwnerBox.Text = single?.Owner ?? "";
        PathBox.Text = single?.Path ?? "";
        StateBox.Text = single == null ? "" : $"{single.StatusLabel} / 암호 {single.PasswordLabel}";
        DatesBox.Text = single == null ? "" : $"{single.AvailableLabel} / {single.ExpiredLabel}";
        UrlBox.Text = single?.Url ?? "";
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!_client.IsConnected || _busy) return;
        var dialog = new LinkEditorWindow(_client) { Owner = this };
        if (dialog.ShowDialog() == true) await BusyAsync(() => LoadAsync());
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || LinksGrid.SelectedItem is not SharingLink link || LinksGrid.SelectedItems.Count != 1) return;
        // Finish the global wait cursor before opening the modal editor.
        // Mouse.OverrideCursor applies to the whole WPF application, including child windows.
        SharingLink? latest = null;
        await BusyAsync(async () =>
        {
            var current = await _client.GetLinkAsync(link.Id);
            if (current != link) throw new InvalidOperationException("링크 정보가 변경됐습니다. 새로고침 후 다시 시도하세요.");
            latest = current;
        });
        if (latest == null) return;
        var dialog = new LinkEditorWindow(_client, latest) { Owner = this };
        if (dialog.ShowDialog() == true) await BusyAsync(() => LoadAsync());
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var targets = LinksGrid.SelectedItems.OfType<SharingLink>().ToList();
        if (targets.Count == 0) return;
        var preview = string.Join("\n", targets.Take(8).Select(x => $"- {x.Owner} / {x.Path}"));
        if (targets.Count > 8) preview += $"\n... 외 {targets.Count - 8}개";
        var confirm = MessageBox.Show($"선택한 공유 링크 {targets.Count}개를 삭제합니다.\n원본 파일·폴더는 삭제되지 않습니다.\n\n{preview}\n\n삭제한 공유 URL은 더 이상 사용할 수 없습니다.",
            "공유 링크 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        await BusyAsync(async () =>
        {
            var deleted = new List<string>();
            var errors = new List<string>();
            foreach (var link in targets)
            {
                try { await _client.DeleteLinkAsync(link); deleted.Add(link.Id); }
                catch (Exception ex) { errors.Add($"{link.Owner} / {link.Name}: {ex.Message}"); }
            }
            // A single authoritative re-query; never claim success solely from an API response.
            var all = await _client.GetLinksAsync();
            _links.Clear();
            foreach (var link in all) _links.Add(link);
            RebuildOwnerFilter();
            _view.Refresh();
            RefreshPage();
            var stillPresent = deleted.Count(id => all.Any(x => x.Id == id));
            StatusText.Text = $"삭제 확인 {deleted.Count - stillPresent}개 / 실패·미확인 {errors.Count + stillPresent}개";
            RefreshUi();
            if (errors.Count > 0 || stillPresent > 0)
                MessageBox.Show(StatusText.Text + "\n" + string.Join("\n", errors.Take(8)) +
                    (stillPresent > 0 ? $"\n삭제 요청 후에도 {stillPresent}개 링크가 목록에 남아 있습니다." : ""),
                    "일부 삭제 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (LinksGrid.SelectedItem is SharingLink link && LinksGrid.SelectedItems.Count == 1)
        {
            Clipboard.SetText(link.Url);
            StatusText.Text = "공유 URL을 복사했습니다.";
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (LinksGrid.SelectedItems.Count != 1 || LinksGrid.SelectedItem is not SharingLink link) return;
        if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            MessageBox.Show("열 수 있는 HTTP(S) 공유 URL이 아닙니다.", "URL 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "URL 열기 실패", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task BusyAsync(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        RefreshUi();
        try { await operation(); }
        catch (Exception ex)
        {
            StatusText.Text = "작업 실패 — NAS 상태를 확인하세요.";
            MessageBox.Show(ex.Message, "DK Synology Share Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _busy = false; Mouse.OverrideCursor = null; RefreshUi(); }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (!_client.IsConnected)
        {
            _allowClose = true;
            _client.Dispose();
            return;
        }
        e.Cancel = true;
        StatusText.Text = "NAS 로그아웃 중...";
        try { await _client.LogoutAsync(); }
        catch { /* Closing even if the NAS is unavailable. */ }
        finally
        {
            _client.Dispose();
            _allowClose = true;
            Close();
        }
    }
}
