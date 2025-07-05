Shader "Custom/AbzuFishSwimming"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        
        [Header(Swimming Parameters)]
        _SwimSpeed ("Swim Speed", Range(0.1, 5.0)) = 1.0
        _SwimIntensity ("Swim Intensity", Range(0.0, 2.0)) = 1.0
        
        [Header(Side to Side Motion)]
        _SideAmplitude ("Side Amplitude", Range(0.0, 0.5)) = 0.1
        
        [Header(Yaw Rotation)]
        _YawAmplitude ("Yaw Amplitude", Range(0.0, 45.0)) = 15.0
        
        [Header(Roll Rotation)]
        _RollAmplitude ("Roll Amplitude", Range(0.0, 45.0)) = 10.0
        
        [Header(Flag Motion)]
        _FlagYawAmplitude ("Flag Yaw Amplitude", Range(0.0, 45.0)) = 20.0
        
        [Header(Fish Proportions)]
        _FishLength ("Fish Length", Range(0.1, 5.0)) = 2.0
        _PivotOffset ("Pivot Offset", Range(-1.0, 1.0)) = 0.3
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        
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
                float3 normal : NORMAL;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD1;
            };
            
            sampler2D _MainTex;  // FIXED: was "sampled2D"
            float4 _MainTex_ST;
            fixed4 _Color;
            
            float _SwimSpeed;
            float _SwimIntensity;
            float _SideAmplitude;
            float _YawAmplitude;
            float _RollAmplitude;
            float _FlagYawAmplitude;
            float _FishLength;
            float _PivotOffset;
            
            // Rotation matrices
            float3x3 rotateY(float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float3x3(
                    c, 0, s,
                    0, 1, 0,
                    -s, 0, c
                );
            }
            
            float3x3 rotateZ(float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float3x3(
                    c, -s, 0,
                    s, c, 0,
                    0, 0, 1
                );
            }
            
            float3 applyFishSwimming(float3 localPos)
            {
                float time = _Time.y * _SwimSpeed;
                
                // Calculate distance from pivot (assuming fish head is at +Z)
                float pivotPoint = _PivotOffset;
                float distanceFromPivot = localPos.z - pivotPoint;
                
                // Normalize distance for consistent motion regardless of fish size
                float normalizedDistance = distanceFromPivot / _FishLength;
                
                // 1. MOTION 1: Side-to-side movement
                float sideOffset = sin(time) * _SideAmplitude * _SwimIntensity;
                
                // 2. MOTION 2: Yaw rotation around pivot (uniform)
                float yawAngle = sin(time) * radians(_YawAmplitude) * _SwimIntensity;
                
                // 3. MOTION 3: Roll rotation (offset by distance from pivot)
                float rollTime = time + normalizedDistance * 2.0;
                float rollAngle = sin(rollTime) * radians(_RollAmplitude) * _SwimIntensity;
                
                // 4. MOTION 4: Flag-like yaw rotation (offset by distance)
                float flagTime = time + normalizedDistance * 3.0;
                float flagYawAngle = sin(flagTime) * radians(_FlagYawAmplitude) * _SwimIntensity;
                
                // Apply transformations in correct order:
                
                // First, translate to pivot
                float3 pos = localPos;
                pos.z -= pivotPoint;
                
                // Apply rotations (ORDER MATTERS!)
                // 1. Apply uniform yaw rotation
                pos = mul(rotateY(yawAngle), pos);
                
                // 2. Apply roll rotation
                pos = mul(rotateZ(rollAngle), pos);
                
                // 3. Apply flag-like yaw rotation
                pos = mul(rotateY(flagYawAngle), pos);
                
                // Translate back from pivot
                pos.z += pivotPoint;
                
                // 4. Apply side-to-side offset AFTER rotations
                pos.x += sideOffset;
                
                return pos;
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                
                // Apply fish swimming animation
                float3 animatedPos = applyFishSwimming(v.vertex.xyz);
                
                o.vertex = UnityObjectToClipPos(float4(animatedPos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                // Transform normal (simplified)
                o.normal = UnityObjectToWorldNormal(v.normal);
                
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                
                // Simple lighting
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = max(0, dot(normalize(i.normal), lightDir));
                col.rgb *= NdotL * 0.5 + 0.5;
                
                return col;
            }
            ENDCG
        }
    }
}