using System;
using System.IO;
using RoRCraftPolish;
class AtlasTilesChecks {
    static void Check(bool condition,string name) {if(!condition) throw new Exception(name);Console.WriteLine("PASS "+name);}
    static byte[] Layout(int width,int height,int[][] rects) {using(var s=new MemoryStream()) using(var w=new BinaryWriter(s)) {w.Write(0x50494d53);w.Write(width);w.Write(height);w.Write(rects.Length);w.Write(rects.Length);foreach(var r in rects) foreach(int v in r) w.Write(v);return s.ToArray();}}
    static void Main() {
        var layout=new AtlasTiles(Layout(8,4,new[]{new[]{0,0,4,4,0},new[]{4,0,4,4,0}}));
        Check(layout.Levels==2,"aligned independent sprites permit two mip levels");
        var original=new byte[8*4*4];for(int y=0;y<4;y++) for(int x=0;x<8;x++) {int o=(y*8+x)*4;original[o+(x<4?0:2)]=255;original[o+3]=255;}
        var mips=layout.Create(original);
        Check(Object.ReferenceEquals(mips[0],original),"level zero retained bit for bit");
        for(int level=1;level<=2;level++) {int w=8>>level;for(int y=0;y<(4>>level);y++) for(int x=0;x<w;x++) {int o=(y*w+x)*4;Check(mips[level][o]==(x<w/2?255:0) && mips[level][o+2]==(x<w/2?0:255),"no red/blue neighbor bleeding mip "+level+" texel "+x+","+y);}}
        original[0]=0;original[1]=255;original[2]=0;original[3]=0;layout.Build(mips,layout.Tiles[0]);Check(mips[1][1]==0,"transparent RGB does not contaminate opaque edges");
        Check(layout.Intersects(layout.Tiles[0],0,0,1,1) && !layout.Intersects(layout.Tiles[1],0,0,1,1),"animated patch rebuild selects only touched sprite");
        var odd=new AtlasTiles(Layout(8,4,new[]{new[]{1,0,3,4,0}}));Check(odd.Levels==0,"unsupported alignment safely disables mip levels");
        var mixedPacket=Layout(8,4,new[]{new[]{0,0,4,4,0},new[]{5,1,1,1,0}});
        Buffer.BlockCopy(BitConverter.GetBytes(1),0,mixedPacket,16,4);
        var mixed=new AtlasTiles(mixedPacket);Check(mixed.Levels==2 && !mixed.Tiles[1].Filtered,"item atlas irregular padding does not reduce block mip levels");
        var top=new AtlasTiles(Layout(8,8,new[]{new[]{0,0,4,4,0}}));Check(top.Tiles[0].Y==4,"Minecraft top origin converted to Unity bottom origin");
        bool rejected=false;try {new AtlasTiles(Layout(8,4,new[]{new[]{7,0,4,4,0}}));}catch(InvalidDataException){rejected=true;}Check(rejected,"out-of-bounds sprite rejected");
        var packet=Layout(8,4,new[]{new[]{0,0,4,4,0}});Array.Resize(ref packet,packet.Length-1);rejected=false;try {new AtlasTiles(packet);}catch(InvalidDataException){rejected=true;}Check(rejected,"truncated descriptor rejected");
    }
}
