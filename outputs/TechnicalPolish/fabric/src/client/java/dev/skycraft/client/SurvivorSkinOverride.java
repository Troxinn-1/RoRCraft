package dev.skycraft.client;

import dev.skycraft.SkyCraft;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.PlayerModelType;
import net.minecraft.world.entity.player.PlayerSkin;
import com.mojang.blaze3d.platform.NativeImage;
import java.nio.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** Local visual override only; profile lookup/export stays the authenticated default. */
public final class SurvivorSkinOverride {
    private static Identifier texture;
    private static PlayerModelType model;
    private static UUID owner;
    private static String applied;
    private static long stamp,next;
    private static String failure;
    public static void tick(Minecraft minecraft) {
        long now=System.nanoTime();if(now<next)return;next=now+250_000_000L;
        String mailbox=System.getProperty("skycraft.skinMailbox",""),session=System.getProperty("skycraft.skinSession","");
        if(mailbox.isEmpty())return;
        try {
            Path account=Path.of(new String(Base64.getDecoder().decode(mailbox),StandardCharsets.UTF_8));
            Path selected=account.resolveSibling("rorcraft-skin-selected.bin");
            if(!Files.exists(selected)) {clear(minecraft);return;}
            long changed=Files.getLastModifiedTime(selected).toMillis();
            UUID profile=minecraft.getUser().getProfileId();
            if(changed==stamp && profile.equals(owner))return;
            byte[] bytes=Files.readAllBytes(selected);
            if(bytes.length!=16504)throw new IllegalArgumentException("Invalid selected skin size");
            var data=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
            if(data.getInt()!=0x5243534B || data.getInt()!=1)throw new IllegalArgumentException("Selected skin version");
            byte[] token=new byte[32],id=new byte[36];data.get(token).get(id);
            if(!session.equals(new String(token,StandardCharsets.US_ASCII)) || !profile.toString().equals(new String(id,StandardCharsets.US_ASCII)))
                throw new IllegalArgumentException("Selected skin belongs to another launch/profile");
            int slim=data.getInt(),width=data.getInt(),height=data.getInt();
            if((slim!=0 && slim!=1)||width!=64||height!=64)throw new IllegalArgumentException("Selected skin model/dimensions");
            byte[] hash=new byte[32],pixels=new byte[16384];data.get(hash).get(pixels);
            if(!MessageDigest.isEqual(hash,MessageDigest.getInstance("SHA-256").digest(pixels)))throw new IllegalArgumentException("Selected skin integrity");
            String key=profile+"|"+HexFormat.of().formatHex(hash)+"|"+slim;
            stamp=changed;failure=null;if(key.equals(applied))return;
            Identifier replacement=Identifier.fromNamespaceAndPath("rorcraft","survivor/"+HexFormat.of().formatHex(hash));
            var image=new NativeImage(64,64,false);
            for(int y=0;y<64;y++)for(int x=0;x<64;x++) {
                int n=(y*64+x)*4;
                image.setPixel(x,y,((pixels[n+3]&255)<<24)|((pixels[n]&255)<<16)|((pixels[n+1]&255)<<8)|(pixels[n+2]&255));
            }
            var dynamic=new DynamicTexture(()->"RoRCraft selected survivor skin",image);
            minecraft.getTextureManager().register(replacement,dynamic);
            if(texture!=null && !texture.equals(replacement))minecraft.getTextureManager().release(texture);
            texture=replacement;owner=profile;model=slim==1?PlayerModelType.SLIM:PlayerModelType.WIDE;applied=key;
            Files.writeString(account.resolveSibling("rorcraft-skin-selected.ack"),session+"\n"+key,StandardCharsets.UTF_8);
            SkyCraft.LOG.info("RoRCraft survivor visual skin applied: {}",key);
        } catch(Exception error) {
            clear(minecraft);
            if(!error.getMessage().equals(failure)) {failure=error.getMessage();SkyCraft.LOG.warn("Selected skin rejected; account skin retained: {}",failure);}
        }
    }
    private static void clear(Minecraft minecraft) {
        if(texture!=null)minecraft.getTextureManager().release(texture);
        texture=null;model=null;owner=null;applied=null;stamp=0;
    }
    public static PlayerSkin resolve(UUID player,PlayerSkin original) {
        if(texture==null || !player.equals(owner))return original;
        // This is an already registered runtime texture, not a resource-pack asset id.
        // The one-argument constructor adds textures/ and .png, producing a missing
        // texture in inventory and dropping the entire exported F5 player mesh.
        return new PlayerSkin(new ClientAsset.ResourceTexture(texture,texture),original.cape(),original.elytra(),model,false);
    }
    private SurvivorSkinOverride() {}
}
