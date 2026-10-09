using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using RoR2;
using UnityEngine;

namespace RoRCraft
{
    [BepInPlugin("cz.jirka.rorcraft-skycraft", "RoRCraft SkyCraft adapter", "0.8.0")]
    [DefaultExecutionOrder(10000)]
    public sealed class RoRCraftPlugin : BaseUnityPlugin, ICameraStateProvider, IOnIncomingDamageServerReceiver
    {
        private SkyMemory link;
        private bool nativeMode,nativeEnabled=true;
        private BlockSurvivor avatar;
        private CharacterBody avatarBody;
        private string skinPath;
        private SkyRenderer renderer;
        private CharacterBody controlled;
        private PlayerCharacterMasterController controller;
        private bool motorWasEnabled,kinematicWasEnabled,controllerWasEnabled,requested,active;
        private readonly Dictionary<Renderer,bool> hidden=new Dictionary<Renderer,bool>();
        private readonly Dictionary<uint,CharacterBody> actors=new Dictionary<uint,CharacterBody>();
        private byte[] mc;
        private Vector3 position;
        private Vector3 worldOrigin;
        private bool originReady;
        private bool dead;
        private float deathStarted;
        private readonly Dictionary<Transform,BlockSurvivor> lobbySkins=new Dictionary<Transform,BlockSurvivor>();
        private float nextLobbyScan;
        private string originScene;
        private CameraRigController rig;
        private int statsSequence;
        private int hudSequence;
        private RoR2.UI.DifficultyBarController difficultyBar;
        private double appliedMcHealthDelta;
        private string upgradeText="";
        private float nextUpgradeText;
        private Run observedRun;
        private int runToken;
        private float blockedDamage;
        private bool shieldRaised,skillMotor;
        private EntityStateMachine motionState;
        private float motionStarted;
        private readonly KeyCode[] skillKeys={KeyCode.C,KeyCode.V,KeyCode.B,KeyCode.G};
        private readonly bool[] skillHeld=new bool[4];
        private readonly RaycastHit[] floorHits=new RaycastHit[32];
        private readonly RaycastHit[] movementHits=new RaycastHit[32];
        private bool floorReported;
        private int lastMcCounter=-1;
        private float lastMcFrame=-10;
        private const int DefaultWorldLayer=1;
        private float yaw,pitch,nextCollision,nextActors,nextHud;
        private int epoch=1,teleport=1,actorSequence;
        private readonly Dictionary<KeyCode,ushort> keys=new Dictionary<KeyCode,ushort> {
            {KeyCode.W,26},{KeyCode.A,4},{KeyCode.S,22},{KeyCode.D,7},{KeyCode.Space,44},
            {KeyCode.LeftShift,225},{KeyCode.LeftControl,224},{KeyCode.E,8},{KeyCode.Q,20},{KeyCode.T,23},
            {KeyCode.Escape,41},{KeyCode.Return,40},{KeyCode.Backspace,42},{KeyCode.Tab,43},
            {KeyCode.Alpha1,30},{KeyCode.Alpha2,31},{KeyCode.Alpha3,32},{KeyCode.Alpha4,33},
            {KeyCode.Alpha5,34},{KeyCode.Alpha6,35},{KeyCode.Alpha7,36},{KeyCode.Alpha8,37},{KeyCode.Alpha9,38},
            {KeyCode.O,18},{KeyCode.F5,62}
        };
        private string status="F8: activate Minecraft controls";
        private void Awake()
        {
            nativeMode=Config.Bind("Gameplay","NativeSurvivor",false,"Optional cosmetic mode. False enables actual Minecraft movement, inventory and combat through SkyCraft.").Value;
            if(nativeMode) {
                skinPath=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(GetType().Assembly.Location),"assets","player-skin.png");
                Logger.LogInfo("Native Minecraft survivor mode ready. Normal RoR2 controls, damage and items. F8 toggles appearance.");return;
            }
            try { link=new SkyMemory(); renderer=new SkyRenderer();link.I(0x944,0x31524F52); GlobalEventManager.onServerDamageDealt+=DamageDealt;
                Logger.LogInfo("SkyCraft protocol v10 host ready at "+SkyMemory.Name+". F8 toggles takeover; start the RoRCraft Minecraft instance."); }
            catch(Exception e) { Logger.LogError(e); enabled=false; }
        }
        private CharacterBody Player()
        {
            var user=LocalUserManager.GetFirstLocalUser(); return user==null ? null : user.cachedBody;
        }
        private void Begin(CharacterBody body)
        {
            RaycastHit startingGround;
            if(!Physics.Raycast(body.footPosition+Vector3.up*12,Vector3.down,out startingGround,84,LayerIndex.world.mask|DefaultWorldLayer,QueryTriggerInteraction.Ignore)) {
                requested=false;Logger.LogWarning("Minecraft takeover rejected: no RoR2 ground found below player. Native control retained.");return;
            }
            controlled=body; position=body.footPosition;shieldRaised=false;
            if(body.healthComponent!=null) body.healthComponent.AddOnIncomingDamageServerReceiver(this);
            rig=null;foreach(var candidate in CameraRigController.readOnlyInstancesList) if(candidate.localUserViewer==LocalUserManager.GetFirstLocalUser()) {rig=candidate;break;}
            var camera=rig==null ? Camera.main : rig.sceneCam; yaw=camera==null?0:camera.transform.eulerAngles.y; pitch=camera==null?0:camera.transform.eulerAngles.x;
            string sceneName=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if(!originReady || originScene!=sceneName) {
                worldOrigin=position-new Vector3(0,64,0);renderer.WorldOrigin=worldOrigin;
                originReady=true;originScene=sceneName;
            }
            if(rig!=null) rig.SetOverrideCam(this,0);
            if(pitch>180) pitch-=360;
            epoch++; teleport++; nextCollision=0;
            floorReported=false;
            if(body.characterMotor!=null) {
                motorWasEnabled=body.characterMotor.enabled; body.characterMotor.enabled=false;
                if(body.characterMotor.Motor!=null) {kinematicWasEnabled=body.characterMotor.Motor.enabled;body.characterMotor.Motor.enabled=false;}
            }
            controller=body.master==null ? null : body.master.GetComponent<PlayerCharacterMasterController>();
            // This master controller is also RoR2's player registration. Disabling
            // it removes the player from the run and can immediately trigger defeat.
            // Camera override blocks native user control; keep the master registered.
            if(controller!=null) controllerWasEnabled=controller.enabled;
            if(body.modelLocator!=null && body.modelLocator.modelTransform!=null)
                foreach(var r in body.modelLocator.modelTransform.GetComponentsInChildren<Renderer>()) { hidden[r]=r.enabled; r.enabled=false; }
            link.CollisionMessage(1,BitConverter.GetBytes(epoch)); active=true; renderer.Visible(true);
            link.Events(delegate(byte[] oldEvent) { }); // A previous Minecraft death is not a new run death.
            if(mc!=null && (BitConverter.ToInt32(mc,4)&2)!=0) {link.Input(1,41,1,0,0);link.Input(1,41,0,0,0);}
            appliedMcHealthDelta=link.I(0x984)==0x31524F52 ? link.D(0x988) : 0;
            Logger.LogInfo("Minecraft takeover started with RoR2 camera override and local world origin.");
        }
        private void End()
        {
            if(controlled!=null && controlled.healthComponent!=null) controlled.healthComponent.RemoveOnIncomingDamageServerReceiver(this);
            skillMotor=false;motionState=null;shieldRaised=false;
            for(int i=0;i<4;i++) skillHeld[i]=false;
            PushSkillButtons();
            if(rig!=null && rig.IsOverrideCam(this)) rig.SetOverrideCam(null,0);rig=null;
            if(link!=null) link.I(0x908,0);
            if(controlled!=null && controlled.characterMotor!=null) {
                if(controlled.characterMotor.Motor!=null) controlled.characterMotor.Motor.enabled=kinematicWasEnabled;
                controlled.characterMotor.enabled=motorWasEnabled;
            }
            if(controller!=null) controller.enabled=controllerWasEnabled;
            foreach(var item in hidden) if(item.Key!=null) item.Key.enabled=item.Value;
            hidden.Clear(); controlled=null; controller=null; active=false;
            if(renderer!=null) renderer.Visible(false);
            if(link!=null) link.Input(6,0,0,0,0);
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
        }
        private void Update()
        {
            if(nativeMode) {
                if(Input.GetKeyDown(KeyCode.F8)) nativeEnabled=!nativeEnabled;
                var body=Player();
                if(avatar!=null && (!nativeEnabled || body!=avatarBody)) {avatar.Dispose();avatar=null;avatarBody=null;}
                if(nativeEnabled && body!=null && avatar==null) {
                    try {avatar=new BlockSurvivor(body,skinPath);avatarBody=body;Logger.LogInfo("Player skin attached to "+body.name+"; native combat and inventory retained.");}
                    catch(Exception e) {nativeEnabled=false;Logger.LogError(e);}
                }
                return;
            }
            if(link==null) return;
            try {
                link.Heartbeat();
                if(Run.instance!=observedRun) {
                    observedRun=Run.instance;
                    if(observedRun!=null) {
                        if(active) End();requested=true;originReady=false;dead=false;link.I(0x948,0);
                        runToken=(int)(DateTime.UtcNow.Ticks&0x7fffffff);if(runToken==0) runToken=1;
                        renderer.ClearBlocks();link.I(0x940,runToken);link.I(0x9A0,0);
                        Logger.LogInfo("New RoR2 run: requested mirror block reset "+runToken);
                    }
                }
                if(observedRun==null) {if(active) End();requested=false;}
                var body=Player();
                if(active && (controlled==null || controlled.healthComponent==null || !controlled.healthComponent.alive)) HostDeath();
                if(active && (!link.Alive || body==null || controlled==null || body!=controlled || !Application.isFocused)) End();
                mc=link.Alive ? link.McState() : null;
                if(mc!=null) {int counter=BitConverter.ToInt32(mc,0x38);if(counter!=lastMcCounter) {lastMcCounter=counter;lastMcFrame=Time.unscaledTime;}}
                if(active && Time.unscaledTime-lastMcFrame>2) {End();status="Minecraft rendering stopped; waiting for recovery.";}
                bool resetReady=runToken!=0 && link.I(0x9B0)==runToken && link.I(0x9B8)==runToken;
                if(requested && !dead && !active && Application.isFocused && body!=null && body.currentVehicle==null && body.healthComponent!=null && body.healthComponent.alive && mc!=null && resetReady && Time.unscaledTime-lastMcFrame<.5f && (BitConverter.ToInt32(mc,4)&33)==1) Begin(body);
                int width=Math.Min(Screen.width,3840),height=Math.Max(1,(int)((double)Screen.height*width/Math.Max(1,Screen.width)));
                if(height>2160) {width=Math.Max(1,(int)((double)width*2160/height));height=2160;}
                if(active) {
                    mc=link.McState();
                    bool menu=mc!=null && (BitConverter.ToInt32(mc,4)&2)!=0;
                    if(!menu) {
                        yaw+=Input.GetAxisRaw("Mouse X")*2.0f;
                        pitch=Mathf.Clamp(pitch-Input.GetAxisRaw("Mouse Y")*2.0f,-89.9f,89.9f);
                    }
                    InputToMinecraft(width,height,menu);
                    if(controlled.inputBank!=null) {
                        controlled.inputBank.aimDirection=Quaternion.Euler(pitch,yaw,0)*Vector3.forward;
                        var walking=new Vector3((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),0,(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
                        controlled.inputBank.moveVector=menu?Vector3.zero:Quaternion.Euler(0,yaw,0)*Vector3.ClampMagnitude(walking,1);
                    }
                    UpdateSkills(menu);UpdateSkillMotion();
                    if(!skillMotor && mc!=null && (BitConverter.ToInt32(mc,4)&1)!=0 && BitConverter.ToInt32(mc,0x30)==teleport) {
                        var next=ToHost(new Vector3((float)BitConverter.ToDouble(mc,8),(float)BitConverter.ToDouble(mc,16),(float)BitConverter.ToDouble(mc,24)));
                        if(Finite(next) && Vector3.Distance(next,position)<32) {
                            // Retain a host floor guard while exact Minecraft terrain
                            // collision is being validated. Never pull the RoR2 body
                            // through a world surface it just crossed.
                            RaycastHit floor;
                            var origin=new Vector3(next.x,Mathf.Max(next.y,position.y)+12,next.z);
                            if(TerrainRay(origin,64,out floor) && next.y<floor.point.y-.4f && position.y>=floor.point.y-.5f) {
                                next.y=floor.point.y+.02f;teleport++;link.I(0x928,teleport);
                            }
                            // A host capsule sweep supplies walls/ceilings until
                            // the sampled Minecraft collision stream includes them.
                            var travel=next-position;float distance=travel.magnitude;
                            if(distance>.001f) {
                                float radius=.29f;
                                var lower=position+Vector3.up*(radius+.04f);
                                var upper=position+Vector3.up*Mathf.Max(radius+.04f,1.8f-radius);
                                int hits=Physics.CapsuleCastNonAlloc(lower,upper,radius,travel/distance,movementHits,distance,LayerIndex.world.mask|DefaultWorldLayer,QueryTriggerInteraction.Ignore);
                                RaycastHit obstacle=default(RaycastHit);float nearest=float.MaxValue;
                                for(int i=0;i<hits;i++) {
                                    var hit=movementHits[i];
                                    if(hit.collider==null || renderer.OwnsCollider(hit.collider) || hit.normal.y>.15f || hit.distance>=nearest) continue;
                                    obstacle=hit;nearest=hit.distance;
                                }
                                if(nearest<float.MaxValue) {
                                    var advance=travel/distance*Mathf.Max(0,obstacle.distance-.02f);
                                    var slide=Vector3.ProjectOnPlane(travel-advance,obstacle.normal);
                                    RaycastHit second;
                                    if(slide.magnitude>.001f && Physics.CapsuleCast(lower+advance,upper+advance,radius,slide.normalized,out second,slide.magnitude,LayerIndex.world.mask|DefaultWorldLayer,QueryTriggerInteraction.Ignore)) slide=slide.normalized*Mathf.Max(0,second.distance-.02f);
                                    var corrected=position+advance+slide;
                                    if(Vector3.Distance(corrected,next)>.06f) {next=corrected;teleport++;link.I(0x928,teleport);}
                                }
                            }
                            position=next;
                            var target=controlled.transform.position+position-controlled.footPosition;
                            if(controlled.characterMotor!=null && controlled.characterMotor.Motor!=null) controlled.characterMotor.Motor.SetPosition(target);
                            else controlled.transform.position=target;
                        }
                    }
                    if(Time.unscaledTime>=nextCollision) { ExportFloor(); nextCollision=Time.unscaledTime+1; }
                    if(Time.unscaledTime>=nextActors) { ExportActors(); nextActors=Time.unscaledTime+.05f; }
                    link.Events(ApplyEvent);
                    if(!menu && Input.GetKeyDown(KeyCode.F)) Interact();
                    if(!menu && Input.GetKeyDown(KeyCode.R)) {var equipment=controlled.GetComponent<EquipmentSlot>();if(equipment!=null) equipment.ExecuteIfReady();}
                    ReadMinecraftHealth();PublishStats();
                    if(Time.unscaledTime>=nextUpgradeText) {RefreshUpgradeText();nextUpgradeText=Time.unscaledTime+.5f;}
                    status="RoRCraft active | MC pid "+link.I(12)+" | sections "+renderer.SectionCount+" | F8: return to RoR2";
                } else status=link.Alive ? (requested && !resetReady ? (link.I(0x9B4)==-2 ? "Mirror reset failed; see Minecraft log. Native RoR2 controls retained." : link.I(0x9B4)<0 ? "Open the SkyCraft mirror world for block reset." : "Clearing previous run's Minecraft blocks: "+link.I(0x9B4)+" chunks remaining.") : requested ? "Minecraft connected; waiting for its mirror world to load." : "Minecraft connected. Start a RoR2 singleplayer run and press F8.") : "Waiting for RoRCraft Minecraft. F8 queues activation.";
                // The mapping drives Minecraft's lifecycle even while paused; in-game only when takeover is requested.
                var at=active ? position : (body==null ? Vector3.zero : body.footPosition);
                at=ToMinecraft(at);
                link.State(active?1:2,epoch,at.x,at.y,at.z,yaw,pitch,teleport,width,height);
                if(Time.unscaledTime>=nextLobbyScan) {UpdateLobbySkins();nextLobbyScan=Time.unscaledTime+.25f;}
                if(link.Alive) {
                    link.DrainRender(renderer.Consume,48);
                    if(Time.unscaledTime>=nextHud) { int w,h; bool flip; var pixels=link.Overlay(out w,out h,out flip);
                        if(pixels!=null) renderer.Overlay(pixels,w,h,flip); nextHud=Time.unscaledTime+1f/60; }
                }
            } catch(Exception e) { requested=false; Logger.LogError(e); End(); status="Adapter stopped: "+e.Message; }
        }
        private static bool Finite(Vector3 v) { return !float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z); }
        private Vector3 ToMinecraft(Vector3 world) {var p=world-worldOrigin;return new Vector3(-p.x,p.y,p.z);}
        private Vector3 ToHost(Vector3 p) {return new Vector3(-p.x,p.y,p.z)+worldOrigin;}
        public void GetCameraState(CameraRigController cameraRig,ref CameraState state)
        {
            float eye=mc==null?1.62f:BitConverter.ToSingle(mc,0x28);
            state.position=position+Vector3.up*Mathf.Clamp(eye,.2f,3);
            state.rotation=Quaternion.Euler(pitch,yaw,0);
            int mode=mc==null?0:BitConverter.ToInt32(mc,0xC0);
            float distance=mc==null?0:BitConverter.ToSingle(mc,0xC4);
            if(mode==1 || mode==2) {
                if(float.IsNaN(distance) || float.IsInfinity(distance)) distance=4;
                distance=Mathf.Clamp(distance,.1f,6);
                var direction=state.rotation*Vector3.forward*(mode==1?-1:1);
                RaycastHit obstruction;
                if(Physics.SphereCast(state.position,.15f,direction,out obstruction,distance,LayerIndex.world.mask|DefaultWorldLayer,QueryTriggerInteraction.Ignore)) distance=Mathf.Max(.05f,obstruction.distance-.05f);
                state.position+=direction*distance;
                if(mode==2) state.rotation=Quaternion.Euler(-pitch,yaw+180,0);
            }
            float fov=mc==null?75:BitConverter.ToSingle(mc,0x40);state.fov=fov>=30 && fov<=120?fov:75;
        }
        public bool IsUserLookAllowed(CameraRigController cameraRig) {return !active;}
        public bool IsUserControlAllowed(CameraRigController cameraRig) {return !active;}
        public bool IsHudAllowed(CameraRigController cameraRig) {return !active;}
        private void Interact()
        {
            var interactor=controlled.GetComponent<Interactor>();if(interactor==null) return;
            var ray=new Ray(position+Vector3.up*1.62f,Quaternion.Euler(pitch,yaw,0)*Vector3.forward);
            var target=interactor.FindBestInteractableObject(ray,6,ray.origin,3);
            if(target!=null) {interactor.AttemptInteraction(target);Logger.LogInfo("Minecraft interaction with "+target.name);}
        }
        private void PublishStats()
        {
            link.I(0x900,++statsSequence*2-1);System.Threading.Thread.MemoryBarrier();
            link.I(0x904,0x31524F52);link.I(0x908,1);
            link.F(0x90C,Mathf.Clamp(controlled.maxHealth,1,1024));
            link.F(0x910,Mathf.Clamp(controlled.healthComponent.health,0,1024));
            link.F(0x914,Mathf.Clamp(.1f*controlled.moveSpeed/Mathf.Max(1,controlled.baseMoveSpeed),.01f,1));
            link.F(0x918,Mathf.Clamp(controlled.attackSpeed/Mathf.Max(.01f,controlled.baseAttackSpeed),.1f,16));
            link.F(0x91C,Mathf.Max(0,controlled.healthComponent.shield+controlled.healthComponent.barrier));
            link.F(0x924,blockedDamage);
            System.Threading.Thread.MemoryBarrier();link.I(0x900,statsSequence*2);
        }
        private void ReadMinecraftHealth()
        {
            int sequence=link.I(0x980);if((sequence&1)!=0 || link.I(0x984)!=0x31524F52) return;
            System.Threading.Thread.MemoryBarrier();double total=link.D(0x988);bool blocking=link.I(0x9A0)==1;System.Threading.Thread.MemoryBarrier();
            if(sequence!=link.I(0x980) || double.IsNaN(total)||double.IsInfinity(total)) return;
            shieldRaised=blocking;
            float change=(float)(total-appliedMcHealthDelta);appliedMcHealthDelta=total;
            if(change>0 && change<=1024) controlled.healthComponent.Heal(change,default(ProcChainMask),true);
            else if(change<0 && change>=-1024) controlled.healthComponent.TakeDamage(new DamageInfo {damage=-change,position=controlled.corePosition,procCoefficient=0});
        }
        private void RefreshUpgradeText()
        {
            var inventory=controlled.inventory;if(inventory==null) {upgradeText="";return;}
            var text=new StringBuilder();int visible=0;
            foreach(var index in inventory.itemAcquisitionOrder) {
                int count=inventory.GetItemCountEffective(index);if(count<=0) continue;
                var definition=ItemCatalog.GetItemDef(index);if(definition==null) continue;
                if(visible++>=10) {text.AppendLine("...");break;}
                text.Append(Language.GetString(definition.nameToken)).Append(" x").Append(count).AppendLine();
            }
            var equipment=EquipmentCatalog.GetEquipmentDef(inventory.currentEquipmentState.equipmentIndex);
            if(equipment!=null) text.Append("R: ").Append(Language.GetString(equipment.nameToken)).AppendLine();
            if(controlled.skillLocator!=null) {
                var skills=new[]{controlled.skillLocator.primary,controlled.skillLocator.secondary,controlled.skillLocator.utility,controlled.skillLocator.special};
                text.AppendLine();for(int i=0;i<skills.Length;i++) if(skills[i]!=null) text.Append(skillKeys[i]).Append(": ").Append(Language.GetString(skills[i].skillNameToken)).Append(skills[i].stock>0?" [ready]":" ["+skills[i].cooldownRemaining.ToString("0.0")+"s]").AppendLine();
            }
            upgradeText=text.ToString();
            link.I(0xA00,++hudSequence*2-1);System.Threading.Thread.MemoryBarrier();
            link.I(0xA04,0x31524F52);link.I(0xA08,(int)controlled.master.money);
            link.F(0xA0C,Run.instance==null?0:Run.instance.GetRunStopwatch());
            var encoded=Encoding.UTF8.GetBytes(upgradeText);int length=Math.Min(encoded.Length,1024);
            // Keep the UTF-8 snapshot complete at its byte boundary.
            if(length<encoded.Length) while(length>0 && (encoded[length]&0xC0)==0x80) length--;
            var payload=new byte[length];Array.Copy(encoded,payload,length);
            link.I(0xA10,length);link.Bytes(0xA20,payload);
            PublishDifficulty();
            System.Threading.Thread.MemoryBarrier();link.I(0xA00,hudSequence*2);
        }
        private void PublishDifficulty()
        {
            if(difficultyBar==null) {
                var bars=UnityEngine.Object.FindObjectsOfType<RoR2.UI.DifficultyBarController>(true);
                if(bars.Length>0) difficultyBar=bars[0];
            }
            var run=Run.instance;float progress=0;Color color=Color.white;string current="",next="";
            string selected="";
            if(run!=null) {
                var definition=DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty);
                if(definition!=null) selected=Language.GetString(definition.nameToken);
                if(difficultyBar!=null && difficultyBar.levelsPerSegment>0 && difficultyBar.segmentDefs!=null && difficultyBar.segmentDefs.Length>0) {
                    float scroll=Mathf.Max(0,(run.ambientLevel-1)/difficultyBar.levelsPerSegment);
                    int index=Mathf.FloorToInt(scroll),last=difficultyBar.segmentDefs.Length-1;
                    var segment=difficultyBar.segmentDefs[Math.Min(index,last)];
                    current=Language.GetString(segment.token);color=segment.color;
                    next=Language.GetString(difficultyBar.segmentDefs[Math.Min(index+1,last)].token);
                    progress=index>=last?1:scroll-index;
                }
            }
            var encoded=Encoding.UTF8.GetBytes(selected+"\n"+current+"\n"+next);int length=Math.Min(encoded.Length,384);
            if(length<encoded.Length) while(length>0 && (encoded[length]&0xC0)==0x80) length--;
            var payload=new byte[length];Array.Copy(encoded,payload,length);
            link.F(0xE20,progress);
            link.I(0xE28,unchecked((int)(0xFF000000U|((uint)Mathf.RoundToInt(color.r*255)<<16)|((uint)Mathf.RoundToInt(color.g*255)<<8)|(uint)Mathf.RoundToInt(color.b*255))));
            link.I(0xE2C,length);link.Bytes(0xE30,payload);
            var teleporter=TeleporterInteraction.instance;
            link.F(0xFB0,teleporter!=null && teleporter.holdoutZoneController!=null ? teleporter.holdoutZoneController.charge : -1);
            link.I(0xFB4,run==null?0:run.stageClearCount+1);
        }
        // Native damage receiver verified in installed RoR2 IL: rejection is checked
        // immediately after these receivers, before health reduction and damage procs.
        public void OnIncomingDamageServer(DamageInfo damage)
        {
            if(!active || !shieldRaised || controlled==null || damage==null || damage.rejected || damage.damage<=0 || Time.unscaledTime-lastMcFrame>.5f) return;
            var flags=(DamageType)damage.damageType;
            if((flags&(DamageType.BypassBlock|DamageType.DoT|DamageType.FallDamage|DamageType.VoidDeath|DamageType.OutOfBounds))!=0 || damage.attacker==null) return;
            Vector3 source=damage.inflictor==null?damage.position:damage.inflictor.transform.position;
            if((source-controlled.corePosition).sqrMagnitude<.25f) {
                var enemy=damage.attacker.GetComponent<CharacterBody>();source=enemy==null?damage.attacker.transform.position:enemy.corePosition;
            }
            Vector3 incoming=source-controlled.corePosition;incoming.y=0;
            Vector3 facing=Quaternion.Euler(0,yaw,0)*Vector3.forward;
            if(incoming.sqrMagnitude<.001f || Vector3.Dot(facing,incoming.normalized)<=0) return;
            damage.rejected=true;damage.force=Vector3.zero;blockedDamage+=damage.damage;
            Logger.LogInfo("Minecraft shield blocked "+damage.damage.ToString("0.0")+" damage");
        }
        private void PushSkillButtons()
        {
            if(controlled==null || controlled.inputBank==null) return;
            var input=controlled.inputBank;input.skill1.PushState(skillHeld[0]);input.skill2.PushState(skillHeld[1]);input.skill3.PushState(skillHeld[2]);input.skill4.PushState(skillHeld[3]);
        }
        private void FixedUpdate() {if(active) PushSkillButtons();}
        private void UpdateSkills(bool menu)
        {
            if(controlled.skillLocator==null) return;
            var skills=new[]{controlled.skillLocator.primary,controlled.skillLocator.secondary,controlled.skillLocator.utility,controlled.skillLocator.special};
            for(int i=0;i<4;i++) skillHeld[i]=!menu && Input.GetKey(skillKeys[i]);PushSkillButtons();
            for(int i=0;i<4;i++) {
                var skill=skills[i];if(menu || skill==null || !skillHeld[i] || (skill.mustKeyPress && !Input.GetKeyDown(skillKeys[i]))) continue;
                if(skill.ExecuteIfReady()) {
                    Logger.LogInfo("Minecraft activated native skill "+skill.skillNameToken);
                    // Movement abilities need their native motor until the Body
                    // state finishes. Forward that movement back into Minecraft.
                    if(skill.stateMachine!=null && skill.stateMachine.customName=="Body" && controlled.characterMotor!=null) {
                        motionState=skill.stateMachine;motionStarted=Time.unscaledTime;skillMotor=true;
                        controlled.characterMotor.enabled=true;if(controlled.characterMotor.Motor!=null) controlled.characterMotor.Motor.enabled=true;
                    }
                }
            }
        }
        private void UpdateSkillMotion()
        {
            if(!skillMotor) return;
            var actual=controlled.footPosition;if(Vector3.Distance(actual,position)>.002f) {position=actual;teleport++;}
            if(Time.unscaledTime-motionStarted>.12f && (motionState==null || (motionState.IsInMainState() && !motionState.HasPendingState()) || Time.unscaledTime-motionStarted>12)) {
                if(controlled.characterMotor!=null) {controlled.characterMotor.enabled=false;if(controlled.characterMotor.Motor!=null) controlled.characterMotor.Motor.enabled=false;}
                skillMotor=false;motionState=null;teleport++;
            }
        }
        private void InputToMinecraft(int width,int height,bool menu)
        {
            foreach(var pair in keys) {
                if(Input.GetKeyDown(pair.Key)) link.Input(1,pair.Value,1,0,0);
                if(Input.GetKeyUp(pair.Key)) link.Input(1,pair.Value,0,0,0);
            }
            for(int button=0;button<3;button++) {
                ushort sdl=(ushort)(button==0?1:button==1?3:2);
                if(Input.GetMouseButtonDown(button)) link.Input(2,sdl,1,0,0);
                if(Input.GetMouseButtonUp(button)) link.Input(2,sdl,0,0,0);
            }
            float scroll=Input.mouseScrollDelta.y; if(scroll!=0) link.Input(3,0,(int)(scroll*120),0,0);
            if(menu) {
                Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
                link.Input(4,0,(int)(Input.mousePosition.x*width/Screen.width),(int)((Screen.height-Input.mousePosition.y)*height/Screen.height),0);
                foreach(char ch in Input.inputString) if(ch>=' ') link.Input(5,0,ch,0,0);
            } else { Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false; }
        }
        private void LateUpdate()
        {
            if(nativeMode) {if(avatar!=null) avatar.Tick();return;}
            if(!active || controlled==null) return;
            var camera=rig==null ? Camera.main : rig.sceneCam;
            if(camera!=null) {
                var state=new CameraState();GetCameraState(rig,ref state);
                camera.transform.position=state.position;camera.transform.rotation=state.rotation;camera.fieldOfView=state.fov;
            }
            renderer.PlayerPosition(ToMinecraft(position));
        }
        private bool TerrainRay(Vector3 origin,float distance,out RaycastHit hit)
        {
            hit=default(RaycastHit);float nearest=float.MaxValue;
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,floorHits,distance,LayerIndex.world.mask|DefaultWorldLayer,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++) if(floorHits[i].collider!=null && !renderer.OwnsCollider(floorHits[i].collider) && floorHits[i].distance<nearest) {hit=floorHits[i];nearest=hit.distance;}
            return nearest<float.MaxValue;
        }
        private void ExportFloor()
        {
            // Phase one: sampled terrain triangles. Walls and overhangs require collider export.
            const int radius=12,step=1,cells=radius*2/step;
            var local=ToMinecraft(position);
            int x0=(int)Math.Floor(local.x)-radius,z0=(int)Math.Floor(local.z)-radius;
            var points=new Vector3[cells+1,cells+1]; var valid=new bool[cells+1,cells+1];
            for(int z=0;z<=cells;z++) for(int x=0;x<=cells;x++) {
                RaycastHit hit; var origin=ToHost(new Vector3(x0+x*step,local.y+12,z0+z*step));
                if(TerrainRay(origin,48,out hit)) { points[x,z]=ToMinecraft(hit.point); valid[x,z]=true; }
            }
            var triangles=new List<Vector3>();
            var covered=new bool[cells,cells];
            for(int z=0;z<cells;z++) for(int x=0;x<cells;x++) {
                if(!valid[x,z]||!valid[x+1,z]||!valid[x,z+1]||!valid[x+1,z+1]) continue;
                var a=points[x,z];var b=points[x+1,z];var c=points[x+1,z+1];var d=points[x,z+1];
                if(Mathf.Max(Mathf.Abs(a.y-b.y),Mathf.Abs(a.y-c.y),Mathf.Abs(a.y-d.y))>3) continue;
                triangles.Add(a);triangles.Add(c);triangles.Add(b); triangles.Add(a);triangles.Add(d);triangles.Add(c);
                covered[x,z]=true;
            }
            var ground=new List<Vector3>();
            for(int z=z0;z<z0+radius*2;z++) for(int x=x0;x<x0+radius*2;x++) {
                RaycastHit hit;
                if(TerrainRay(ToHost(new Vector3(x+.5f,local.y+12,z+.5f)),48,out hit)) {
                    float y=hit.point.y-worldOrigin.y;ground.Add(new Vector3(x,y,z));
                    // The local MC player deliberately bypasses voxel collision.
                    // Even isolated ground samples therefore need real triangles.
                    if(!covered[(x-x0)/step,(z-z0)/step]) {
                        var a=new Vector3(x,y,z);var b=new Vector3(x+1,y,z);var c=new Vector3(x+1,y,z+1);var d=new Vector3(x,y,z+1);
                        triangles.Add(a);triangles.Add(c);triangles.Add(b);triangles.Add(a);triangles.Add(d);triangles.Add(c);
                    }
                }
            }
            // SkyCraft indexes exact triangles by the minimum region in each message.
            // Duplicate triangles across every touched 8-block region, including boundaries.
            var regions=new Dictionary<Vector3Int,List<Vector3>>();
            for(int i=0;i<triangles.Count;i+=3) {
                var min=Vector3.Min(triangles[i],Vector3.Min(triangles[i+1],triangles[i+2]));
                var max=Vector3.Max(triangles[i],Vector3.Max(triangles[i+1],triangles[i+2]));
                for(int x=Mathf.FloorToInt((min.x-.001f)/8);x<=Mathf.FloorToInt((max.x+.001f)/8);x++)
                for(int y=Mathf.FloorToInt((min.y-.001f)/8);y<=Mathf.FloorToInt((max.y+.001f)/8);y++)
                for(int z=Mathf.FloorToInt((min.z-.001f)/8);z<=Mathf.FloorToInt((max.z+.001f)/8);z++) {
                    var key=new Vector3Int(x,y,z);List<Vector3> list;
                    if(!regions.TryGetValue(key,out list)) {list=new List<Vector3>();regions.Add(key,list);}
                    list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);
                }
            }
            foreach(var region in regions) using(var buffer=new MemoryStream()) using(var writer=new BinaryWriter(buffer)) {
                var key=region.Key;writer.Write(key.x*8);writer.Write(key.y*8);writer.Write(key.z*8);
                writer.Write(key.x*8+7);writer.Write(key.y*8+7);writer.Write(key.z*8+7);
                writer.Write(epoch);writer.Write(region.Value.Count/3);
                for(int i=0;i<region.Value.Count;i+=3) {for(int j=0;j<3;j++) {var v=region.Value[i+j];writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);} writer.Write(0);}
                link.CollisionMessage(3,buffer.ToArray());
            }
            // Region packets also provide the readiness and ground-support information
            // expected by the unmodified Minecraft client, including empty air above ground.
            using(var buffer=new MemoryStream()) using(var writer=new BinaryWriter(buffer)) {
                writer.Write(x0);writer.Write(Mathf.FloorToInt(local.y)-40);writer.Write(z0);
                writer.Write(x0+radius*2-1);writer.Write(Mathf.FloorToInt(local.y)+16);writer.Write(z0+radius*2-1);
                writer.Write(epoch);writer.Write(ground.Count);
                foreach(var v in ground) {
                    int y=Mathf.FloorToInt(v.y-.001f),top=Mathf.Clamp(Mathf.CeilToInt((v.y-y)*8)-1,0,7);
                    writer.Write((int)v.x);writer.Write(y);writer.Write((int)v.z);writer.Write(0);
                    for(int layer=0;layer<8;layer++) writer.Write(layer<=top ? ulong.MaxValue : 0UL);
                }
                link.CollisionMessage(2,buffer.ToArray());
            }
            if(!floorReported) {
                Logger.LogInfo("Terrain export: "+triangles.Count/3+" triangles, "+regions.Count+" triangle regions, "+ground.Count+" ground cells. Host feet "+position+", Minecraft feet "+local);
                floorReported=true;
            }
        }
        private void ExportActors()
        {
            int seq=++actorSequence; link.I(0x12000,seq*2-1); System.Threading.Thread.MemoryBarrier();
            actors.Clear(); int index=0;
            foreach(var body in CharacterBody.readOnlyInstancesList) {
                if(body==controlled || body.healthComponent==null || !body.healthComponent.alive || Vector3.Distance(body.footPosition,position)>64 || index>=256) continue;
                uint id=(uint)body.GetInstanceID(); actors[id]=body; long p=0x12040+index*64;
                link.I(p,(int)id);link.I(p+4,body.teamComponent!=null && body.teamComponent.teamIndex==TeamIndex.Monster?9:0);
                var pos=ToMinecraft(body.footPosition);link.F(p+8,pos.x);link.F(p+12,pos.y);link.F(p+16,pos.z);link.F(p+20,body.transform.eulerAngles.y);
                link.F(p+24,Mathf.Max(.5f,body.radius*2));link.F(p+28,Mathf.Max(1.5f,(body.corePosition.y-body.footPosition.y)*2));
                link.F(p+32,body.healthComponent.combinedHealthFraction);link.I(p+36,Mathf.Min(65535,(int)body.level));
                var name=new byte[24];var encoded=Encoding.UTF8.GetBytes(body.GetDisplayName());Array.Copy(encoded,name,Math.Min(encoded.Length,23));link.Bytes(p+40,name);index++;
            }
            link.I(0x12004,index);System.Threading.Thread.MemoryBarrier();link.I(0x12000,seq*2);
        }
        private void ApplyEvent(byte[] b)
        {
            if(controlled==null) return;
            if(BitConverter.ToInt32(b,0)==2) {if(controlled.healthComponent!=null && controlled.healthComponent.alive) controlled.healthComponent.Suicide();HostDeath();return;}
            if(BitConverter.ToInt32(b,0)!=1) return;
            CharacterBody target; if(!actors.TryGetValue(BitConverter.ToUInt32(b,4),out target)||target==null) return;
            if(target.teamComponent==null || target.teamComponent.teamIndex==controlled.teamComponent.teamIndex) return;
            float amount=BitConverter.ToSingle(b,8); if(float.IsNaN(amount)||amount<=0||amount>10000) return;
            var hit=new DamageInfo {damage=amount*controlled.damage/6f,attacker=controlled.gameObject,position=target.corePosition,procCoefficient=1,crit=Util.CheckRoll(controlled.crit,controlled.master)};
            target.healthComponent.TakeDamage(hit);
            if((BitConverter.ToInt32(b,24)&2)!=0) Logger.LogInfo("Minecraft projectile hit "+target.GetDisplayName()+" for "+hit.damage.ToString("0.0")+" RoR2 damage.");
            if(GlobalEventManager.instance!=null) {GlobalEventManager.instance.OnHitEnemy(hit,target.gameObject);GlobalEventManager.instance.OnHitAll(hit,target.gameObject);}
        }
        private void DamageDealt(DamageReport report)
        {
            // Current health is shared directly. Do not apply RoR2 damage a second
            // time through SkyCraft's Skyrim-to-Minecraft damage conversion.
        }
        private void HostDeath()
        {
            if(dead) return;dead=true;deathStarted=Time.unscaledTime;requested=false;
            link.I(0x948,1);link.Input(6,0,0,0,0);End();
            // Death is independent of the normal live-stat stream, which End disables.
            Logger.LogInfo("RoRCraft death: movement released, Minecraft death requested.");
        }
        private void UpdateLobbySkins()
        {
            var remove=new List<Transform>();
            foreach(var pair in lobbySkins) if(pair.Key==null || Run.instance!=null) {pair.Value.Dispose();remove.Add(pair.Key);}
            foreach(var key in remove) lobbySkins.Remove(key);
            if(Run.instance!=null) return;
            var user=LocalUserManager.GetFirstLocalUser();
            foreach(var preview in UnityEngine.Object.FindObjectsOfType<CharacterSelectSurvivorPreviewDisplayController>()) {
                if(user!=null && preview.networkUser!=null && preview.networkUser!=user.currentNetworkUser) continue;
                if(!lobbySkins.ContainsKey(preview.transform)) {
                    string path=Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location),"assets","player-skin.png");
                    lobbySkins[preview.transform]=new BlockSurvivor(preview.transform,path);
                    Logger.LogInfo("Minecraft skin replaces selected survivor lobby display.");
                }
            }
            foreach(var skin in lobbySkins.Values) skin.Tick();
        }
        private void OnGUI()
        {
            if(nativeMode) return;
            if(renderer!=null && (active || dead && Time.unscaledTime-deathStarted<5) && renderer.Hud!=null) {
                var uv=renderer.HudBottomUp?new Rect(0,0,1,1):new Rect(0,1,1,-1);
                GUI.DrawTextureWithTexCoords(new Rect(0,0,Screen.width,Screen.height),renderer.Hud,uv,true);
            }
        }
        private void OnDestroy()
        {
            if(nativeMode) {if(avatar!=null) avatar.Dispose();return;}
            foreach(var skin in lobbySkins.Values) skin.Dispose();lobbySkins.Clear();
            GlobalEventManager.onServerDamageDealt-=DamageDealt; End();
            if(renderer!=null) renderer.Dispose(); if(link!=null) link.Dispose();
        }
    }
}
