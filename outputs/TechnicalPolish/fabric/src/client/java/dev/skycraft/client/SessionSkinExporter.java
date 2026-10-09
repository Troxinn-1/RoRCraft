package dev.skycraft.client.render;

import dev.skycraft.SkyCraft;
import net.minecraft.client.Minecraft;
import net.minecraft.world.entity.player.PlayerSkin;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.Base64;
import java.util.UUID;
import java.util.function.Supplier;

/** Only the authenticated local Minecraft profile; never a package author's PNG. */
public final class SessionSkinExporter {
    private static Supplier<PlayerSkin> lookup;
    private static UUID profile;
    private static String published;
    private static long next;
    private static boolean warned;
    public static void tick(Minecraft minecraft) {
        String destination=System.getProperty("skycraft.skinMailbox",""),session=System.getProperty("skycraft.skinSession","");
        if(destination.isEmpty() || !session.matches("[0-9a-f]{32}")) return;
        long now=System.nanoTime();if(now<next) return;next=now+1_000_000_000L;
        try {
            UUID account=minecraft.getUser().getProfileId();
            if(lookup==null || !account.equals(profile)) {
                profile=account;lookup=minecraft.getSkinManager().createLookup(minecraft.getGameProfile(),false);published=null;
            }
            // Survivor getSkin overrides must never become the account default.
            PlayerSkin skin=lookup.get();
            if(skin==null) return;
            var location=skin.body().texturePath();
            int slim=skin.model().toString().equalsIgnoreCase("SLIM")?1:0;
            String key=account+"|"+location+"|"+slim;
            if(key.equals(published)) return;
            try(var image=AvatarExporter.readTexture(location)) {
                if(image==null) return;
                if(image.getWidth()!=64 || image.getHeight()!=64) throw new IllegalStateException("Expected normalized Minecraft 64x64 skin");
                byte[] pixels=new byte[64*64*4];int p=0;
                for(int y=0;y<64;y++) for(int x=0;x<64;x++) {
                    int argb=image.getPixel(x,y);pixels[p++]=(byte)(argb>>16);pixels[p++]=(byte)(argb>>8);
                    pixels[p++]=(byte)argb;pixels[p++]=(byte)(argb>>>24);
                }
                byte[] hash=MessageDigest.getInstance("SHA-256").digest(pixels);
                ByteBuffer bytes=ByteBuffer.allocate(8+32+36+12+32+pixels.length).order(ByteOrder.LITTLE_ENDIAN);
                bytes.putInt(0x5243534B).putInt(1).put(session.getBytes(StandardCharsets.US_ASCII));
                bytes.put(account.toString().getBytes(StandardCharsets.US_ASCII));
                bytes.putInt(slim).putInt(64).putInt(64).put(hash).put(pixels);
                Path target=Path.of(new String(Base64.getDecoder().decode(destination),StandardCharsets.UTF_8));
                Files.createDirectories(target.getParent());Path temporary=target.resolveSibling(target.getFileName()+".tmp");
                Files.write(temporary,bytes.array());
                // Unsupported atomic replacement fails closed; never expose a partial payload.
                Files.move(temporary,target,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);
                published=key;warned=false;
                SkyCraft.LOG.info("RoRCraft local profile skin published: model={} texture={}",skin.model(),location);
            }
        } catch(Exception error) {
            if(!warned) {warned=true;SkyCraft.LOG.warn("RoRCraft profile skin unavailable; native lobby fallback retained",error);}
        }
    }
    private SessionSkinExporter() {}
}
