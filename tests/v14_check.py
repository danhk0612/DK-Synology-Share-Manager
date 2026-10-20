from pathlib import Path
from lxml import etree
import re
root = Path(__file__).resolve().parents[1]
src = root / 'src'
xamls = list(src.rglob('*.xaml'))
assert len(xamls) == 5
for file in xamls:
    tree=etree.parse(str(file))
    code=(file.with_suffix('.xaml.cs') if file.stem!='App' else src/'App.xaml.cs')
    if not code.exists():
        if file.stem=='MainWindow': code=src/'MainWindow.xaml.cs'
        else: raise AssertionError(f'codebehind absent {file}')
    source=code.read_text()
    for el in tree.iter():
        for attr,value in el.attrib.items():
            if (attr in {'Click','Loaded','Closing','MouseDoubleClick','SelectionChanged',
                         'SelectedDateChanged','Checked','Unchecked','SizeChanged',
                         'SelectedItemChanged','Expanded','Sorting','TextChanged'}
                or attr.endswith('}ScrollChanged') or attr.endswith('}Expanded')):
                assert re.search(r'\b'+re.escape(value)+r'\s*\(',source),(file.name,attr,value)
api=(src/'Services/DsmApiClient.cs').read_text()
settings=(src/'Services/SettingsStore.cs').read_text()
main=(src/'MainWindow.xaml.cs').read_text()
picker=(src/'Views/FilePickerWindow.xaml.cs').read_text()
model=(src/'Models/NasFile.cs').read_text()
assert all(x in api for x in ['enable_device_token','device_name','device_id','TrustedDeviceId','SYNO.FileStation.Thumb','GetThumbnailAsync'])
assert 'SYNO.FileStation.Delete' not in api
assert 'TrustedDeviceIdDpapi' in settings and 'Protect(Encoding.UTF8.GetBytes(deviceId))' in settings
assert all(x in main for x in ['Window_Loaded','profile.DeviceId','LinksGrid_MouseDoubleClick','Edit_Click(sender, e)'])
assert all(x in model for x in ['crtime','mtime','SizeLabel','CreatedLabel','ModifiedLabel','Thumbnail'])
assert all(x in picker for x in ['QueueVisibleThumbnails','SemaphoreSlim _thumbnailRequests = new(3)','IconsList_ScrollChanged','LoadThumbnailAsync'])
assert 'AccentBrush' in (src/'MainWindow.xaml').read_text()
assert '<Popup x:Name="PART_Popup"' in (src/'App.xaml').read_text()
print('PASS v1.4: XML/events, trust-device login/storage, list selection, double-click edit, NAS metadata and lazy thumbs')
print('NOT TESTED: Windows .NET WPF build, DSM trusted-device registration, live thumbnail responses')
