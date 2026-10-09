package dev.skycraft;
import java.nio.file.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.*;
import net.minecraft.world.entity.monster.zombie.Zombie;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.phys.Vec3;
/** Opt-in real player kill / item bridge / nearby damage / automatic pickup test. */
public final class RoRAreaProbe {
    private static final String[] CASES={"none","gas","wisp","tooth","combo","herd","gas2","wisp2","tooth2"};
    private static int index,phase,at,gas,wisp,orbs,pickups;
    private static Zombie source,near,receiver,far,outer;
    private static float playerHP;
    private static boolean replayed;
    private record Saved(Entity entity,Vec3 pos,boolean gravity){}
    private static final List<Saved> saved=new ArrayList<>();
    private static void removeTagged(ServerPlayer p,String tag){
        var removed=new ArrayList<Entity>();for(var e:p.level().getAllEntities())if(e!=null && e.entityTags().contains(tag))removed.add(e);for(var e:removed)e.discard();
    }
    private static void restore(){for(var old:saved)if(!old.entity.isRemoved()){old.entity.setPos(old.pos);old.entity.setNoGravity(old.gravity);}saved.clear();}
    private static Zombie mob(ServerPlayer p,Vec3 pos,float health){
        var z=EntityTypes.ZOMBIE.create(p.level(),EntitySpawnReason.EVENT);z.addTag(RoRMobs.OWNED);z.addTag("rorcraft_area_fixture");z.setNoAi(true);z.setNoGravity(true);z.setPersistenceRequired();
        z.getAttribute(Attributes.MAX_HEALTH).setBaseValue(200);z.setHealth(health);z.snapTo(pos.x,pos.y,pos.z,0,0);p.level().addFreshEntity(z);return z;
    }
    public static void init(){
        if(!Boolean.getBoolean("skycraft.mobProcProbe"))return;
        var folder=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent();
        ServerTickEvents.END_SERVER_TICK.register(server->{
            if(index>=CASES.length || server.getPlayerList().getPlayers().isEmpty())return;var p=server.getPlayerList().getPlayers().getFirst();String mode=CASES[index];
            try{
                if(phase==0 && Files.exists(folder.resolve("rorcraft-area-"+mode+"-request.txt"))){
                    p.getFoodData().setFoodLevel(17); // isolate item healing from vanilla food regeneration
                    var xyz=Files.readString(folder.resolve("rorcraft-area-"+mode+"-request.txt")).trim().split(" ");
                    var origin=new Vec3(Double.parseDouble(xyz[0]),Double.parseDouble(xyz[1]),Double.parseDouble(xyz[2]));
                    if(index==0)for(var e:p.level().getAllEntities())if(e instanceof LivingEntity && e.entityTags().contains(RoRMobs.OWNED)){
                        saved.add(new Saved(e,e.position(),e.isNoGravity()));e.setNoGravity(true);e.setPos(origin.add(100+saved.size()*3,0,0));
                    }
                    source=mob(p,origin,1);near=mob(p,origin.add(2,0,0),mode.equals("wisp")||mode.equals("wisp2")||mode.equals("combo")||mode.equals("herd")?1:200);
                    receiver=mob(p,origin.add(4,0,0),200);
                    if(mode.equals("herd")){receiver.getAttribute(Attributes.MAX_HEALTH).setBaseValue(1000);receiver.setHealth(1000);for(int n=0;n<6;n++)mob(p,origin.add(.5+n*.15,0,.5),1);}
                    outer=(mode.equals("wisp")||mode.equals("wisp2"))?mob(p,origin.add(10,0,0),200):null;
                    far=mob(p,origin.add(24,0,0),200);
                    p.setPos(origin.x,origin.y,origin.z-1.2);p.resetAttackStrengthTicker();at=p.tickCount+30;phase=1;
                    gas=RoRItemProcs.gasApplications;wisp=RoRItemProcs.wispApplications;orbs=RoRItemProcs.orbSpawns;pickups=RoRItemProcs.orbPickups;playerHP=p.getHealth();replayed=false;
                }else if(phase==1 && p.tickCount>=at){p.attack(source);if(source.isAlive())throw new IllegalStateException("Test sword failed to kill source");at=p.tickCount+170;phase=2;}
                else if(phase==2){
                    if(mode.equals("none") && !replayed && p.tickCount>=at-150){
                        dev.skycraft.link.SkyLink.pushEvent(0x702,source.getId(),0,(float)source.getX(),(float)source.getY(),(float)source.getZ(),0);
                        dev.skycraft.link.SkyLink.pushEvent(0x702,source.getId(),0,(float)source.getX(),(float)source.getY(),(float)source.getZ(),0);replayed=true;
                    }
                    if(mode.equals("gas") && p.tickCount>=at-155 && !Files.exists(folder.resolve("rorcraft-area-preview.txt")))Files.writeString(folder.resolve("rorcraft-area-preview.txt"),"area-gas|"+receiver.getX()+"|"+receiver.getY()+"|"+receiver.getZ());
                    if((mode.equals("tooth")||mode.equals("tooth2")) && RoRItemProcs.orbSpawns>orbs){
                        for(var e:p.level().getAllEntities())if(e instanceof ItemEntity && e.entityTags().contains(RoRMobs.OWNED) && e.isAlive())p.setPos(e.getX(),e.getY(),e.getZ());
                    }
                    if((mode.equals("tooth")||mode.equals("tooth2")) && RoRItemProcs.orbPickups>pickups && !replayed){
                        dev.skycraft.link.SkyLink.pushEvent(0x704,source.getId(),0,0,0,0,0);dev.skycraft.link.SkyLink.pushEvent(0x704,source.getId(),0,0,0,0,0);replayed=true;
                    }
                    if(p.tickCount>=at){
                        int dg=RoRItemProcs.gasApplications-gas,dw=RoRItemProcs.wispApplications-wisp,dh=RoRItemProcs.orbSpawns-orbs;
                        float expected=mode.equals("gas")?182:mode.equals("wisp")?158:mode.equals("combo")?122:mode.equals("herd")?688:mode.equals("gas2")?177.5f:mode.equals("wisp2")?124.4f:200;
                        if(Math.abs(receiver.getHealth()-expected)>.15 || Math.abs(far.getHealth()-200)>.01)throw new IllegalStateException(mode+" area/radius mismatch: receiver="+receiver.getHealth()+" expected="+expected+" far="+far.getHealth()+" max="+receiver.getMaxHealth()+" gas="+dg+" wisp="+dw+" orbs="+dh);
                        if(outer!=null && Math.abs(outer.getHealth()-(mode.equals("wisp")?189.5f:181.1f))>.15)throw new IllegalStateException("Wisp SweetSpot falloff mismatch outer="+outer.getHealth());
                        if(dg!=((mode.equals("gas")||mode.equals("gas2"))?1:mode.equals("combo")?2:mode.equals("herd")?8:0) || dw!=(mode.equals("wisp")||mode.equals("wisp2")||mode.equals("combo")?2:mode.equals("herd")?8:0) || dh!=((mode.equals("tooth")||mode.equals("tooth2"))?1:mode.equals("herd")?8:0))throw new IllegalStateException(mode+" effect count mismatch gas="+dg+" wisp="+dw+" tooth="+dh);
                        if((mode.equals("tooth")||mode.equals("tooth2")) && (RoRItemProcs.orbPickups!=pickups+1 || p.getHealth()<=playerHP+5))throw new IllegalStateException("Healing pickup did not reach linked player HP");
                        Files.writeString(folder.resolve("rorcraft-area-"+mode+"-done.txt"),"PASS "+mode+": real sword kill, native inventory effects gas="+dg+" wisp="+dw+" healOrbs="+dh+"; receiver->"+receiver.getHealth()+", outer="+(outer==null?"n/a":outer.getHealth())+", far unchanged; chain kills/loot attribution; HP="+playerHP+"->"+p.getHealth());
                        removeTagged(p,"rorcraft_area_fixture");
                        index++;phase=0;if(index>=CASES.length){restore();removeTagged(p,RoRItemProcs.HEAL_TAG);}
                    }
                }
            }catch(Throwable error){restore();index=CASES.length;try{Files.writeString(folder.resolve("rorcraft-combat-failure.txt"),"FAIL area "+error);}catch(Exception ignored){}}
        });
    }
}
