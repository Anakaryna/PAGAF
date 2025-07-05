Shader "Custom/InstancedFishShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            // Per-instance data
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _SwimPhase)
                UNITY_DEFINE_INSTANCED_PROP(float, _SwimSpeed)
                UNITY_DEFINE_INSTANCED_PROP(float, _SwimAmplitude)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                // Get per-instance properties
                float swimPhase = UNITY_ACCESS_INSTANCED_PROP(Props, _SwimPhase);
                float swimSpeed = UNITY_ACCESS_INSTANCED_PROP(Props, _SwimSpeed);
                float swimAmplitude = UNITY_ACCESS_INSTANCED_PROP(Props, _SwimAmplitude);

                // Calculate swimming animation
                float time = _Time.y * swimSpeed + swimPhase;
                float wave = sin(time + v.vertex.z * 3.0);
                
                // Tail influence (stronger at back)
                float tailInfluence = saturate(-v.vertex.z + 0.5);
                
                // Apply vertex deformation
                v.vertex.x += wave * swimAmplitude * tailInfluence;
                v.vertex.y += sin(time * 0.7 + v.vertex.z * 2.0) * 
                              swimAmplitude * 0.4 * tailInfluence;

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDCG
        }
    }
}