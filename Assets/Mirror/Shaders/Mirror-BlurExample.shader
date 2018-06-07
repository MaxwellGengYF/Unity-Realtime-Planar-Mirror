/*
 This is just an example of mirror's post processing shader.
 */
Shader "Hidden/Mirror-Blur"
{
properties{
	_MainTex("TEX", 2D) = "white"{}
}
	SubShader
	{
	CGINCLUDE
	#include "UnityCG.cginc"
	sampler2D _MainTex;
	sampler2D _SignTex;
	float4 _MainTex_TexelSize;
	#define BLUR0 0.12516840610182164
	#define BLUR1 0.11975714566876787
	#define BLUR2 0.10488697964330942
	#define BLUR3 0.08409209097592142
	#define BLUR4 0.061716622693291805
	#define BLUR5 0.04146317758515726
	#define BLUR6 0.025499780382641484
	#define Gaus(offset, blur)\
		offsetUV = uv + offset;\
		sign = tex2D(_SignTex, offsetUV).r;\
		col = lerp(originColor, tex2D(_MainTex, offsetUV), sign);\
		c += col * blur;\
		offsetUV = uv - offset;\
		sign = tex2D(_SignTex, offsetUV).r;\
		col = lerp(originColor, tex2D(_MainTex, offsetUV), sign);\
		c += col * blur;

    inline float4 getWeightedColor(float2 uv, float2 offset){
		float4 originColor =  tex2D(_MainTex, uv);
		float sign = tex2D(_SignTex, uv).r;
		offset *= saturate(sign);
    	float4 c = originColor * BLUR0;
		float4 col;
		float2 off = offset;
		float2 offsetUV;
		Gaus(off,BLUR1)
		off = offset * 2;
		Gaus(off, BLUR2)
		off = offset * 3;
		Gaus(off, BLUR3)
		off = offset * 4;
		Gaus(off, BLUR4)
		off = offset * 5;
		Gaus(off, BLUR5)
		off = offset * 6;
		Gaus(off, BLUR6)
		return c;
    }
    	struct v2f_mg
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				float2 offset : TEXCOORD1;
			};
    		inline float4 frag_blur (v2f_mg i) : SV_Target
			{
				return getWeightedColor(i.uv, i.offset);
			}

	ENDCG
	//0. vert 1. hori 2. blend 3. add
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
		//Vertical 
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag_blur


			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};



			inline v2f_mg vert (appdata v)
			{
				v2f_mg o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.offset = _MainTex_TexelSize.xyxy * float2(0,1);
				return o;
			}
			ENDCG
		}

		Pass
		{
		//Horizontal 
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag_blur


			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};


			inline v2f_mg vert (appdata v)
			{
				v2f_mg o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.offset = _MainTex_TexelSize.xyxy * float2(1,0);
				return o;
			}
			ENDCG
		}
	}
}
