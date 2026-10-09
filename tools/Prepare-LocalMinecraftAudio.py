import wave
from pathlib import Path
import numpy as np
import soundfile as sf
root=Path(__file__).resolve().parents[1]/'outputs/TechnicalPolish/ui-assets'
for source,target,gain in [('click.ogg','click.wav',.3),('pigstep.ogg','pigstep-dance.wav',.5)]:
    data,rate=sf.read(root/source,dtype='float64',always_2d=True)
    if source=='pigstep.ogg': data=data[:rate*6]
    pcm=np.rint(np.clip(data*gain,-1,1)*32767).astype('<i2')
    with wave.open(str(root/target),'wb') as out:
        out.setnchannels(pcm.shape[1]);out.setsampwidth(2);out.setframerate(rate);out.writeframes(pcm.tobytes())
print('Local Minecraft audio prepared')
