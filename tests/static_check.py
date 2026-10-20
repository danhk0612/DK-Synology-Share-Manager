"""Dependency-free structural safety checks, not a Windows build/test."""
from pathlib import Path
from xml.etree import ElementTree as ET
import re
root=Path(__file__).resolve().parents[1]
src=root/'src'
files=list(src.rglob('*.xaml'))
assert len(files)==5, files
for x in files:
    parsed=ET.parse(x)
    c=Path(str(x)+'.cs')
    assert c.exists(), c
    code=c.read_text(encoding='utf-8')
    for element in parsed.iter():
        for prop,val in element.attrib.items():
            if prop.split('}')[-1] in {'Loaded','Closed','Click','TextChanged','SelectionChanged','Checked','Unchecked','MouseDoubleClick'}:
                assert re.search(r'\b'+re.escape(val)+r'\s*\(',code), (x, val)
api=(src/'Services'/'DsmApiClient.cs').read_text(encoding='utf-8')
assert 'SYNO.FileStation.Sharing' in api
assert 'SYNO.FileStation.List' in api
assert 'SYNO.FileStation.Delete' not in api
assert 'SYNO.FileStation.Sharing", 3, "delete"' in api
assert 'AssertIdentity(original, before)' in api
assert '"?_sid="' in api and '"&SynoToken="' in api
assert '"force_clean"' in api
assert 'GetLinksAsync' in api
assert 'DatePart(' in api
model=(src/'Models'/'SharingLink.cs').read_text(encoding='utf-8')
assert 'string.IsNullOrWhiteSpace(value) || value == "0"' in model
assert 'value[..10]' in model
settings=(src/'Services'/'SettingsStore.cs').read_text(encoding='utf-8')
assert 'CryptProtectData' in settings and 'CryptUnprotectData' in settings
assert 'AppContext.BaseDirectory' in settings
print('PASS: 5 XAML documents parsed, declared handlers found, API and safety invariants checked')
print('NOT TESTED: .NET compilation, Windows UI, live DSM API or NAS operations')
