package dev.skycraft.world;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/** Same terrain collision for the local player and its integrated-server copy. */
public final class TriangleMovement {
    private TriangleMovement() {}

    public static Vec3 collide(Entity player, Vec3 move) {
        AABB box = player.getBoundingBox();
        double step = player.maxUpStep();
        List<SkyTri> tris = new ArrayList<>();
        SkyCollision.trianglesNear(box.expandTowards(move).inflate(1.0, 1.0 + step, 1.0), tris);
        if (tris.isEmpty()) return move;
        double[] result = TriCollider.resolve(tris,
            (box.minX + box.maxX) * .5, box.minY, (box.minZ + box.maxZ) * .5,
            box.getXsize() * .5, box.getYsize(), step, player.onGround(), move.x, move.y, move.z);
        if (result[0] == move.x && result[1] == move.y && result[2] == move.z) return move;
        return Entity.collideBoundingBox(player, new Vec3(result[0], result[1], result[2]), box, player.level(), List.of());
    }
}
