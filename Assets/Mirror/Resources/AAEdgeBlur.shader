Shader "Hidden/Mirror/AAEdgeBlur"
{
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Pass
		{
			CGPROGRAM

			#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			SamplerState my_point_clamp_sampler;
			SamplerState my_linear_clamp_sampler;

			Texture2D _MainTex;

			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;
			#define _Strength 2
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = v.vertex;
				o.uv = v.uv;
				return o;
			}

			inline float Luminance(float3 value)
			{
				return dot(value, float3(0.212f, 0.716f, 0.072f));
			}
			
			float4 frag (v2f i) : SV_Target
			{
				float2 filterSize = 1.0f * _MainTex_TexelSize.xy;
				float luminance_left = Luminance(_MainTex.SampleLevel(my_linear_clamp_sampler, i.uv + filterSize*float2(-1.0f, 0.0f), 0).xyz);
				float luminance_right = Luminance(_MainTex.SampleLevel(my_linear_clamp_sampler, i.uv + filterSize*float2(1.0f, 0.0f), 0).xyz);
				float luminance_top = Luminance(_MainTex.SampleLevel(my_linear_clamp_sampler, i.uv + filterSize*float2(0.0f, -1.0f), 0).xyz);
				float luminance_bottom = Luminance(_MainTex.SampleLevel(my_linear_clamp_sampler, i.uv + filterSize*float2(0.0f, 1.0f), 0).xyz);

				float2 tangent = float2( -(luminance_top - luminance_bottom), (luminance_right - luminance_left) );
				float tangentLength = length(tangent);
				tangent /= tangentLength;
				tangent *= _MainTex_TexelSize.xy;

				float4 color_center = _MainTex.SampleLevel(my_point_clamp_sampler, i.uv, 0);
				float4 color_left = _MainTex.SampleLevel(my_linear_clamp_sampler, i.uv - 0.5f*tangent, 0);// * 0.666f;
				float4 color_left2 = _MainTex.SampleLevel(my_linear_clamp_sampler, i.uv - tangent, 0);// * 0.333f;
				float4 color_right = _MainTex.SampleLevel(my_linear_clamp_sampler, i.uv + 0.5f*tangent, 0);// * 0.666f;
				float4 color_right2 = _MainTex.SampleLevel(my_linear_clamp_sampler, i.uv + tangent, 0);// * 0.333f;

				float4 color_blurred = (color_center + color_left + color_left2 + color_right + color_right2) / 5.0f;
				float blurStrength = saturate(_Strength * tangentLength);

				return lerp(color_center, color_blurred, blurStrength);
			}

			ENDCG
		}
	}
}
