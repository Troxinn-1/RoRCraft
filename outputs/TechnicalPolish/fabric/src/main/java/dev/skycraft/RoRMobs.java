package dev.skycraft;

import dev.skycraft.world.SkyClip;
import java.util.*;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.minecraft.core.BlockPos;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.*;
import net.minecraft.world.item.*;
import net.minecraft.world.phys.Vec3;

/** Small finite vanilla population per stage; native terrain, ordinary AI and loot. */
public final class RoRMobs {
    public static final String OWNED="rorcraft_stage_mob";
    private static final String MARKER="rorcraft_mob_spawn_";
    private static final EntityType<?>[] TYPES={EntityTypes.CHICKEN,EntityTypes.COW,EntityTypes.SPIDER,EntityTypes.CHICKEN,EntityTypes.COW,EntityTypes.SPIDER,EntityTypes.SPIDER,EntityTypes.SKELETON,EntityTypes.ZOMBIE,EntityTypes.ZOMBIE,EntityTypes.CREEPER};
    public static int population(){return TYPES.length;}
    public static void init() {
        ServerLivingEntityEvents.AFTER_DEATH.register((entity,source)->{
            if(!entity.entityTags().contains(OWNED) || !(entity.level() instanceof net.minecraft.server.level.ServerLevel level))return;
            // A small guaranteed resource reward supplements normal vanilla loot.
            var item=entity.getType()==EntityTypes.ZOMBIE?Items.ROTTEN_FLESH:entity.getType()==EntityTypes.CREEPER?Items.GUNPOWDER:entity.getType()==EntityTypes.SPIDER?Items.STRING:entity.getType()==EntityTypes.COW?Items.LEATHER:entity.getType()==EntityTypes.CHICKEN?Items.FEATHER:Items.ARROW;
            entity.spawnAtLocation(level,new ItemStack(item,entity.getType()==EntityTypes.SKELETON?4:1));
            if(source.getEntity() instanceof ServerPlayer player && dev.skycraft.link.SkyLink.active()){
                RoRItemProcs.recordKill(entity,player);
                dev.skycraft.link.SkyLink.pushEvent(dev.skycraft.link.Proto.EV_ROR_MOB_REWARD,entity.getId(),0,(float)entity.getX(),(float)entity.getY(),(float)entity.getZ(),entity.getType()==EntityTypes.CREEPER?1:0);
            }

        });
    }
    public static void clear(MinecraftServer server) {
        for(var level:server.getAllLevels()){
            var removed=new ArrayList<Entity>();for(var entity:level.getAllEntities())if(entity!=null && entity.entityTags().contains(OWNED))removed.add(entity);
            for(var entity:removed)entity.discard();
        }
    }
    public static void reset(MinecraftServer server) {
        clear(server);
        for(var p:server.getPlayerList().getPlayers())for(var tag:new ArrayList<>(p.entityTags()))if(tag.startsWith(MARKER))p.removeTag(tag);
    }
    static void tickStage(ServerPlayer p,BlockPos anchor,int run,int stage) {
        for(int slot=0;slot<TYPES.length;slot++) {
            String marker=MARKER+run+"_"+stage+"_"+slot;
            if(p.entityTags().contains(marker))continue;
            if(spawn(p,anchor,slot,stage))p.addTag(marker);
            break; // bounded one successful spawn/attempt per second, no kill-based farming
        }
    }
    static int nativeLevel(){
        var memory=dev.skycraft.link.SkyLink.segment();
        if(memory==null || !dev.skycraft.link.SkyLink.active() || memory.get(java.lang.foreign.ValueLayout.JAVA_INT,0xC1C)!=0x314C564C)return 1;
        return Math.max(1,Math.min(1000,memory.get(java.lang.foreign.ValueLayout.JAVA_INT,0xC18)));
    }
    static void scaleHostile(Mob mob){
        if(!(mob instanceof net.minecraft.world.entity.monster.Monster))return;
        int level=nativeLevel();
        var hp=mob.getAttribute(net.minecraft.world.entity.ai.attributes.Attributes.MAX_HEALTH);
        if(hp!=null){hp.setBaseValue(Math.min(1024,hp.getBaseValue()*(1+.3*(level-1))));mob.setHealth(mob.getMaxHealth());}
        var attack=mob.getAttribute(net.minecraft.world.entity.ai.attributes.Attributes.ATTACK_DAMAGE);
        if(attack!=null)attack.setBaseValue(Math.min(128,attack.getBaseValue()*(1+.2*(level-1))));
        mob.addTag("rorcraft_native_level_"+level);
    }
    private static boolean spawn(ServerPlayer p,BlockPos anchor,int slot,int stage) {
        for(int attempt=0;attempt<16;attempt++) {
            double angle=(slot*2+attempt)*Math.PI/8;int radius=12+(attempt/4)*3;
            double x=anchor.getX()+.5+Math.cos(angle)*radius,z=anchor.getZ()+.5+Math.sin(angle)*radius;
            var hit=SkyClip.cast(new Vec3(x,anchor.getY()+4,z),new Vec3(x,anchor.getY()-6,z));
            if(hit==null || hit.ny()<.85 || Math.abs(hit.y()-anchor.getY())>4)continue;
            var made=TYPES[slot].create(p.level(),EntitySpawnReason.EVENT);
            if(!(made instanceof Mob mob))return false;
            mob.snapTo(x,hit.y()+.15,z,0,0);
            // Native support checked above; ordinary blocks and other mobs still block spawn.
            if(!p.level().noCollision(mob,mob.getBoundingBox()))continue;
            mob.addTag(OWNED);mob.addTag("rorcraft_mob_stage_"+stage);mob.setPersistenceRequired();
            mob.finalizeSpawn(p.level(),p.level().getCurrentDifficultyAt(mob.blockPosition()),EntitySpawnReason.EVENT,null);
            scaleHostile(mob);
            if(p.level().addFreshEntity(mob)) {
                SkyCraft.LOG.info("RoRCraft mob {} spawned for stage {} at {}",mob.getType(),stage,mob.position());
                return true;
            }
        }
        return false;
    }
}
