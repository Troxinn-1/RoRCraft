package dev.skycraft.client;

import dev.skycraft.RoRStats;
import dev.skycraft.link.SkyLink;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.VarHandle;
import java.nio.charset.StandardCharsets;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.fabric.api.client.rendering.v1.hud.VanillaHudElements;
import net.minecraft.client.Minecraft;
import net.minecraft.resources.Identifier;

/** Gameplay information is rendered by Minecraft, in the same frame as its inventory HUD. */
public final class RoRHud {
    private RoRHud() {}
    public static void init() {
        HudElementRegistry.replaceElement(VanillaHudElements.HEALTH_BAR, original -> (g, delta) -> {
            var segment=SkyLink.segment();
            var stats=segment==null || !SkyLink.active()?null:RoRStats.read(segment);
            if(stats==null) {original.extractRenderState(g,delta);return;}
            var font=Minecraft.getInstance().font;
            int x=g.guiWidth()/2-91,y=g.guiHeight()-39;
            float filled=10*stats.health()/stats.maximum();
            for(int i=0;i<10;i++) g.text(font,"\u2665",x+i*8,y,i<filled?0xFFFF3535:0xFF522020);
            String hp=(int)Math.ceil(stats.health())+" / "+(int)Math.ceil(stats.maximum());
            if(stats.shield()>0) hp+=" +"+(int)Math.ceil(stats.shield());
            g.text(font,hp,x,y-11,0xFFFFFFFF);
        });
        HudElementRegistry.attachElementAfter(VanillaHudElements.HOTBAR,Identifier.fromNamespaceAndPath("skycraft","ror_run"),(g,delta)-> {
            var s=SkyLink.segment();
            if(s==null || !SkyLink.active() || RoRStats.read(s)==null) return;
            int sequence=s.get(ValueLayout.JAVA_INT,0xA00);
            if((sequence&1)!=0 || s.get(ValueLayout.JAVA_INT,0xA04)!=0x31524F52) return;
            VarHandle.loadLoadFence();
            int money=s.get(ValueLayout.JAVA_INT,0xA08),length=s.get(ValueLayout.JAVA_INT,0xA10);
            float seconds=s.get(ValueLayout.JAVA_FLOAT,0xA0C);
            int difficultyLength=s.get(ValueLayout.JAVA_INT,0xE2C);
            float progress=s.get(ValueLayout.JAVA_FLOAT,0xE20),charge=s.get(ValueLayout.JAVA_FLOAT,0xFB0);
            int difficultyColor=s.get(ValueLayout.JAVA_INT,0xE28),stage=s.get(ValueLayout.JAVA_INT,0xFB4);
            if(length<0 || length>1024 || !Float.isFinite(seconds) || seconds<0) return;
            byte[] bytes=s.asSlice(0xA20,length).toArray(ValueLayout.JAVA_BYTE);
            if(difficultyLength<0 || difficultyLength>384 || !Float.isFinite(progress) || !Float.isFinite(charge)) return;
            byte[] difficulty=s.asSlice(0xE30,difficultyLength).toArray(ValueLayout.JAVA_BYTE);
            VarHandle.loadLoadFence();if(sequence!=s.get(ValueLayout.JAVA_INT,0xA00)) return;
            var font=Minecraft.getInstance().font;
            g.text(font,"$ "+Integer.toUnsignedString(money),8,8,0xFFFFDD55);
            int minutes=(int)seconds/60,remainder=(int)seconds%60;
            String clock=minutes+":"+(remainder<10?"0":"")+remainder;
            int right=g.guiWidth()-8,width=Math.min(150,g.guiWidth()/3),left=right-width;
            g.text(font,clock,right-font.width(clock),8,0xFFFFFFFF);
            String[] labels=new String(difficulty,StandardCharsets.UTF_8).split("\n",-1);
            if(labels.length>=3) {
                g.text(font,labels[0],left,20,0xFFCCCCCC);
                g.text(font,labels[1],left,33,difficultyColor);
                g.fill(left,45,right,50,0xB0202020);
                g.fill(left,45,left+(int)(width*Math.clamp(progress,0f,1f)),50,difficultyColor);
                g.text(font,"Next: "+labels[2],left,54,0xFFCCCCCC);
            }
            g.text(font,"Stage "+stage,left,67,0xFFFFFFFF);
            if(charge>=0) g.text(font,"Teleporter "+Math.round(Math.clamp(charge,0f,1f)*100)+"%",left,80,0xFFFFDD55);
            String[] lines=new String(bytes,StandardCharsets.UTF_8).split("\n");
            int y=24;
            for(String line:lines) {
                if(line.isBlank()) continue;
                if(y>g.guiHeight()-80) break;
                g.text(font,line.strip(),8,y,0xFFFFFFFF);y+=11;
            }
        });
    }
}
