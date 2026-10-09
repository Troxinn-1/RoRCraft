package dev.skycraft;

import dev.skycraft.link.SkyLink;
import java.lang.foreign.ValueLayout;
import java.nio.file.*;
import java.io.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.block.Blocks;

/** Deletes only the dedicated void mirror's blocks, on a new host run, never on F8. */
public final class RoRRunReset {
    private static final Set<Long> touched=new LinkedHashSet<>();
    private static final ArrayDeque<Long> pending=new ArrayDeque<>();
    private static MinecraftServer previous;
    private static Path ledger;
    private static int token,generation=-1,removed;
    private static boolean clearing,failed;
    private RoRRunReset() {}
    public static void init() {ServerTickEvents.END_SERVER_TICK.register(RoRRunReset::tick);}
    public static long chunkKey(int x,int z) {return ((long)x<<32)|(z&0xffffffffL);}
    public static boolean isMirror(MinecraftServer server) {return !server.isDedicatedServer() && SkyCraft.WORLD_NAME.equals(server.getWorldData().getLevelName());}
    public static void changed(ServerLevel level,BlockPos pos) {
        if(clearing || !isMirror(level.getServer())) return;
        prepare(level.getServer());
        if(failed) return;
        long key=chunkKey(pos.getX()>>4,pos.getZ()>>4);
        if(touched.add(key)) try {Files.writeString(ledger,key+"\n",StandardOpenOption.CREATE,StandardOpenOption.APPEND);} catch(IOException e) {failed=true;SkyCraft.LOG.error("RoRCraft cannot record building chunk",e);}
    }
    private static void prepare(MinecraftServer server) {
        if(previous==server) return;
        previous=server;token=0;generation=-1;failed=false;pending.clear();touched.clear();
        ledger=server.getWorldPath(LevelResource.ROOT).resolve("rorcraft-built-chunks.txt");
        try {
            if(Files.exists(ledger)) {for(String line:Files.readAllLines(ledger)) if(!line.isBlank()) touched.add(Long.parseLong(line.trim()));}
            Path migration=server.getWorldPath(LevelResource.ROOT).resolve("rorcraft-reset-index-v2");
            if(!Files.exists(migration)) {
                // First migration also removes builds saved by older SkyCraft versions.
                touched.addAll(RoRRegionIndex.populatedChunks(server.getWorldPath(LevelResource.ROOT)));
                StringBuilder text=new StringBuilder();for(long key:touched) text.append(key).append('\n');Files.writeString(ledger,text);
                Files.writeString(migration,"Indexed legacy builds for reset v2\n");
            }
        } catch(IOException|NumberFormatException e) {failed=true;SkyCraft.LOG.error("RoRCraft cannot load building reset ledger",e);}
    }
    private static void tick(MinecraftServer server) {
        var s=SkyLink.segment();if(s==null || !SkyLink.active() || s.get(ValueLayout.JAVA_INT,0x944)!=0x31524F52) return;
        int requested=s.get(ValueLayout.JAVA_INT,0x940);if(requested==0) return;
        if(!isMirror(server)) {s.set(ValueLayout.JAVA_INT,0x9B4,-1);return;}
        prepare(server);
        if(failed) {s.set(ValueLayout.JAVA_INT,0x9B4,-2);return;}
        if(token!=requested || generation!=SkyLink.generation()) {
            token=requested;generation=SkyLink.generation();pending.clear();pending.addAll(touched);removed=0;
            s.set(ValueLayout.JAVA_INT,0x9B0,0);s.set(ValueLayout.JAVA_INT,0x9B4,pending.size());
            SkyCraft.LOG.info("RoRCraft new run: clearing {} mirror chunks",pending.size());
        }
        long deadline=System.nanoTime()+8_000_000L;
        ServerLevel level=server.overworld();clearing=true;
        try {
            while(!pending.isEmpty() && System.nanoTime()<deadline) {
                long key=pending.removeFirst();int cx=(int)(key>>32),cz=(int)key;
                var chunk=level.getChunk(cx,cz,ChunkStatus.FULL,true);
                var sections=chunk.getSections();var pos=new BlockPos.MutableBlockPos();
                for(int i=0;i<sections.length;i++) {
                    var section=sections[i];if(section.hasOnlyAir()) continue;
                    int base=chunk.getSectionYFromSectionIndex(i)*16;
                    for(int y=0;y<16;y++) for(int z=0;z<16;z++) for(int x=0;x<16;x++) if(!section.getBlockState(x,y,z).isAir()) {
                        pos.set(cx*16+x,base+y,cz*16+z);level.setBlock(pos,Blocks.AIR.defaultBlockState(),3);removed++;
                    }
                }
            }
        } catch(RuntimeException e) {failed=true;s.set(ValueLayout.JAVA_INT,0x9B4,-2);SkyCraft.LOG.error("RoRCraft mirror reset stopped",e);return;} finally {clearing=false;}
        s.set(ValueLayout.JAVA_INT,0x9B4,pending.size());
        if(pending.isEmpty() && s.get(ValueLayout.JAVA_INT,0x9B0)!=token) {
            // Dropped materials and arrows from the previous run should not reappear either.
            for(var entity:level.getAllEntities()) if(entity instanceof net.minecraft.world.entity.item.ItemEntity || entity instanceof net.minecraft.world.entity.projectile.arrow.AbstractArrow || entity instanceof net.minecraft.world.entity.item.FallingBlockEntity || entity instanceof net.minecraft.world.entity.item.PrimedTnt) entity.discard();
            s.set(ValueLayout.JAVA_INT,0x9B0,token);
            RoRGameplay.resourcesCleared(server);
            SkyCraft.LOG.info("RoRCraft new run: removed {} blocks; mirror ready",removed);
        }
    }
}
