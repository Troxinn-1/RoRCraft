package dev.skycraft;

import dev.skycraft.link.SkyLink;
import dev.skycraft.world.SkyClip;
import dev.skycraft.world.SkyRay;
import java.lang.foreign.ValueLayout;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.Vec3;

/** First playable resource loop. Vanilla owns inventory, mining, recipes and drops. */
public final class RoRGameplay {
    private static final String RUN="rorcraft_inventory_run_", NODE="rorcraft_resource_";
    private static final SkyLink.SkyState state=new SkyLink.SkyState();
    private static final Map<String,BlockPos> anchors=new HashMap<>();
    static final Map<Integer,BlockPos> livePlacements=new HashMap<>();
    static int liveStage;
    private static int resourceRun,stableStageSamples;
    private static MinecraftServer previous;
    private static int ticks;
    private RoRGameplay() {}
    public static boolean enabled(MinecraftServer server) {
        return RoRRunReset.isMirror(server) && !Boolean.getBoolean("skycraft.polishTest");
    }
    public static void init() {ServerTickEvents.END_SERVER_TICK.register(RoRGameplay::tick);}
    private static void tick(MinecraftServer server) {
        if(!enabled(server))return;
        if(previous!=server){previous=server;anchors.clear();livePlacements.clear();resourceRun=0;liveStage=0;ticks=0;}
        if(++ticks%20!=0)return;
        var segment=SkyLink.segment();
        if(segment==null || !SkyLink.active() || segment.get(ValueLayout.JAVA_INT,0x944)!=0x31524F52)return;
        int token=segment.get(ValueLayout.JAVA_INT,0x940);
        if(token==0 || segment.get(ValueLayout.JAVA_INT,0x9B0)!=token)return;
        // Inventory reset belongs to the run, never to a body respawn or stage change.
        for(var player:server.getPlayerList().getPlayers())beginRun(player,token);
        if(!SkyLink.readSkyState(state) || !state.inGame() || state.loading())return;
        if(RoRStats.read(segment)==null)return; // takeover/terrain must be ready
        // Real stage counter already exported by the host HUD, unlike worldId=1.
        int stage=segment.get(ValueLayout.JAVA_INT,0xFB4);
        if(stage<1)return;
        if(resourceRun!=token || liveStage!=stage) {
            RoRMobs.clear(server);
            resourceRun=token;liveStage=stage;stableStageSamples=0;
            anchors.clear();livePlacements.clear();
        }
        // Let scene transfer and the new terrain origin settle before choosing an anchor.
        if(++stableStageSamples<3)return;
        for(var player:server.getPlayerList().getPlayers()) {
            if(!player.isAlive())continue;
            String runWorld=token+"_stage_"+stage;
            String key=player.getUUID()+"_"+runWorld;
            BlockPos anchor=anchors.computeIfAbsent(key,k->player.blockPosition());
            boolean depositsReady=true;
            for(int node=0;node<4;node++) {
                String marker=NODE+runWorld+"_"+node;
                if(player.entityTags().contains(marker))continue;
                depositsReady=false;
                if(placeResource(player,anchor,node)) {
                    player.addTag(marker);
                    SkyCraft.LOG.info("RoRCraft gameplay: placed resource {} for run {}, stage {}",node,token,stage);
                }
                break; // bounded work: one patch attempt per second
            }
            if(depositsReady)RoRMobs.tickStage(player,anchor,token,stage);
        }
    }
    static boolean beginRun(ServerPlayer player,int token) {
        if(token==0 || !enabled(player.level().getServer()) || !player.isAlive())return false;
        String marker=RUN+token;
        if(player.entityTags().contains(marker))return false;
        player.closeContainer(); // return crafting/cursor stacks before clearing the old run
        player.getInventory().clearContent();
        for(var slot:EquipmentSlot.values())player.setItemSlot(slot,ItemStack.EMPTY);
        player.getInventory().setItem(0,new ItemStack(Items.WOODEN_SWORD));
        player.getInventory().setItem(1,new ItemStack(Items.WOODEN_PICKAXE));
        player.getInventory().setItem(2,new ItemStack(Items.COOKED_BEEF,8));
        player.setItemSlot(EquipmentSlot.OFFHAND,new ItemStack(Items.SHIELD));
        player.getInventory().setSelectedSlot(0);
        player.setGameMode(GameType.SURVIVAL);
        player.experienceLevel=0;player.totalExperience=0;player.experienceProgress=0;
        player.getFoodData().setFoodLevel(20);
        var recipes=List.of("oak_planks","stick","crafting_table","wooden_pickaxe","stone_pickaxe","stone_sword","furnace","iron_ingot_from_smelting_raw_iron","iron_pickaxe","iron_sword","shield","bow","leather_helmet","leather_chestplate","leather_leggings","leather_boots");
        player.awardRecipesByKey(recipes.stream().map(id->ResourceKey.create(Registries.RECIPE,Identifier.withDefaultNamespace(id))).toList());
        for(String tag:new ArrayList<>(player.entityTags()))if(tag.startsWith(RUN)||tag.startsWith(NODE))player.removeTag(tag);
        player.addTag(marker);
        player.inventoryMenu.broadcastChanges();
        SkyCraft.LOG.info("RoRCraft gameplay: fresh survival inventory for run {}",token);
        return true;
    }
    public static void resourcesCleared(MinecraftServer server) {
        anchors.clear();
        RoRMobs.reset(server);
        livePlacements.clear();
        resourceRun=0;liveStage=0;stableStageSamples=0;
        for(var player:server.getPlayerList().getPlayers())for(String tag:new ArrayList<>(player.entityTags()))if(tag.startsWith(NODE))player.removeTag(tag);
    }
    static boolean placeResource(ServerPlayer player,BlockPos anchor,int node) {
        for(int attempt=0;attempt<48;attempt++) {
            double angle=(node*3+attempt%12)*Math.PI/6;
            int radius=6+(attempt/12)*6;
            int x=anchor.getX()+(int)Math.round(Math.cos(angle)*radius);
            int z=anchor.getZ()+(int)Math.round(Math.sin(angle)*radius);
            var hit=SkyClip.cast(new Vec3(x+.5,anchor.getY()+2,z+.5),new Vec3(x+.5,anchor.getY()-5,z+.5));
            if(hit==null || hit.ny()<.85 || Math.abs(hit.y()-anchor.getY())>3)continue;
            var base=new BlockPos(x,(int)Math.floor(hit.y()+.15),z);
            if(player.position().distanceToSqr(Vec3.atCenterOf(base))<16)continue;
            boolean supported=true;var positions=new ArrayList<BlockPos>();
            for(int dx=0;dx<(node==0?1:2);dx++)for(int dz=0;dz<(node==0?1:2);dz++) {
                var support=SkyClip.cast(new Vec3(x+dx+.5,anchor.getY()+2,z+dz+.5),new Vec3(x+dx+.5,anchor.getY()-5,z+dz+.5));
                if(support==null || support.ny()<.7 || Math.abs(support.y()-hit.y())>2){supported=false;continue;}
                // Each column rests on its own terrain height; a sloping map does
                // not require a flat 2x2 platform or leave floating cube bottoms.
                var foot=new BlockPos(x+dx,(int)Math.floor(support.y()+.15),z+dz);
                int height=node==0?5:node==1?4:((dx+dz)%2==0?2:1);
                for(int y=0;y<height;y++)positions.add(foot.above(y));
            }
            if(!supported)continue;
            if(placeBlocks(player,positions,node)){livePlacements.put(node,base);return true;}
        }
        return false;
    }
    static boolean placePatch(ServerPlayer player,BlockPos base,int node) {
        var level=player.level();
        // Every footprint cell was checked above; never overwrite a player's build.
        int height=node==0?5:3;
        var positions=new ArrayList<BlockPos>();
        if(node==0)for(int y=0;y<height;y++)positions.add(base.above(y));
        // Sixteen stone covers a pickaxe (3), sword (2), furnace (8), plus spare.
        else for(int x=0;x<2;x++)for(int z=0;z<2;z++)for(int y=0;y<(node==1?4:((x+z)%2==0?2:1));y++)positions.add(base.offset(x,y,z));
        return placeBlocks(player,positions,node);
    }
    private static boolean placeBlocks(ServerPlayer player,List<BlockPos> positions,int node) {
        var level=player.level();
        for(var pos:positions)if(!level.isEmptyBlock(pos))return false;
        var block=switch(node){case 0->Blocks.OAK_LOG;case 1->Blocks.STONE;case 2->Blocks.COAL_ORE;default->Blocks.IRON_ORE;};
        for(var pos:positions)level.setBlock(pos,block.defaultBlockState(),3);
        // Ordinary blocks: real tool requirements, durability, loot and recipes.
        return true;
    }
}
