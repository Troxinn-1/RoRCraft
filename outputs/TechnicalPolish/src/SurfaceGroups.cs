using System;
using System.Collections.Generic;

namespace RoRCraftPolish {
    internal static class SurfaceGroups {
        internal const int Count=7; // matte, metal, clear glass, fluid, emissive, legacy transparent, stained glass
        internal static int Category(int flags) {
            int category=(flags>>16)&7;
            if(category>6) category=0;
            return category==0 && (flags&2)!=0 ? 5:category;
        }
        internal static void Partition(byte[] bytes,int offset,int vertices,List<int>[] groups) {
            if(bytes==null || vertices<0 || vertices%3!=0 || offset<0 || (long)offset+vertices*32>bytes.Length)
                throw new ArgumentException("Invalid surface vertex packet.");
            for(int g=0;g<Count;g++) groups[g].Clear();
            for(int i=0;i<vertices;i+=3) {
                int flags=BitConverter.ToInt32(bytes,offset+i*32+28);
                var group=groups[Category(flags)];
                group.Add(i);group.Add(i+2);group.Add(i+1);
            }
        }
    }
}
