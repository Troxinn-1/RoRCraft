using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using RoR2;
using RoR2.Networking;
using Path = System.IO.Path;
namespace RoRCraftPolish {
    internal static class GameplayLiveProbe {
        private static bool Focus(){return true;}
        private static bool NoInput(){return false;}
        private static CharacterBody testVictim,testAttacker;
        private static DamageInfo melee,environment;
        private static void Damage(DamageReport report) {
            if(report.victimBody==testVictim && report.damageInfo.attacker==null && report.damageInfo.dotIndex==DotController.DotIndex.None)environment=report.damageInfo;
            if(report.victimBody==testVictim && report.attackerBody==testAttacker && report.damageInfo.dotIndex==DotController.DotIndex.None && melee==null)melee=report.damageInfo;
        }
        private static IEnumerable<CodeInstruction> Background(IEnumerable<CodeInstruction> codes) {
            var original=AccessTools.PropertyGetter(typeof(Application),"isFocused");
            foreach(var code in codes){if(code.Calls(original)){code.opcode=OpCodes.Call;code.operand=AccessTools.Method(typeof(GameplayLiveProbe),"Focus");}yield return code;}
        }
        internal static IEnumerator Run(List<string> checks) {
            float deadline=Time.realtimeSinceStartup+90;
            while((NetworkManagerSystem.singleton==null || LocalUserManager.GetFirstLocalUser()==null) && Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.25f);
            if(LocalUserManager.GetFirstLocalUser()==null || NetworkManagerSystem.singleton==null){checks.Add("FAIL native local user did not load");yield break;}
            if(RoR2.Run.instance!=null){checks.Add("FAIL refuses existing run");yield break;}
            var type=AccessTools.TypeByName("RoRCraft.RoRCraftPlugin");var adapter=UnityEngine.Object.FindObjectOfType(type);
            var patch=new Harmony("cz.jirka.rorcraft-gameplay-live-probe");
            CursorSafety.BackgroundTest=true;
            patch.Patch(AccessTools.Method(type,"Update"),transpiler:new HarmonyMethod(typeof(GameplayLiveProbe),"Background"));
            patch.Patch(AccessTools.Method(type,"InputToMinecraft"),prefix:new HarmonyMethod(typeof(GameplayLiveProbe),"NoInput"));
            CharacterBody body=null;bool priorGod=false;
            try {
                NetworkManagerSystem.singleton.desiredHost=new HostDescription(new HostDescription.HostingParameters{listen=false,maxPlayers=1});
                deadline=Time.realtimeSinceStartup+50;
                while(PreGameController.instance==null && Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.25f);
                if(PreGameController.instance==null){checks.Add("FAIL pregame not ready");yield break;}
                yield return new WaitForSecondsRealtime(3);
                AccessTools.Method(typeof(PreGameController),"StartRun").Invoke(PreGameController.instance,null);
                string receipt=System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-gameplay-live-probe.txt");
                deadline=Time.realtimeSinceStartup+90;float spawned=Time.realtimeSinceStartup;
                while(!File.Exists(receipt) && Time.realtimeSinceStartup<deadline) {
                    var local=LocalUserManager.GetFirstLocalUser();var current=local==null || local.currentNetworkUser==null?null:local.currentNetworkUser.GetCurrentBody();
                    if(current!=null) {
                        if(body!=current){body=current;priorGod=body.healthComponent.godMode;body.healthComponent.godMode=true;}
                        if(body.currentVehicle!=null && Time.realtimeSinceStartup-spawned>10)body.currentVehicle.EjectPassenger();
                    }
                    foreach(var director in UnityEngine.Object.FindObjectsOfType<CombatDirector>())director.enabled=false;
                    yield return new WaitForSecondsRealtime(.25f);
                }
                if(File.Exists(receipt))checks.AddRange(File.ReadAllLines(receipt));else checks.Add("FAIL production resources did not finish within native run deadline");
                bool active=(bool)AccessTools.Field(type,"active").GetValue(adapter);
                checks.Add((active?"PASS ":"FAIL ")+"real RoR2 run has active Minecraft takeover");
                if(active)yield return VisualRegression(checks,type,adapter,body);
                if(active && Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftGameplayCombatProbe")>=0)yield return CombatAndStage(checks,type,adapter,body);
            } finally {if(body!=null)body.healthComponent.godMode=priorGod;patch.UnpatchSelf();CursorSafety.BackgroundTest=false;NetworkManagerSystem.singleton.StopHost();}
        }
        private static IEnumerator VisualRegression(List<string> checks,Type type,object adapter,CharacterBody body) {
            string folder=BepInEx.Paths.ConfigPath,request=Path.Combine(folder,"rorcraft-visual-request.txt"),done=Path.Combine(folder,"rorcraft-visual-done.txt");
            var renderer=AccessTools.Field(type,"renderer").GetValue(adapter);
            try {
                foreach(string mode in new[]{"back","front","inventory","mining","first"}) {
                    if(File.Exists(done))File.Delete(done);File.WriteAllText(request,mode);
                    float deadline=Time.realtimeSinceStartup+15;
                    while(Time.realtimeSinceStartup<deadline) {
                        if(File.Exists(done)){if(File.ReadAllText(done).StartsWith("PASS "+mode+" "))break;File.Delete(done);}
                        yield return new WaitForSecondsRealtime(.1f);
                    }
                    if(!File.Exists(done) || !File.ReadAllText(done).StartsWith("PASS "+mode+" ")){checks.Add("FAIL visual mode timeout "+mode);yield break;}
                    checks.Add(File.ReadAllText(done));yield return new WaitForSecondsRealtime(.3f);
                    if(mode=="back" || mode=="front") {
                        var player=(List<GameObject>)AccessTools.Field(renderer.GetType(),"player").GetValue(renderer);
                        int vertices=0;foreach(var go in player)if(go!=null && go.activeInHierarchy && go.GetComponent<MeshRenderer>().enabled)vertices+=go.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                        checks.Add((vertices>=144?"PASS ":"FAIL ")+"F5 "+mode+" visible exported player vertices="+vertices);
                        CaptureRegression(player,folder,mode,mode=="front");
                    } else if(mode=="inventory") {
                        var hud=(Texture2D)AccessTools.Field(renderer.GetType(),"Hud").GetValue(renderer);
                        if(hud==null)checks.Add("FAIL inventory overlay absent");else {File.WriteAllBytes(Path.Combine(folder,"rorcraft-regression-inventory.png"),hud.EncodeToPNG());checks.Add("PASS actual inventory overlay captured; visual inspection required");}
                    } else if(mode=="mining") {
                        var scene=(List<GameObject>)AccessTools.Field(renderer.GetType(),"scene").GetValue(renderer);
                        var cracks=new List<GameObject>();
                        foreach(var go in scene)if(go!=null && go.activeInHierarchy){var mesh=go.GetComponent<MeshFilter>().sharedMesh;if(mesh.vertexCount==36 && mesh.GetIndexCount(1)>0)cracks.Add(go);}
                        checks.Add((cracks.Count>0?"PASS ":"FAIL ")+"mining exported transparent crack geometry="+cracks.Count);
                        if(cracks.Count>0)CaptureRegression(cracks,folder,"mining",true,((Dictionary<string,GameObject>)AccessTools.Field(renderer.GetType(),"sections").GetValue(renderer)).Values);
                        yield return new WaitForSecondsRealtime(10);
                    }
                }
                // Exercise the installed incoming-hit hook, not just the formula.
                var master=new MasterSummon{masterPrefab=MasterCatalog.FindMasterPrefab("BeetleMaster"),position=body.footPosition+Vector3.forward*5,rotation=Quaternion.identity,teamIndexOverride=TeamIndex.Monster,ignoreTeamMemberLimit=true}.Perform();
                if(master==null){checks.Add("FAIL balance attacker unavailable");yield break;}
                yield return new WaitForSecondsRealtime(.5f);
                try {
                    var attacker=master.GetBody();foreach(var ai in master.GetComponents<RoR2.CharacterAI.BaseAI>())ai.enabled=false;
                    var hit=new DamageInfo{damage=20,attacker=attacker.gameObject,position=body.corePosition,procCoefficient=0};
                    AccessTools.Method(type,"OnIncomingDamageServer").Invoke(adapter,new object[]{hit});
                    checks.Add((Math.Abs(hit.damage-13)<.01f?"PASS ":"FAIL ")+"unarmored native enemy hit 20 -> "+hit.damage+" (35% survival reduction)");
                    bool god=body.healthComponent.godMode;float hp=body.healthComponent.health;
                    try {
                        body.healthComponent.godMode=false;body.healthComponent.health=body.maxHealth;
                        for(int i=0;i<4;i++)body.healthComponent.TakeDamage(new DamageInfo{damage=20,attacker=attacker.gameObject,position=body.corePosition,procCoefficient=0});
                        float lost=body.maxHealth-body.healthComponent.health;
                        checks.Add((body.healthComponent.alive && lost>0 && lost<=52.1f?"PASS ":"FAIL ")+"four real unarmored native melee hits: lost="+lost+" remaining="+body.healthComponent.health);
                    } finally {body.healthComponent.godMode=god;body.healthComponent.health=hp;}
                    var hazard=new DamageInfo{damage=20,attacker=attacker.gameObject,damageType=DamageType.FallDamage};
                    AccessTools.Method(type,"OnIncomingDamageServer").Invoke(adapter,new object[]{hazard});
                    checks.Add((hazard.damage==20?"PASS ":"FAIL ")+"fall/hazard bypasses melee assistance");
                    float iron=SurvivalBalance.EquipmentMultiplier(13,110,15,0),diamond=SurvivalBalance.EquipmentMultiplier(13,110,20,8);
                    checks.Add((diamond<iron && iron<.5f && diamond>.19f?"PASS ":"FAIL ")+"equipment progression iron="+iron+" diamond="+diamond);
                    if(File.Exists(done))File.Delete(done);File.WriteAllText(request,"armor");
                    float until=Time.realtimeSinceStartup+10;
                    while(Time.realtimeSinceStartup<until){if(File.Exists(done)){if(File.ReadAllText(done).StartsWith("PASS armor "))break;File.Delete(done);}yield return new WaitForSecondsRealtime(.1f);}
                    var armored=new DamageInfo{damage=20,attacker=attacker.gameObject,position=body.corePosition,procCoefficient=0};
                    AccessTools.Method(type,"OnIncomingDamageServer").Invoke(adapter,new object[]{armored});
                    checks.Add((armored.damage>0 && armored.damage<12?"PASS ":"FAIL ")+"equipped real Minecraft iron chestplate reduces native hit below 13: "+armored.damage);
                } finally {UnityEngine.Object.Destroy(master.gameObject);}
            } finally {if(File.Exists(request))File.Delete(request);if(File.Exists(done))File.Delete(done);}
        }
        private static void CaptureRegression(List<GameObject> meshes,string folder,string name,bool front,IEnumerable<GameObject> context=null) {
            var layers=new Dictionary<GameObject,int>();Bounds bounds=new Bounds();bool first=true;
            foreach(var go in meshes)if(go!=null && go.activeInHierarchy){layers[go]=go.layer;go.layer=31;var b=go.GetComponent<Renderer>().bounds;if(first){bounds=b;first=false;}else bounds.Encapsulate(b);}
            if(first)throw new Exception("No regression geometry "+name);
            if(context!=null)foreach(var go in context)if(go!=null && go.activeInHierarchy && !layers.ContainsKey(go)){layers[go]=go.layer;go.layer=31;}
            var obj=new GameObject("Regression camera");var camera=obj.AddComponent<Camera>();camera.enabled=false;camera.renderingPath=RenderingPath.DeferredShading;
            var lightObject=new GameObject("Regression light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.cullingMask=1<<31;light.intensity=1;lightObject.transform.rotation=Quaternion.Euler(35,20,0);
            var rt=new RenderTexture(960,540,24);rt.Create();var image=new Texture2D(960,540,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.28f,.3f);camera.fieldOfView=60;camera.aspect=960f/540;camera.nearClipPlane=.05f;camera.farClipPlane=30;camera.targetTexture=rt;
            camera.transform.position=bounds.center+new Vector3(.6f,.25f,front?-4:4);camera.transform.LookAt(bounds.center);
            try{camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"rorcraft-regression-"+name+".png"),image.EncodeToPNG());}
            finally{foreach(var kv in layers)kv.Key.layer=kv.Value;camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(obj);UnityEngine.Object.Destroy(lightObject);}
        }
        private static IEnumerator CombatAndStage(List<string> checks,Type type,object adapter,CharacterBody body) {
            string folder=BepInEx.Paths.ConfigPath;
            uint moneyBefore=body.master.money;
            string mobReceipt=System.IO.Path.Combine(folder,"rorcraft-gameplay-mobs.txt"),mobFailure=System.IO.Path.Combine(folder,"rorcraft-combat-failure.txt");
            float mobDeadline=Time.realtimeSinceStartup+70;
            bool captured=false;
            while(!File.Exists(mobReceipt) && Time.realtimeSinceStartup<mobDeadline) {
                if(File.Exists(mobFailure)){checks.AddRange(File.ReadAllLines(mobFailure));yield break;}
                string ready=System.IO.Path.Combine(folder,"rorcraft-mob-preview-ready.txt");
                if(!captured && File.Exists(ready)) {
                    try {CaptureMobs(checks,type,adapter,ready);}catch(Exception error){checks.Add("FAIL native mob capture: "+error);}
                    File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-mob-preview-done.txt"),"done");captured=true;
                }
                yield return new WaitForSecondsRealtime(.25f);
            }
            if(!File.Exists(mobReceipt)){checks.Add("FAIL mob physics/loot/crafting test timed out");yield break;}
            checks.AddRange(File.ReadAllLines(mobReceipt));
            bool enemyProbe=Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftEnemyProbe")>=0;
            if(enemyProbe)checks.Add((body.master.money>moneyBefore?"PASS ":"FAIL ")+"Minecraft mob player kills awarded native RoR2 money");
            var item=ItemCatalog.FindItemIndex("BleedOnHit");
            if(item==ItemIndex.None){checks.Add("FAIL bleed item unavailable");yield break;}
            int old=body.inventory.GetItemCount(item);body.inventory.GiveItem(item,10);
            var summoned=new MasterSummon{masterPrefab=MasterCatalog.FindMasterPrefab("BeetleMaster"),position=body.footPosition+new Vector3(3,0,0),rotation=Quaternion.identity,teamIndexOverride=TeamIndex.Monster,ignoreTeamMemberLimit=true}.Perform();
            if(summoned==null){checks.Add("FAIL native target summon failed");yield break;}
            CharacterBody target=null;
            float deadline=Time.realtimeSinceStartup+15;
            while(target==null && Time.realtimeSinceStartup<deadline){target=summoned.GetBody();yield return new WaitForSecondsRealtime(.1f);}
            if(target==null){checks.Add("FAIL native target body missing");yield break;}
            foreach(var ai in summoned.GetComponents<RoR2.CharacterAI.BaseAI>())ai.enabled=false;
            if(target.characterMotor!=null)target.characterMotor.enabled=false; // stationary test target, no fall off native cliffs
            target.baseMaxHealth=10000;target.baseRegen=0;target.baseArmor=0;target.RecalculateStats();target.healthComponent.health=target.maxHealth;
            testVictim=target;testAttacker=body;melee=null;
            GlobalEventManager.onServerDamageDealt+=Damage;
            try {
                uint id=(uint)target.GetInstanceID();
                var actors=(Dictionary<uint,CharacterBody>)AccessTools.Field(type,"actors").GetValue(adapter);
                deadline=Time.realtimeSinceStartup+15;
                while(!actors.ContainsKey(id) && Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.1f);
                if(!actors.ContainsKey(id)){checks.Add("FAIL native target not exported; active="+AccessTools.Field(type,"active").GetValue(adapter)+" target="+target.footPosition+" player="+body.footPosition+" alive="+target.healthComponent.alive);yield break;}
                File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-combat-target.txt"),id.ToString());
                string receipt=System.IO.Path.Combine(folder,"rorcraft-combat-mc.txt"),failure=System.IO.Path.Combine(folder,"rorcraft-combat-failure.txt");
                deadline=Time.realtimeSinceStartup+40;bool bleed=false;
                while(Time.realtimeSinceStartup<deadline && !(File.Exists(receipt) && melee!=null && bleed)) {
                    var dots=DotController.FindDotController(target.gameObject);bleed|=dots!=null && dots.HasDotActive(DotController.DotIndex.Bleed);
                    if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}
                    yield return new WaitForSecondsRealtime(.1f);
                }
                if(!File.Exists(receipt) || melee==null){checks.Add("FAIL no vanilla MC attack reached native damage receiver");yield break;}
                var lines=File.ReadAllLines(receipt);checks.Add(lines[0]);
                float mc=float.Parse(lines[1],System.Globalization.CultureInfo.InvariantCulture);
                float expected=mc*body.damage/6;
                checks.Add((Math.Abs(melee.damage-expected)<.1f && melee.attacker==body.gameObject && melee.procCoefficient==1?"PASS ":"FAIL ")+"native damage "+melee.damage+" matches MC "+mc+" * survivor damage/6; real attacker and procCoefficient1");
                checks.Add((bleed?"PASS ":"FAIL ")+"real Tri-Tip Dagger applied native Bleed DOT from crafted Minecraft sword hit (ten test stacks guarantee proc)");
                if(!bleed)yield break;
                checks.Add((target.healthComponent.health<target.maxHealth?"PASS ":"FAIL ")+"native victim lost health");
                DotController.RemoveAllDots(target.gameObject);
                melee=null;bleed=false;
                File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-arrow-request.txt"),"release");
                string arrow=System.IO.Path.Combine(folder,"rorcraft-arrow-mc.txt");
                deadline=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<deadline && !(File.Exists(arrow) && melee!=null && bleed)) {
                    var arrowDots=DotController.FindDotController(target.gameObject);bleed|=arrowDots!=null && arrowDots.HasDotActive(DotController.DotIndex.Bleed);
                    if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}
                    yield return new WaitForSecondsRealtime(.1f);
                }
                if(!File.Exists(arrow) || melee==null){checks.Add("FAIL actual crafted bow arrow did not reach native victim");yield break;}
                lines=File.ReadAllLines(arrow);checks.Add(lines[0]);
                mc=float.Parse(lines[1],System.Globalization.CultureInfo.InvariantCulture);expected=mc*body.damage/6;
                checks.Add((Math.Abs(melee.damage-expected)<.1f && melee.attacker==body.gameObject && melee.procCoefficient==1?"PASS ":"FAIL ")+"actual bow arrow native scaled damage="+melee.damage+", MC="+mc+", attacker/proc1");
                checks.Add((bleed?"PASS ":"FAIL ")+"crafted bow arrow reapplied actual native Tri-Tip Dagger Bleed after clearing melee DOT");
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftCrowbarProbe")>=0)yield return Crowbar(checks,body,failure);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-rorcraftMobProcProbe")>=0) {
                    body.RecalculateStats();
                    File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-proc-request.txt"),"hit");
                    string positive=System.IO.Path.Combine(folder,"rorcraft-proc-positive.txt"),negative=System.IO.Path.Combine(folder,"rorcraft-proc-negative.txt");
                    deadline=Time.realtimeSinceStartup+30;
                    while(!File.Exists(positive) && Time.realtimeSinceStartup<deadline){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                    if(!File.Exists(positive)){checks.Add("FAIL MC mob Bleed positive timeout");yield break;}
                    checks.AddRange(File.ReadAllLines(positive));
                    int currentStacks=body.inventory.GetItemCount(item);body.inventory.RemoveItem(item,currentStacks);body.RecalculateStats();
                    File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-proc-zero.txt"),"hit without item");
                    deadline=Time.realtimeSinceStartup+15;
                    while(!File.Exists(negative) && Time.realtimeSinceStartup<deadline){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                    if(!File.Exists(negative)){checks.Add("FAIL MC mob Bleed negative timeout");yield break;}
                    checks.AddRange(File.ReadAllLines(negative));
                    var slowItem=RoR2Content.Items.SlowOnHit.itemIndex;int originalSlow=body.inventory.GetItemCount(slowItem);
                    if(originalSlow>0)body.inventory.RemoveItem(slowItem,originalSlow);body.inventory.GiveItem(slowItem,2);body.RecalculateStats();
                    string slowPositive=System.IO.Path.Combine(folder,"rorcraft-slow-positive.txt"),slowNegative=System.IO.Path.Combine(folder,"rorcraft-slow-negative.txt");
                    File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-slow-request.txt"),"two Chronobauble, no Bleed");
                    deadline=Time.realtimeSinceStartup+30;
                    while(!File.Exists(slowPositive) && Time.realtimeSinceStartup<deadline){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                    if(!File.Exists(slowPositive)){checks.Add("FAIL MC mob slow positive timeout");yield break;}checks.AddRange(File.ReadAllLines(slowPositive));
                    body.inventory.RemoveItem(slowItem,2);body.RecalculateStats();File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-slow-zero.txt"),"hit without slow item");
                    deadline=Time.realtimeSinceStartup+15;
                    while(!File.Exists(slowNegative) && Time.realtimeSinceStartup<deadline){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                    if(!File.Exists(slowNegative)){checks.Add("FAIL MC mob slow negative timeout");yield break;}checks.AddRange(File.ReadAllLines(slowNegative));
                    if(originalSlow>0)body.inventory.GiveItem(slowItem,originalSlow);
                    var procItems=new[]{RoR2Content.Items.IgniteOnKill.itemIndex,RoR2Content.Items.ExplodeOnDeath.itemIndex,RoR2Content.Items.Tooth.itemIndex};
                    int[] savedItems=new int[3];for(int n=0;n<3;n++){savedItems[n]=body.inventory.GetItemCount(procItems[n]);if(savedItems[n]>0)body.inventory.RemoveItem(procItems[n],savedItems[n]);}
                    var protectedNative=new List<KeyValuePair<HealthComponent,bool>>();
                    foreach(var other in CharacterBody.readOnlyInstancesList)if(other!=null && other!=body && other!=target && other.healthComponent!=null){protectedNative.Add(new KeyValuePair<HealthComponent,bool>(other.healthComponent,other.healthComponent.godMode));other.healthComponent.godMode=true;}
                    RoR2.Run.instance.SetForcePauseRunStopwatch(true);
                    float savedRegen=body.baseRegen,savedLevelRegen=body.levelRegen;body.baseRegen=0;body.levelRegen=0;
                    ulong preAreaXP=TeamManager.instance.GetTeamExperience(TeamIndex.Player),fixtureXP=TeamManager.GetExperienceForLevel(10);
                    // Keep healing measurement away from a level threshold; XP increments are still asserted per kill.
                    AccessTools.Method(typeof(TeamManager),"SetTeamExperience").Invoke(TeamManager.instance,new object[]{TeamIndex.Player,fixtureXP});body.RecalculateStats();
                    foreach(string mode in new[]{"none","gas","wisp","tooth","combo","herd","gas2","wisp2","tooth2"}){
                        for(int n=0;n<3;n++){int count=body.inventory.GetItemCount(procItems[n]);if(count>0)body.inventory.RemoveItem(procItems[n],count);}
                        if(mode=="gas" || mode=="combo" || mode=="herd" || mode=="gas2")body.inventory.GiveItem(procItems[0],mode=="gas2"?2:1);
                        if(mode=="wisp" || mode=="combo" || mode=="herd" || mode=="wisp2")body.inventory.GiveItem(procItems[1],mode=="wisp2"?2:1);
                        if(mode=="herd")body.inventory.GiveItem(procItems[2],1);
                        if(mode=="tooth" || mode=="tooth2"){body.inventory.GiveItem(procItems[2],mode=="tooth2"?2:1);body.healthComponent.Networkhealth=40;}
                        body.RecalculateStats();yield return new WaitForSecondsRealtime(1);
                        float nativeBefore=target.healthComponent.health,healBefore=body.healthComponent.health;uint areaMoneyBefore=body.master.money;ulong areaExperienceBefore=TeamManager.instance.GetTeamExperience(TeamIndex.Player);
                        var center=(Vector3)AccessTools.Method(type,"ToMinecraft").Invoke(adapter,new object[]{target.corePosition});
                        string point=center.x.ToString(System.Globalization.CultureInfo.InvariantCulture)+" "+center.y.ToString(System.Globalization.CultureInfo.InvariantCulture)+" "+center.z.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-area-"+mode+"-request.txt"),point);
                        string areaReceipt=System.IO.Path.Combine(folder,"rorcraft-area-"+mode+"-done.txt");deadline=Time.realtimeSinceStartup+25;
                        bool areaRendered=false;string preview=System.IO.Path.Combine(folder,"rorcraft-area-preview.txt");
                        while(!File.Exists(areaReceipt) && Time.realtimeSinceStartup<deadline){
                            if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}
                            if(mode=="gas" && !areaRendered && File.Exists(preview)){yield return new WaitForSecondsRealtime(.15f);CaptureMobs(checks,type,adapter,preview);areaRendered=true;}
                            yield return new WaitForSecondsRealtime(.1f);
                        }
                        if(!File.Exists(areaReceipt)){checks.Add("FAIL area "+mode+" timeout");yield break;}checks.AddRange(File.ReadAllLines(areaReceipt));
                        float nativeLost=nativeBefore-target.healthComponent.health;
                        checks.Add((mode=="gas"||mode=="wisp"||mode=="combo"||mode=="herd"||mode=="gas2"||mode=="wisp2"?nativeLost>0:Math.Abs(nativeLost)<.1f)?"PASS "+mode+" MC kill -> actual native enemy damage="+nativeLost:"FAIL "+mode+" native area damage="+nativeLost);
                        uint expectedGold=(uint)RoR2.Run.instance.GetDifficultyScaledCost(8)*(uint)(mode=="herd"?8:mode=="wisp"||mode=="wisp2"||mode=="combo"?2:1);
                        checks.Add((body.master.money-areaMoneyBefore==expectedGold?"PASS ":"FAIL ")+mode+" native kill rewards="+(body.master.money-areaMoneyBefore)+" expected="+expectedGold);
                        ulong xpGained=TeamManager.instance.GetTeamExperience(TeamIndex.Player)-areaExperienceBefore;
                        ulong expectedXP=(ulong)RoR2.Run.instance.GetDifficultyScaledCost(4)*(ulong)(mode=="herd"?8:mode=="wisp"||mode=="wisp2"||mode=="combo"?2:1);
                        checks.Add((xpGained==expectedXP?"PASS ":"FAIL ")+mode+" native team XP="+xpGained+" expected="+expectedXP);
                        if(mode=="tooth" || mode=="tooth2"){
                            float healed=body.healthComponent.health-healBefore,expectedHeal=8+.02f*(mode=="tooth2"?2:1)*body.healthComponent.fullHealth;
                            checks.Add((Math.Abs(healed-expectedHeal)<.1f?"PASS ":"FAIL ")+"Monster Tooth pickup actual native HP heal="+healed+" expected="+expectedHeal);
                        }
                    }
                    yield return Reverse(checks,type,adapter,body,target,procItems,failure);
                    for(int n=0;n<3;n++){int count=body.inventory.GetItemCount(procItems[n]);if(count>0)body.inventory.RemoveItem(procItems[n],count);if(savedItems[n]>0)body.inventory.GiveItem(procItems[n],savedItems[n]);}
                    checks.Add("INFO effect queue peak="+MinecraftEnemyCombat.queuedPeak+", bounded shared ring15; eight-mob herd receipt covers24 effects");
                    foreach(var protectedEntry in protectedNative)if(protectedEntry.Key!=null)protectedEntry.Key.godMode=protectedEntry.Value;
                    ulong gainedFixtureXP=TeamManager.instance.GetTeamExperience(TeamIndex.Player)-fixtureXP;
                    AccessTools.Method(typeof(TeamManager),"SetTeamExperience").Invoke(TeamManager.instance,new object[]{TeamIndex.Player,preAreaXP+gainedFixtureXP});
                    RoR2.Run.instance.SetForcePauseRunStopwatch(false);body.levelRegen=savedLevelRegen;body.baseRegen=savedRegen;body.healthComponent.Networkhealth=body.healthComponent.fullHealth;body.inventory.GiveItem(item,currentStacks);body.RecalculateStats();
                }
                if(enemyProbe) {
                    DotController.RemoveAllDots(target.gameObject);environment=null;
                    body.healthComponent.godMode=false;body.baseRegen=0;body.RecalculateStats();float healthBefore=body.healthComponent.health,minHealth=healthBefore;
                    File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-enemy-request.txt"),"start");
                    string ready=System.IO.Path.Combine(folder,"rorcraft-enemy-ready.txt"),enemy=System.IO.Path.Combine(folder,"rorcraft-enemy-mc.txt");
                    deadline=Time.realtimeSinceStartup+30;bool rendered=false;
                    while(Time.realtimeSinceStartup<deadline && !(File.Exists(enemy) && environment!=null)) {
                        minHealth=Math.Min(minHealth,body.healthComponent.health);
                        if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}
                        if(!rendered && File.Exists(ready)) {
                            yield return new WaitForSecondsRealtime(1);
                            CaptureMobs(checks,type,adapter,ready);
                            File.WriteAllText(System.IO.Path.Combine(folder,"rorcraft-enemy-done.txt"),"ignite");rendered=true;
                        }
                        yield return new WaitForSecondsRealtime(.1f);
                    }
                    if(File.Exists(enemy))checks.AddRange(File.ReadAllLines(enemy));else checks.Add("FAIL zombie/creeper production receipt missing");
                    checks.Add((environment!=null && environment.attacker==null && environment.procCoefficient==0 && !environment.crit?"PASS ":"FAIL ")+"actual creeper explosion damaged native enemy without player attacker, crit or player proc");
                    var hostileDots=DotController.FindDotController(target.gameObject);
                    checks.Add((hostileDots==null || !hostileDots.HasDotActive(DotController.DotIndex.Bleed)?"PASS ":"FAIL ")+"creeper blast did not trigger player's Tri-Tip Dagger");
                    minHealth=Math.Min(minHealth,body.healthComponent.health);
                    checks.Add((minHealth<healthBefore?"PASS ":"FAIL ")+"Minecraft hostile damage lowered linked native player HP (regen disabled in fixture): "+healthBefore+" -> min "+minHealth+" final "+body.healthComponent.health+" god="+body.healthComponent.godMode);
                    body.healthComponent.godMode=true;
                }
                body.inventory.RemoveItem(item,body.inventory.GetItemCount(item)-old);
                // Use native stage completion + scene transfer, never fake the shared stage number.
                RoR2.Run.instance.AdvanceStage(SceneCatalog.FindSceneDef("golemplains"));
                string stage=System.IO.Path.Combine(folder,"rorcraft-gameplay-stage.txt");
                deadline=Time.realtimeSinceStartup+100;
                while(!File.Exists(stage) && Time.realtimeSinceStartup<deadline) {
                    if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}
                    var local=LocalUserManager.GetFirstLocalUser();var current=local.currentNetworkUser==null?null:local.currentNetworkUser.GetCurrentBody();
                    if(current!=null){current.healthComponent.godMode=true;if(current.currentVehicle!=null)current.currentVehicle.EjectPassenger();}
                    foreach(var director in UnityEngine.Object.FindObjectsOfType<CombatDirector>())director.enabled=false;
                    yield return new WaitForSecondsRealtime(.25f);
                }
                if(File.Exists(stage))checks.AddRange(File.ReadAllLines(stage));else checks.Add("FAIL second-stage resource/inventory receipt missing");
                checks.Add((RoR2.Run.instance!=null && RoR2.Run.instance.stageClearCount>=1 && SceneCatalog.GetSceneDefForCurrentScene().cachedName=="golemplains"?"PASS ":"FAIL ")+"native run really advanced to golemplains");
            } finally {GlobalEventManager.onServerDamageDealt-=Damage;testVictim=null;testAttacker=null;melee=null;if(body!=null)body.inventory.RemoveItem(item,Math.Max(0,body.inventory.GetItemCount(item)-old));}
        }
        private static IEnumerator Crowbar(List<string> checks,CharacterBody body,string failure){
            var item=RoR2Content.Items.Crowbar.itemIndex;int saved=body.inventory.GetItemCount(item);
            var bleed=RoR2Content.Items.BleedOnHit.itemIndex;int savedBleed=body.inventory.GetItemCount(bleed);body.inventory.RemoveItem(bleed,savedBleed);
            try{
                int count=0;foreach(string mode in new[]{"zero","one","two"}){
                    int present=body.inventory.GetItemCount(item);if(present>0)body.inventory.RemoveItem(item,present);if(count>0)body.inventory.GiveItem(item,count);body.RecalculateStats();yield return new WaitForSecondsRealtime(.5f);
                    string prefix=System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-crowbar-"+mode);File.WriteAllText(prefix+"-request.txt",count.ToString());
                    float end=Time.realtimeSinceStartup+25;while(!File.Exists(prefix+"-done.txt") && Time.realtimeSinceStartup<end){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                    if(!File.Exists(prefix+"-done.txt")){checks.Add("FAIL Crowbar "+mode+" timeout");yield break;}checks.AddRange(File.ReadAllLines(prefix+"-done.txt"));count++;
                }
            }finally{int present=body.inventory.GetItemCount(item);if(present>0)body.inventory.RemoveItem(item,present);if(saved>0)body.inventory.GiveItem(item,saved);if(savedBleed>0)body.inventory.GiveItem(bleed,savedBleed);body.RecalculateStats();}
        }
        private static IEnumerator Reverse(List<string> checks,Type type,object adapter,CharacterBody body,CharacterBody target,ItemIndex[] items,string failure){
            string folder=BepInEx.Paths.ConfigPath;
            foreach(string mode in new[]{"none","gas","wisp","combo","unowned"}){
                for(int n=0;n<3;n++){int count=body.inventory.GetItemCount(items[n]);if(count>0)body.inventory.RemoveItem(items[n],count);}
                if(mode=="gas" || mode=="combo" || mode=="unowned")body.inventory.GiveItem(items[0],1);
                if(mode=="wisp" || mode=="combo" || mode=="unowned")body.inventory.GiveItem(items[1],1);
                body.inventory.GiveItem(items[2],1);body.RecalculateStats();
                var master=new MasterSummon{masterPrefab=MasterCatalog.FindMasterPrefab("BeetleMaster"),position=target.footPosition,rotation=Quaternion.identity,teamIndexOverride=TeamIndex.Monster,ignoreTeamMemberLimit=true}.Perform();
                CharacterBody victim=null;float end=Time.realtimeSinceStartup+10;while(victim==null && Time.realtimeSinceStartup<end){victim=master.GetBody();yield return new WaitForSecondsRealtime(.1f);}
                if(victim==null){checks.Add("FAIL reverse target not spawned");yield break;}
                foreach(var ai in master.GetComponents<RoR2.CharacterAI.BaseAI>())ai.enabled=false;if(victim.characterMotor!=null)victim.characterMotor.enabled=false;
                var pos=(Vector3)AccessTools.Method(type,"ToMinecraft").Invoke(adapter,new object[]{victim.corePosition});
                string prefix=System.IO.Path.Combine(folder,"rorcraft-reverse-"+mode);
                File.WriteAllText(prefix+"-request.txt",pos.x.ToString(System.Globalization.CultureInfo.InvariantCulture)+" "+pos.y.ToString(System.Globalization.CultureInfo.InvariantCulture)+" "+pos.z.ToString(System.Globalization.CultureInfo.InvariantCulture));
                end=Time.realtimeSinceStartup+15;while(!File.Exists(prefix+"-ready.txt") && Time.realtimeSinceStartup<end){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                if(!File.Exists(prefix+"-ready.txt")){checks.Add("FAIL reverse MC ready timeout");yield break;}
                int before=MinecraftEnemyCombat.reversePulses;
                victim.healthComponent.TakeDamage(new DamageInfo{attacker=mode=="unowned"?null:body.gameObject,damage=100000,procCoefficient=0,position=victim.corePosition});
                File.WriteAllText(prefix+"-killed.txt","actual native death");
                end=Time.realtimeSinceStartup+20;while(!File.Exists(prefix+"-done.txt") && Time.realtimeSinceStartup<end){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                if(!File.Exists(prefix+"-done.txt")){checks.Add("FAIL reverse "+mode+" timeout");yield break;}checks.AddRange(File.ReadAllLines(prefix+"-done.txt"));
                checks.Add((MinecraftEnemyCombat.reversePulses-before==(mode=="none"||mode=="unowned"?0:1)?"PASS ":"FAIL ")+"native death attribution/event count "+mode+" = "+(MinecraftEnemyCombat.reversePulses-before));
            }
            ulong prior=TeamManager.instance.GetTeamExperience(TeamIndex.Monster);
            try{
                AccessTools.Method(typeof(TeamManager),"SetTeamExperience").Invoke(TeamManager.instance,new object[]{TeamIndex.Monster,TeamManager.GetExperienceForLevel(5)});yield return new WaitForSecondsRealtime(.5f);
                string level=System.IO.Path.Combine(folder,"rorcraft-reverse-level");File.WriteAllText(level+"-request.txt","0 0 0");
                float end=Time.realtimeSinceStartup+10;while(!File.Exists(level+"-done.txt") && Time.realtimeSinceStartup<end){if(File.Exists(failure)){checks.AddRange(File.ReadAllLines(failure));yield break;}yield return new WaitForSecondsRealtime(.1f);}
                if(File.Exists(level+"-done.txt"))checks.AddRange(File.ReadAllLines(level+"-done.txt"));else checks.Add("FAIL native level snapshot/MC stats timeout");
            }finally{AccessTools.Method(typeof(TeamManager),"SetTeamExperience").Invoke(TeamManager.instance,new object[]{TeamIndex.Monster,prior});}
        }
        private static void CaptureMobs(List<string> checks,Type type,object adapter,string ready) {
            var renderer=AccessTools.Field(type,"renderer").GetValue(adapter);
            var scene=(List<GameObject>)AccessTools.Field(renderer.GetType(),"scene").GetValue(renderer);
            int vertices=0;var layers=new Dictionary<GameObject,int>();
            foreach(var go in scene){if(go==null)continue;layers[go]=go.layer;go.layer=31;var mesh=go.GetComponent<MeshFilter>();if(mesh!=null && mesh.sharedMesh!=null)vertices+=mesh.sharedMesh.vertexCount;}
            if(vertices<100)throw new Exception("Exported mob scene mesh missing: "+vertices);
            var origin=(Vector3)AccessTools.Field(type,"worldOrigin").GetValue(adapter);
            var cameraObject=new GameObject("Owned native mob preview camera");var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.renderingPath=RenderingPath.DeferredShading;
            var lampObject=new GameObject("Owned native mob preview light");var lamp=lampObject.AddComponent<Light>();lamp.type=LightType.Directional;lamp.cullingMask=1<<31;lamp.intensity=1;lampObject.transform.rotation=Quaternion.Euler(35,20,0);
            var target=new RenderTexture(512,512,24);target.Create();var image=new Texture2D(512,512,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.2f,.24f);camera.orthographic=false;camera.fieldOfView=30;camera.aspect=1;camera.allowHDR=false;camera.allowMSAA=false;camera.nearClipPlane=.05f;camera.farClipPlane=20;camera.targetTexture=target;
            try {
                var seen=new HashSet<string>();
                foreach(string line in File.ReadAllLines(ready)) {
                    var parts=line.Split('|');if(!seen.Add(parts[0]))continue;
                    float x=float.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture),y=float.Parse(parts[2],System.Globalization.CultureInfo.InvariantCulture),z=float.Parse(parts[3],System.Globalization.CultureInfo.InvariantCulture);
                    var center=new Vector3(-x,y+.6f,z)+origin;
                    camera.transform.position=center+new Vector3(-4,2,-4);camera.transform.LookAt(center);
                    camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
                    if(camera.actualRenderingPath!=RenderingPath.DeferredShading)throw new Exception("Mob preview must use the actual game deferred path");
                    string name=parts[0].Replace("entity.minecraft.","");
                    File.WriteAllBytes(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"rorcraft-mob-"+name+".png"),image.EncodeToPNG());
                }
                checks.Add("PASS actual exported native GPU scene captured for "+seen.Count+" mob types, vertices="+vertices+" (agent visual inspection still required)");
            } finally {foreach(var entry in layers)if(entry.Key!=null)entry.Key.layer=entry.Value;camera.targetTexture=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(lampObject);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);}
        }
    }
}

