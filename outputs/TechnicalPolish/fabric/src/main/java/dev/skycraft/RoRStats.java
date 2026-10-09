package dev.skycraft;

import java.lang.foreign.MemorySegment;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.VarHandle;

/** Committed host snapshot; no Minecraft objects are touched during parsing. */
public record RoRStats(float maximum,float health,float speed,float attack,float shield) {
    public static RoRStats read(MemorySegment s) {
        final long p=0x900;
        if(s.byteSize()<p+32) return null;
        int sequence=s.get(ValueLayout.JAVA_INT,p);
        if((sequence&1)!=0 || s.get(ValueLayout.JAVA_INT,p+4)!=0x31524F52 || s.get(ValueLayout.JAVA_INT,p+8)==0) return null;
        VarHandle.loadLoadFence();
        var out=new RoRStats(s.get(ValueLayout.JAVA_FLOAT,p+12),s.get(ValueLayout.JAVA_FLOAT,p+16),s.get(ValueLayout.JAVA_FLOAT,p+20),s.get(ValueLayout.JAVA_FLOAT,p+24),s.get(ValueLayout.JAVA_FLOAT,p+28));
        VarHandle.loadLoadFence();
        if(sequence!=s.get(ValueLayout.JAVA_INT,p)) return null;
        if(!Float.isFinite(out.maximum)||out.maximum<1||out.maximum>1024 || !Float.isFinite(out.health)||out.health<0||out.health>out.maximum || !Float.isFinite(out.speed)||out.speed<=0 || !Float.isFinite(out.attack)||out.attack<=0 || !Float.isFinite(out.shield)||out.shield<0) return null;
        return out;
    }
}
