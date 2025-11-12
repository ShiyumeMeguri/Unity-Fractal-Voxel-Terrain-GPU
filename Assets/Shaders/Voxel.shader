Shader "Custom/Voxel"
{
    Properties
    {
        // Exposed Properties
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _AOColor ("AO Color", Color) = (0,0,0,1)
        _AOIntensity ("AO Intensity", Range(0, 1)) = 1.0
        _AOPower ("AO Power", Range(1, 10)) = 1.0

        // Properties for CBUFFER variables to be compatible with SRP Batcher
        [HideInInspector] _AtlasX ("Atlas X Size", Float) = 8
        [HideInInspector] _AtlasY ("Atlas Y Size", Float) = 8
        [HideInInspector] _AtlasRec ("Atlas Reciprocal", Vector) = (0.125, 0.125, 0, 0)
        [HideInInspector] _LightColor0 ("Atlas Reciprocal", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode"="ForwardBase" } // 指定LightMode以接收光照
        LOD 200

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase // 编译ForwardBase光照Pass

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 texcoord : TEXCOORD1; // custo_uv in surface shader
                float4 color : COLOR;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 custo_uv : TEXCOORD0;
                float4 color : COLOR;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            sampler2D _MainTex;

            // CBUFFER is compatible with both URP and built-in pipeline
            CBUFFER_START(UnityPerMaterial)
                half _Glossiness;
                half _Metallic; // Note: In custom lighting, metallic has a different meaning
                fixed4 _Color;

                float _AtlasX;
                float _AtlasY;
                fixed4 _AtlasRec;
                fixed4 _LightColor0;

                half4 _AOColor;
                float _AOIntensity;
                float _AOPower;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.custo_uv = v.texcoord;
                
                // AO calculation moved from surf to vert, as it's based on vertex color
                float3 ao_color = _AOColor.rgb;
                float ao_mix = pow((1.0 - v.color.a) * _AOIntensity, _AOPower);

                // Pass AO color and intensity to frag via the v2f.color
                o.color.rgb = ao_color;
                o.color.a = ao_mix;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // -- Logic from original 'surf' function --
                fixed2 atlasOffset = i.custo_uv.zw;
                fixed2 scaledUV = i.custo_uv.xy;
                fixed2 atlasUV = scaledUV;

                atlasUV.x = (atlasOffset.x * _AtlasRec.x) + frac(atlasUV.x) * _AtlasRec.x;
                atlasUV.y = ((_AtlasY - 1.0 - atlasOffset.y) * _AtlasRec.y) + frac(atlasUV.y) * _AtlasRec.y;

                fixed4 c = tex2D(_MainTex, atlasUV) * _Color;
                fixed3 albedo = c.rgb;

                // Apply AO
                albedo = lerp(albedo, i.color.rgb, i.color.a);

                // -- Manual Lighting Calculation to replace 'Standard' model --
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos.xyz);

                // Ambient Light
                fixed3 ambient = UNITY_LIGHTMODEL_AMBIENT.xyz * albedo;

                // Diffuse Light
                float ndotl = max(0.0, dot(normal, lightDir));
                fixed3 diffuse = _LightColor0.rgb * albedo * ndotl;

                // Specular Light (Blinn-Phong)
                float3 halfwayDir = normalize(lightDir + viewDir);
                float spec = pow(max(0.0, dot(normal, halfwayDir)), _Glossiness * 128.0); // Remap smoothness to specular power
                fixed3 specular = _LightColor0.rgb * spec;
                
                // Combine lighting components
                // Note: a true metallic workflow is more complex. This is a simplified version.
                fixed3 finalColor = ambient + lerp(diffuse, 0, _Metallic) + lerp(specular, 0, _Metallic);

                return fixed4(finalColor, c.a);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}