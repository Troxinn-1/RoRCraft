using System;
using RoRCraftPolish;
public static class TickSamplingChecks {
    public static void Main() {
        long frequency=10000000,tick=10000000;
        foreach(int fps in new[]{60,120,144,165,260}) {
            double previous=0;
            // A constant 6m/s physics motion must produce the same speed on each
            // host render clock, even with an unrelated producer frame cadence.
            for(int i=0;i<fps;i++) {
                double time=i/(double)fps;
                int simulationTick=(int)Math.Floor(time/.05);
                long tickAt=tick+(long)(simulationTick*.05*frequency);
                long now=tick+(long)(time*frequency);double phase;
                if(!TickSampling.Phase(tickAt,now,frequency,50,out phase)) throw new Exception("Valid tick rejected");
                double position=TickSampling.Position(simulationTick*.3,(simulationTick+1)*.3,phase);
                if(Math.Abs(position-time*6)>.000002) throw new Exception("Clock-dependent motion at "+fps);
                if(position<previous) throw new Exception("Backward motion");previous=position;
            }
        }
        double alpha;
        if(TickSampling.Phase(0,tick,frequency,50,out alpha)) throw new Exception("Missing tick accepted");
        if(TickSampling.Phase(tick,tick+frequency,frequency,50,out alpha)) throw new Exception("Stale tick accepted");
        if(TickSampling.Phase(tick,tick,frequency,float.NaN,out alpha)) throw new Exception("Invalid rate accepted");
        if(!TickSampling.Phase(tick,tick+frequency/10,frequency,50,out alpha)||alpha!=1) throw new Exception("Extrapolated outside current physics step");
        Console.WriteLine("Tick clock checks passed at 60/120/144/165/260 FPS; invalid/stale data rejected and no extrapolation. Live gameplay still requires verification.");
    }
}
