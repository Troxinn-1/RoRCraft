package dev.skycraft.mixin;

import dev.skycraft.world.SkyCollision;
import dev.skycraft.world.TriangleMovement;
import net.minecraft.world.entity.Entity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
import net.minecraft.world.phys.Vec3;

/** A player that bypasses voxel terrain must receive exact terrain on either side. */
@Mixin(Entity.class)
public abstract class EntityTriangleCollisionMixin {
    @Inject(method = "collide", at = @At("RETURN"), cancellable = true)
    private void skycraft$terrainCollision(Vec3 movement, CallbackInfoReturnable<Vec3> cir) {
        Entity entity = (Entity)(Object)this;
        if (!entity.noPhysics && SkyCollision.usesSmoothCollider(entity))
            cir.setReturnValue(TriangleMovement.collide(entity, cir.getReturnValue()));
    }
}
