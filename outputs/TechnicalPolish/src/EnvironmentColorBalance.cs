using System;

namespace RoRCraftPolish {
    // Chromatic adaptation of the material albedo. Neutralization keeps the
    // ambient luminance; directional lighting, shadows, fog and exposure remain.
    internal static class EnvironmentColorBalance {
        internal static double[] Gains(double r,double g,double b,double influence) {
            if(double.IsNaN(r+g+b+influence) || double.IsInfinity(r+g+b+influence)) return new[]{1d,1d,1d};
            influence=Math.Max(0,Math.Min(1,influence));
            r=Math.Max(0,r);g=Math.Max(0,g);b=Math.Max(0,b);
            double luminance=.2126*r+.7152*g+.0722*b;
            if(luminance<.0001 || influence>=1) return new[]{1d,1d,1d};
            double floor=luminance*.05;
            double gr=Math.Pow(luminance/Math.Max(floor,r),1-influence);
            double gg=Math.Pow(luminance/Math.Max(floor,g),1-influence);
            double gb=Math.Pow(luminance/Math.Max(floor,b),1-influence);
            double preserve=luminance/(.2126*r*gr+.7152*g*gg+.0722*b*gb);
            return new[]{Math.Max(.1,Math.Min(4,gr*preserve)),Math.Max(.1,Math.Min(4,gg*preserve)),Math.Max(.1,Math.Min(4,gb*preserve))};
        }
    }
}
