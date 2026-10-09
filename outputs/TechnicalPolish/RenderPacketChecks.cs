using System;
using RoRCraftPolish;
class RenderPacketChecks {
    static void Require(bool value,string message) {if(!value) throw new Exception(message);}
    static void Main() {
        var buffers=new RenderPacketBuffers();
        var pendingPlayer=buffers.Get(5,32);pendingPlayer[0]=11;
        var pendingScene=buffers.Get(6,32);pendingScene[0]=22;
        Require(!ReferenceEquals(pendingPlayer,pendingScene),"equal-length pending channels alias");
        var latestPlayer=buffers.Get(5,32);latestPlayer[0]=33;
        Require(ReferenceEquals(latestPlayer,pendingPlayer),"stable player packet allocated again");
        Require(pendingScene[0]==22,"coalescing player packet corrupted pending scene");
        var resized=buffers.Get(5,64);resized[0]=44;
        Require(resized.Length==64 && pendingScene[0]==22,"resize changed another pending channel");
        Require(buffers.AllocatedPayloadBytes==128 && buffers.AllocatedArrays==3,"allocation accounting mismatch");
        Require(buffers.Get(5,8).Length==8,"shrinking packet retains stale tail");
        var region=buffers.Get(7,1040);region[0]=55;
        buffers.Get(7,4112)[0]=66;
        Require(ReferenceEquals(region,buffers.Get(7,1040)) && region[0]==55,"mixed atlas region sizes allocate again or alias");
        Require(pendingScene[0]==22,"atlas region overwrote pending scene");
        bool rejected=false;try {buffers.Get(2,32);} catch(ArgumentOutOfRangeException) {rejected=true;}
        Require(rejected,"section packet unexpectedly pooled");
        Console.WriteLine("Render packet ownership checks passed: distinct pending channels, reuse, resize and exact lengths. Live renderer validation pending.");
    }
}
