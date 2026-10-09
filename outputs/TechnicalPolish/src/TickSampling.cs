using System;

namespace RoRCraftPolish {
    // Use Minecraft's original previous/current physics pair on the host render
    // clock. No temporal low-pass filter, extrapolation or extra buffered frame.
    public static class TickSampling {
        public static bool Phase(long tick,long now,long frequency,float tickMs,out double phase) {
            phase=0;
            if(tick<=0 || frequency<=0 || tickMs<5 || tickMs>1000 || float.IsNaN(tickMs)) return false;
            double age=(now-tick)*1000.0/frequency;
            if(age < -2 || age>tickMs*3) return false;
            phase=Math.Max(0,Math.Min(1,age/tickMs));return true;
        }
        public static double Position(double previous,double current,double phase) {
            return previous+(current-previous)*phase;
        }
    }
}
