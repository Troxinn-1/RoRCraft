package dev.skycraft;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.nio.file.*;
import java.io.*;
import java.util.zip.DeflaterOutputStream;
import net.minecraft.nbt.*;
import static org.junit.jupiter.api.Assertions.*;
class RoRRegionIndexTest {
    @TempDir Path root;
    private void region(String directory,String name,int index) throws IOException {
        Path folder=root.resolve(directory);Files.createDirectories(folder);
        try(var out=new DataOutputStream(Files.newOutputStream(folder.resolve(name)))) {for(int i=0;i<1024;i++) out.writeInt(i==index?513:0);}
    }
    @Test void indexesNewDimensionLayoutAndNegativeCoordinates() throws Exception {
        region("dimensions/minecraft/overworld/region","r.-1.-2.mca",1023);
        assertEquals(java.util.Set.of(((long)-1<<32)|(-33&0xffffffffL)),RoRRegionIndex.chunks(root));
    }
    @Test void legacyWorldWorksAndEmptyChunksAreSkipped() throws Exception {
        region("region","r.0.0.mca",0);assertEquals(java.util.Set.of(0L),RoRRegionIndex.chunks(root));
    }
    @Test void missingRegionFolderIsAnEmptyMirror() throws Exception {assertTrue(RoRRegionIndex.chunks(root).isEmpty());}
    @Test void netherAndOtherWorldsAreNotIncluded() throws Exception {
        region("dimensions/minecraft/the_nether/region","r.0.0.mca",0);assertTrue(RoRRegionIndex.chunks(root).isEmpty());
    }
    @Test void truncatedRegionIsReportedRatherThanSilentlySkipped() throws Exception {
        Path p=root.resolve("region");Files.createDirectories(p);Files.write(p.resolve("r.0.0.mca"),new byte[10]);assertThrows(EOFException.class,()->RoRRegionIndex.chunks(root));
    }
    private void storedChunk(String block) throws Exception {
        var state=new CompoundTag();state.putString("Name",block);
        var palette=new ListTag();palette.add(state);var blockStates=new CompoundTag();blockStates.put("palette",palette);
        var section=new CompoundTag();section.put("block_states",blockStates);var sections=new ListTag();sections.add(section);
        var chunk=new CompoundTag();chunk.put("sections",sections);
        var compressed=new ByteArrayOutputStream();try(var out=new DataOutputStream(new DeflaterOutputStream(compressed))) {NbtIo.write(chunk,out);}
        Path folder=root.resolve("dimensions/minecraft/overworld/region");Files.createDirectories(folder);
        try(var out=new RandomAccessFile(folder.resolve("r.0.0.mca").toFile(),"rw")) {
            out.setLength(12288);out.writeInt(513);out.seek(8192);out.writeInt(compressed.size()+1);out.writeByte(2);out.write(compressed.toByteArray());
        }
    }
    @Test void migrationSkipsStoredVoidAirChunks() throws Exception {storedChunk("minecraft:air");assertTrue(RoRRegionIndex.populatedChunks(root).isEmpty());}
    @Test void migrationIncludesStoredBuildingBlocks() throws Exception {storedChunk("minecraft:stone_bricks");assertEquals(java.util.Set.of(0L),RoRRegionIndex.populatedChunks(root));}
    @Test void migrationIncludesGlassEvenThoughItIsTransparent() throws Exception {storedChunk("minecraft:glass");assertEquals(java.util.Set.of(0L),RoRRegionIndex.populatedChunks(root));}
}
