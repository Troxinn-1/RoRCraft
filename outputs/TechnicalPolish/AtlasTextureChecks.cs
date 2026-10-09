// Synthetic texture backend exercises the production atlas update/lifetime code.
// It proves CPU data and ownership contracts, not Unity GPU behavior or visuals.
using System;
using System.IO;
using RoRCraftPolish;
using UnityEngine;
using Object=UnityEngine.Object;
namespace UnityEngine.Rendering {public enum CopyTextureSupport {None=0,Basic=1}}
namespace UnityEngine {
    public enum TextureFormat {RGBA32} public enum FilterMode {Point} public enum TextureWrapMode {Clamp}
    public class Object {
        internal bool Destroyed;
        public static int Alive;
        public static void Destroy(Object o) {if(o!=null && !o.Destroyed) {o.Destroyed=true;Alive--;}}
    }
    public sealed class Texture2D:Object {
        public int width,height,mipmapCount,anisoLevel;public string name;public FilterMode filterMode;public TextureWrapMode wrapMode;
        public byte[][] Pixels;
        public Texture2D(int w,int h,TextureFormat format,int levels,bool linear) {width=w;height=h;mipmapCount=levels;Pixels=new byte[levels][];for(int i=0;i<levels;i++) Pixels[i]=new byte[(w>>i)*(h>>i)*4];Alive++;}
        public byte[] GetRawTextureData() {if(Destroyed) throw new Exception("Read disposed texture");return (byte[])Pixels[0].Clone();}
        public void SetPixelData<T>(T[] data,int level,int offset) {if(Destroyed) throw new Exception("Write disposed texture");var bytes=(byte[])(object)data;if(bytes.Length!=Pixels[level].Length) throw new Exception("Incorrect upload length");Buffer.BlockCopy(bytes,offset,Pixels[level],0,Pixels[level].Length);}
        public void Apply(bool mip,bool unreadable) {if(mip || unreadable) throw new Exception("Unexpected auto-mips or unreadable staging texture");}
    }
    public static class SystemInfo {public static Rendering.CopyTextureSupport copyTextureSupport;}
    public static class Graphics {
        public static void CopyTexture(Texture2D from,int element,int mip,int sx,int sy,int w,int h,Texture2D to,int targetElement,int targetMip,int tx,int ty) {
            if(from.Destroyed || to.Destroyed) throw new Exception("Copy disposed texture");
            for(int y=0;y<h;y++) Buffer.BlockCopy(from.Pixels[mip],((sy+y)*(from.width>>mip)+sx)*4,to.Pixels[targetMip],((ty+y)*(to.width>>targetMip)+tx)*4,w*4);
        }
    }
}
class AtlasTextureChecks {
    static void Check(bool value,string name) {if(!value) throw new Exception(name);Console.WriteLine("PASS "+name);}
    static AtlasTiles Layout() {using(var s=new MemoryStream()) using(var w=new BinaryWriter(s)) {foreach(int v in new[]{0x50494d53,8,4,2,1,0,0,4,4,0,4,0,4,4,0}) w.Write(v);return new AtlasTiles(s.ToArray());}}
    static byte[] Packet(int x,int y,int width,int height,byte red) {using(var s=new MemoryStream()) using(var w=new BinaryWriter(s)) {w.Write(x);w.Write(y);w.Write(width);w.Write(height);for(int i=0;i<width*height;i++) {w.Write(red);w.Write((byte)0);w.Write((byte)0);w.Write((byte)255);}return s.ToArray();}}
    static Texture2D Original() {return new Texture2D(8,4,TextureFormat.RGBA32,1,false);}
    static void Path(UnityEngine.Rendering.CopyTextureSupport support) {
        SystemInfo.copyTextureSupport=support;
        var source=Original();var atlas=new AtlasTexture(Layout(),source,true);
        var rows=Packet(4,0,1,2,111);rows[20]=112;
        atlas.Region(rows);atlas.Region(Packet(0,0,4,4,255));atlas.Flush();
        Check(source.Pixels[0][(3*8+4)*4]==111 && source.Pixels[0][(2*8+4)*4]==112,"item overlay animation + top-origin rows: "+support);
        Check(atlas.Texture.Pixels[0][(3*8+4)*4]==111,"block/overlay level-zero agreement: "+support);
        Check(atlas.Texture.Pixels[2][0]==255 && atlas.Texture.Pixels[2][3]==255,"animated sprite mip rebuild: "+support);
        long uploads=atlas.Uploads;atlas.Flush();Check(atlas.Uploads==uploads,"idle frame does not upload: "+support);
        atlas.Region(Packet(7,0,2,1,90));atlas.Region(null);atlas.Region(new byte[3]);atlas.Flush();Check(atlas.Regions==2,"malformed patches rejected: "+support);
        var old=atlas.SelectMipmaps(false);Object.Destroy(old);Check(atlas.Texture.mipmapCount==1 && atlas.Texture.Pixels[0][(3*8+4)*4]==111,"toggle preserves current animation: "+support);
        old=atlas.SelectMipmaps(true);Object.Destroy(old);Check(atlas.Texture.mipmapCount==3 && atlas.Texture.Pixels[2][0]==255,"toggle restores current mip pixels: "+support);
        // Same level-zero texture survives a descriptor rebuild; temporary atlas dies.
        var replacement=new AtlasTexture(Layout(),source,true);atlas.Dispose(false);
        Check(!source.Destroyed && replacement.Texture.Pixels[0][(3*8+4)*4]==111,"descriptor replacement retains current source: "+support);
        replacement.Dispose(true);Check(Object.Alive==0,"all textures and staging patches released: "+support);
    }
    static void Main() {
        Path(UnityEngine.Rendering.CopyTextureSupport.None);Path(UnityEngine.Rendering.CopyTextureSupport.Basic);
        var layout=Layout();var pixels=new byte[8*4*4];for(int i=0;i<4*4;i++) {int p=((i/4)*8+i%4)*4;pixels[p]=255;pixels[p+3]=255;}
        var mips=layout.Create(pixels);Array.Clear(pixels,0,pixels.Length);layout.Build(mips,layout.Tiles[0]);
        Check(mips[2][0]==0 && mips[2][3]==0,"opaque-to-transparent animation clears stale RGB and alpha");
        for(int i=0;i<100;i++) {var a=new AtlasTexture(Layout(),Original(),true);a.Region(Packet(0,0,4,4,255));a.Flush();a.Dispose(true);}
        Check(Object.Alive==0,"100 atlas lifecycles retain no owned textures");
        Console.WriteLine("Synthetic backend only: actual Unity/GPU and visual acceptance still required.");
    }
}
