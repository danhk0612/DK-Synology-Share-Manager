"""Static WPF header-click wiring regression; run Windows build for runtime confirmation."""
from pathlib import Path
from xml.etree import ElementTree as ET

root = Path(__file__).resolve().parents[1]
xml = root / 'src/Views/FilePickerWindow.xaml'
code = (root / 'src/Views/FilePickerWindow.xaml.cs').read_text(encoding='utf-8')
ns = {'w': 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
headers = ET.parse(xml).findall('.//w:GridViewColumnHeader', ns)
assert len(headers) == 6
assert {h.attrib.get('Tag'): h.attrib.get('Content') for h in headers} == {
    'Name': '이름', 'Modified': '수정한 날짜', 'Type': '유형',
    'Size': '크기', 'Created': '만든 날짜', 'Path': 'NAS 경로'
}
assert all(h.attrib.get('Click') == 'DetailsHeader_Click' for h in headers)
assert 'ButtonBase.Click="DetailsHeader_Click"' not in xml.read_text(encoding='utf-8')
assert 'sender is not GridViewColumnHeader header || header.Tag is not string field' in code
assert 'if (_sortField == field)' in code
assert 'SortDirection_Click(sender, e);' in code
assert 'SortField.SelectedItem = dropdownItem;' in code
assert 'else ApplySort();' in code
print('PASS v1.4.2: six direct column-header click handlers, fields mapped, direction and dropdown behavior retained')
print('NOT TESTED: live Windows WPF click interaction')
