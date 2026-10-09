using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoRCraftPolish {
    // Section material categories are supplied by Minecraft block identity.
    // Atlas-safe mipmaps and environmental color response remain separate work.
    [BepInPlugin("cz.jirka.rorcraft-material-polish","RoRCraft material reference polish","0.1.7")]
    [BepInDependency("cz.jirka.rorcraft-skycraft",BepInDependency.DependencyFlags.HardDependency)]
    public sealed class MaterialPolish : BaseUnityPlugin {
        private Harmony harmony;
        private static MaterialPolish instance;
        private ConfigEntry<bool> nativeLighting,blockShadows;
        private ConfigEntry<float> environmentColorInfluence;
        private ConfigEntry<bool> distantFiltering;
        private static AtlasTexture atlasTexture;
        private static Dictionary<int,Material> atlasMaterials;
        private static readonly Dictionary<bool,Material> overlayMaterials=new Dictionary<bool,Material>();
        private static Material[] overlayPair;
        public ConfigEntry<bool> DistantFiltering {get {return distantFiltering;}}
        public ConfigEntry<float> EnvironmentColorInfluence {get {return environmentColorInfluence;}}
        private static readonly Dictionary<int,Material> categoryMaterials=new Dictionary<int,Material>();
        private static readonly List<int>[] surfaceGroups=CreateGroups();
        private static List<int>[] CreateGroups() {var groups=new List<int>[SurfaceGroups.Count];for(int i=0;i<groups.Length;i++) groups[i]=new List<int>();return groups;}
        private static Shader nativeShader;
        private static float nextShaderSearch;
        private static bool applyPending;
        private static bool developerNativeBlocks;
        private static readonly Dictionary<Material,Shader> originalShaders=new Dictionary<Material,Shader>();
        private static readonly Dictionary<Material,int> materialIds=new Dictionary<Material,int>();
        private static readonly HashSet<MeshRenderer> meshes=new HashSet<MeshRenderer>();
        public static MaterialPolish Instance {get {return instance;}}
        public ConfigEntry<bool> NativeLighting {get {return nativeLighting;}}
        public ConfigEntry<bool> BlockShadows {get {return blockShadows;}}
        private static readonly HashSet<Material> configured=new HashSet<Material>();
        private void Awake() {
            instance=this;
            nativeLighting=Config.Bind("Minecraft Visuals","NativeLitShader",true,"Use the bundled Minecraft tint/AO shader with native deferred lighting for blocks. Skins retain the loaded RoR2 shader. Retains a lit fallback when unsupported. Does not change native RoR2 materials.");
            blockShadows=Config.Bind("Minecraft Visuals","BlockShadows",true,"Opaque Minecraft geometry casts and receives native world shadows. Subject to the game's shadow quality and distance.");
            environmentColorInfluence=Config.Bind("Minecraft Visuals","EnvironmentColorInfluence",.25f,new ConfigDescription("Amount of ambient color retained. 1 keeps the stage color; 0 neutralizes ambient chroma on Minecraft materials while retaining lit shading, luminance, shadows and fog. Strong local lights and post-processing can retain some color.",new AcceptableValueRange<float>(0f,1f)));
            environmentColorInfluence.SettingChanged+=SettingsChanged;
            distantFiltering=Config.Bind("Minecraft Visuals","SpriteSafeDistantFiltering",true,"Nearest pixels nearby with up to two sprite-isolated mip levels at distance. Unsupported atlas layouts retain nearest sampling without mipmaps. Applies immediately.");
            distantFiltering.SettingChanged+=SettingsChanged;
            nativeLighting.SettingChanged+=SettingsChanged;
            blockShadows.SettingChanged+=SettingsChanged;
            harmony=new Harmony("cz.jirka.rorcraft-material-polish");
            MinecraftEnemyCombat.Install(harmony);
            var renderer=AccessTools.TypeByName("RoRCraft.SkyRenderer");
            harmony.Patch(AccessTools.Method(renderer,"MaterialFor"),postfix:new HarmonyMethod(typeof(MaterialPolish),"MaterialCreated"));
            harmony.Patch(AccessTools.Method(renderer,"Texture"),postfix:new HarmonyMethod(typeof(MaterialPolish),"TextureCreated"));
            harmony.Patch(AccessTools.Method(renderer,"Dispose"),postfix:new HarmonyMethod(typeof(MaterialPolish),"Disposed"));
            harmony.Patch(AccessTools.Method(renderer,"Build"),postfix:new HarmonyMethod(typeof(MaterialPolish),"MeshCreated"));
            harmony.Patch(AccessTools.Method(renderer,"Consume"),prefix:new HarmonyMethod(typeof(MaterialPolish),"AtlasPacket"));
            SceneManager.sceneLoaded+=SceneLoaded;
            Logger.LogInfo("Minecraft reference materials: sprite-safe texture sampling, embedded tint/AO shader, category reflections and explicit mesh shadows. Native skins and scene lighting retained.");
        }
        private static void SettingsChanged(object sender,System.EventArgs args) {applyPending=true;}
        private void LateUpdate() {if(atlasTexture!=null) atlasTexture.Flush();}
        private static bool AtlasPacket(int type,byte[] b,Dictionary<int,Material> ___materials) {
            if(type==7 && atlasTexture!=null) {atlasTexture.Region(b);return false;}
            if(type!=20) return true;
            try {
                Material material;if(!___materials.TryGetValue(0,out material)) return false;
                var original=material.mainTexture as Texture2D;if(original==null) return false;
                var layout=new AtlasTiles(b);
                if(layout.Width!=original.width || layout.Height!=original.height) return false;
                // A repeated descriptor can arrive while the material already owns
                // the mipmapped texture. Rebuild from its current level-zero source.
                var previous=atlasTexture;
                var source=previous!=null && original==previous.Texture?previous.NoMipTexture:original;
                var replacement=new AtlasTexture(layout,source,instance.distantFiltering.Value);
                atlasTexture=replacement;
                atlasMaterials=___materials;
                foreach(var entry in ___materials) if(entry.Value!=null && entry.Value.mainTexture==original) entry.Value.mainTexture=atlasTexture.Texture;
                foreach(var entry in categoryMaterials) if(entry.Value!=null) {entry.Value.mainTexture=atlasTexture.Texture;ConfigureCategory(entry.Value,entry.Key);}
                foreach(var entry in overlayMaterials) if(entry.Value!=null) entry.Value.mainTexture=atlasTexture.NoMipTexture;
                if(previous!=null) previous.Dispose(previous.NoMipTexture!=source);
                instance.Logger.LogInfo("Sprite-safe atlas: "+layout.Tiles.Length+" sprites, "+atlasTexture.Texture.mipmapCount+" mip levels, Point/Clamp; layout limit="+layout.Limitation+"; animated patches coalesced once per frame.");
            } catch(System.Exception error) {instance.Logger.LogWarning("Atlas descriptor rejected; retaining original nearest texture: "+error.Message);}
            return false;
        }
        private void Update() {
            if(!applyPending) return;applyPending=false;
            if(atlasTexture!=null && atlasTexture.Enabled!=distantFiltering.Value) {
                var old=atlasTexture.SelectMipmaps(distantFiltering.Value);
                foreach(var entry in atlasMaterials) if(entry.Value!=null && entry.Value.mainTexture==old) entry.Value.mainTexture=atlasTexture.Texture;
                foreach(var entry in categoryMaterials) if(entry.Value!=null) entry.Value.mainTexture=atlasTexture.Texture;
                UnityEngine.Object.Destroy(old);
            }
            configured.Clear();
            foreach(var entry in materialIds) if(entry.Key!=null) Configure(entry.Key,entry.Value);
            foreach(var entry in categoryMaterials) if(entry.Value!=null) ConfigureCategory(entry.Value,entry.Key);
            foreach(var mesh in meshes) if(mesh!=null) Shadows(mesh);
            Config.Save();
        }
        private static void SceneLoaded(Scene scene,LoadSceneMode mode) {
            nativeShader=null;nextShaderSearch=0;configured.Clear();
            meshes.RemoveWhere(m=>m==null);
            var removed=new List<Material>();
            foreach(var entry in materialIds) if(entry.Key==null) removed.Add(entry.Key);
            foreach(var material in removed) {materialIds.Remove(material);originalShaders.Remove(material);}
        }
        private static Shader LoadedNativeShader() {
            if(nativeShader!=null) return nativeShader;
            if(Time.unscaledTime<nextShaderSearch) return null;
            nextShaderSearch=Time.unscaledTime+2;
            // Addressables can load a shader that Shader.Find does not register.
            // Search loaded shader references, never load another copy of a bundle.
            foreach(var shader in Resources.FindObjectsOfTypeAll<Shader>()) {
                if(shader!=null && shader.name=="Hopoo Games/Deferred/Standard" && shader.isSupported) {
                    nativeShader=shader;
                    instance.Logger.LogInfo("Loaded native Minecraft material shader: "+shader.name);
                    break;
                }
            }
            return nativeShader;
        }
        private static void Set(Material m,string property,float value) {if(m.HasProperty(property)) m.SetFloat(property,value);}
        private static void ColorResponse(Material material,float influence) {
            if(!material.HasProperty("_Color")) return;
            var ambient=RenderSettings.ambientLight;
            if(QualitySettings.activeColorSpace==ColorSpace.Linear) ambient=ambient.linear;
            var gain=EnvironmentColorBalance.Gains(ambient.r,ambient.g,ambient.b,influence);
            var tint=new Color((float)gain[0],(float)gain[1],(float)gain[2],1);
            // Color properties cross from authoring sRGB to linear shader input.
            // Gains were calculated in linear light, so encode them only once.
            if(QualitySettings.activeColorSpace==ColorSpace.Linear) tint=tint.gamma;
            material.SetColor("_Color",tint);
        }
        // Called only by the opt-in developer harness, never normal play.
        public static string DeveloperAtlasAudit() {
            if(atlasTexture==null) return "Atlas unavailable";
            return "Atlas blocks mipCount="+atlasTexture.Texture.mipmapCount+" overlays mipCount="+atlasTexture.NoMipTexture.mipmapCount+" regions="+atlasTexture.Regions+" uploads="+atlasTexture.Uploads+" lastWorkMs="+atlasTexture.LastWorkMs+" maxWorkMs="+atlasTexture.MaxWorkMs;
        }
        public static void DeveloperBlockShaderReference(bool useNative) {developerNativeBlocks=useNative;applyPending=true;}
        public static void DeveloperReference(int mode) {
            int[] layers={1,0,3,2,0};
            int layer=layers[mode%layers.Length];
            foreach(var entry in materialIds) if(entry.Key!=null && entry.Value==0) Set(entry.Key,"_DecalLayer",layer);
            instance.Logger.LogInfo("Developer block decal layer "+layer+"; native light shadows and grass remain enabled.");
        }
        private static void MaterialCreated(Material __result,int id,bool translucent) {
            if(__result==null || translucent) return;
            if(!originalShaders.ContainsKey(__result)) {originalShaders[__result]=__result.shader;materialIds[__result]=id;}
            Configure(__result,id);
        }
        private static void Configure(Material __result,int id) {
            Shader shader=null;
            if(instance.nativeLighting.Value) {
                if(id==0 && !developerNativeBlocks) shader=MinecraftBlockShader.Get(message=>instance.Logger.LogInfo(message));
                if(shader==null) shader=LoadedNativeShader();
            } else shader=originalShaders[__result];
            if(shader!=null && __result.shader!=shader) {
                // Switching only our material keeps its atlas/skin texture and UVs.
                var texture=__result.mainTexture;
                __result.shader=shader;__result.shaderKeywords=new string[0];
                __result.mainTexture=texture;configured.Remove(__result);
            }
            if(!configured.Add(__result)) return;
            if(shader!=null && (shader.name=="Hopoo Games/Deferred/Standard" || shader.name=="RoRCraft/Block Deferred") && __result.shader==shader) {
                Set(__result,"_EnableCutout",1);__result.EnableKeyword("CUTOUT");
                Set(__result,"_Cull",2);Set(__result,"_NormalStrength",0);
                Set(__result,"_SpecularStrength",0);Set(__result,"_SecondarySmoothness",0);
                Set(__result,"_EmPower",0);Set(__result,"_Fade",1);
                Set(__result,"_RampInfo",1);Set(__result,"_SpecularMask",0);
                // Placed blocks are independent surfaces, not the native terrain
                // receiver. Environment projects the stage's terrain decals onto
                // their raised faces, producing opaque black patches.
                // Keep the native shader's Default layer and actual world shadows.
                Set(__result,"_DecalLayer",0);
                Set(__result,"_Cutoff",.5f);Set(__result,"_VertexTint",id==0?1:0);
                // HG USE_VERTEX_COLORS is not a proven texture*tint path. Do not
                // enable it blindly and risk replacing the player's skin albedo.
                Set(__result,"_ColorsOn",0);Set(__result,"_DoubleColorsOn",0);
                __result.SetColor("_Color",Color.white);
                __result.renderQueue=2450;
            } else {
                __result.EnableKeyword("_ALPHATEST_ON");
                Set(__result,"_Cutoff",.5f);
            }
            Set(__result,"_Glossiness",0); Set(__result,"_Smoothness",0); Set(__result,"_Metallic",0);
            Set(__result,"_SpecularHighlights",0); Set(__result,"_GlossyReflections",0);
            __result.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); __result.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            ColorResponse(__result,instance.environmentColorInfluence.Value);
            // Never make stone unlit or double geometry to hide winding problems.
        }
        private static void MeshCreated(GameObject __result,byte[] bytes,int offset,int count,int texture,bool solid) {
            if(__result==null) return;
            var renderer=__result.GetComponent<MeshRenderer>();
            if(renderer==null) return;
            meshes.Add(renderer);Shadows(renderer);
            if(!solid && texture==0 && atlasTexture!=null) {
                if(overlayPair==null) {
                var originals=renderer.sharedMaterials;
                for(int n=0;n<originals.Length;n++) {
                    bool transparent=n==1;Material overlay;
                    if(!overlayMaterials.TryGetValue(transparent,out overlay)) {
                        overlay=new Material(originals[n]);overlay.name="Minecraft full-resolution item overlay";
                        overlayMaterials[transparent]=overlay;
                        if(!transparent) {originalShaders[overlay]=Shader.Find("Standard");materialIds[overlay]=-1;Configure(overlay,-1);}
                    }
                    overlay.mainTexture=atlasTexture.NoMipTexture;originals[n]=overlay;
                }
                overlayPair=originals;
                }
                renderer.sharedMaterials=overlayPair;
            }
            if(!solid || texture!=0 || count==0) return;
            var original=renderer.sharedMaterials;
            if(original.Length<2) return;
            SurfaceGroups.Partition(bytes,offset,count,surfaceGroups);
            var mesh=__result.GetComponent<MeshFilter>().sharedMesh;
            int used=0;for(int i=0;i<SurfaceGroups.Count;i++) if(surfaceGroups[i].Count>0) used++;
            var assigned=new Material[used];mesh.subMeshCount=used;int submesh=0;
            for(int category=0;category<SurfaceGroups.Count;category++) {
                if(surfaceGroups[category].Count==0) continue;
                Material material;
                if(category==0) material=original[0];
                else if(category==5) material=original[1];
                else {
                    if(!categoryMaterials.TryGetValue(category,out material)) {
                        material=new Material(original[0]);material.name="Minecraft surface category "+category;
                        categoryMaterials[category]=material;ConfigureCategory(material,category);
                        instance.Logger.LogInfo("Created Minecraft material category "+category+" shader="+material.shader.name);
                    }
                    material.mainTexture=original[0].mainTexture;
                }
                assigned[submesh]=material;mesh.SetTriangles(surfaceGroups[category],submesh,false);submesh++;
            }
            renderer.sharedMaterials=assigned;
            // Same vertices and triangle winding as the original builder. No new
            // colliders, boxes, positions or physics movement are introduced.
            var collider=__result.GetComponent<MeshCollider>();
            if(collider!=null) {collider.sharedMesh=null;collider.sharedMesh=mesh;}
        }
        private static void ConfigureCategory(Material material,int category) {
            if(category==1 || category==2 || category==4) {
                if(!originalShaders.ContainsKey(material)) {originalShaders[material]=Shader.Find("Standard");materialIds[material]=0;}
                configured.Remove(material);Configure(material,0);
                if(category==1 || category==2) {
                    Set(material,"_SpecularMask",1);
                    Set(material,"_SpecularStrength",category==1?.2f:.12f);Set(material,"_Smoothness",.23f);Set(material,"_Glossiness",.23f);
                    Set(material,"_Metallic",category==1?.65f:0);Set(material,"_SpecularHighlights",1);Set(material,"_GlossyReflections",1);
                    material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");material.DisableKeyword("_GLOSSYREFLECTIONS_OFF");
                } else {
                    if(material.HasProperty("_EmTex")) {material.SetTexture("_EmTex",material.mainTexture);material.SetColor("_EmColor",Color.white);Set(material,"_EmPower",.35f);}
                    if(material.HasProperty("_EmissionMap")) {material.SetTexture("_EmissionMap",material.mainTexture);material.SetColor("_EmissionColor",Color.white*.35f);material.EnableKeyword("_EMISSION");}
                }
            } else {
                var standard=Shader.Find("Standard");if(standard==null) return;
                material.shader=standard;material.shaderKeywords=new string[0];
                material.SetColor("_Color",Color.white);Set(material,"_Mode",2);Set(material,"_Metallic",0);
                Set(material,"_Glossiness",category==2?.3f:.12f);Set(material,"_SrcBlend",5);Set(material,"_DstBlend",10);
                Set(material,"_ZWrite",0);Set(material,"_Cutoff",0);
                material.EnableKeyword("_ALPHABLEND_ON");material.SetOverrideTag("RenderType","Transparent");material.SetShaderPassEnabled("ShadowCaster",false);material.renderQueue=3000;
            }
            ColorResponse(material,instance.environmentColorInfluence.Value);

        }
        private static void Shadows(MeshRenderer renderer) {
            renderer.shadowCastingMode=instance.blockShadows.Value ? ShadowCastingMode.On:ShadowCastingMode.Off;
            renderer.receiveShadows=instance.blockShadows.Value;
        }
        private static void TextureCreated(Dictionary<int,Material> ___materials,int id) {
            if(id==0) {if(atlasTexture!=null) atlasTexture.Dispose(true);atlasTexture=null;atlasMaterials=null;}
            Material material;
            if(!___materials.TryGetValue(id*2,out material) || material.mainTexture==null) return;
            material.mainTexture.filterMode=FilterMode.Point;
            material.mainTexture.wrapMode=TextureWrapMode.Clamp;
            if(id==0) foreach(var entry in overlayMaterials) if(entry.Value!=null) entry.Value.mainTexture=material.mainTexture;
            if(id==0) foreach(var entry in categoryMaterials) {
                if(entry.Value==null) continue;
                entry.Value.mainTexture=material.mainTexture;ConfigureCategory(entry.Value,entry.Key);
            }
            // Do not generate whole-atlas mipmaps: they blend unrelated sprites.
            // Exporter tile boundaries must be known before adding safe mip levels.
        }
        private static void Disposed() {if(atlasTexture!=null) atlasTexture.Dispose(true);atlasTexture=null;atlasMaterials=null;overlayPair=null;foreach(var material in overlayMaterials.Values) if(material!=null) UnityEngine.Object.Destroy(material);overlayMaterials.Clear();foreach(var material in categoryMaterials.Values) if(material!=null) UnityEngine.Object.Destroy(material);categoryMaterials.Clear();configured.Clear();originalShaders.Clear();materialIds.Clear();meshes.Clear();}
        private void OnDestroy() {environmentColorInfluence.SettingChanged-=SettingsChanged;nativeLighting.SettingChanged-=SettingsChanged;blockShadows.SettingChanged-=SettingsChanged;SceneManager.sceneLoaded-=SceneLoaded;if(harmony!=null) harmony.UnpatchSelf();Disposed();MinecraftBlockShader.Dispose();developerNativeBlocks=false;nativeShader=null;instance=null;}
    }
}


