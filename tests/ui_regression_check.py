"""Static checks for v1.1 UI changes only; does not replace Windows build or runtime tests."""
from pathlib import Path
from xml.etree import ElementTree as ET
import re
import hashlib

root=Path(__file__).resolve().parents[1]
src=root/'src'
read=lambda s:(src/s).read_text(encoding='utf-8')
app=read('App.xaml')
main=read('MainWindow.xaml')
main_code=read('MainWindow.xaml.cs')
picker=read('Views/FilePickerWindow.xaml')
picker_code=read('Views/FilePickerWindow.xaml.cs')
connect=read('Views/ConnectionWindow.xaml')
model=read('Models/SharingLink.cs')
project=read('SynologyShareManager.csproj')
assert '#2D7BC6' in app
assert 'Title="DK Synology Share Manager"' in main
assert 'DKSynologyShareManager' in project
assert '<ApplicationIcon>Assets\\DKShareManager.ico</ApplicationIcon>' in project
assert 'Icon="/Assets/DKShareManager.ico"' in main
assert (src/'Assets/DKShareManager.ico').exists()
assert 'Click="Open_Click"' in main and 'UseShellExecute = true' in main_code
assert 'OpenButton.IsEnabled' in main_code
assert 'GridLinesVisibility="None"' in main and 'MinWidth="235"' in main
assert main.index('Content="＋ 새 공유 링크"') < main.index('Content="선택 항목 설정 수정"')
assert 'Grid.Row="0"' in main and 'Grid.Column="1"' in main
assert '<ScrollViewer Grid.Row="1"' in connect and 'ResizeMode="CanResize"' in connect
assert 'SaveCheck' in connect
assert '아이콘 보기' in picker and '자세히 보기' in picker
assert 'DetailsList' in picker and 'IconsList' in picker
assert 'IconView_Click' in picker_code and 'DetailView_Click' in picker_code
assert 'CurrentSelection' in picker_code and 'BrowseAsync' in picker_code
assert 'var actual = value!;' in model
assert 'SynologyShareManager.config.json' in read('Services/SettingsStore.cs')
assert 'DKSynologyShareManager.exe' in (root/'publish.ps1').read_text()
ns={'x':'http://schemas.microsoft.com/winfx/2006/xaml'}
for f in src.rglob('*.xaml'):
    tree=ET.parse(f)
    code=Path(str(f)+'.cs').read_text(encoding='utf-8')
    for e in tree.iter():
        for k,v in e.attrib.items():
            if k.rsplit('}',1)[-1] in ('Click','Loaded','Closing','MouseDoubleClick','Checked','Unchecked','TextChanged','SelectionChanged'):
                assert re.search(r'\b'+re.escape(v)+r'\s*\(', code), (f,v)
print('PASS UI checks: brand, resources, handlers, explorer modes, URL open, connection scroll and preserved config')
print('NOT TESTED: Windows WPF compilation, real GUI rendering and DSM operations')
