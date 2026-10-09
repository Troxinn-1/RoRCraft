"""Convert the already-owned MC UI asset; no device playback during builds."""
import hashlib
import json
import sys
import wave
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path('work/audio-inspection').resolve()))
import soundfile as sf

root = Path(__file__).resolve().parent
source = root / 'ui-assets/click.ogg'
target = root / 'ui-assets/click.wav'
data, rate = sf.read(source, dtype='float64', always_2d=True)
# Matches the former AudioSource volume, without changing the native game's
# Wwise audio manager or listeners. Windows playback uses its own PCM backend.
pcm = np.rint(np.clip(data * .3, -1, 1) * 32767).astype('<i2')
if not len(pcm) or not np.any(pcm) or data.shape[1] not in (1, 2):
    raise RuntimeError('Invalid or silent menu source')
with wave.open(str(target), 'wb') as output:
    output.setnchannels(pcm.shape[1]); output.setsampwidth(2); output.setframerate(rate)
    output.writeframes(pcm.tobytes())
report = dict(source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
              wave_sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
              rate=rate, channels=pcm.shape[1], frames=len(pcm),
              duration=len(pcm)/rate, peak=int(np.max(np.abs(pcm))),
              scope='Decoded asset and PCM format only; no audible playback test')
(root / 'captures/menu-wave-asset.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
