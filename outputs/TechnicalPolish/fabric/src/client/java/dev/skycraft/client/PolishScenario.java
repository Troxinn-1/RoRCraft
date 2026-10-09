package dev.skycraft.client;

import dev.skycraft.link.SkyLink;
import java.lang.foreign.ValueLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.ClientInput;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.player.Input;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.Vec2;
import net.minecraft.world.phys.Vec3;

/** Developer-only in-engine scenario driver. It never injects OS keyboard events. */
public final class PolishScenario {
    private static int lastToken;
    private static LocalPlayer controlled;
    private static ClientInput previous;
    private static net.minecraft.server.MinecraftServer testServer;
    private static java.util.UUID testPlayer;
    private static boolean previousInvulnerable;
    private static float time;
    private static double anchorZ;
    private static boolean forward=true;
    private static final ClientInput SCRIPT = new ClientInput() {
        @Override public void tick() {
            boolean moving = time >= 0 && time < Math.max(1, Integer.getInteger("skycraft.polishSeconds", 60) - 10);
            // Sprinting is faster forward than backward. Time-only alternation
            // drifts off the fixture and does not test a flat platform for 5 min.
            // Reverse at spatial boundaries while retaining vanilla acceleration.
            if (controlled != null) {
                if (controlled.getZ() >= anchorZ + 3.5) forward=false;
                else if (controlled.getZ() <= anchorZ - 3.5) forward=true;
            }
            boolean jump = moving && time % 8 > 5 && time % 8 < 5.2F;
            boolean sprint = moving && time % 20 > 10;
            keyPresses = new Input(moving && forward, moving && !forward, false, false, jump, false, sprint);
            moveVector = moving ? new Vec2(0, forward ? 1 : -1) : Vec2.ZERO;
            var segment=SkyLink.segment();
            if(segment!=null) segment.set(ValueLayout.JAVA_FLOAT,0x9F8,moving ? (forward ? 1 : -1) : 0);
        }
    };
    private PolishScenario() {}
    public static void frame(Minecraft minecraft) {
        var segment = SkyLink.segment();
        boolean enabled = Boolean.getBoolean("skycraft.polishTest") && segment != null
            && segment.get(ValueLayout.JAVA_INT, 0x9D0) == 0x31545354;
        if (!enabled || minecraft.player == null) {
            if (controlled != null && controlled == minecraft.player) controlled.input = previous;
            if (testServer != null && !enabled) {
                var server = testServer; var uuid = testPlayer; boolean restore = previousInvulnerable;
                testServer = null; testPlayer = null;
                server.execute(() -> { var player = server.getPlayerList().getPlayer(uuid); if (player != null) {
                    player.getAbilities().invulnerable = restore; player.onUpdateAbilities();
                }});
            }
            controlled = null; return;
        }
        var player = minecraft.player;
        if (player != controlled) { controlled = player; previous = player.input; }
        player.input = SCRIPT;
        time = segment.get(ValueLayout.JAVA_FLOAT, 0x9DC);
        int token = segment.get(ValueLayout.JAVA_INT, 0x9FC);
        if (token == lastToken) return;
        lastToken = token;
        int scenario = segment.get(ValueLayout.JAVA_INT, 0x9D4);
        // The native escape pod occupies the original spawn's forward column.
        // Keep all benchmark paths beside it rather than inside its capsule;
        // F5 against the pod otherwise captures the player's own armor face.
        double x = segment.get(ValueLayout.JAVA_DOUBLE, 0x9E0) + 4;
        double y = segment.get(ValueLayout.JAVA_DOUBLE, 0x9E8);
        double z = segment.get(ValueLayout.JAVA_DOUBLE, 0x9F0);
        anchorZ=z; forward=true;
        var server = minecraft.getSingleplayerServer();
        if (server == null) return;
        var uuid = player.getUUID();
        int floor = (int)Math.floor(y) + 5;
        double targetZ = scenario == 2 ? z - 1.5 : z;
        // Select support near the original feet, not the top of an overhang
        // up to five metres above them (y+2 plus maxAbove=3).
        double nativeGround = SkyCollider.groundAt(x, y, targetZ, .6);
        double targetY = scenario == 1 ? floor + 1 : Double.isFinite(nativeGround) ? nativeGround + .02 : y;
        int transitionBase = (int)Math.floor(y);
        server.execute(() -> {
            var serverPlayer = server.getPlayerList().getPlayer(uuid);
            if (serverPlayer == null) return;
            if (testServer != server || !uuid.equals(testPlayer)) {
                testServer = server; testPlayer = uuid; previousInvulnerable = serverPlayer.getAbilities().invulnerable;
            }
            // The host already protects its test body. Match that protection in
            // the separate MC test world so death does not silently invalidate a
            // movement case. Falling through geometry still remains measurable.
            serverPlayer.getAbilities().invulnerable = true; serverPlayer.onUpdateAbilities();
            if (scenario == 1) {
                int bx = (int)Math.floor(x), bz = (int)Math.floor(z);
                for (int dx = -6; dx <= 6; dx++) for (int dz = -10; dz <= 10; dz++)
                    serverPlayer.level().setBlock(new BlockPos(bx + dx, floor, bz + dz), Blocks.STONE_BRICKS.defaultBlockState(), 3);
            } else if (scenario == 2) {
                int bx = (int)Math.floor(x), bz = (int)Math.floor(z);
                // Isolate this surface from the elevated platform of case 1.
                for(int dx=-6;dx<=6;dx++) for(int dz=-10;dz<=10;dz++)
                    serverPlayer.level().setBlock(new BlockPos(bx+dx,floor,bz+dz),Blocks.AIR.defaultBlockState(),3);
                for (int dx = -2; dx <= 2; dx++) {
                    serverPlayer.level().setBlock(new BlockPos(bx + dx, transitionBase, bz), Blocks.STONE_BRICK_SLAB.defaultBlockState(), 3);
                    for (int dz = 1; dz <= 3; dz++)
                        serverPlayer.level().setBlock(new BlockPos(bx + dx, transitionBase, bz + dz), Blocks.STONE_BRICKS.defaultBlockState(), 3);
                }
                // Material comparison fixtures, outside the walking corridor.
                var samples = new net.minecraft.world.level.block.Block[]{Blocks.GRASS_BLOCK,Blocks.DIRT,
                    Blocks.STONE,Blocks.COBBLESTONE,Blocks.STONE_BRICKS,Blocks.OAK_PLANKS,
                    Blocks.OAK_LOG,Blocks.GLASS,Blocks.IRON_BLOCK,Blocks.GOLD_BLOCK,
                    Blocks.DIAMOND_BLOCK,Blocks.GLOWSTONE};
                for(int i=0;i<samples.length;i++) {
                    int px=bx-6+i;
                    for(int dy=0;dy<2;dy++) serverPlayer.level().setBlock(new BlockPos(px,transitionBase+dy,bz+5),samples[i].defaultBlockState(),3);
                    serverPlayer.level().setBlock(new BlockPos(px,transitionBase-1,bz+5),Blocks.STONE_BRICKS.defaultBlockState(),3);
                }
                serverPlayer.level().setBlock(new BlockPos(bx+6,transitionBase,bz+8),Blocks.TORCH.defaultBlockState(),3);
                // A contrasting backing makes alpha preservation observable.
                for(int dy=0;dy<2;dy++) serverPlayer.level().setBlock(new BlockPos(bx+1,transitionBase+dy,bz+6),Blocks.WOOL.red().defaultBlockState(),3);
                // Isolated water basin outside the collision test corridor.
                for(int dx=9;dx<=11;dx++) for(int dz=4;dz<=6;dz++) {
                    serverPlayer.level().setBlock(new BlockPos(bx+dx,transitionBase-1,bz+dz),Blocks.STONE_BRICKS.defaultBlockState(),3);
                    serverPlayer.level().setBlock(new BlockPos(bx+dx,transitionBase,bz+dz),dx==10 && dz==5?Blocks.WATER.defaultBlockState():Blocks.STONE_BRICKS.defaultBlockState(),3);
                }
                // Twenty identical reference faces beyond the movement path.
                // Inspect crispness/seams under the real stage lighting.
                for(int dx=-2;dx<=2;dx++) for(int dy=0;dy<4;dy++)
                    serverPlayer.level().setBlock(new BlockPos(bx+dx,transitionBase+dy,bz+7),Blocks.STONE_BRICKS.defaultBlockState(),3);
            }
            // The host must also know that a fixture is a spawn. A client-only
            // downward teleport was interpreted as falling through an overhang.
            // The harness now drives the normal adapter teleport/ack protocol.
            minecraft.execute(() -> {
                segment.set(ValueLayout.JAVA_DOUBLE,0xA00,x);
                segment.set(ValueLayout.JAVA_DOUBLE,0xA08,targetY);
                segment.set(ValueLayout.JAVA_DOUBLE,0xA10,targetZ);
                segment.set(ValueLayout.JAVA_INT, 0x9D8, token);
            });
        });
    }
}
