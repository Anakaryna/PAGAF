// FishSwimmingFunction.hlsl - Proper Implementation
void ApplyFishSwimming_float(
    float3 LocalPosition,
    float Time,
    float SwimSpeed,
    float SwimIntensity,
    float SideAmplitude,
    float YawAmplitude,
    float RollAmplitude,
    float FlagYawAmplitude,
    float FishLength,
    float PivotOffset,
    out float3 AnimatedPosition
)
{
    float time = Time * SwimSpeed;
    
    // Calculate distance from pivot
    float distanceFromPivot = LocalPosition.z - PivotOffset;
    float normalizedDistance = distanceFromPivot / FishLength;
    
    // 1. Side-to-side movement
    float sideOffset = sin(time) * SideAmplitude * SwimIntensity;
    
    // 2. Yaw rotation (uniform)
    float yawAngle = sin(time) * radians(YawAmplitude) * SwimIntensity;
    
    // 3. Roll rotation (offset by distance from pivot)
    float rollTime = time + normalizedDistance * 2.0;
    float rollAngle = sin(rollTime) * radians(RollAmplitude) * SwimIntensity;
    
    // 4. Flag-like yaw rotation (offset by distance)
    float flagTime = time + normalizedDistance * 3.0;
    float flagYawAngle = sin(flagTime) * radians(FlagYawAmplitude) * SwimIntensity;
    
    // Apply transformations in correct order:
    
    // First, translate to pivot
    float3 pos = LocalPosition;
    pos.z -= PivotOffset;
    
    // Apply rotations (ORDER MATTERS!)
    
    // 1. Apply uniform yaw rotation (Y-axis)
    float c1 = cos(yawAngle);
    float s1 = sin(yawAngle);
    float3 temp1 = pos;
    pos.x = c1 * temp1.x + s1 * temp1.z;
    pos.z = -s1 * temp1.x + c1 * temp1.z;
    
    // 2. Apply roll rotation (Z-axis)
    float c2 = cos(rollAngle);
    float s2 = sin(rollAngle);
    float3 temp2 = pos;
    pos.x = c2 * temp2.x - s2 * temp2.y;
    pos.y = s2 * temp2.x + c2 * temp2.y;
    
    // 3. Apply flag-like yaw rotation (Y-axis)
    float c3 = cos(flagYawAngle);
    float s3 = sin(flagYawAngle);
    float3 temp3 = pos;
    pos.x = c3 * temp3.x + s3 * temp3.z;
    pos.z = -s3 * temp3.x + c3 * temp3.z;
    
    // Translate back from pivot
    pos.z += PivotOffset;
    
    // 4. Apply side-to-side offset AFTER rotations
    pos.x += sideOffset;
    
    AnimatedPosition = pos;
}