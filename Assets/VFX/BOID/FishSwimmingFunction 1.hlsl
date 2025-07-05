// FishSwimmingFunction.hlsl - 8 Parameters Version
void ApplyFishSwimming_float(
    float3 LocalPosition,
    float Time,
    float SwimSpeed,
    float SwimIntensity,
    float SideAmplitude,
    float YawAmplitude,
    float FlagYawAmplitude,
    float PivotOffset,
    out float3 AnimatedPosition
)
{
    AnimatedPosition = LocalPosition;
    
    float time = Time * SwimSpeed;
    
    // Use a fixed fish length instead of parameter
    float fishLength = 2.0; // Fixed value
    
    // Calculate distance from pivot and normalize it
    float distanceFromPivot = LocalPosition.z - PivotOffset;
    float normalizedDistance = distanceFromPivot / fishLength;
    
    // Side-to-side movement
    float sideOffset = sin(time) * SideAmplitude * SwimIntensity;
    
    // Simple yaw effect
    float yawEffect = sin(time) * (YawAmplitude * 0.001) * SwimIntensity * distanceFromPivot;
    
    // Tail wagging
    float tailEffect = sin(time + LocalPosition.z * 3.0) * 0.01 * SwimIntensity;
    
    // FLAG EFFECT
    float flagTime = time + normalizedDistance * 2.0;
    float flagEffect = sin(flagTime) * (FlagYawAmplitude * 0.002) * SwimIntensity * abs(normalizedDistance);
    
    AnimatedPosition.x += sideOffset + yawEffect + tailEffect + flagEffect;
}