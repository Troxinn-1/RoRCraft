package dev.skycraft;
import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.ValueLayout;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
public class RoRStatsTest {
    private MemorySegment snapshot(Arena arena) {
        var m=arena.allocate(0x1000,8);m.set(ValueLayout.JAVA_INT,0x900,2);m.set(ValueLayout.JAVA_INT,0x904,0x31524F52);m.set(ValueLayout.JAVA_INT,0x908,1);
        m.set(ValueLayout.JAVA_FLOAT,0x90C,110);m.set(ValueLayout.JAVA_FLOAT,0x910,75);m.set(ValueLayout.JAVA_FLOAT,0x914,.115f);m.set(ValueLayout.JAVA_FLOAT,0x918,1.15f);m.set(ValueLayout.JAVA_FLOAT,0x91C,20);return m;
    }
    @Test void retainsOriginalRoRHealthInsteadOfTwentyPointPool() {try(var a=Arena.ofConfined()){var s=RoRStats.read(snapshot(a));assertNotNull(s);assertEquals(110,s.maximum());assertEquals(75,s.health());assertEquals(20,s.shield());assertEquals(1.15f,s.attack());}}
    @Test void rejectsUncommittedWriter() {try(var a=Arena.ofConfined()){var s=snapshot(a);s.set(ValueLayout.JAVA_INT,0x900,3);assertNull(RoRStats.read(s));}}
    @Test void ignoresInactiveHost() {try(var a=Arena.ofConfined()){var s=snapshot(a);s.set(ValueLayout.JAVA_INT,0x908,0);assertNull(RoRStats.read(s));}}
    @Test void rejectsNonfiniteHealth() {try(var a=Arena.ofConfined()){var s=snapshot(a);s.set(ValueLayout.JAVA_FLOAT,0x910,Float.NaN);assertNull(RoRStats.read(s));}}
    @Test void refusesHealthAboveMaximum() {try(var a=Arena.ofConfined()){var s=snapshot(a);s.set(ValueLayout.JAVA_FLOAT,0x910,200);assertNull(RoRStats.read(s));}}
    @Test void wrongMagicCannotConfigureMinecraft() {try(var a=Arena.ofConfined()){var s=snapshot(a);s.set(ValueLayout.JAVA_INT,0x904,0);assertNull(RoRStats.read(s));}}
    @Test void smallMappingIsRejectedWithoutRead() {try(var a=Arena.ofConfined()){assertNull(RoRStats.read(a.allocate(64)));}}
}
