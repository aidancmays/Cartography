// An inked, hand-printed map of one MapLayer, for UI (MapView).
//
//   _MainTex     the layer's plan: R floor coverage, G walls
//   _RevealTex   R how much of each cell has been seen
//   _View        (u, v, width, height) of the layer shown by the rect, in layer UV
//   _LayerSize   (metres x, metres z, cells x, cells z)
//   _RectSize    rect size in canvas units, for the rough sheet edge
//
// Unseen paper stays blank; seen floor gets a mottled wash with pigment pooled at its edge, an inked outline and
// walls; the ground just outside rooms is hatched, as on old engraved plans. The edge of what has been seen bleeds
// like wet paint.
Shader "Cartography/InkMap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Plan", 2D) = "black" {}
        _RevealTex ("Reveal", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _PaperColor ("Paper", Color) = (0.86, 0.80, 0.67, 1)
        _WashColor ("Floor wash", Color) = (0.66, 0.55, 0.40, 1)
        _InkColor ("Ink", Color) = (0.16, 0.12, 0.09, 1)
        _FogColor ("Unseen", Color) = (0.78, 0.72, 0.60, 1)

        [Header(Lines)]
        _OutlineWidth ("Outline width (cells)", Float) = 1.2
        _WallStrength ("Wall ink", Range(0, 1)) = 1
        _Wobble ("Line wobble (cells)", Float) = 1.2
        _HatchSpacing ("Hatch spacing (m)", Float) = 0.7
        _HatchReach ("Hatch reach (m)", Float) = 2.5
        _HatchStrength ("Hatch ink", Range(0, 1)) = 0.8

        [Header(Wash)]
        _Pooling ("Pigment pooling", Range(0, 1)) = 0.35
        _Mottle ("Mottling", Range(0, 1)) = 0.25
        _BleedScale ("Reveal edge size (m)", Float) = 3
        _Bleed ("Reveal edge raggedness", Range(0, 1)) = 0.55

        [Header(Sheet)]
        _Grain ("Grain", Range(0, 1)) = 0.25
        _EdgeRoughness ("Sheet edge roughness", Float) = 6
        _EdgeScale ("Sheet edge noise size", Float) = 40

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
            Name "InkMap"
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

            sampler2D _MainTex, _RevealTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _View, _LayerSize, _RectSize;
            fixed4 _PaperColor, _WashColor, _InkColor, _FogColor;
            float _OutlineWidth, _WallStrength, _Wobble, _HatchSpacing, _HatchReach, _HatchStrength;
            float _Pooling, _Mottle, _BleedScale, _Bleed;
            float _Grain, _EdgeRoughness, _EdgeScale;

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
                float a = hash(i);
                float b = hash(i + float2(1, 0));
                float c = hash(i + float2(0, 1));
                float d = hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                return noise(p) * 0.57 + noise(p * 2.03 + 17.1) * 0.29 + noise(p * 4.01 + 41.7) * 0.14;
            }

            float planR(float2 uv)
            {
                // Outside the layer is empty paper (clamped sampling would smear the border cells).
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return tex2D(_MainTex, uv).r * inside;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 luv = _View.xy + i.uv * _View.zw;              // layer UV
                float2 m = luv * _LayerSize.xy;                       // metres, fixed to the ground
                float2 texel = 1.0 / max(_LayerSize.zw, 1.0);
                float cellM = _LayerSize.x * texel.x;                 // metres per cell
                float inside = step(0.0, luv.x) * step(luv.x, 1.0) * step(0.0, luv.y) * step(luv.y, 1.0);

                // Lines wander a little, like a hand following a ruler badly.
                float2 wob = (float2(fbm(m * 0.35), fbm(m * 0.35 + 31.7)) - 0.5) * texel * 2.0 * _Wobble;
                float2 puv = luv + wob;

                float4 plan = tex2D(_MainTex, puv) * inside;
                float f = plan.r;
                float floorMask = smoothstep(0.35, 0.65, f);

                // Outline: where floor coverage changes fast.
                float2 o = texel * _OutlineWidth;
                float gx = planR(puv + float2(o.x, 0)) - planR(puv - float2(o.x, 0));
                float gy = planR(puv + float2(0, o.y)) - planR(puv - float2(0, o.y));
                float outline = saturate(length(float2(gx, gy)) * 1.6 - 0.15);

                float wall = smoothstep(0.25, 0.75, plan.g) * _WallStrength;

                // Near a room but outside it: hatched ground.
                float reach = _HatchReach / max(cellM, 1e-4);
                float near = 0.0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float a = k * 0.785398;
                    near += planR(puv + float2(cos(a), sin(a)) * texel * reach);
                    near += planR(puv + float2(cos(a + 0.39), sin(a + 0.39)) * texel * reach * 0.5);
                }
                near = saturate(near / 16.0 * 3.0);
                float hatchZone = near * (1.0 - floorMask);
                float h = frac((m.x + m.y) / max(_HatchSpacing, 0.05) + (fbm(m * 0.8) - 0.5) * 0.25);
                float aa = fwidth((m.x + m.y) / max(_HatchSpacing, 0.05)) * 2.0 + 1e-4;
                float hatch = 1.0 - smoothstep(0.2, 0.2 + aa, abs(h - 0.5) * 2.0);
                hatch *= hatchZone * (0.6 + 0.4 * noise(m * 1.7));

                // What has been seen, with a ragged wet edge.
                float seen = tex2D(_RevealTex, luv).r * inside;
                float bleed = (fbm(m / max(_BleedScale, 0.1)) - 0.5) * _Bleed;
                float shown = smoothstep(0.35, 0.6, seen + bleed);
                float rim = smoothstep(0.25, 0.45, seen + bleed) * (1.0 - smoothstep(0.45, 0.65, seen + bleed));

                // Paper.
                float2 px = i.vertex.xy;
                float grain = (hash(floor(px / 2.0)) - 0.5) * _Grain;
                float mottle = (fbm(m * 0.12) - 0.5) * 2.0;
                fixed3 col = _PaperColor.rgb * (1.0 + grain * 0.25 + mottle * 0.03);
                col = lerp(col, _FogColor.rgb, (1.0 - shown) * 0.35);

                // Floor wash with pigment pooled at its edge.
                float pool = saturate(outline * 2.0) * _Pooling;
                fixed3 wash = _WashColor.rgb * (1.0 + mottle * _Mottle * 0.5) * (1.0 - pool);
                col = lerp(col, wash, floorMask * shown * 0.85);

                // Ink.
                float ink = max(max(outline, wall), hatch * _HatchStrength) * shown;
                ink *= 0.85 + 0.15 * noise(m * 3.0);
                col = lerp(col, _InkColor.rgb, saturate(ink));

                // Damp rim where the reveal stops.
                col = lerp(col, _WashColor.rgb * 0.85, rim * 0.25);

                // Rough edge of the sheet.
                float2 rp = i.uv * max(_RectSize.xy, 1.0);
                float d = min(min(rp.x, _RectSize.x - rp.x), min(rp.y, _RectSize.y - rp.y));
                float edge = d - _EdgeRoughness * (1.0 - fbm(rp / max(_EdgeScale, 1.0)));
                float sheet = saturate(edge / 1.5 + 0.5);
                col *= 1.0 - (1.0 - saturate(edge / (_EdgeRoughness * 3.0 + 1.0))) * 0.25;

                fixed4 result = fixed4(col, sheet) * i.color;

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
