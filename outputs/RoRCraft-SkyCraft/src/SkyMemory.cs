// SkyCraft v0.1.0 protocol v10, adapted for a Unity host.
// Protocol copyright (c) 2026 chasmlol; MIT license in THIRD-PARTY-LICENSE.txt.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace RoRCraft
{
    public unsafe sealed class SkyMemory : IDisposable
    {
        public const string Name = "LocalRoRCraft_v1";
        public const long Collision = 0x20000, CollisionBytes = 32L << 20;
        public const long Pixels = Collision + CollisionBytes, SlotBytes = 3840L * 2160 * 4;
        public const long Render = Pixels + SlotBytes * 3, RenderBytes = 64L << 20;
        public const long Size = Render + RenderBytes;
        private MemoryMappedFile mapping;
        private MemoryMappedViewAccessor view;
        private byte* ptr;
        private int front = 2, sequence;
        private byte[] overlayBuffer;
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        public SkyMemory(string mappingName = null)
        {
            // Minecraft keeps the mapping open across host restarts. Reuse it only
            // after its previous host has exited, then publish the new host last.
            mapping = MemoryMappedFile.CreateOrOpen(mappingName ?? Name, Size);
            view = mapping.CreateViewAccessor(0, Size);
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            ptr += view.PointerOffset;
            int previous=I(8),minecraft=I(12);
            if(previous!=0) {
                bool running=false;
                try { using(var process=Process.GetProcessById(previous)) running=!process.HasExited; } catch(ArgumentException) { }
                if(running) { view.SafeMemoryMappedViewHandle.ReleasePointer(); ptr=null; view.Dispose(); mapping.Dispose(); throw new IOException("Another RoRCraft host is already running."); }
            }
            L(0x10,0);
            for(long p=0;p<Size;p+=8) *(long*)(ptr+p)=0;
            I(0, 0x43594B53); I(4, 10); I(12,minecraft);
            for (int i = 0; i < 256; i++) F(0x410 + i * 4, -1e30f);
            Thread.MemoryBarrier(); I(8, Process.GetCurrentProcess().Id); Heartbeat();
        }
        public int I(long p) { return *(int*)(ptr + p); }
        public long L(long p) { return Interlocked.Read(ref *(long*)(ptr + p)); }
        public float F(long p) { return *(float*)(ptr + p); }
        public double D(long p) { return *(double*)(ptr + p); }
        public void I(long p, int v) { *(int*)(ptr + p) = v; }
        public void L(long p, long v) { Interlocked.Exchange(ref *(long*)(ptr + p), v); }
        public void F(long p, float v) { *(float*)(ptr + p) = v; }
        public void D(long p, double v) { *(double*)(ptr + p) = v; }
        public void Heartbeat() { L(0x10, (long)GetTickCount64()); }
        public bool Alive { get { ulong b = (ulong)L(0x18); return I(12) != 0 && b != 0 && GetTickCount64() - b < 3000; } }
        public byte[] Bytes(long p, int n)
        {
            if (p < 0 || n < 0 || p + n > Size) throw new ArgumentOutOfRangeException();
            var result = new byte[n]; Marshal.Copy((IntPtr)(ptr+p), result, 0, n); return result;
        }
        public void Bytes(long p, byte[] bytes)
        {
            if (p < 0 || p + bytes.Length > Size) throw new ArgumentOutOfRangeException();
            Marshal.Copy(bytes, 0, (IntPtr)(ptr+p), bytes.Length);
        }
        public void State(int flags, int epoch, double x, double y, double z, float yaw, float pitch, int teleport, int w, int h)
        {
            int s = ++sequence;
            I(0x100, s*2-1); Thread.MemoryBarrier();
            I(0x104, flags); I(0x108, 1); I(0x10C, epoch);
            D(0x110,x); D(0x118,y); D(0x120,z); F(0x128,yaw); F(0x12C,pitch);
            I(0x130,teleport); I(0x134,w); I(0x138,h); F(0x13C,12);
            Thread.MemoryBarrier(); I(0x100,s*2);
        }
        public byte[] McState()
        {
            for (int i=0;i<3;i++) {
                int seq=I(0x200); if ((seq&1)!=0) continue;
                Thread.MemoryBarrier(); byte[] data=Bytes(0x200,0xC8); Thread.MemoryBarrier();
                if (seq==I(0x200)) return data;
            }
            return null;
        }
        public bool Input(ushort type, ushort code, int a, int b, int c)
        {
            long head=L(0x1000), tail=L(0x1040);
            if (head-tail>=4096 || head<tail) return false;
            long p=0x1080+(head%4096)*16;
            *(ushort*)(ptr+p)=type; *(ushort*)(ptr+p+2)=code;
            I(p+4,a); I(p+8,b); I(p+12,c); Thread.MemoryBarrier(); L(0x1000,head+1); return true;
        }
        public bool CollisionMessage(int type, byte[] data)
        {
            long capacity=CollisionBytes-0x80, head=L(Collision), tail=L(Collision+0x40);
            long total=(8L+data.Length+7)&~7L, pos=head%capacity;
            long pad=pos+total>capacity ? capacity-pos : 0;
            if (total>capacity/2 || head<tail || head-tail+total+pad>capacity) return false;
            if (pad>0) { I(Collision+0x80+pos,0); I(Collision+0x84+pos,0); head+=pad; pos=0; }
            long p=Collision+0x80+pos; I(p,type); I(p+4,data.Length); Bytes(p+8,data);
            Thread.MemoryBarrier(); L(Collision,head+total); return true;
        }
        public void DrainRender(Action<int,byte[]> consume, int budget)
        {
            long capacity=RenderBytes-0x80, head=L(Render), tail=L(Render+0x40);
            if (head<tail || head-tail>capacity) throw new InvalidOperationException("Render ring overflow.");
            byte[] latestPlayer=null,latestScene=null;
            for (int i=0;i<budget && tail<head;i++) {
                long pos=tail%capacity, p=Render+0x80+pos; int type=I(p), length=I(p+4);
                if (type==0) { tail+=capacity-pos; continue; }
                long total=(8L+length+7)&~7L;
                if (length<0 || length>capacity/2 || pos+total>capacity || tail+total>head)
                    throw new InvalidOperationException("Malformed render ring message.");
                if(type==5) latestPlayer=Bytes(p+8,length);
                else if(type==6) latestScene=Bytes(p+8,length);
                else {
                    if(type==3) {latestPlayer=null;latestScene=null;}
                    consume(type,Bytes(p+8,length));
                }
                tail+=total;
            }
            if(latestPlayer!=null) consume(5,latestPlayer);
            if(latestScene!=null) consume(6,latestScene);
            L(Render+0x40,tail);
        }
        public void Events(Action<byte[]> consume)
        {
            long head=L(0x17000), tail=L(0x17040);
            if (head<tail || head-tail>512) throw new InvalidOperationException("Event ring overflow.");
            while (tail<head) { consume(Bytes(0x17080+(tail%512)*32,32)); tail++; }
            L(0x17040,tail);
        }
        public byte[] Overlay(out int w, out int h, out bool bottomUp)
        {
            w=h=0; bottomUp=false;
            int state=Volatile.Read(ref *(int*)(ptr+0x300)); if ((state&4)==0) return null;
            int old=Interlocked.Exchange(ref *(int*)(ptr+0x300),front); front=old&3;
            if (front>2) throw new InvalidOperationException("Invalid overlay slot.");
            long p=0x340+front*0x40; w=I(p); h=I(p+4); bottomUp=(I(p+8)&1)!=0;
            if (w<=0 || w>3840 || h<=0 || h>2160) return null;
            int size=checked(w*h*4);
            if(overlayBuffer==null || overlayBuffer.Length!=size) overlayBuffer=new byte[size];
            Marshal.Copy((IntPtr)(ptr+Pixels+front*SlotBytes),overlayBuffer,0,size);
            return overlayBuffer;
        }
        public void Dispose()
        {
            if (view!=null) { L(0x10,0); view.SafeMemoryMappedViewHandle.ReleasePointer(); ptr=null; view.Dispose(); view=null; }
            if (mapping!=null) { mapping.Dispose(); mapping=null; }
        }
    }
}
