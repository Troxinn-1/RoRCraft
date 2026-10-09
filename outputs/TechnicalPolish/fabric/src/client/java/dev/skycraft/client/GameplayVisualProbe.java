package dev.skycraft.client;

import java.nio.file.*;
import java.util.Base64;
import net.minecraft.client.Minecraft;
import net.minecraft.client.CameraType;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.block.Blocks;

/** Isolated regression scenario, no OS input and never enabled in a normal launch. */
final class GameplayVisualProbe {
    private static String current="";
    private static long started;
    private static BlockPos block;
    private static long lastMine;
    static void frame(Minecraft mc) {
        if(!Boolean.getBoolean("skycraft.gameplayLiveProbe") || !"SkyCraft-GameplayTest".equals(System.getProperty("skycraft.polishWorld")) || mc.player==null || mc.level==null)return;
        try {
            Path folder=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent();
            Path request=folder.resolve("rorcraft-visual-request.txt"),done=folder.resolve("rorcraft-visual-done.txt");
            if(!Files.exists(request))return;
            String next=Files.readString(request).trim();
            if(!next.equals(current)) {
                current=next;started=System.nanoTime();mc.options.keyAttack.setDown(false);mc.gui.setScreen(null);mc.gameMode.stopDestroyBlock();
                mc.options.setCameraType(next.equals("back")?CameraType.THIRD_PERSON_BACK:next.equals("front")?CameraType.THIRD_PERSON_FRONT:CameraType.FIRST_PERSON);
                if(next.equals("inventory"))mc.gui.setScreen(new InventoryScreen(mc.player));
                if(next.equals("armor")) {
                    var uuid=mc.player.getUUID();mc.getSingleplayerServer().execute(()->{
                        var p=mc.getSingleplayerServer().getPlayerList().getPlayer(uuid);
                        p.setItemSlot(net.minecraft.world.entity.EquipmentSlot.CHEST,new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.IRON_CHESTPLATE));
                    });
                }
                if(next.equals("mining")) {
                    mc.player.getInventory().setSelectedSlot(1); // actual survival wooden pickaxe
                    block=mc.player.blockPosition().offset(0,1,2);
                    var uuid=mc.player.getUUID();
                    mc.getSingleplayerServer().execute(()->{var p=mc.getSingleplayerServer().getPlayerList().getPlayer(uuid);p.level().setBlock(block,Blocks.STONE.defaultBlockState(),3);});
                }
            }
            long elapsed=System.nanoTime()-started;
            if(current.equals("mining") && elapsed>700_000_000L) {
                var delta=net.minecraft.world.phys.Vec3.atCenterOf(block).subtract(mc.player.getEyePosition());
                mc.player.setYRot((float)Math.toDegrees(Math.atan2(-delta.x,delta.z)));
                mc.player.setXRot((float)-Math.toDegrees(Math.atan2(delta.y,Math.sqrt(delta.x*delta.x+delta.z*delta.z))));
                mc.options.keyAttack.setDown(true);
                if(System.nanoTime()-lastMine>50_000_000L) {
                    lastMine=System.nanoTime();
                    if(!mc.gameMode.isDestroying())mc.gameMode.startDestroyBlock(block,Direction.NORTH);
                    else mc.gameMode.continueDestroyBlock(block,Direction.NORTH);
                }
            }
            if(elapsed>2_000_000_000L && (!current.equals("mining") || mc.gameMode.getDestroyStage()>=1) && !Files.exists(done)) {
                var skin=mc.player.getSkin();
                var location=skin.body().texturePath();
                try(var pixels=dev.skycraft.client.render.AvatarExporter.readTexture(location)) {
                    if(pixels==null)throw new IllegalStateException("Player texture missing: "+location);
                }
                if(current.equals("first") && block!=null && !mc.level.getBlockState(block).isAir())throw new IllegalStateException("Mined block is still present");
                String detail=current.equals("mining")?" stage="+mc.gameMode.getDestroyStage()+" grounded="+mc.player.onGround()+" tool="+mc.player.getMainHandItem()+" pos="+block.getX()+","+block.getY()+","+block.getZ():" texture="+location;
                Files.writeString(done,"PASS "+current+detail);
            }
        } catch(Exception error) { dev.skycraft.SkyCraft.LOG.error("Visual regression probe",error); }
    }
}
