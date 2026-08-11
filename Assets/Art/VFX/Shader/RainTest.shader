Shader "azhao/Rain"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
		_color("Color",Color) = (1,1,1,1)
		_cols("Cols",int) = 1
		_rows("Rows",int) = 1
		_tiling("Tiling",int) = 1
		_speed("Speed",float) = 25
		_startFrame("startFrame",int) = 0
		_NormalTex("Normal Tex", 2D) = "black"{}
		_normalScale("normalScale", Range(0 , 1)) = 0
		_specColor("SpecColor",Color) = (1,1,1,1)
		_shininess("shininess", Range(1 , 100)) = 1
		_specIntensity("specIntensity",Range(0,1)) = 1

    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
				float3 normal:NORMAL;
				float3 tangent:TANGENT;
            };

            struct v2f
            {
                
                float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 worldPos:TEXCOORD1;
				float3 worldNormal : TEXCOORD2;
				float3 worldTangent :TEXCOORD3;
				float3 worldBitangent : TEXCOORD4;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
			float4 _color;
			float _cols;
			float _rows;
			float _tiling;
			float _speed;
			float _startFrame;
			sampler2D _NormalTex;
			float4 _NormalTex_ST;
			float _normalScale;
			float4 _specColor;
			float _shininess;
			float _specIntensity;
			float _ambientIntensity;


			float2 GetSequenceAnimUV(float2 uv,float cols,float rows,float speed,float startFrame)
			{
				float totalTiles = cols * rows;

				float colsOffset = 1.0f / cols;
				float rowsOffset = 1.0f / rows;
				float speedVal = _Time.y * speed;
				float2 offsetTiling = float2(colsOffset, rowsOffset);
				float currentIndex = round(fmod(speedVal + startFrame, totalTiles));
				currentIndex += (currentIndex < 0) ? totalTiles : 0;
				float lineNum = round(fmod(currentIndex, cols));
				float offsetX = lineNum * colsOffset;
				float rowCount = round(fmod((currentIndex - lineNum) / cols, rows));
				rowCount = (int)(rows - 1) - rowCount;
				float offsetY = rowCount * rowsOffset;
				float2 offsetXY = float2(offsetX, offsetY);
				float2 result = uv*offsetTiling +offsetXY;
				return result;
			}

			//简化版的转换法线并缩放的方法
			half3 UnpackScaleNormal(half4 packednormal, half bumpScale)
			{
				half3 normal;
				//由于法线贴图代表的颜色是0到1，而法线向量的范围是-1到1
				//所以通过*2-1,把色值范围转换到-1到1
				normal = packednormal * 2 - 1;
				//对法线进行缩放
				normal.xy *= bumpScale;
				//向量标准化
				normal = normalize(normal);
				return normal;
			}

			//获取HalfLambert漫反射值
			float GetHalfLambertDiffuse(float3 worldPos, float3 worldNormal)
			{
				float3 lightDir = UnityWorldSpaceLightDir(worldPos);
				float NDotL = saturate(dot(worldNormal, lightDir));
				float halfVal = NDotL * 0.5 + 0.5;
				return halfVal;
			}

			//获取BlinnPhong高光
			float GetBlinnPhongSpec(float3 worldPos, float3 worldNormal)
			{
				float3 viewDir = normalize(UnityWorldSpaceViewDir(worldPos));
				float3 halfDir = normalize((viewDir + _WorldSpaceLightPos0.xyz));
				float specDir = max(dot(normalize(worldNormal), halfDir), 0);
				float specVal = pow(specDir, _shininess);
				return specVal;
			}

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.worldPos = mul(unity_ObjectToWorld, v.vertex);
				o.worldNormal = UnityObjectToWorldNormal(v.normal);
				o.worldTangent = UnityObjectToWorldDir(v.tangent);
				o.worldBitangent = cross(o.worldNormal, o.worldTangent);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
				float2 uv = frac(i.worldPos.xz*_tiling);
				float2 normalUV = GetSequenceAnimUV(uv,_cols,_rows,_speed, _startFrame);

				//采样法线贴图的颜色
				half4 normalCol = tex2D(_NormalTex, normalUV);
				//得到切线空间的法线方向
				half3 normalVal = UnpackScaleNormal(normalCol, _normalScale).rgb;

				//构建TBN矩阵
				float3 tanToWorld0 = float3(i.worldTangent.x, i.worldBitangent.x, i.worldNormal.x);
				float3 tanToWorld1 = float3(i.worldTangent.y, i.worldBitangent.y, i.worldNormal.y);
				float3 tanToWorld2 = float3(i.worldTangent.z, i.worldBitangent.z, i.worldNormal.z);

				//通过切线空间的法线方向和TBN矩阵，得出法线贴图代表的物体世界空间的法线方向
				float3 worldNormal = float3(dot(tanToWorld0, normalVal), dot(tanToWorld1, normalVal), dot(tanToWorld2, normalVal));

				//用法线贴图的世界空间法线，算漫反射
				half diffuseVal = GetHalfLambertDiffuse(i.worldPos, worldNormal);

				//用法线贴图的世界空间法线，算高光角度
				half3 specCol = _specColor * GetBlinnPhongSpec(i.worldPos, worldNormal)*_specIntensity;

                half4 col = tex2D(_MainTex,i.uv);
				col.rgb = col.rgb*_color.rgb + specCol;
                return col;
            }
            ENDCG
        }
    }
}