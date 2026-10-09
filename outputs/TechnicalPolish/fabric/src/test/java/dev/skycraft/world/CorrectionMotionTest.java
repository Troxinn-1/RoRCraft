package dev.skycraft.world;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class CorrectionMotionTest {
    @Test void floorPreservesWalkingAndStopsFalling() {
        assertArrayEquals(new double[] { .3, 0, .4 }, CorrectionMotion.velocity(.3, -.2, .4, 0, .42, 0), 1e-12);
    }
    @Test void floorDoesNotCancelJumpingAway() {
        assertArrayEquals(new double[] { .3, .2, .4 }, CorrectionMotion.velocity(.3, .2, .4, 0, .1, 0), 1e-12);
    }
    @Test void wallPreservesTangentialVelocity() {
        assertArrayEquals(new double[] { 0, -.2, .4 }, CorrectionMotion.velocity(.3, -.2, .4, -.1, 0, 0), 1e-12);
    }
    @Test void movingAwayFromWallIsPreserved() {
        assertArrayEquals(new double[] { -.3, -.2, .4 }, CorrectionMotion.velocity(-.3, -.2, .4, -.1, 0, 0), 1e-12);
    }
    @Test void ceilingCancelsOnlyUpwardVelocity() {
        assertArrayEquals(new double[] { .3, 0, .4 }, CorrectionMotion.velocity(.3, .2, .4, 0, -.1, 0), 1e-12);
    }
    @Test void invalidDiscontinuitiesAreRejected() {
        assertFalse(CorrectionMotion.valid(Double.NaN, 0, 0));
        assertFalse(CorrectionMotion.valid(0, Double.POSITIVE_INFINITY, 0));
        assertFalse(CorrectionMotion.valid(0, 0, 4.1));
        assertTrue(CorrectionMotion.valid(.1, .42, 0));
    }
}
