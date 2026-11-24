// A small hand-inked mark for a map (MapMark). The shape comes from _Shape (MarkShape order:
// 0 Dot, 1 Arrow, 2 Cross, 3 Ring, 4 Steps, 5 Square); colour from the graphic. Edges wobble a little so marks look
// drawn with a pen rather than stamped.
Shader "Cartography/InkMark"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Shape ("Shape", Float) = 0
        _Wobble ("Wobble", Range(0, 0.3)) = 0.08

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "InkMark"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float _Shape, _Wobble;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float box(float2 p, float2 b)
            {
                float2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            float segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Signed distance in mark space (-1..1 across the rect), negative inside.
            float shapeDistance(float2 p, int shape)
            {
                if (shape == 1) // Arrow, pointing up
                {
                    float2 q = float2(abs(p.x), p.y);
                    // Triangle with tip (0, 0.85) and base corners (+-0.6, -0.65), notched at the back.
                    float side = dot(q - float2(0.0, 0.85), normalize(float2(1.5, 0.6)));
                    float back = -0.65 - q.y + q.x * 0.5;
                    return max(side, back);
                }
                if (shape == 2) // Cross
                    return min(segment(p, float2(-0.65, -0.65), float2(0.65, 0.65)), segment(p, float2(-0.65, 0.65), float2(0.65, -0.65))) - 0.17;
                if (shape == 3) // Ring
                    return abs(length(p) - 0.62) - 0.14;
                if (shape == 4) // Steps
                {
                    float d = box(p - float2(0.0, 0.5), float2(0.3, 0.12));
                    d = min(d, box(p - float2(0.0, 0.0), float2(0.55, 0.12)));
                    d = min(d, box(p - float2(0.0, -0.5), float2(0.8, 0.12)));
                    return d;
                }
                if (shape == 5) // Square
                    return box(p, float2(0.6, 0.6));
                return length(p) - 0.6; // Dot
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float d = shapeDistance(p, (int)round(_Shape));
                d += (noise(p * 3.0 + _Shape * 7.1) - 0.5) * _Wobble;
                float aa = max(fwidth(d), 1e-4);
                float a = saturate(0.5 - d / aa);

                fixed4 result = fixed4(i.color.rgb, i.color.a * a);
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
        ENDCG
        }
    }
}
