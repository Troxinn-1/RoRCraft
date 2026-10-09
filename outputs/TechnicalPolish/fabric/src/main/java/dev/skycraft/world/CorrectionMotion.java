package dev.skycraft.world;

/** Velocity response to an authoritative collision displacement, not a spawn teleport. */
public final class CorrectionMotion {
    private CorrectionMotion() {}
    public static boolean valid(double x, double y, double z) {
        return Double.isFinite(x) && Double.isFinite(y) && Double.isFinite(z)
            && Math.abs(x) <= 4 && Math.abs(y) <= 4 && Math.abs(z) <= 4;
    }
    public static double[] velocity(double vx, double vy, double vz, double dx, double dy, double dz) {
        double squared = dx * dx + dz * dz;
        if (squared > 1e-10) {
            double intoWall = vx * dx + vz * dz;
            if (intoWall < 0) { vx -= dx * intoWall / squared; vz -= dz * intoWall / squared; }
        }
        if (dy > 0 && vy < 0 || dy < 0 && vy > 0) vy = 0;
        return new double[] { vx, vy, vz };
    }
}
