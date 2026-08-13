// A hard, object-shaped shadow, drawn as geometry rather than cast by a light.
//
// PreviewRenderUtility renders into its own scene and its real-time shadow support is
// unreliable - under a scriptable pipeline it frequently produces none at all - so a shadow
// that depends on it works on some machines and silently does not on others. The previous
// answer was a soft radial blob, which always draws but is not the shape of anything.
//
// This is the classic planar projection: the vertex shader flattens each vertex onto the
// ground plane along the light direction, so the silhouette is the model's own and its edge
// is as crisp as its geometry. It is just a mesh drawn with a material, so it renders the
// same under every pipeline.
//
// Used as a SECOND material on the model's renderers, which is what makes Unity draw the
// same mesh twice: once in colour, once flattened.
Shader "Polyfork/Planar Shadow"
{
    Properties
    {
        _ShadowColor ("Shadow", Color) = (0.10, 0.10, 0.12, 0.38)
        _GroundY ("Ground Height", Float) = 0
        _LightDir ("Direction To Light", Vector) = (0.3, 1.0, 0.35, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1        // toward the camera, so it does not fight the model's own depth

            /* Each pixel once.
             *
             * A projected mesh folds over itself - legs, overhangs, anything with depth - and
             * every overlap blends again, so without this the shadow is a patchwork of
             * darker blotches wherever the silhouette self-covers rather than one flat shape. */
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; };

            fixed4 _ShadowColor;
            float _GroundY;
            float4 _LightDir;

            v2f vert (appdata v)
            {
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 toLight = normalize(_LightDir.xyz);

                /* Walk each vertex back along the light until it reaches the ground. Guarded
                 * because a light at the horizon divides by nothing and throws the shadow to
                 * infinity; clamping the vertical component keeps a grazing light producing a
                 * long shadow rather than a broken one. */
                float ly = max(toLight.y, 0.15);
                float drop = (world.y - _GroundY) / ly;

                world -= toLight * drop;
                world.y = _GroundY;

                v2f o;
                o.pos = UnityObjectToClipPos(mul(unity_WorldToObject, float4(world, 1.0)));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _ShadowColor;
            }
            ENDCG
        }
    }

    Fallback Off
}
