package dev.skycraft.client.render;

import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

/** Category in otherwise unused section-vertex bits 16..18. Older hosts ignore it. */
final class SurfaceCategory {
    static final int MATTE=0, METAL=1, GLASS=2, FLUID=3, EMISSIVE=4, STAINED_GLASS=6;
    private static final java.util.Set<net.minecraft.world.level.block.Block> COPPER = new java.util.HashSet<>(Blocks.COPPER_BLOCK.asList());
    static { COPPER.addAll(Blocks.CUT_COPPER.asList()); }
    private SurfaceCategory() {}
    static int of(BlockState state) {
        if(state.is(Blocks.IRON_BLOCK) || state.is(Blocks.GOLD_BLOCK) || COPPER.contains(state.getBlock()) || state.is(Blocks.NETHERITE_BLOCK)) return METAL;
        if(state.getBlock() instanceof net.minecraft.world.level.block.StainedGlassBlock
            || state.getBlock() instanceof net.minecraft.world.level.block.StainedGlassPaneBlock
            || state.is(Blocks.TINTED_GLASS)) return STAINED_GLASS;
        if(state.is(Blocks.GLASS) || state.is(Blocks.GLASS_PANE)) return GLASS;
        if(state.is(Blocks.GLOWSTONE) || state.is(Blocks.SEA_LANTERN)
            || state.is(Blocks.SHROOMLIGHT) || state.is(Blocks.OCHRE_FROGLIGHT)
            || state.is(Blocks.PEARLESCENT_FROGLIGHT) || state.is(Blocks.VERDANT_FROGLIGHT)) return EMISSIVE;
        return MATTE;
    }
}
