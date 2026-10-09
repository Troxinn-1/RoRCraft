package dev.skycraft.mixin;
import dev.skycraft.RoRRunReset;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
@Mixin(LevelChunk.class)
public abstract class RoRBuildingMixin {
    @Inject(method="setBlockState",at=@At("RETURN"))
    private void rorcraft$recordBuilding(BlockPos pos,BlockState state,int flags,CallbackInfoReturnable<BlockState> ci) {
        LevelChunk chunk=(LevelChunk)(Object)this;
        if(ci.getReturnValue()!=null && chunk.getLevel() instanceof ServerLevel server) RoRRunReset.changed(server,pos);
    }
}
