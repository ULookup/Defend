"""Restore only observed legacy URP serialization side effects, with a strict guard."""
from pathlib import Path
import json
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
restored = []
names = ['ArmorIvory', 'EnemyOrange', 'EnergyCyan', 'GroundConcrete', 'MetalSteel', 'RockSlate', 'StructureDark']
for name in names:
    relative = f'Assets/Defend/Content/Art/PhaseFive/Materials/PhaseFive_{name}.mat'
    path = ROOT / relative
    baseline = subprocess.check_output(['git', 'show', 'HEAD:' + relative], cwd=ROOT)
    current = path.read_bytes()
    if current == baseline:
        continue
    original_color = re.search(rb'^    - _Color: .+$', baseline, re.M)
    current_color = re.search(rb'^    - _Color: .+$', current, re.M)
    if not original_color or not current_color:
        raise RuntimeError(f'Cannot identify compatibility color in {relative}')
    repaired = current.replace(current_color.group(), original_color.group(), 1)
    if name == 'EnergyCyan':
        repaired = repaired.replace(b'  m_ValidKeywords: []\n', b'  m_ValidKeywords:\n  - _EMISSION\n', 1)
    if repaired != baseline:
        raise RuntimeError(f'Refusing unrelated changes in {relative}')
    path.write_bytes(repaired)
    restored.append(relative)
result = {'restored': restored, 'fields': ['compatibility _Color', 'EnergyCyan _EMISSION keyword'],
          'guard': 'Only these field changes are accepted; direct byte comparison, no hashes.'}
(ROOT / 'Docs/Art/RapidTurret/Checks/ImportSideEffects.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result))
