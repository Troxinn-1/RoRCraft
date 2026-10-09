using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RoRCraftPolish {
    // Dedicated waveOut stream: menu PlaySound clicks cannot interrupt Pigstep.
    internal sealed class WindowsDanceWave : IDisposable {
        [StructLayout(LayoutKind.Sequential,Pack=2)]
        private struct Format {public ushort tag,channels;public uint rate,bytesPerSecond;public ushort align,bits,extra;}
        [StructLayout(LayoutKind.Sequential)]
        private struct Header {public IntPtr data;public uint length,recorded;public IntPtr user;public uint flags,loops;public IntPtr next,reserved;}
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr user,uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
        private Format format;
        private readonly byte[] source;
        private IntPtr handle,header;
        private GCHandle pinned;
        private bool prepared;
        internal float Duration {get{return source.Length/(float)format.bytesPerSecond;}}
        internal WindowsDanceWave() {
            byte[] bytes;
            using(var input=typeof(WindowsDanceWave).Assembly.GetManifestResourceStream("MinecraftUi.pigstep-dance.wav")) {
                if(input==null)throw new InvalidDataException("Pigstep dance audio missing");
                using(var copy=new MemoryStream()){input.CopyTo(copy);bytes=copy.ToArray();}
            }
            if(bytes.Length<44 || BitConverter.ToInt32(bytes,0)!=0x46464952 || BitConverter.ToInt32(bytes,8)!=0x45564157 || BitConverter.ToInt32(bytes,12)!=0x20746d66 || BitConverter.ToInt32(bytes,16)!=16 || BitConverter.ToInt32(bytes,36)!=0x61746164)
                throw new InvalidDataException("Invalid Pigstep PCM header");
            format=new Format{tag=BitConverter.ToUInt16(bytes,20),channels=BitConverter.ToUInt16(bytes,22),rate=BitConverter.ToUInt32(bytes,24),bytesPerSecond=BitConverter.ToUInt32(bytes,28),align=BitConverter.ToUInt16(bytes,32),bits=BitConverter.ToUInt16(bytes,34)};
            if(format.tag!=1 || format.bits!=16 || format.channels<1 || format.channels>2 || format.align!=format.channels*2 || format.bytesPerSecond!=format.rate*format.align || BitConverter.ToInt32(bytes,40)!=bytes.Length-44)
                throw new InvalidDataException("Invalid Pigstep PCM format");
            source=new byte[bytes.Length-44];Buffer.BlockCopy(bytes,44,source,0,source.Length);
            if(Math.Abs(Duration-6)>.01)throw new InvalidDataException("Pigstep excerpt must match six-second dance");
            IntPtr unused;Check(waveOutOpen(out unused,0xffffffff,ref format,IntPtr.Zero,IntPtr.Zero,1)); // FORMAT_QUERY only, silent.
        }
        private static void Check(uint code){if(code!=0)throw new InvalidOperationException("Pigstep Windows audio error "+code);}
        internal void Play(float gain) {
            Stop();if(float.IsNaN(gain) || float.IsInfinity(gain))return;
            gain=Math.Max(0,Math.Min(1,gain));if(gain==0)return;
            var buffer=new byte[source.Length];
            for(int i=0;i<buffer.Length;i+=2){short value=(short)Math.Round(BitConverter.ToInt16(source,i)*gain);buffer[i]=(byte)value;buffer[i+1]=(byte)(value>>8);}
            try {
                Check(waveOutOpen(out handle,0xffffffff,ref format,IntPtr.Zero,IntPtr.Zero,0));
                pinned=GCHandle.Alloc(buffer,GCHandleType.Pinned);
                header=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Header)));
                Marshal.StructureToPtr(new Header{data=pinned.AddrOfPinnedObject(),length=(uint)buffer.Length},header,false);
                Check(waveOutPrepareHeader(handle,header,(uint)Marshal.SizeOf(typeof(Header))));prepared=true;
                Check(waveOutWrite(handle,header,(uint)Marshal.SizeOf(typeof(Header))));
            } catch {Stop();throw;}
        }
        internal void Stop() {
            if(handle!=IntPtr.Zero){waveOutReset(handle);if(prepared)waveOutUnprepareHeader(handle,header,(uint)Marshal.SizeOf(typeof(Header)));waveOutClose(handle);handle=IntPtr.Zero;}
            prepared=false;if(header!=IntPtr.Zero){Marshal.FreeHGlobal(header);header=IntPtr.Zero;}if(pinned.IsAllocated)pinned.Free();
        }
        public void Dispose(){Stop();}
    }
}
