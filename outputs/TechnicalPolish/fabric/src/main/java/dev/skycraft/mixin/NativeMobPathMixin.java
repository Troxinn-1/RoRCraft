package dev.skycraft.mixin;

import dev.skycraft.link.SkyLink;
import dev.skycraft.world.SkyCollision;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.pathfinder.*;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Vanilla navigation must see exported native floor/walls, not an infinite void. */
@Mixin(WalkNodeEvaluator.class)
public abstract class NativeMobPathMixin {
    @Inject(method="getPathTypeFromState(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/pathfinder/PathType;",at=@At("RETURN"),cancellable=true)
    private static void rorcraft$nativeFloor(BlockGetter level,BlockPos pos,CallbackInfoReturnable<PathType> cir) {
        if(cir.getReturnValue()==PathType.OPEN && SkyLink.active() && SkyCollision.shapeAt(pos)!=null)cir.setReturnValue(PathType.BLOCKED);
    }
}
