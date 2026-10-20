from pathlib import Path
from xml.etree import ElementTree as ET
root=Path(__file__).resolve().parents[1]
app=(root/'src/App.xaml').read_text(encoding='utf-8')
main=(root/'src/MainWindow.xaml').read_text(encoding='utf-8')
editor=(root/'src/Views/LinkEditorWindow.xaml').read_text(encoding='utf-8')
for f in (root/'src').rglob('*.xaml'): ET.parse(f)
assert 'x:Key="CenteredDatePickerStyle"' in app
for part in ('PART_Root', 'PART_TextBox', 'PART_Button', 'PART_Popup'):
    assert ('x:Name="'+part+'"') in app
assert 'VerticalContentAlignment="Center"' in app
assert app.count('Style x:Key="CenteredDatePickerStyle"')==1
assert main.count('Style="{StaticResource CenteredDatePickerStyle}"')==2
assert editor.count('Style="{StaticResource CenteredDatePickerStyle}"')==2
print('PASS v1.3.2: 4 datepickers use centered inner template and named WPF parts')
