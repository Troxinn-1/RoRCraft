using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RoRCraftPolish {
    // RoR2 disables Unity's audio engine. This owns one short PCM UI sound in
    // this process's Windows audio session; it never changes Wwise/listeners.
    internal sealed class WindowsMenuWave : IDisposable {
        [StructLayout(LayoutKind.Sequential,Pack=2)]
        private struct WaveFormat {public ushort format,channels;public uint rate,bytesPerSecond;public ushort align,bits,extra;}
        [DllImport("winmm.dll",EntryPoint="PlaySoundW")]
        [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PlaySound(IntPtr sound,IntPtr module,uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr output,uint device,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
        private GCHandle pinned;
        private byte[] template,buffer;
        private float currentGain=-1;
        private bool played;
        internal WindowsMenuWave() {
            byte[] bytes;
            using(var stream=typeof(WindowsMenuWave).Assembly.GetManifestResourceStream("MinecraftUi.click.wav")) {
                if(stream==null) throw new InvalidOperationException("Minecraft PCM menu sound missing");
                using(var copy=new MemoryStream()) {stream.CopyTo(copy);bytes=copy.ToArray();}
            }
            if(bytes.Length<44 || BitConverter.ToInt32(bytes,0)!=0x46464952 || BitConverter.ToInt32(bytes,8)!=0x45564157) throw new InvalidOperationException("Invalid menu WAVE header");
            // This embedded asset is generated as canonical PCM16 RIFF by the
            // reproducible build converter, with an explicit format/data chunk.
            if(BitConverter.ToInt32(bytes,12)!=0x20746d66 || BitConverter.ToInt32(bytes,16)!=16 || BitConverter.ToInt32(bytes,36)!=0x61746164) throw new InvalidOperationException("Unexpected menu WAVE chunks");
            var format=new WaveFormat {format=BitConverter.ToUInt16(bytes,20),channels=BitConverter.ToUInt16(bytes,22),rate=BitConverter.ToUInt32(bytes,24),bytesPerSecond=BitConverter.ToUInt32(bytes,28),align=BitConverter.ToUInt16(bytes,32),bits=BitConverter.ToUInt16(bytes,34),extra=0};
            if(format.format!=1 || format.bits!=16 || format.align!=format.channels*2 || BitConverter.ToInt32(bytes,40)!=bytes.Length-44) throw new InvalidOperationException("Invalid menu PCM format");
            IntPtr unused;uint result=waveOutOpen(out unused,0xffffffff,ref format,IntPtr.Zero,IntPtr.Zero,1); // WAVE_FORMAT_QUERY: no playback/device opening.
            if(result!=0) throw new InvalidOperationException("Default Windows audio device rejects menu PCM format: "+result);
            template=bytes;buffer=(byte[])bytes.Clone();pinned=GCHandle.Alloc(buffer,GCHandleType.Pinned);
        }
        internal bool Play(float gain) {
            if(!pinned.IsAllocated) return false;
            if(float.IsNaN(gain) || float.IsInfinity(gain)) return false;
            gain=Math.Max(0,Math.Min(1,gain));
            if(gain==0) {if(played) {PlaySound(IntPtr.Zero,IntPtr.Zero,0);played=false;}return true;}
            if(gain!=currentGain) {
                // Stop this process's previous UI sample before modifying its
                // pinned buffer; async playback must not race PCM writes.
                if(played) PlaySound(IntPtr.Zero,IntPtr.Zero,0);
                for(int i=44;i<buffer.Length;i+=2) {short value=(short)Math.Round(BitConverter.ToInt16(template,i)*gain);buffer[i]=(byte)value;buffer[i+1]=(byte)(value>>8);}
                currentGain=gain;
            }
            // SND_MEMORY | SND_ASYNC | SND_NODEFAULT; memory stays pinned until
            // playback is stopped at disposal, never just for the P/Invoke call.
            bool ok=PlaySound(pinned.AddrOfPinnedObject(),IntPtr.Zero,0x0004|0x0001|0x0002);played|=ok;return ok;
        }
        public void Dispose() {
            if(!pinned.IsAllocated) return;
            if(played) PlaySound(IntPtr.Zero,IntPtr.Zero,0);
            pinned.Free();template=buffer=null;
        }
    }
}
