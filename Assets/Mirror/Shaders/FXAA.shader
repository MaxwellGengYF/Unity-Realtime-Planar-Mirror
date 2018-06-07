Shader "Hidden/Mirror/FXAA"
{
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Pass
		{
			CGPROGRAM

			#pragma target 5.0
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"

			#define FXAA_PC 1
			#define FXAA_HLSL_5 1
			#define FXAA_GREEN_AS_LUMA 1
			#define FXAA_QUALITY__PRESET 12
			#include "fxaa_3.11.hlsl"

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

			Texture2D _MainTex;

			SamplerState my_point_clamp_sampler;
			SamplerState my_linear_clamp_sampler;

			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;
			
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = (v.vertex);
				o.uv = v.uv;
				return o;
			}

			float Luminance(float3 value)
			{
				return dot(value, float3(0.212f, 0.716f, 0.072f));
			}
			
			float4 frag (v2f i) : SV_Target
			{
				FxaaTex fxaaTexture;
				fxaaTexture.tex = _MainTex;
				fxaaTexture.smpl = my_linear_clamp_sampler;

				return FxaaPixelShader(
					i.uv,
					float4(0.0f, 0.0f, 0.0f, 0.0f),
					fxaaTexture,
					fxaaTexture,
					fxaaTexture,
					_MainTex_TexelSize.xy,
					float4(0.0f, 0.0f, 0.0f, 0.0f),
					float4(0.0f, 0.0f, 0.0f, 0.0f),
					float4(0.0f, 0.0f, 0.0f, 0.0f),
					0.75f,
					0.166f,
					0.0f,
					0.0f,
					0.0f,
					0.0f,
					float4(0.0f, 0.0f, 0.0f, 0.0f)
				);
			}

			ENDCG
		}
	}
}
