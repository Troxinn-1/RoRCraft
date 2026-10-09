using System;
using System.Collections.Generic;

namespace RoRCraftPolish {
    // Both channels can be pending in DrainRender simultaneously, even with equal
    // lengths. They must not alias. Consumer finishes synchronously before reuse.
    public sealed class RenderPacketBuffers {
        private byte[] player,scene;
        private readonly Dictionary<int,byte[]> atlasRegions=new Dictionary<int,byte[]>();
        private int regionCacheBytes;
        public long AllocatedPayloadBytes,AllocatedArrays;
        public byte[] Get(int type,int length) {
            if(type!=5 && type!=6 && type!=7) throw new ArgumentOutOfRangeException("type");
            if(length<0) throw new ArgumentOutOfRangeException("length");
            if(type==7) {
                byte[] region;
                if(atlasRegions.TryGetValue(length,out region)) return region;
                region=Allocate(length);
                if(atlasRegions.Count<32 && length<=1024*1024-regionCacheBytes) {atlasRegions.Add(length,region);regionCacheBytes+=length;}
                return region;
            }
            if(type==5) {if(player==null || player.Length!=length) player=Allocate(length);return player;}
            if(scene==null || scene.Length!=length) scene=Allocate(length);return scene;
        }
        private byte[] Allocate(int length) {var result=new byte[length];AllocatedPayloadBytes+=length;AllocatedArrays++;return result;}
    }
}
