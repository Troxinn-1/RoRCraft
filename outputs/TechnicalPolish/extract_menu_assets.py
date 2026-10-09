from zipfile import ZipFile
from pathlib import Path
import json
z=ZipFile('outputs/RoRCraft-SkyCraft/minecraft/Prism/libraries/com/mojang/minecraft/26.3/minecraft-26.3-client.jar')
out=Path('outputs/TechnicalPolish/ui-assets');out.mkdir(exist_ok=True)
for name,path in {'button':'textures/gui/sprites/widget/button.png','hover':'textures/gui/sprites/widget/button_highlighted.png','background':'textures/block/dirt.png','ascii':'textures/font/ascii.png','slider':'textures/gui/sprites/widget/slider.png','handle':'textures/gui/sprites/widget/slider_handle.png'}.items():
 (out/(name+'.png')).write_bytes(z.read('assets/minecraft/'+path))
definition=json.loads(z.read('assets/minecraft/font/include/default.json'))
provider=next(p for p in definition['providers'] if p.get('file')=='minecraft:font/ascii.png')
chars=''.join(provider['chars'])
Path('outputs/TechnicalPolish/src/MinecraftGlyphs.cs').write_text('namespace RoRCraftPolish { internal static class MinecraftGlyphs { internal const string Characters='+json.dumps(chars)+'; } }',encoding='utf8')
print('Extracted six original GUI textures and',len(chars),'bitmap glyph positions.')
print('Button metadata:',z.read('assets/minecraft/textures/gui/sprites/widget/button.png.mcmeta').decode())


assets=Path("outputs/RoRCraft-SkyCraft/minecraft/Prism/assets")
index=json.loads((assets/"indexes/34.json").read_text())
h=index["objects"]["minecraft/sounds/random/click_stereo.ogg"]["hash"]
(out/"click.ogg").write_bytes((assets/"objects"/h[:2]/h).read_bytes())
