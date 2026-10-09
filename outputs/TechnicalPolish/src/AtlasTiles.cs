using System;
using System.IO;

namespace RoRCraftPolish {
    internal sealed class AtlasTiles {
        internal sealed class Tile {internal int X,Y,W,H,Pad;internal bool Filtered;}
        internal readonly int Width,Height,Levels;
        internal readonly Tile[] Tiles;
        internal string Limitation="none";
        private static readonly double[] Linear=Lookup();
        private static readonly byte[] Encoded=EncodeLookup();
        private static double[] Lookup() {var a=new double[256];for(int i=0;i<256;i++) {double x=i/255d;a[i]=x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return a;}
        private static byte[] EncodeLookup() {var a=new byte[65536];for(int i=0;i<a.Length;i++) {double x=i/65535d;a[i]=(byte)Math.Round(255*(x<=.0031308?12.92*x:1.055*Math.Pow(x,1/2.4)-.055));}return a;}
        private static byte Srgb(double x) {return Encoded[(int)Math.Round(Math.Max(0,Math.Min(1,x))*65535)];}
        internal AtlasTiles(byte[] packet) {
            if(packet==null || packet.Length<20 || BitConverter.ToInt32(packet,0)!=0x50494d53) throw new InvalidDataException("Invalid atlas layout header");
            Width=BitConverter.ToInt32(packet,4);Height=BitConverter.ToInt32(packet,8);int count=BitConverter.ToInt32(packet,12);
            int blockCount=BitConverter.ToInt32(packet,16);
            if(Width<1 || Height<1 || (long)Width*Height>16*1024*1024 || count<1 || count>8192 || blockCount<1 || blockCount>count || packet.Length!=20+count*20) throw new InvalidDataException("Invalid atlas layout bounds");
            Tiles=new Tile[count];int levels=2;
            for(int i=0;i<count;i++) {
                int p=20+i*20;var t=new Tile {X=BitConverter.ToInt32(packet,p),Y=BitConverter.ToInt32(packet,p+4),W=BitConverter.ToInt32(packet,p+8),H=BitConverter.ToInt32(packet,p+12),Pad=BitConverter.ToInt32(packet,p+16),Filtered=i<blockCount};
                if(t.Pad<0 || t.Pad>64 || t.W<1 || t.H<1 || t.X<t.Pad || t.Y<t.Pad || (long)t.X+t.W+t.Pad>Width || (long)t.Y+t.H+t.Pad>Height) throw new InvalidDataException("Invalid sprite bounds");
                t.Y=Height-t.Y-t.H;Tiles[i]=t;
                int before=levels;
                while(t.Filtered && levels>0 && ((Width|Height|t.X|t.Y|t.W|t.H|t.Pad)&((1<<levels)-1))!=0) levels--;
                if(levels!=before) Limitation="sprite "+i+" rect="+t.X+","+t.Y+","+t.W+","+t.H+" pad="+t.Pad;
            }
            Levels=levels;
        }
        internal byte[][] Create(byte[] original) {
            if(original.Length!=(long)Width*Height*4) throw new InvalidDataException("Invalid atlas pixel length");
            var mips=new byte[Levels+1][];mips[0]=original;
            for(int level=1;level<mips.Length;level++) mips[level]=new byte[(Width>>level)*(Height>>level)*4];
            foreach(var tile in Tiles) Build(mips,tile);
            return mips;
        }
        internal void Build(byte[][] mips,Tile tile) {
            if(!tile.Filtered) return;
            for(int level=1;level<mips.Length;level++) {
                int scale=1<<level,w=Width>>level,x0=tile.X>>level,y0=tile.Y>>level,tw=tile.W>>level,th=tile.H>>level,pad=tile.Pad>>level;
                for(int y=0;y<th;y++) for(int x=0;x<tw;x++) {
                    double r=0,g=0,b=0,alpha=0;int maxAlpha=0;
                    // Sample only this sprite; transparent RGB never contaminates edges.
                    for(int dy=0;dy<scale;dy++) for(int dx=0;dx<scale;dx++) {
                        int source=((tile.Y+y*scale+dy)*Width+tile.X+x*scale+dx)*4;int a=mips[0][source+3];
                        alpha+=a;r+=Linear[mips[0][source]]*a;g+=Linear[mips[0][source+1]]*a;b+=Linear[mips[0][source+2]]*a;maxAlpha=Math.Max(maxAlpha,a);
                    }
                    int target=((y0+y)*w+x0+x)*4;
                    if(alpha>0) {mips[level][target]=Srgb(r/alpha);mips[level][target+1]=Srgb(g/alpha);mips[level][target+2]=Srgb(b/alpha);}
                    else {mips[level][target]=mips[level][target+1]=mips[level][target+2]=0;}
                    // Conservative alpha keeps thin cutout glass/leaf edges visible.
                    mips[level][target+3]=(byte)maxAlpha;
                }
                for(int y=-pad;y<th+pad;y++) for(int x=-pad;x<tw+pad;x++) {
                    if(x>=0 && x<tw && y>=0 && y<th) continue;
                    int from=((y0+Math.Max(0,Math.Min(th-1,y)))*w+x0+Math.Max(0,Math.Min(tw-1,x)))*4;
                    int to=((y0+y)*w+x0+x)*4;Buffer.BlockCopy(mips[level],from,mips[level],to,4);
                }
            }
        }
        internal bool Intersects(Tile t,int x,int y,int w,int h) {return x<t.X+t.W+t.Pad && x+w>t.X-t.Pad && y<t.Y+t.H+t.Pad && y+h>t.Y-t.Pad;}
    }
}
