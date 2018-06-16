Shader "Hidden/RadiusBlur"
{
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always
CGINCLUDE
#include "UnityCG.cginc"
sampler2D _MainTex; float4 _MainTex_TexelSize;
float4 _Direction;	//offset direction XY
static const float4 BLUR = float4(0.4260422633680632, 0.17057398775107083, 0.08757560537305543, 0.02882927519184219);
			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = v.vertex;
				o.uv = v.uv;
				return o;
			}
			
			
ENDCG
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			float4 frag (v2f i) : SV_Target
			{
				float4 c = tex2D(_MainTex, i.uv) * BLUR.x;
				float2 offset = float2(0, _MainTex_TexelSize.y);
				c += (tex2D(_MainTex, i.uv + offset) + tex2D(_MainTex, i.uv - offset)) * BLUR.y;
				float2 offset2 = offset * 2;
				c += (tex2D(_MainTex, i.uv + offset2) + tex2D(_MainTex, i.uv - offset2)) * BLUR.z;
				float2 offset3 = offset * 3;
				c += (tex2D(_MainTex, i.uv + offset3) + tex2D(_MainTex, i.uv - offset3)) * BLUR.w;
				return c;
			}
			ENDCG
		}
	}
}
