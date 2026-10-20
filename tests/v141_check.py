from pathlib import Path
import re
from lxml import etree
p=Path(__file__).resolve().parents[1]
orig=p.parent/'DK_SynologyShareManager_v1.4_source'
for f in ['src/Services/DsmApiClient.cs','src/Services/SettingsStore.cs', 'src/Models/NasFile.cs', 'src/Views/LinkEditorWindow.xaml.cs']:
    assert (p/f).read_bytes() == (orig/f).read_bytes(), 'unrequested change: '+f
main=(p/'src/MainWindow.xaml.cs').read_text()
con=(p/'src/Views/ConnectionWindow.xaml.cs').read_text()
picker=(p/'src/Views/FilePickerWindow.xaml.cs').read_text()
xaml=(p/'src/Views/FilePickerWindow.xaml').read_text()
assert 'await BusyAsync(async () =>' not in main.split('private async void Window_Loaded')[1].split('private async void Connect_Click')[0]
assert 'renewDevice = ex.Message.Contains("오류 코드 119"' in main
assert 'SettingsStore.Save(profile.Address, profile.Account, profile.Password);' in main
assert 'new ConnectionWindow(renewDevice)' in main
assert '!renewDevice && dialog.RememberDevice' in main
assert 'if (_renewDevice && string.IsNullOrWhiteSpace(Otp))' in con
assert 'ApplySort();' in picker
assert all(f'"{field}" =>' in picker for field in ['Modified', 'Created','Size','Type','Path'])
assert all(x in xaml for x in ['SortField_Changed','SortDirection_Click','DetailsHeader_Click','Tag="Name"','Tag="Modified"','Tag="Created"','Tag="Size"','Tag="Type"','Tag="Path"'])
assert 'OrderByDescending(group => group.Key)' in picker
etree.parse(str(p/'src/Views/FilePickerWindow.xaml'))
assert xaml.count('Click="DetailsHeader_Click"') == 6
assert re.search(r'\bDetailsHeader_Click\s*\(',picker)
assert all((p/'src/Views/FilePickerWindow.xaml').read_text().count(f'Grid.Row="{i}"') >= 1 for i in [4,5,6,7])
print('PASS v1.4.1: trusted-device fallback, manual OTP renewal, independent folder-first Explorer sorting, unchanged DSM API/service contract')
print('NOT TESTED: actual WPF build, trusted-device reuse on DSM, Explorer sorting UI')
