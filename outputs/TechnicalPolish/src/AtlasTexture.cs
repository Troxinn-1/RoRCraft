using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoRCraftPolish {
    internal sealed class AtlasTexture {
        internal readonly AtlasTiles Layout;
        internal Texture2D Texture;
        internal readonly Texture2D NoMipTexture;
        private readonly byte[][] mips;
        private readonly bool[] changed;
        private sealed class Patch {internal Texture2D Texture;internal byte[][] Pixels;internal int W,H;}
        private readonly Patch[] patches;
        private bool dirty;
        internal long Regions,Uploads;
        internal double LastWorkMs,MaxWorkMs;
        internal bool Enabled;
        internal AtlasTexture(AtlasTiles layout,Texture2D original,bool enabled) {
            Layout=layout;NoMipTexture=original;mips=layout.Create(original.GetRawTextureData());changed=new bool[layout.Tiles.Length];patches=new Patch[layout.Tiles.Length];
            Enabled=enabled;CreateTexture();
        }
        private void CreateTexture() {
            Texture=new Texture2D(Layout.Width,Layout.Height,TextureFormat.RGBA32,Enabled?Layout.Levels+1:1,false);
            Texture.name="Minecraft sprite-safe atlas";Texture.filterMode=FilterMode.Point;Texture.wrapMode=TextureWrapMode.Clamp;Texture.anisoLevel=1;
            Upload();
        }
        internal Texture2D SelectMipmaps(bool enabled) {var old=Texture;Enabled=enabled;CreateTexture();return old;}
        internal void Region(byte[] packet) {
            if(packet==null || packet.Length<16) return;
            int x=BitConverter.ToInt32(packet,0),top=BitConverter.ToInt32(packet,4),w=BitConverter.ToInt32(packet,8),h=BitConverter.ToInt32(packet,12);
            if(x<0 || top<0 || w<1 || h<1 || (long)x+w>Layout.Width || (long)top+h>Layout.Height || (long)w*h*4+16!=packet.Length) return;
            int y=Layout.Height-top-h;
            for(int row=0;row<h;row++) Buffer.BlockCopy(packet,16+row*w*4,mips[0],((y+h-1-row)*Layout.Width+x)*4,w*4);
            for(int i=0;i<changed.Length;i++) if(Layout.Intersects(Layout.Tiles[i],x,y,w,h)) changed[i]=true;
            dirty=true;Regions++;
        }
        internal void Flush() {
            if(!dirty || Texture==null) return;dirty=false;
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            bool copy=(SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic)!=0;
            for(int i=0;i<changed.Length;i++) if(changed[i]) {Layout.Build(mips,Layout.Tiles[i]);if(copy) UploadPatch(i);changed[i]=false;}
            if(!copy) {
                Upload();
                // Items use the separate level-zero atlas even without GPU copies.
                NoMipTexture.SetPixelData<byte>(mips[0],0,0);NoMipTexture.Apply(false,false);
            } else Uploads++;
            LastWorkMs=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency;
            MaxWorkMs=Math.Max(MaxWorkMs,LastWorkMs);
        }
        private void UploadPatch(int index) {
            var tile=Layout.Tiles[index];var patch=patches[index];int levels=tile.Filtered?Layout.Levels+1:1;
            if(patch==null) {
                patch=new Patch{W=tile.W+2*tile.Pad,H=tile.H+2*tile.Pad,Pixels=new byte[levels][]};
                patch.Texture=new Texture2D(patch.W,patch.H,TextureFormat.RGBA32,levels,false);
                patch.Texture.name="Minecraft animated atlas staging";
                for(int level=0;level<levels;level++) patch.Pixels[level]=new byte[(patch.W>>level)*(patch.H>>level)*4];
                patches[index]=patch;
            }
            for(int level=0;level<levels;level++) {
                int w=patch.W>>level,h=patch.H>>level,x=(tile.X-tile.Pad)>>level,y=(tile.Y-tile.Pad)>>level,atlasW=Layout.Width>>level;
                for(int row=0;row<h;row++) Buffer.BlockCopy(mips[level],((y+row)*atlasW+x)*4,patch.Pixels[level],row*w*4,w*4);
                patch.Texture.SetPixelData<byte>(patch.Pixels[level],level,0);
            }
            patch.Texture.Apply(false,false);
            for(int level=0;level<Math.Min(levels,Texture.mipmapCount);level++) Graphics.CopyTexture(patch.Texture,0,level,0,0,patch.W>>level,patch.H>>level,Texture,0,level,(tile.X-tile.Pad)>>level,(tile.Y-tile.Pad)>>level);
            Graphics.CopyTexture(patch.Texture,0,0,0,0,patch.W,patch.H,NoMipTexture,0,0,tile.X-tile.Pad,tile.Y-tile.Pad);
        }
        internal void DisposePatches() {foreach(var patch in patches) if(patch!=null) UnityEngine.Object.Destroy(patch.Texture);}
        internal void Dispose(bool destroyOriginal) {
            DisposePatches();
            if(Texture!=null) UnityEngine.Object.Destroy(Texture);
            if(destroyOriginal && NoMipTexture!=null) UnityEngine.Object.Destroy(NoMipTexture);
        }
        private void Upload() {for(int i=0;i<Texture.mipmapCount;i++) Texture.SetPixelData<byte>(mips[i],i,0);Texture.Apply(false,false);Uploads++;}
    }
}
