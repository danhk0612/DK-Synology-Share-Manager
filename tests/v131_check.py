"""Layout-only regression checks; no Windows rendering or NAS calls."""
from pathlib import Path
from xml.etree import ElementTree as ET
root = Path(__file__).resolve().parents[1]
app = (root/'src/App.xaml').read_text(encoding='utf-8')
main = (root/'src/MainWindow.xaml').read_text(encoding='utf-8')
code = (root/'src/MainWindow.xaml.cs').read_text(encoding='utf-8')
editor = (root/'src/Views/LinkEditorWindow.xaml').read_text(encoding='utf-8')
for f in (root/'src').rglob('*.xaml'):
    ET.parse(f)
assert '<Style TargetType="DatePickerTextBox">' in app
assert '<Setter Property="MinHeight" Value="0"/>' in app
assert editor.count('Grid Margin="0,15,0,0" Height="33"') == 2
assert editor.count('Checked="DateCheck_Changed"') == 2
assert editor.count('ColumnDefinition Width="64"') == 2
assert 'x:Name="AvailablePicker"' in editor and 'x:Name="ExpirePicker"' in editor
assert 'x:Name="DateFromFilter" Width="150" Height="30"' in main
assert 'x:Name="DateToFilter" Width="150" Height="30"' in main
assert 'DetailHint.Visibility = count == 1 ? Visibility.Collapsed : Visibility.Visible;' in code
assert 'VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"' in main
assert main.count('Margin="0,7,0,2"') == 5
print('PASS v1.3.1: date rows/control alignment, compact link-detail layout, conditional hint visibility')
print('NOT TESTED: Windows WPF rendering/build or DSM API')
