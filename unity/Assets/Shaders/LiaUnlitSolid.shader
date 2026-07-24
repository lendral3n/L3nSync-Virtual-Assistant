// Unlit URP untuk karakter di overlay TRANSPARAN.
// Sama seperti URP/Unlit (flat anime, tanpa shading dinamis) TAPI alpha output
// dipaksa = 1 → piksel badan opaque tetap terlihat di framebuffer transparan
// (macOS Metal tidak menghormati preserveFramebufferAlpha untuk geometri opaque,
// jadi alpha tekstur VRM yang < 1 bikin karakter ikut tembus — ini fix-nya).
// Cull Off supaya bagian double-sided (rok/rambut) tidak bolong.
Shader "LiaVA/UnlitSolid"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Unlit"
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 col = tex.rgb * _BaseColor.rgb;
                return half4(col, 1.0h);   // alpha SELALU 1 → opaque di overlay transparan
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
