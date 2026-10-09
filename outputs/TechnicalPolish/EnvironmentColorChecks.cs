using System;
using RoRCraftPolish;
class EnvironmentColorChecks {
    static void Check(bool condition,string label) {if(!condition) throw new Exception(label);Console.WriteLine("PASS "+label);}
    static bool Near(double a,double b) {return Math.Abs(a-b)<.000001;}
    static void Main() {
        var untouched=EnvironmentColorBalance.Gains(.3,.4,.9,1);
        Check(Near(untouched[0],1)&&Near(untouched[1],1)&&Near(untouched[2],1),"full influence preserves material albedo");
        var neutral=EnvironmentColorBalance.Gains(.3,.4,.9,0);
        Check(Near(.3*neutral[0],.4*neutral[1])&&Near(.4*neutral[1],.9*neutral[2]),"neutralized ambient has equal channels");
        double oldL=.2126*.3+.7152*.4+.0722*.9;
        double newL=.2126*.3*neutral[0]+.7152*.4*neutral[1]+.0722*.9*neutral[2];
        Check(Near(oldL,newL),"ambient luminance retained");
        var mid=EnvironmentColorBalance.Gains(.3,.4,.9,.5);
        Check(mid[0]/mid[2]>1 && mid[0]/mid[2]<neutral[0]/neutral[2],"intermediate response is between native and neutral");
        var dim=EnvironmentColorBalance.Gains(.03,.04,.09,.5);
        Check(Near(mid[0],dim[0])&&Near(mid[1],dim[1])&&Near(mid[2],dim[2]),"changing light intensity does not change chromatic adaptation");
        var gray=EnvironmentColorBalance.Gains(.4,.4,.4,0);
        Check(Near(gray[0],1)&&Near(gray[1],1)&&Near(gray[2],1),"neutral lighting unchanged");
        foreach(var value in EnvironmentColorBalance.Gains(0,0,0,0)) Check(Near(value,1),"darkness does not amplify black");
        foreach(var value in EnvironmentColorBalance.Gains(double.NaN,0,0,0)) Check(Near(value,1),"invalid light input is safe");
        foreach(var value in EnvironmentColorBalance.Gains(0,.01,1,0)) Check(value>=.1&&value<=4,"extreme color has bounded gains");
    }
}
