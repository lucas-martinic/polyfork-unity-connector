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
                /* A fixed key light rather than the scene's. The preview is an isolated
                 * utility scene with its own lighting, and these assets are flat-shaded, so
                 * a stable wrap term keeps every face distinguishable without making the
                 * result depend on whatever the project's lighting happens to be. */
                float3 n = normalize(i.normal);
                float ndl = saturate(dot(n, normalize(float3(0.3, 1.0, 0.35))) * 0.5 + 0.5);

                fixed3 lit = i.color.rgb * _Tint.rgb * lerp(0.62, 1.12, ndl);
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
