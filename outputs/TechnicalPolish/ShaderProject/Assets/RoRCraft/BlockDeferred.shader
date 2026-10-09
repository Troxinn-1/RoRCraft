// Authored RoRCraft shader. Native packing is based on a read-only audit of
// the locally installed HG deferred pass; no game shader assets are copied.
// EXPERIMENTAL: require native MRT and real-stage acceptance before shipping.
Shader "RoRCraft/Block Deferred" {
    Properties {
        _MainTex ("Minecraft atlas", 2D) = "white" {}
        _Color ("Material color", Color) = (1,1,1,1)
        _Cutoff ("Cutout", Range(0,1)) = 0.5
        _Cull ("Cull", Float) = 2
        _SpecularStrength ("Specular strength", Range(0,1)) = 0
        _SpecularMask ("Independent opaque material reflection", Range(0,1)) = 0
        _SpecularExponent ("Specular exponent", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0
        _DecalLayer ("Native decal layer", Float) = 0
        _RampInfo ("Native lighting ramp", Float) = 1
        _EmTex ("Emission", 2D) = "black" {}
        _EmColor ("Emission color", Color) = (1,1,1,1)
        _EmPower ("Emission power", Float) = 0
        _VertexTint ("Use Minecraft tint and AO", Range(0,1)) = 1
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        Pass {
            Name "DEFERRED"
            Tags { "LightMode"="Deferred" }
            ZWrite On ZTest LEqual Blend One Zero
            CGPROGRAM
            #pragma target 4.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ CUTOUT
            #pragma multi_compile __ UNITY_HDR_ON
            #pragma multi_compile __ LIGHTPROBE_SH
            #include "UnityCG.cginc"
            sampler2D _MainTex, _EmTex;
            float4 _MainTex_ST, _Color, _EmColor;
            float _Cutoff, _SpecularStrength, _SpecularExponent, _Smoothness, _SpecularMask;
            float _DecalLayer, _RampInfo, _EmPower, _VertexTint;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varying { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; float3 world:TEXCOORD2; float3 tint:TEXCOORD3; };
            Varying vert(Input v) {
                Varying o;
                o.vertex=UnityObjectToClipPos(v.vertex);
                o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                // Mesh Color32 is raw gamma-authored Minecraft tint/AO.
                // Convert before interpolation, just as sRGB atlas albedo is
                // converted by texture sampling in a linear Unity player.
                float3 tint=v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                tint=GammaToLinearSpace(tint);
                #endif
                o.tint=lerp(float3(1,1,1),tint,_VertexTint);
                return o;
            }
            struct GBuffer { float4 albedo:SV_Target0; float4 specular:SV_Target1; float4 normal:SV_Target2; float4 emission:SV_Target3; };
            GBuffer frag(Varying i) {
                float4 texel=tex2D(_MainTex,i.uv)*_Color;
                #ifdef CUTOUT
                clip(texel.a-_Cutoff);
                #endif
                float3 n=normalize(i.normal);
                float3 albedo=texel.rgb*i.tint;
                GBuffer o;
                o.albedo=float4(albedo,1);
                // Names/offsets are checked against the native shader's actual
                // parameter table and GPU readback, not standard Unity packing.
                // Native HG masks specular with inverse albedo alpha. Minecraft
                // opaque metal uses alpha=1, so its category needs an independent
                // mask without modifying the atlas or alpha-cutout geometry.
                o.specular=float4(lerp(1-texel.a,1,_SpecularMask)*_SpecularStrength,_SpecularExponent*.05,_RampInfo/16,_Smoothness);
                o.normal=float4(n*.5+.5,_DecalLayer/3);
                float3 emission=tex2D(_EmTex,i.uv).rgb*_EmColor.rgb*_EmPower;
                // HG adds probe illumination only in the LIGHTPROBE_SH variant.
                // The no-probe variant must not inject a second ambient term.
                #ifdef LIGHTPROBE_SH
                emission+=max(0,ShadeSH9(float4(n,1)))*albedo;
                #endif
                #ifdef UNITY_HDR_ON
                o.emission=float4(emission,1);
                #else
                o.emission=float4(exp2(-emission),1);
                #endif
                return o;
            }
            ENDCG
        }
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile __ CUTOUT
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color;
            float _Cutoff;
            struct ShadowOutput { V2F_SHADOW_CASTER; float2 uv:TEXCOORD1; };
            ShadowOutput shadowVert(appdata_base v) {
                ShadowOutput o;
                o.uv=TRANSFORM_TEX(v.texcoord,_MainTex);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 shadowFrag(ShadowOutput i):SV_Target {
                #ifdef CUTOUT
                clip(tex2D(_MainTex,i.uv).a*_Color.a-_Cutoff);
                #endif
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    Fallback Off
}
