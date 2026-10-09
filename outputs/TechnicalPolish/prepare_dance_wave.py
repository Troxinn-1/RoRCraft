"""Decode six seconds of the player's installed Pigstep asset for lobby dance."""
import hashlib, json, sys, wave
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path('work/audio-inspection').resolve()))
import soundfile as sf

root = Path(__file__).resolve().parent
assets = root.parent / 'RoRCraft-SkyCraft/minecraft/Prism/assets'
entry = json.loads((assets/'indexes/34.json').read_text())['objects']['minecraft/sounds/records/pigstep.ogg']
source = assets/'objects'/entry['hash'][:2]/entry['hash']
if hashlib.sha1(source.read_bytes()).hexdigest() != entry['hash']:
    raise ValueError('Installed Pigstep asset hash mismatch')
with sf.SoundFile(source) as audio:
    rate = audio.samplerate
    data = audio.read(rate*6, dtype='float64', always_2d=True)
if len(data) != rate*6 or data.shape[1] not in (1,2) or not np.any(data):
    raise ValueError('Invalid Pigstep excerpt')
# Avoid clicks at either boundary; game Master/Music gain applied at playback.
fade = max(1, int(rate*.15))
data[:fade] *= np.linspace(0,1,fade)[:,None]
data[-fade:] *= np.linspace(1,0,fade)[:,None]
pcm = np.rint(np.clip(data*.5,-1,1)*32767).astype('<i2')
target = root/'ui-assets/pigstep-dance.wav'
with wave.open(str(target),'wb') as output:
    output.setnchannels(pcm.shape[1]); output.setsampwidth(2); output.setframerate(rate)
    output.writeframes(pcm.tobytes())
report = dict(source_sha1=entry['hash'], wave_sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
              rate=rate, channels=pcm.shape[1], frames=len(pcm), duration=6,
              scope='Owned installed audio excerpt; decoded/format checks, no audible acceptance')
(root/'captures/dance-wave-asset.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
