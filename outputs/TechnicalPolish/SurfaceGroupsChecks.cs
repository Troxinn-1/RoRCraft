using System;
using System.Collections.Generic;
using RoRCraftPolish;
class SurfaceGroupsChecks {
    static void Check(bool condition,string label) {if(!condition) throw new Exception(label);Console.WriteLine("PASS "+label);}
    static void Main() {
        var groups=new List<int>[SurfaceGroups.Count];for(int i=0;i<groups.Length;i++) groups[i]=new List<int>();
        int[] flags={1,1|(1<<16),2|(2<<16),2|(3<<16),1|(4<<16),2,1|(7<<16),2|(6<<16)};
        var packet=new byte[16+flags.Length*3*32];
        for(int tri=0;tri<flags.Length;tri++) for(int v=0;v<3;v++) Buffer.BlockCopy(BitConverter.GetBytes(flags[tri]),0,packet,16+(tri*3+v)*32+28,4);
        SurfaceGroups.Partition(packet,16,flags.Length*3,groups);
        var indices=new HashSet<int>();int total=0;
        for(int g=0;g<groups.Length;g++) foreach(var index in groups[g]) {Check(indices.Add(index),"unique vertex index "+index);total++;}
        Check(total==flags.Length*3,"all triangles retained exactly once");
        Check(groups[0].Count==6 && groups[1].Count==3 && groups[2].Count==3 && groups[3].Count==3 && groups[4].Count==3 && groups[5].Count==3 && groups[6].Count==3,"mixed section category counts including stained glass; unknown bits fall back");
        Check(groups[1][0]==3 && groups[1][1]==5 && groups[1][2]==4,"host handedness/winding preserved");
        SurfaceGroups.Partition(packet,16,3,groups);Check(groups[0].Count==3 && groups[1].Count==0,"scratch buffers clear between sections");
        bool rejected=false;try{SurfaceGroups.Partition(packet,16,2,groups);}catch(ArgumentException){rejected=true;}Check(rejected,"partial triangle rejected");
        rejected=false;try{SurfaceGroups.Partition(packet,packet.Length-1,3,groups);}catch(ArgumentException){rejected=true;}Check(rejected,"truncated packet rejected");
        Check(SurfaceGroups.Category(1|0x70)==0 && SurfaceGroups.Category(2|0x30)==5,"legacy normal bits do not change category");
    }
}
