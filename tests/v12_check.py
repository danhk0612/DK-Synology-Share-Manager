"""v1.2 UI contract checks; run on Linux as well as Windows (not an actual WPF compile)."""
from pathlib import Path
from xml.etree import ElementTree as ET
root = Path(__file__).resolve().parents[1]
src = root / 'src'
main = (src/'MainWindow.xaml').read_text(encoding='utf-8')
code = (src/'MainWindow.xaml.cs').read_text(encoding='utf-8')
picker = (src/'Views/FilePickerWindow.xaml').read_text(encoding='utf-8')
pickercode = (src/'Views/FilePickerWindow.xaml.cs').read_text(encoding='utf-8')
editor = (src/'Views/LinkEditorWindow.xaml').read_text(encoding='utf-8')
app = (src/'App.xaml').read_text(encoding='utf-8')
assert 'Width="215" MinWidth="150"' in picker
assert '<TreeView x:Name="FolderTree"' in picker
assert 'TreeViewItem.Expanded="FolderTree_Expanded"' in picker
assert 'GridSplitter' in picker
assert 'ScrollViewer.HorizontalScrollBarVisibility="Disabled"' in picker
assert '<WrapPanel IsItemsHost="True" Orientation="Horizontal"' in picker
assert 'x:Name="SelectButton" Content="선택"' in picker
assert 'SelectCurrent_Click' not in picker and 'SelectItem_Click' not in picker
assert 'var target = CurrentSelection?.Path ?? _currentPath;' in pickercode
assert 'SelectedPath = target;' in pickercode
assert 'PopulateChildrenAsync' in pickercode and 'BrowseAsync' in pickercode
assert 'Style TargetType="DatePickerTextBox"' in app
assert editor.count('Height="30" Padding="0" VerticalAlignment="Center" IsEnabled="False"') == 2
assert 'x:Name="UrlBox" IsReadOnly="True" Height="32" TextWrapping="NoWrap"' in main
assert 'Grid.Row="5"' in main and 'PageText' in main
assert 'PreviousPage_Click' in main and 'NextPage_Click' in main
assert 'PageSize = 25' in code
assert '_view.SortDescriptions.Add' in code
assert '_view.Cast<SharingLink>().ToList()' in code
assert 'filtered.Skip(_pageIndex * PageSize).Take(PageSize)' in code
assert 'RefreshPage(resetPage: true)' in code
assert 'LinksGrid_Sorting' in main
assert 'Background" Value="{StaticResource AccentSoftBrush}"' in main
assert main.count('ElementStyle="{StaticResource LinkTextStyle}"') == 6
assert '<DataGridTextColumn Header="' in main
# Validate layout is in the XAML of all five windows (structural only).
for item in src.rglob('*.xaml'): ET.parse(item)
print('PASS: v1.2 explorer, single selection, icon wrapping, date alignment, rows, URL, global filtered sorting then 25-item paging')
