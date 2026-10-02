"""Prepare one fresh, private incident replay. Does not launch, deploy, or delete."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil

ROOT = Path('C:/HDQA/release-regressions-20261002')  # Probe.cs enforces this private root.
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('name')
p.add_argument('--reference-manifest', type=Path, required=True)
p.add_argument('--seed-save', type=Path, required=True)
p.add_argument('--release-mod', type=Path, required=True)
p.add_argument('--workspace', type=Path, default=Path(__file__).resolve().parents[2])
p.add_argument('--workshop', type=Path, default=Path('C:/Steam/steamapps/workshop/content/294100'))
p.add_argument('--product', choices=['none', 'release', 'fixed'], required=True)
p.add_argument('--mode', choices=['produce', 'verify', 'refuel', 'haul', 'haul-urgent', 'haul-urgent-foreign', 'enroute', 'enroute-foreign'], required=True)
p.add_argument('--medieval', action='store_true')
p.add_argument('--before', action='store_true')
p.add_argument('--expected', type=Path)
a = p.parse_args()
assert re.fullmatch(r'[a-zA-Z0-9_-]+', a.name), 'Simple run name required'
run = ROOT / 'runs' / a.name
assert not run.exists(), 'Never overwrite evidence'
assert a.seed_save.is_file()
if a.mode == 'verify': assert a.expected and a.expected.is_file()
runtime, save, out = run/'runtime', run/'SaveData', run/'output'
sha = lambda file: hashlib.sha256(file.read_bytes()).hexdigest().upper()
pins = []
reference = a.reference_manifest.resolve().parent / 'runtime'
for entry in json.loads(a.reference_manifest.read_text(encoding='utf-8-sig'))['copiedFiles']:
    if entry['role'] not in ('game', 'harmony'): continue
    source = Path(entry['path']).resolve(strict=True)
    assert sha(source) == entry['sha256'], 'Frozen reference changed'
    dest = runtime / source.relative_to(reference)
    dest.parent.mkdir(parents=True, exist_ok=True)
    os.link(source, dest)
    pins.append(dict(path=str(dest), sha256=entry['sha256']))
mods = ['brrainz.harmony', 'ludeon.rimworld', 'local.releaseregressionprobe']
providers = []
if 'urgent' in a.mode:
    providers += [('818773962', 'HugsLib', 'unlimitedhugs.hugslib'), ('761421485', 'AllowTool', 'unlimitedhugs.allowtool')]
if a.medieval:
    providers += [('2023507013', 'VEF', 'oskarpotocki.vanillafactionsexpanded.core'), ('3210544395', 'Processor', 'syrchalis.processor.framework'), ('3219596926', 'MedievalOverhaul', 'dankpyon.medieval.overhaul')]
for workshop_id, name, package in providers:
    shutil.copytree(a.workshop/workshop_id, runtime/'Mods'/name)
    mods.append(package)
if a.product != 'none':
    product = runtime/'Mods/HaulersDream'
    shutil.copytree(a.release_mod, product)
    if a.product == 'fixed':
        for dll in ('HaulersDream.dll', 'HaulersDream.Core.dll'):
            shutil.copyfile(a.workspace/'1.6/Assemblies'/dll, product/'1.6/Assemblies'/dll)
    mods.insert(3, 'giwaffed.haulersdream') if a.before else mods.append('giwaffed.haulersdream')
probe = runtime/'Mods/ReleaseRegressionProbe'
(probe/'About').mkdir(parents=True)
(probe/'Assemblies').mkdir()
(probe/'About/About.xml').write_text('<ModMetaData><name>Private release regression probe</name><author>Local QA</author><packageId>local.releaseregressionprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>')
shutil.copyfile(Path(__file__).parent/'bin/Release/ReleaseRegressionProbe.dll', probe/'Assemblies/ReleaseRegressionProbe.dll')
(save/'Config').mkdir(parents=True)
(save/'Saves').mkdir()
out.mkdir()
(save/'Config/ModsConfig.xml').write_text('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>'+''.join('<li>'+m+'</li>' for m in mods)+'</activeMods></ModsConfigData>')
(save/'Config/Prefs.xml').write_text('<PrefsData><devMode>False</devMode><pauseOnLoad>True</pauseOnLoad><runInBackground>True</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic></PrefsData>')
shutil.copyfile(a.seed_save, save/'Saves'/('RegressionBeforeUpdate.rws' if a.mode == 'verify' else 'Benchmark20.rws'))
if a.expected: shutil.copyfile(a.expected, out/'expected.txt')
for base in (runtime/'Mods', save):
    for file in base.rglob('*'):
        if file.is_file(): pins.append(dict(path=str(file), sha256=sha(file)))
(run/'pins.json').write_text(json.dumps(pins, indent=2))
(run/'args.json').write_text(json.dumps([str(runtime/'RimWorldWin64.exe'), '-savedatafolder='+str(save), '-logFile', str(out/'Player.log'), '-screen-fullscreen', '0', '-screen-width', '1024', '-screen-height', '768', '-regression-output='+str(out), '-regression-mode='+a.mode]))
print(run)
