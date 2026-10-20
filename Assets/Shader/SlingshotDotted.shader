Shader "Gatsha/SlingshotDotted"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _LineLength ("Line Length", Float) = 1
        _DashLength ("Dash Length", Float) = 0.08
        _DashGap ("Dash Gap", Float) = 0.06
        _Dashed ("Dashed", Float) = 1
        _FadeToEnd ("Fade To End", Float) = 0
        _FadeStart ("Fade Start", Range(0, 0.99)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _Tint;
            float _LineLength;
            float _DashLength;
            float _DashGap;
            float _Dashed;
            float _FadeToEnd;
            float _FadeStart;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float t = saturate(input.uv.x);
                float distance = t * max(_LineLength, 0.0001);
                float period = max(_DashLength + _DashGap, 0.002);
                float phase = frac(distance / period) * period;
                float edge = max(fwidth(distance), 0.0001);
                float dash = smoothstep(0.0, edge, min(phase, _DashLength - phase));
                float pattern = lerp(1.0, dash, saturate(_Dashed));
                float fade = 1.0 - smoothstep(clamp(_FadeStart, 0.0, 0.99), 1.0, t);
                float opacity = lerp(1.0, fade, saturate(_FadeToEnd));
                fixed4 color = input.color * _Tint;
                color.a *= pattern * opacity;
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
