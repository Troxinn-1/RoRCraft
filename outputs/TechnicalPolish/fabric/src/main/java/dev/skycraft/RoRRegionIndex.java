package dev.skycraft;
import java.nio.file.*;
import java.io.*;
import java.util.*;
import java.util.zip.*;
import net.minecraft.nbt.NbtIo;
import net.minecraft.nbt.NbtAccounter;
/** Read-only migration index: both legacy worlds and Minecraft 26.3's dimension layout. */
public final class RoRRegionIndex {
    private RoRRegionIndex() {}
    public static Set<Long> chunks(Path world) throws IOException {
        Set<Long> result=new LinkedHashSet<>();
        for(Path folder:List.of(world.resolve("region"),world.resolve("dimensions/minecraft/overworld/region"))) {
            if(!Files.isDirectory(folder)) continue;
            try(var files=Files.list(folder)) {
                for(Path file:files.filter(p->p.getFileName().toString().matches("r\\.-?\\d+\\.-?\\d+\\.mca")).toList()) {
                    String[] names=file.getFileName().toString().split("\\.");int rx=Integer.parseInt(names[1]),rz=Integer.parseInt(names[2]);
                    try(var in=new DataInputStream(Files.newInputStream(file))) {
                        for(int i=0;i<1024;i++) if(in.readInt()!=0) result.add(((long)(rx*32+i%32)<<32)|((rz*32+i/32)&0xffffffffL));
                    }
                }
            }
        }
        return result;
    }
    public static Set<Long> populatedChunks(Path world) throws IOException {
        Set<Long> populated=new LinkedHashSet<>();
        for(long key:chunks(world)) {
            int x=(int)(key>>32),z=(int)key;String name="r."+Math.floorDiv(x,32)+"."+Math.floorDiv(z,32)+".mca";
            Path file=world.resolve("dimensions/minecraft/overworld/region").resolve(name);
            if(!Files.exists(file)) file=world.resolve("region").resolve(name);
            try(var in=new RandomAccessFile(file.toFile(),"r")) {
                in.seek((Math.floorMod(x,32)+Math.floorMod(z,32)*32)*4L);int location=in.readInt();
                if(location==0) continue;
                in.seek((location>>>8)*4096L);int length=in.readInt();int compression=in.readUnsignedByte();
                // External chunks and unfamiliar compression are conservatively cleared by the server.
                if((compression&128)!=0 || compression<1 || compression>3) {populated.add(key);continue;}
                if(length<2 || length>16*1024*1024) throw new IOException("Invalid chunk size in "+file);
                byte[] bytes=new byte[length-1];in.readFully(bytes);
                InputStream data=new ByteArrayInputStream(bytes);
                if(compression==1) data=new GZIPInputStream(data);else if(compression==2) data=new InflaterInputStream(data);
                try(var tags=new DataInputStream(data)) {
                    var nbt=NbtIo.read(tags,NbtAccounter.create(32*1024*1024));
                    var sections=nbt.getListOrEmpty("sections");
                    if(sections.isEmpty()) {populated.add(key);continue;} // Unknown legacy encoding: do not miss builds.
                    boolean nonAir=false;
                    for(var section:sections.compoundStream().toList()) {
                        var palette=section.getCompoundOrEmpty("block_states").getListOrEmpty("palette");
                        for(var state:palette.compoundStream().toList()) {
                            String block=state.getStringOr("Name","");
                            if(!block.equals("minecraft:air") && !block.equals("minecraft:cave_air") && !block.equals("minecraft:void_air")) nonAir=true;
                        }
                    }
                    if(nonAir) populated.add(key);
                }
            }
        }
        return populated;
    }
}
