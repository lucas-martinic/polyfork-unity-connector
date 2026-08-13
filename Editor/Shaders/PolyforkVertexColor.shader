// Shows a Polyfork mesh the way it is authored: all of its colour lives in COLOR_0.
//
// Unity's stock shaders ignore vertex colour. URP/Lit, URP/Simple Lit and Standard all
// discard COLOR_0 entirely, so a locally baked asset came out grey while the same asset
// fetched as a .glb looked right - glTFast supplies its own vertex-colour material, and the
// local path had nothing equivalent.
//
// Deliberately a plain vertex/fragment shader over UnityCG rather than a surface shader or
// URP HLSL: those bind to one pipeline, and this has to draw in whichever the consumer's
// project uses. Nothing here touches built-in lighting, which is what makes a shader render
// magenta under URP.
//
// Editor-only, like the rest of local baking: it never reaches a player build.
Shader "Polyfork/Vertex Color"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                fixed4 color  : COLOR;
                float3 normal : TEXCOORD0;
            };

            fixed4 _Tint;

            /* Taken from public/viewer.js: HemisphereLight(0xffffff, 0xb8b0a4, 1.15), a key
             * DirectionalLight(0xfff2e0, 2.4) at (4,7,5) and a rim (0xdfe8ff, 0.9) at
             * (-5,4,-6). The intensities are scaled down from three.js's, whose lighting is
             * physical and whose renderer tone maps; these land in the same place by eye
             * without blowing the lit side to white. */
            #define SKY_COLOR      float3(1.00, 1.00, 1.00)
            #define GROUND_COLOR   float3(0.72, 0.69, 0.64)
            #define HEMI_INTENSITY 0.52

            #define KEY_COLOR      float3(1.00, 0.949, 0.878)
            #define KEY_DIR        float3(4.0, 7.0, 5.0)
            #define KEY_INTENSITY  0.78

            #define RIM_COLOR      float3(0.874, 0.910, 1.00)
            #define RIM_DIR        float3(-5.0, 4.0, -6.0)
            #define RIM_INTENSITY  0.20

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                /* The store viewer's rig, written out rather than sampled from the scene.
                 * The preview is an isolated utility scene with its own lighting, so
                 * matching public/viewer.js here is what keeps an asset looking the same in
                 * the editor as on its own store page.
                 *
                 * Real N dot L, not the wrap term this used to have. Wrapping maps the whole
                 * sphere into [0,1] and never lets anything go properly dark, so every face
                 * came out within a hair of every other and the model read as flat. Clamped,
                 * the far side falls to the hemisphere term alone - which is what makes a
                 * shaded side look shaded. */
                float3 n = normalize(i.normal);

                float3 hemi = lerp(GROUND_COLOR, SKY_COLOR, n.y * 0.5 + 0.5) * HEMI_INTENSITY;
                float3 key  = KEY_COLOR * saturate(dot(n, normalize(KEY_DIR))) * KEY_INTENSITY;
                float3 rim  = RIM_COLOR * saturate(dot(n, normalize(RIM_DIR))) * RIM_INTENSITY;

                fixed3 lit = i.color.rgb * _Tint.rgb * (hemi + key + rim);
                return fixed4(lit, 1);
            }
            ENDCG
        }

        /* Without this the model casts no shadow at all, whatever the light is told to do:
         * a mesh is only drawn into the shadow map by a pass tagged ShadowCaster, and the
         * colour pass above is not one. URP looks for the same tag, which is what lets one
         * pass serve both pipelines. */
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct shadowIn
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct shadowOut
            {
                V2F_SHADOW_CASTER;
            };

            shadowOut shadowVert (shadowIn v)
            {
                shadowOut o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 shadowFrag (shadowOut i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
