"""Extract original MC dance textures from the local, installed client assets."""
from pathlib import Path
from zipfile import ZipFile
import hashlib,json
root=Path(__file__).resolve().parent
project=root.parent.parent
jar=project/'work/gradle-cache/caches/fabric-loom/26.3/minecraft-client.jar'
paths=['assets/minecraft/textures/particle/note.png',
       'assets/minecraft/textures/entity/parrot/parrot_green.png',
       'assets/minecraft/textures/block/jukebox_side.png',
       'assets/minecraft/textures/block/jukebox_top.png']
report={'source':'Minecraft 26.3 local client','textures':[]}
with ZipFile(jar) as archive:
    for name in paths:
        data=archive.read(name);destination=root/'ui-assets'/Path(name).name
        destination.write_bytes(data)
        report['textures'].append({'source':name,'file':destination.name,'sha256':hashlib.sha256(data).hexdigest()})
(root/'captures/dance-texture-assets.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
