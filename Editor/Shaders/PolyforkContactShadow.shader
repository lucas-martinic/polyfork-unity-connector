// The soft shadow a model sits in.
//
// Not a real-time shadow. PreviewRenderUtility renders into its own scene and its shadow
// support is unreliable - under a scriptable pipeline it frequently produces none at all,
// whatever the light is configured to do - so depending on it means the feature works on
// some machines and silently does not on others.
//
// This is a radial falloff computed from the quad's UVs: no texture, no light, no pipeline
// opinion. It draws the same everywhere, which for a contact shadow under a flat-shaded
// model is most of what a real one would have given.
Shader "Polyfork/Contact Shadow"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 0.22)
        _Softness ("Softness", Range(0.01, 1.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Color;
            float _Softness;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Distance from the middle of the quad, 0 at the centre and 1 at the edge.
                float d = saturate(length(i.uv - 0.5) * 2.0);

                /* Falls off from the very centre rather than holding a plateau and then
                 * fading. The plateau version drew a disc with a visible rim - read as "a
                 * weird circle under the object" rather than as a shadow - and because it was
                 * warm-tinted, blending it over the cream background turned it orange. Pure
                 * black now, and never flat. */
                float a = pow(1.0 - d, 2.4) * _Softness;

                return fixed4(_Color.rgb, _Color.a * saturate(a));
            }
            ENDCG
        }
    }

    Fallback Off
}
