package dev.skycraft;
import java.nio.file.*;
import java.util.*;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.world.entity.*;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.phys.Vec3;
/** Opt-in native kill -> actual MC effect recipient and native level -> MC stats. */
public final class RoRReverseProbe {
    private static final String[] CASES={"none","gas","wisp","combo","unowned","level"};
    private static int index,phase,at,gas,wisp,orbs;
    private static net.minecraft.world.entity.monster.zombie.Zombie receiver,far;
    private record Saved(Entity entity,Vec3 pos,boolean gravity){}
    private static final List<Saved> saved=new ArrayList<>();
    private static void restore(){for(var e:saved)if(!e.entity.isRemoved()){e.entity.setPos(e.pos);e.entity.setNoGravity(e.gravity);}saved.clear();}
    private static net.minecraft.world.entity.monster.zombie.Zombie mob(net.minecraft.server.level.ServerPlayer p,Vec3 pos){
        var z=EntityTypes.ZOMBIE.create(p.level(),EntitySpawnReason.EVENT);z.addTag(RoRMobs.OWNED);z.setNoAi(true);z.setNoGravity(true);z.getAttribute(Attributes.MAX_HEALTH).setBaseValue(200);z.setHealth(200);z.setPos(pos);p.level().addFreshEntity(z);return z;
    }
    public static void init(){if(!Boolean.getBoolean("skycraft.mobProcProbe"))return;
        var folder=Path.of(new String(Base64.getDecoder().decode(System.getProperty("skycraft.skinMailbox")),java.nio.charset.StandardCharsets.UTF_8)).getParent();
        ServerTickEvents.END_SERVER_TICK.register(server->{if(index>=CASES.length || server.getPlayerList().getPlayers().isEmpty())return;var p=server.getPlayerList().getPlayers().getFirst();String mode=CASES[index];
            try{
                if(phase==0 && Files.exists(folder.resolve("rorcraft-reverse-"+mode+"-request.txt"))){
                    var xyz=Files.readString(folder.resolve("rorcraft-reverse-"+mode+"-request.txt")).trim().split(" ");var origin=new Vec3(Double.parseDouble(xyz[0]),Double.parseDouble(xyz[1]),Double.parseDouble(xyz[2]));
                    if(index==0)for(var e:p.level().getAllEntities())if(e instanceof LivingEntity && e.entityTags().contains(RoRMobs.OWNED)){saved.add(new Saved(e,e.position(),e.isNoGravity()));e.setNoGravity(true);e.setPos(origin.add(100+saved.size()*3,0,0));}
                    if(mode.equals("level")){
                        if(RoRMobs.nativeLevel()!=5)throw new IllegalStateException("Native level snapshot not5: "+RoRMobs.nativeLevel());
                        var z=EntityTypes.ZOMBIE.create(p.level(),EntitySpawnReason.EVENT);double baseHP=z.getMaxHealth(),baseAttack=z.getAttributeValue(Attributes.ATTACK_DAMAGE);RoRMobs.scaleHostile(z);
                        var cow=EntityTypes.COW.create(p.level(),EntitySpawnReason.EVENT);double cowHP=cow.getMaxHealth();RoRMobs.scaleHostile(cow);
                        if(Math.abs(z.getMaxHealth()-baseHP*2.2)>.01 || Math.abs(z.getAttributeValue(Attributes.ATTACK_DAMAGE)-baseAttack*1.8)>.01 || cow.getMaxHealth()!=cowHP)throw new IllegalStateException("Level5 stats/passive mismatch");
                        Files.writeString(folder.resolve("rorcraft-reverse-level-done.txt"),"PASS actual native monster level5 -> MC zombie HP "+baseHP+"->"+z.getMaxHealth()+", attack "+baseAttack+"->"+z.getAttributeValue(Attributes.ATTACK_DAMAGE)+"; passive cow unchanged");restore();index++;return;
                    }
                    receiver=mob(p,origin.add(4,0,0));far=mob(p,origin.add(24,0,0));gas=RoRItemProcs.gasApplications;wisp=RoRItemProcs.wispApplications;orbs=RoRItemProcs.orbSpawns;
                    Files.writeString(folder.resolve("rorcraft-reverse-"+mode+"-ready.txt"),"ready");phase=1;
                }else if(phase==1 && Files.exists(folder.resolve("rorcraft-reverse-"+mode+"-killed.txt"))){at=p.tickCount+160;phase=2;}
                else if(phase==2 && p.tickCount>=at){float expected=mode.equals("gas")?182:mode.equals("wisp")?179:mode.equals("combo")?161:200;
                    if(Math.abs(receiver.getHealth()-expected)>.1 || far.getHealth()!=200 || RoRItemProcs.orbSpawns!=orbs || RoRItemProcs.gasApplications-gas!=(mode.equals("gas")||mode.equals("combo")?1:0) || RoRItemProcs.wispApplications-wisp!=(mode.equals("wisp")||mode.equals("combo")?1:0))throw new IllegalStateException(mode+" reverse effects mismatch HP="+receiver.getHealth()+" expected="+expected);
                    Files.writeString(folder.resolve("rorcraft-reverse-"+mode+"-done.txt"),"PASS native kill "+mode+" -> actual owned MC zombie "+200+"->"+receiver.getHealth()+"; outside radius unchanged; no duplicate MC Tooth orb");receiver.discard();far.discard();index++;phase=0;
                }
            }catch(Throwable error){restore();index=CASES.length;try{Files.writeString(folder.resolve("rorcraft-combat-failure.txt"),"FAIL reverse "+error);}catch(Exception ignored){}}
        });
    }
}
