"""v1.3 source-only regression tests; does not compile WPF or contact a NAS."""
from pathlib import Path
from xml.etree import ElementTree as ET
import re
root=Path(__file__).resolve().parents[1]
src=root/'src'
main=(src/'MainWindow.xaml').read_text(encoding='utf-8')
code=(src/'MainWindow.xaml.cs').read_text(encoding='utf-8')
editor=(src/'Views/LinkEditorWindow.xaml').read_text(encoding='utf-8')
api=(src/'Services/DsmApiClient.cs').read_text(encoding='utf-8')
assert 'x:Name="DateFieldFilter"' in main and 'x:Name="DateFromFilter"' in main and 'x:Name="DateToFilter"' in main
assert 'x:Name="DateFromFilter" Width="150" Height="30"' in main
assert 'x:Name="DateToFilter" Width="150" Height="30"' in main
assert 'SelectedDateChanged="Filter_Changed"' in main
assert 'Click="ClearDateFilter_Click"' in main
assert 'DateFromFilter.SelectedDate = null;' in code and 'DateToFilter.SelectedDate = null;' in code
assert 'SharingLink.IsUnset(raw)' in code and 'SharingLink.DatePart(raw)' in code
assert 'date < rangeStart.Value' in code and 'date > rangeEnd.Value' in code
assert code.index('DateFromFilter?.SelectedDate') < code.index('var search = SearchBox?.Text?.Trim();')
assert code.index('var dialog = new LinkEditorWindow(_client, latest)') > code.index('if (latest == null) return;')
assert 'if (dialog.ShowDialog() == true) await BusyAsync(() => LoadAsync());' in code
edit=code[code.index('private async void Edit_Click'):code.index('private async void Delete_Click')]
assert 'dialog.ShowDialog()' not in edit.split('await BusyAsync(async () =>',1)[1].split('        });',1)[0]
assert editor.count('Checked="DateCheck_Changed"') == 2
assert editor.count('Width="175" Height="30" Padding="0" VerticalAlignment="Center"') == 2
assert 'SharingLink' in api and 'SYNO.FileStation.Sharing' in api
for path in src.rglob('*.xaml'):
    tree=ET.parse(path)
    cs=Path(str(path)+'.cs')
    codebehind=cs.read_text(encoding='utf-8') if cs.exists() else ''
    for item in tree.iter():
        for attr,val in item.attrib.items():
            if attr.rsplit('}',1)[-1] in {'Click','Loaded','Closing','MouseDoubleClick','Checked','Unchecked','TextChanged','SelectionChanged','SelectedDateChanged'}:
                assert re.search(r'\b'+re.escape(val)+r'\s*\(',codebehind),(path,val)
print('PASS: v1.3 period filter UI/logic, cursor sequencing, compact editor dates, XAML and handler structure')
print('NOT TESTED: Windows WPF build/runtime, DSM actual data, creation/modified link timestamps')
