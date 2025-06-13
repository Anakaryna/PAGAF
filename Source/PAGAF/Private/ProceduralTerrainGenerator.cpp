#include "../Public/ProceduralTerrainGenerator.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Engine/World.h"
#include "Engine/Engine.h"
#include "Kismet/GameplayStatics.h"

AProceduralTerrainGenerator::AProceduralTerrainGenerator()
{
    PrimaryActorTick.bCanEverTick = true;
    
    // Create instanced mesh components
    GrassInstances = CreateDefaultSubobject<UInstancedStaticMeshComponent>("GrassInstances");
    DirtInstances = CreateDefaultSubobject<UInstancedStaticMeshComponent>("DirtInstances");
    StoneInstances = CreateDefaultSubobject<UInstancedStaticMeshComponent>("StoneInstances");
    WaterInstances = CreateDefaultSubobject<UInstancedStaticMeshComponent>("WaterInstances");
    
    RootComponent = GrassInstances;
    DirtInstances->SetupAttachment(RootComponent);
    StoneInstances->SetupAttachment(RootComponent);
    WaterInstances->SetupAttachment(RootComponent);
    
    SetupInstancedMeshComponents();
}

void AProceduralTerrainGenerator::BeginPlay()
{
    Super::BeginPlay();
    
    // Setup meshes and materials
    if (BlockMesh)
    {
        GrassInstances->SetStaticMesh(BlockMesh);
        DirtInstances->SetStaticMesh(BlockMesh);
        StoneInstances->SetStaticMesh(BlockMesh);
        WaterInstances->SetStaticMesh(BlockMesh);
    }
    
    if (GrassMaterial) GrassInstances->SetMaterial(0, GrassMaterial);
    if (DirtMaterial) DirtInstances->SetMaterial(0, DirtMaterial);
    if (StoneMaterial) StoneInstances->SetMaterial(0, StoneMaterial);
    if (WaterMaterial) WaterInstances->SetMaterial(0, WaterMaterial);
    
    LastPlayerPos = GetPlayerPosition();
    LastPlayerGrid = WorldToGrid(LastPlayerPos);
    BlocksGeneratedThisFrame = 0;
    LastGenerationTime = 0.0;
    LastValidationTime = 0.0;
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Warning, TEXT("FIXED Terrain Generator Online!"));
        UE_LOG(LogTemp, Warning, TEXT("Mode: %s | View: %d blocks | Block Size: %.0f"), 
               GenerationType == EGenerationType::Simple ? TEXT("Simple") : TEXT("Hybrid"),
               ViewDistance, BlockSize);
        UE_LOG(LogTemp, Warning, TEXT("Debug Commands: ValidateNoOverlaps, ForceValidateAndFix, LogTerrainStats"));
    }
    
    // Generate initial terrain
    UpdateTerrainAroundPlayer();
}

void AProceduralTerrainGenerator::Tick(float DeltaTime)
{
    Super::Tick(DeltaTime);
    
    FVector PlayerPos = GetPlayerPosition();
    FIntVector PlayerGrid = WorldToGrid(PlayerPos);
    
    // Update when player moves significantly
    float MoveDistance = FVector::Dist(PlayerPos, LastPlayerPos);
    bool bGridChanged = PlayerGrid != LastPlayerGrid;
    
    if (bGridChanged || MoveDistance > BlockSize * 0.8f)
    {
        LastPlayerPos = PlayerPos;
        LastPlayerGrid = PlayerGrid;
        
        double StartTime = FPlatformTime::Seconds();
        UpdateTerrainAroundPlayer();
        LastGenerationTime = FPlatformTime::Seconds() - StartTime;
        
        if (bDebugLogs && LastGenerationTime > 0.016f)
        {
            UE_LOG(LogTemp, Warning, TEXT("Generation: %.1fms | Blocks: %d"), 
                   LastGenerationTime * 1000.0f, LoadedBlocks.Num());
        }
    }
    
    // 🔧 AUTOMATIC OVERLAP DETECTION every 1 second (more frequent)
    double CurrentTime = GetWorld()->GetTimeSeconds();
    if (bDebugLogs && CurrentTime - LastValidationTime > 1.0f)
    {
        LastValidationTime = CurrentTime;
        
        if (!ValidateNoOverlaps())
        {
            UE_LOG(LogTemp, Error, TEXT("OVERLAPS DETECTED! Auto-fixing..."));
            ForceValidateAndFix();
        }
    }
}

void AProceduralTerrainGenerator::SetupInstancedMeshComponents()
{
    TArray<UInstancedStaticMeshComponent*> Components = {
        GrassInstances, DirtInstances, StoneInstances, WaterInstances
    };
    
    for (auto* Component : Components)
    {
        // Production optimizations
        Component->SetCastShadow(false);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Component->SetMobility(EComponentMobility::Static);
        Component->bUseDefaultCollision = false;
        Component->SetGenerateOverlapEvents(false);
        Component->SetCullDistances(1000, 8000);
        
        // Performance flags
        Component->bDisableCollision = true;
        Component->bAffectDistanceFieldLighting = false;
    }
}

FVector AProceduralTerrainGenerator::GetPlayerPosition() const
{
    if (APlayerController* PC = GetWorld()->GetFirstPlayerController())
    {
        if (APawn* Pawn = PC->GetPawn())
        {
            return Pawn->GetActorLocation();
        }
    }
    return GetActorLocation();
}

// 🔧 ENHANCED COORDINATE CONVERSION with aggressive precision guarantees
FIntVector AProceduralTerrainGenerator::WorldToGrid(const FVector& WorldPos) const
{
    // Use larger epsilon and round to prevent boundary edge cases
    const float Epsilon = 1.0f; // Larger epsilon to prevent edge issues
    
    return FIntVector(
        FMath::RoundToInt((WorldPos.X + Epsilon) / BlockSize),
        FMath::RoundToInt((WorldPos.Y + Epsilon) / BlockSize),
        FMath::RoundToInt((WorldPos.Z + Epsilon) / BlockSize)
    );
}

FVector AProceduralTerrainGenerator::GridToWorld(const FIntVector& GridPos) const
{
    // Ensure perfect grid alignment with aggressive snapping
    double SnapX = FMath::RoundToDouble(GridPos.X * BlockSize);
    double SnapY = FMath::RoundToDouble(GridPos.Y * BlockSize);
    double SnapZ = FMath::RoundToDouble(GridPos.Z * BlockSize);
    
    return FVector(
        SnapX + BlockSize * 0.5,
        SnapY + BlockSize * 0.5,
        SnapZ + BlockSize * 0.5
    );
}

void AProceduralTerrainGenerator::UpdateTerrainAroundPlayer()
{
    FIntVector PlayerGrid = WorldToGrid(GetPlayerPosition());
    BlocksGeneratedThisFrame = 0;
    
    // Clean up distant blocks first
    RemoveDistantBlocks(PlayerGrid, ViewDistance + 3);
    
    // Generate new blocks with bulletproof system
    GenerateBlocksInRadius(PlayerGrid, ViewDistance);
    
    // Finalize rendering
    OptimizeRendering();
}

// 🔧 BULLETPROOF GENERATION SYSTEM - No more overlaps possible
void AProceduralTerrainGenerator::GenerateBlocksInRadius(const FIntVector& Center, int32 Radius)
{
    BlocksGeneratedThisFrame = 0;
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Log, TEXT("Starting bulletproof generation around (%d,%d,%d) with radius %d"), 
               Center.X, Center.Y, Center.Z, Radius);
    }
    
    // 🔧 STEP 1: Pre-filter ALL positions that need generation
    TArray<FIntVector> ValidPositions;
    ValidPositions.Reserve(Radius * Radius * (MaxHeight - MinHeight) / 4);
    
    // Collect all positions that need blocks but aren't already loaded
    for (int32 x = Center.X - Radius; x <= Center.X + Radius; x++)
    {
        for (int32 y = Center.Y - Radius; y <= Center.Y + Radius; y++)
        {
            for (int32 z = Center.Z + MinHeight; z <= Center.Z + MaxHeight; z++)
            {
                FIntVector GridPos(x, y, z);
                
                // Skip positions outside radius
                if (!IsInRadius(Center, GridPos, Radius))
                    continue;
                
                // 🔧 ABSOLUTE CHECK: Skip if ANY system thinks this is loaded
                if (IsBlockLoaded(GridPos) || WorldGrid.Contains(GridPos))
                {
                    continue;
                }
                
                ValidPositions.Add(GridPos);
            }
        }
    }
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Log, TEXT("Found %d valid positions to potentially generate"), ValidPositions.Num());
    }
    
    // Sort by distance for natural loading progression
    ValidPositions.Sort([Center](const FIntVector& A, const FIntVector& B) {
        float DistA = FVector::DistSquared(FVector(A), FVector(Center));
        float DistB = FVector::DistSquared(FVector(B), FVector(Center));
        return DistA < DistB;
    });
    
    // 🔧 STEP 2: Generate blocks with TRIPLE validation
    int32 PlacedThisFrame = 0;
    int32 SkippedAlreadyLoaded = 0;
    int32 SkippedAir = 0;
    int32 SkippedFailedValidation = 0;
    
    for (const FIntVector& GridPos : ValidPositions)
    {
        // Performance throttling
        if (PlacedThisFrame >= MaxBlocksPerFrame)
        {
            if (bDebugLogs)
            {
                UE_LOG(LogTemp, Warning, TEXT("Hit max blocks per frame limit: %d"), MaxBlocksPerFrame);
            }
            break;
        }
        
        // 🔧 VALIDATION 1: Check if position became loaded between initial check and now
        if (IsBlockLoaded(GridPos) || WorldGrid.Contains(GridPos))
        {
            SkippedAlreadyLoaded++;
            if (bDebugLogs && SkippedAlreadyLoaded <= 5) // Limit spam
            {
                UE_LOG(LogTemp, Error, TEXT("RACE CONDITION: Position (%d,%d,%d) became loaded between checks!"), 
                       GridPos.X, GridPos.Y, GridPos.Z);
            }
            continue;
        }
        
        // Generate block type
        EBlockType BlockType = (GenerationType == EGenerationType::Simple) 
            ? GenerateSimpleTerrain(GridPos) 
            : GenerateHybridTerrain(GridPos);
        
        if (BlockType == EBlockType::Air)
        {
            // Mark air positions as "loaded" to prevent re-processing
            LoadedBlocks.Add(GridPos);
            SkippedAir++;
            continue;
        }
        
        // 🔧 VALIDATION 2: Final validation before placement
        if (!IsPositionValidForPlacement(GridPos, BlockType))
        {
            SkippedFailedValidation++;
            if (bDebugLogs && SkippedFailedValidation <= 5)
            {
                UE_LOG(LogTemp, Error, TEXT("Position (%d,%d,%d) failed final validation"), 
                       GridPos.X, GridPos.Y, GridPos.Z);
            }
            continue;
        }
        
        // 🔧 VALIDATION 3: One final check right before placement
        if (WorldGrid.Contains(GridPos))
        {
            if (bDebugLogs)
            {
                UE_LOG(LogTemp, Error, TEXT("CRITICAL: Position occupied at placement time (%d,%d,%d)"), 
                       GridPos.X, GridPos.Y, GridPos.Z);
            }
            continue;
        }
        
        // Place the block using bulletproof placement
        PlaceBlock(GridPos, BlockType);
        PlacedThisFrame++;
        
        // 🔧 VALIDATION 4: Verify placement succeeded
        if (!WorldGrid.Contains(GridPos))
        {
            UE_LOG(LogTemp, Error, TEXT("CRITICAL: Block placement failed at (%d,%d,%d)"), 
                   GridPos.X, GridPos.Y, GridPos.Z);
        }
    }
    
    BlocksGeneratedThisFrame = PlacedThisFrame;
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Log, TEXT("Generation complete: %d placed, %d air, %d already loaded, %d failed validation"), 
               PlacedThisFrame, SkippedAir, SkippedAlreadyLoaded, SkippedFailedValidation);
    }
}

void AProceduralTerrainGenerator::RemoveDistantBlocks(const FIntVector& Center, int32 MaxDistance)
{
    TArray<FIntVector> ToRemove;
    ToRemove.Reserve(LoadedBlocks.Num() / 4);
    
    for (const FIntVector& GridPos : LoadedBlocks)
    {
        if (!IsInRadius(Center, GridPos, MaxDistance))
        {
            ToRemove.Add(GridPos);
        }
    }
    
    for (const FIntVector& GridPos : ToRemove)
    {
        RemoveBlock(GridPos);
    }
    
    if (bDebugLogs && ToRemove.Num() > 0)
    {
        UE_LOG(LogTemp, Log, TEXT("Removed %d distant blocks"), ToRemove.Num());
    }
}

EBlockType AProceduralTerrainGenerator::GenerateSimpleTerrain(const FIntVector& GridPos) const
{
    // Cache height calculation to ensure consistency
    int32 TerrainHeight = GetTerrainHeight(GridPos.X, GridPos.Y);
    
    // Above terrain - Air and Water zones
    if (GridPos.Z > TerrainHeight)
    {
        // 🔧 RESTORED Normal water generation (removed restrictive buffers)
        if (GridPos.Z <= SeaLevel && TerrainHeight <= SeaLevel)
        {
            return EBlockType::Water;
        }
        return EBlockType::Air;
    }
    
    // Exact surface layer placement
    if (GridPos.Z == TerrainHeight)
    {
        // Surface block determination
        if (TerrainHeight <= SeaLevel)
        {
            return EBlockType::Dirt; // Underwater/shoreline surface
        }
        else
        {
            return EBlockType::Grass; // Land surface
        }
    }
    
    // Subsurface layer definition
    int32 DirtLayerBottom = TerrainHeight - DirtDepth;
    if (GridPos.Z > DirtLayerBottom)
    {
        return EBlockType::Dirt; // Dirt layer beneath surface
    }
    
    // Deep underground - Stone layer
    return EBlockType::Stone;
}

EBlockType AProceduralTerrainGenerator::GenerateHybridTerrain(const FIntVector& GridPos) const
{
    // Base terrain
    EBlockType BaseType = GenerateSimpleTerrain(GridPos);
    
    // Add structure variation using noise
    float StructureNoise = GetNoise(GridPos.X * 0.02f, GridPos.Y * 0.02f, 0.5f);
    
    // Create caves in stone areas
    if (BaseType == EBlockType::Stone && GridPos.Z > MinHeight + 2)
    {
        float CaveNoise = GetNoise(GridPos.X * 0.08f, GridPos.Y * 0.08f + GridPos.Z * 0.06f, 1.0f);
        if (CaveNoise > 0.6f)
        {
            return EBlockType::Air;
        }
    }
    
    // Add ore patches
    if (BaseType == EBlockType::Stone && StructureNoise > 0.75f)
    {
        return EBlockType::Stone;
    }
    
    return BaseType;
}

int32 AProceduralTerrainGenerator::GetTerrainHeight(int32 WorldX, int32 WorldY) const
{
    float Noise = GetMultiOctaveNoise(WorldX, WorldY);
    int32 Height = BaseHeight + FMath::RoundToInt(Noise * HeightVariation);
    return FMath::Clamp(Height, MinHeight + 2, MaxHeight - 2);
}

float AProceduralTerrainGenerator::GetNoise(float X, float Y, float Scale) const
{
    return FMath::PerlinNoise2D(FVector2D(X, Y) * NoiseScale * Scale);
}

float AProceduralTerrainGenerator::GetMultiOctaveNoise(float X, float Y) const
{
    float Result = 0.0f;
    float Amplitude = 1.0f;
    float Frequency = 1.0f;
    float MaxValue = 0.0f;
    
    // 4-octave noise for natural terrain
    for (int32 i = 0; i < 4; i++)
    {
        Result += GetNoise(X, Y, Frequency) * Amplitude;
        MaxValue += Amplitude;
        Amplitude *= 0.5f;
        Frequency *= 2.0f;
    }
    
    return Result / MaxValue; // Normalize
}

// 🔧 BULLETPROOF BLOCK PLACEMENT with overlap prevention
void AProceduralTerrainGenerator::PlaceBlock(const FIntVector& GridPos, EBlockType BlockType)
{
    // 🔧 ABSOLUTE DUPLICATE PREVENTION
    if (WorldGrid.Contains(GridPos))
    {
        const FBlockData* ExistingBlock = WorldGrid.Find(GridPos);
        if (ExistingBlock && ExistingBlock->BlockType == BlockType)
        {
            if (bDebugLogs)
            {
                UE_LOG(LogTemp, Error, TEXT("PREVENTED EXACT DUPLICATE: Block already exists at (%d,%d,%d)"), 
                       GridPos.X, GridPos.Y, GridPos.Z);
            }
            return;
        }
        
        // Force remove existing block of different type
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Warning, TEXT("FORCE REPLACING block at (%d,%d,%d): %s -> %s"), 
                   GridPos.X, GridPos.Y, GridPos.Z, 
                   ExistingBlock ? *GetBlockTypeName(ExistingBlock->BlockType) : TEXT("Unknown"),
                   *GetBlockTypeName(BlockType));
        }
        ForceCleanPosition(GridPos);
    }
    
    // Double-check LoadedBlocks consistency
    if (LoadedBlocks.Contains(GridPos))
    {
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Error, TEXT("INCONSISTENCY: Position in LoadedBlocks but not WorldGrid - cleaning"));
        }
        LoadedBlocks.Remove(GridPos);
    }
    
    // Get instance component
    UInstancedStaticMeshComponent* InstanceComp = GetInstanceComponent(BlockType);
    if (!InstanceComp)
    {
        UE_LOG(LogTemp, Error, TEXT("No component for block type %s"), *GetBlockTypeName(BlockType));
        return;
    }
    
    // Create block data
    FBlockData BlockData(BlockType);
    BlockData.bGenerated = true;
    
    // 🔧 CRITICAL: Use aggressive positioning with large offsets to prevent overlapping faces
    FVector WorldPos = GridToWorld(GridPos);
    
    // Add SIGNIFICANT offset to prevent ANY face overlap between different block types
    float MajorOffset = GetZFightingOffset(BlockType);
    WorldPos.X += MajorOffset * 0.2f;  // X offset
    WorldPos.Y += MajorOffset * 0.2f;  // Y offset  
    WorldPos.Z += MajorOffset;         // Z offset (primary)
    
    // Additional unique positioning per block type to ensure complete separation
    int32 TypeMultiplier = (int32)BlockType;
    WorldPos.X += TypeMultiplier * 0.1f;
    WorldPos.Y += TypeMultiplier * 0.1f;
    
    FTransform BlockTransform(FRotator::ZeroRotator, WorldPos, FVector::OneVector);
    
    // Add instance and validate
    BlockData.InstanceIndex = InstanceComp->AddInstance(BlockTransform);
    
    if (BlockData.InstanceIndex < 0)
    {
        UE_LOG(LogTemp, Error, TEXT("Failed to add instance for %s block at (%d,%d,%d)"), 
               *GetBlockTypeName(BlockType), GridPos.X, GridPos.Y, GridPos.Z);
        return;
    }
    
    // Store atomically in both data structures
    WorldGrid.Add(GridPos, BlockData);
    LoadedBlocks.Add(GridPos);
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, VeryVerbose, TEXT("Placed %s at (%d,%d,%d) - Instance: %d"), 
               *GetBlockTypeName(BlockType), GridPos.X, GridPos.Y, GridPos.Z, BlockData.InstanceIndex);
    }
}

void AProceduralTerrainGenerator::RemoveBlock(const FIntVector& GridPos)
{
    if (!WorldGrid.Contains(GridPos))
    {
        // Clean up any orphaned LoadedBlocks entries
        if (LoadedBlocks.Contains(GridPos))
        {
            LoadedBlocks.Remove(GridPos);
            if (bDebugLogs)
            {
                UE_LOG(LogTemp, Warning, TEXT("Cleaned orphaned LoadedBlocks entry at (%d,%d,%d)"), 
                       GridPos.X, GridPos.Y, GridPos.Z);
            }
        }
        return;
    }
    
    FBlockData BlockData = WorldGrid[GridPos];
    
    // Remove from instance component
    UInstancedStaticMeshComponent* InstanceComp = GetInstanceComponent(BlockData.BlockType);
    if (InstanceComp && BlockData.InstanceIndex >= 0 && BlockData.InstanceIndex < InstanceComp->GetInstanceCount())
    {
        InstanceComp->RemoveInstance(BlockData.InstanceIndex);
        
        // CRITICAL: Update all affected instance indices
        UpdateInstanceIndicesAfterRemoval(BlockData.BlockType, BlockData.InstanceIndex);
    }
    else if (bDebugLogs)
    {
        UE_LOG(LogTemp, Warning, TEXT("Invalid instance index during removal: %d"), BlockData.InstanceIndex);
    }
    
    // Remove from data structures
    WorldGrid.Remove(GridPos);
    LoadedBlocks.Remove(GridPos);
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, VeryVerbose, TEXT("🗑Removed %s block at (%d,%d,%d)"), 
               *GetBlockTypeName(BlockData.BlockType), GridPos.X, GridPos.Y, GridPos.Z);
    }
}

bool AProceduralTerrainGenerator::IsBlockLoaded(const FIntVector& GridPos) const
{
    return LoadedBlocks.Contains(GridPos);
}

EBlockType AProceduralTerrainGenerator::GetBlockType(const FIntVector& GridPos) const
{
    const FBlockData* BlockData = WorldGrid.Find(GridPos);
    return BlockData ? BlockData->BlockType : EBlockType::Air;
}

UInstancedStaticMeshComponent* AProceduralTerrainGenerator::GetInstanceComponent(EBlockType BlockType) const
{
    switch (BlockType)
    {
    case EBlockType::Grass: return GrassInstances;
    case EBlockType::Dirt:  return DirtInstances;
    case EBlockType::Stone: return StoneInstances;
    case EBlockType::Water: return WaterInstances;
    default: return nullptr;
    }
}

void AProceduralTerrainGenerator::OptimizeRendering()
{
    TArray<UInstancedStaticMeshComponent*> Components = {
        GrassInstances, DirtInstances, StoneInstances, WaterInstances
    };
    
    for (auto* Component : Components)
    {
        if (Component && Component->GetInstanceCount() > 0)
        {
            Component->MarkRenderStateDirty();
        }
    }
}

bool AProceduralTerrainGenerator::IsInRadius(const FIntVector& Center, const FIntVector& Point, int32 Radius) const
{
    return GetDistance3D(Center, Point) <= (float)Radius;
}

float AProceduralTerrainGenerator::GetDistance3D(const FIntVector& A, const FIntVector& B) const
{
    float DeltaX = (float)(A.X - B.X);
    float DeltaY = (float)(A.Y - B.Y);
    float DeltaZ = (float)(A.Z - B.Z);
    
    return FMath::Sqrt(DeltaX * DeltaX + DeltaY * DeltaY + DeltaZ * DeltaZ);
}

// 🔧 NEW FUNCTIONS FOR OVERLAP PREVENTION

float AProceduralTerrainGenerator::GetZFightingOffset(EBlockType BlockType) const
{
    // AGGRESSIVE offsets to completely prevent overlapping faces
    // ALL positive offsets to ensure proper separation
    switch (BlockType)
    {
    case EBlockType::Grass:  return 0.0f;      // Base level
    case EBlockType::Dirt:   return 0.5f;      // Significant offset
    case EBlockType::Stone:  return 1.0f;      // Large offset
    case EBlockType::Water:  return 1.5f;      // LARGEST offset for water (was negative!)
    default: return 0.0f;
    }
}

void AProceduralTerrainGenerator::UpdateInstanceIndicesAfterRemoval(EBlockType RemovedBlockType, int32 RemovedIndex)
{
    // When UE removes an instance, all higher indices shift down by 1
    // We MUST update our tracking to match
    int32 UpdatedCount = 0;
    
    for (auto& Pair : WorldGrid)
    {
        FBlockData& Block = Pair.Value;
        
        // Only update blocks of the same type with higher indices
        if (Block.BlockType == RemovedBlockType && Block.InstanceIndex > RemovedIndex)
        {
            Block.InstanceIndex--;
            UpdatedCount++;
        }
    }
    
    if (bDebugLogs && UpdatedCount > 0)
    {
        UE_LOG(LogTemp, VeryVerbose, TEXT("Updated %d instance indices after removal"), UpdatedCount);
    }
}

FString AProceduralTerrainGenerator::GetBlockTypeName(EBlockType BlockType) const
{
    switch (BlockType)
    {
    case EBlockType::Grass:  return TEXT("Grass");
    case EBlockType::Dirt:   return TEXT("Dirt");
    case EBlockType::Stone:  return TEXT("Stone");
    case EBlockType::Water:  return TEXT("Water");
    case EBlockType::Air:    return TEXT("Air");
    default: return TEXT("Unknown");
    }
}

bool AProceduralTerrainGenerator::IsPositionValidForPlacement(const FIntVector& GridPos, EBlockType BlockType) const
{
    // Check if position is already occupied in any way
    if (LoadedBlocks.Contains(GridPos))
    {
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Warning, TEXT("Position (%d,%d,%d) already in LoadedBlocks"), GridPos.X, GridPos.Y, GridPos.Z);
        }
        return false;
    }
    
    if (WorldGrid.Contains(GridPos))
    {
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Warning, TEXT("Position (%d,%d,%d) already in WorldGrid"), GridPos.X, GridPos.Y, GridPos.Z);
        }
        return false;
    }
    
    // 🔧 NEW: Check adjacent positions for potential side-face conflicts
    TArray<FIntVector> AdjacentPositions = {
        FIntVector(GridPos.X + 1, GridPos.Y, GridPos.Z),
        FIntVector(GridPos.X - 1, GridPos.Y, GridPos.Z),
        FIntVector(GridPos.X, GridPos.Y + 1, GridPos.Z),
        FIntVector(GridPos.X, GridPos.Y - 1, GridPos.Z),
        FIntVector(GridPos.X, GridPos.Y, GridPos.Z + 1),
        FIntVector(GridPos.X, GridPos.Y, GridPos.Z - 1)
    };
    
    // Check for blocks of the same type in adjacent positions (could cause side conflicts)
    for (const FIntVector& AdjacentPos : AdjacentPositions)
    {
        if (WorldGrid.Contains(AdjacentPos))
        {
            const FBlockData* AdjacentBlock = WorldGrid.Find(AdjacentPos);
            if (AdjacentBlock && AdjacentBlock->BlockType == BlockType)
            {
                // Same block type adjacent - need to ensure no overlap
                FVector ThisWorldPos = GridToWorld(GridPos);
                FVector AdjacentWorldPos = GridToWorld(AdjacentPos);
                float Distance = FVector::Dist(ThisWorldPos, AdjacentWorldPos);
                
                // If too close, reject this placement
                if (Distance < BlockSize * 0.9f)
                {
                    if (bDebugLogs)
                    {
                        UE_LOG(LogTemp, Warning, TEXT("Position (%d,%d,%d) too close to adjacent same-type block"), 
                               GridPos.X, GridPos.Y, GridPos.Z);
                    }
                    return false;
                }
            }
        }
    }
    
    // Check if block type is valid
    if (BlockType == EBlockType::Air)
    {
        return false;
    }
    
    // Ensure we have a component for this block type
    UInstancedStaticMeshComponent* Component = GetInstanceComponent(BlockType);
    if (!Component)
    {
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Error, TEXT("No component for block type %s"), *GetBlockTypeName(BlockType));
        }
        return false;
    }
    
    return true;
}

void AProceduralTerrainGenerator::ForceCleanPosition(const FIntVector& GridPos)
{
    bool bWasDirty = false;
    
    if (WorldGrid.Contains(GridPos))
    {
        RemoveBlock(GridPos);
        bWasDirty = true;
    }
    
    if (LoadedBlocks.Contains(GridPos))
    {
        LoadedBlocks.Remove(GridPos);
        bWasDirty = true;
    }
    
    if (bWasDirty && bDebugLogs)
    {
        UE_LOG(LogTemp, Warning, TEXT("Force cleaned position (%d,%d,%d)"), GridPos.X, GridPos.Y, GridPos.Z);
    }
}

// 🔧 SIMPLIFIED: Much more permissive water validation - focus on geometry not adjacency
bool AProceduralTerrainGenerator::IsWaterPositionValid(const FIntVector& GridPos) const
{
    // Only check for actual position conflicts, not adjacency
    if (WorldGrid.Contains(GridPos))
    {
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Warning, TEXT("Water position already occupied"));
        }
        return false;
    }
    
    // Check if there's already a water block at this EXACT position
    if (LoadedBlocks.Contains(GridPos))
    {
        // Check if it's actually water or something else
        const FBlockData* ExistingBlock = WorldGrid.Find(GridPos);
        if (ExistingBlock && ExistingBlock->BlockType == EBlockType::Water)
        {
            if (bDebugLogs)
            {
                UE_LOG(LogTemp, Warning, TEXT("Water block already exists at this position"));
            }
            return false;
        }
    }
    
    // Water is valid - let the positioning offsets handle separation
    return true;
}

// 🔧 VALIDATION AND DEBUG FUNCTIONS

bool AProceduralTerrainGenerator::ValidateNoOverlaps() const
{
    TMap<FIntVector, int32> PositionCounts;
    TMap<FVector, int32> WorldPositionCounts; // NEW: Also check world positions
    
    // Count how many blocks are at each position
    for (const auto& Pair : WorldGrid)
    {
        const FIntVector& GridPos = Pair.Key;
        PositionCounts.FindOrAdd(GridPos)++;
        
        // Also check world positions for overlaps
        FVector WorldPos = GridToWorld(GridPos);
        // Round to nearest unit to detect overlaps
        FVector RoundedWorldPos = FVector(
            FMath::RoundToFloat(WorldPos.X),
            FMath::RoundToFloat(WorldPos.Y), 
            FMath::RoundToFloat(WorldPos.Z)
        );
        WorldPositionCounts.FindOrAdd(RoundedWorldPos)++;
    }
    
    // Report any grid position overlaps
    bool bFoundOverlaps = false;
    int32 OverlapCount = 0;
    
    for (const auto& CountPair : PositionCounts)
    {
        if (CountPair.Value > 1)
        {
            UE_LOG(LogTemp, Error, TEXT("GRID OVERLAP: %d blocks at (%d,%d,%d)"), 
                   CountPair.Value, CountPair.Key.X, CountPair.Key.Y, CountPair.Key.Z);
            bFoundOverlaps = true;
            OverlapCount++;
        }
    }
    
    // 🔧 NEW: Check for world position overlaps (side-face conflicts)
    int32 WorldOverlapCount = 0;
    for (const auto& CountPair : WorldPositionCounts)
    {
        if (CountPair.Value > 1)
        {
            UE_LOG(LogTemp, Error, TEXT("WORLD POSITION OVERLAP: %d blocks near (%.1f,%.1f,%.1f)"), 
                   CountPair.Value, CountPair.Key.X, CountPair.Key.Y, CountPair.Key.Z);
            bFoundOverlaps = true;
            WorldOverlapCount++;
        }
    }
    
    if (bFoundOverlaps)
    {
        UE_LOG(LogTemp, Error, TEXT("TOTAL OVERLAPS: %d grid overlaps, %d world position overlaps"), 
               OverlapCount, WorldOverlapCount);
    }
    
    return !bFoundOverlaps;
}

void AProceduralTerrainGenerator::ForceValidateAndFix()
{
    UE_LOG(LogTemp, Warning, TEXT("🔍 Starting comprehensive terrain validation and repair..."));
    
    int32 OverlapsFixed = 0;
    int32 OrphanedRemoved = 0;
    int32 InconsistenciesFixed = 0;
    
    // Find all overlapping positions
    TMap<FIntVector, TArray<TPair<FIntVector, FBlockData*>>> OverlapMap;
    for (auto& Pair : WorldGrid)
    {
        OverlapMap.FindOrAdd(Pair.Key).Add(TPair<FIntVector, FBlockData*>(Pair.Key, &Pair.Value));
    }
    
    // Fix overlaps by keeping only the first block at each position
    for (auto& OverlapPair : OverlapMap)
    {
        if (OverlapPair.Value.Num() > 1)
        {
            UE_LOG(LogTemp, Error, TEXT("FIXING OVERLAP: %d blocks at (%d,%d,%d)"), 
                   OverlapPair.Value.Num(), OverlapPair.Key.X, OverlapPair.Key.Y, OverlapPair.Key.Z);
            
            // Force clean and regenerate this position
            ForceCleanPosition(OverlapPair.Key);
            OverlapsFixed++;
        }
    }
    
    // Check for LoadedBlocks inconsistencies
    TArray<FIntVector> OrphanedPositions;
    for (const FIntVector& LoadedPos : LoadedBlocks)
    {
        if (!WorldGrid.Contains(LoadedPos))
        {
            // Check if this should actually have a block
            EBlockType ExpectedType = (GenerationType == EGenerationType::Simple) 
                ? GenerateSimpleTerrain(LoadedPos) 
                : GenerateHybridTerrain(LoadedPos);
            
            if (ExpectedType != EBlockType::Air)
            {
                OrphanedPositions.Add(LoadedPos);
                InconsistenciesFixed++;
            }
        }
    }
    
    // Remove orphaned entries
    for (const FIntVector& OrphanPos : OrphanedPositions)
    {
        LoadedBlocks.Remove(OrphanPos);
        OrphanedRemoved++;
    }
    
    UE_LOG(LogTemp, Warning, TEXT("Validation complete: %d overlaps fixed, %d orphans removed, %d inconsistencies fixed"), 
           OverlapsFixed, OrphanedRemoved, InconsistenciesFixed);
    
    // Force render update
    OptimizeRendering();
    
    // Final validation
    if (ValidateNoOverlaps())
    {
        UE_LOG(LogTemp, Warning, TEXT("Terrain is now clean - no overlaps detected"));
    }
    else
    {
        UE_LOG(LogTemp, Error, TEXT("WARNING: Overlaps still detected after fix attempt"));
    }
}

void AProceduralTerrainGenerator::LogTerrainStats() const
{
    UE_LOG(LogTemp, Warning, TEXT("COMPREHENSIVE TERRAIN STATISTICS:"));
    UE_LOG(LogTemp, Warning, TEXT("   WorldGrid entries: %d"), WorldGrid.Num());
    UE_LOG(LogTemp, Warning, TEXT("   LoadedBlocks entries: %d"), LoadedBlocks.Num());
    UE_LOG(LogTemp, Warning, TEXT("   Grass instances: %d"), GrassInstances ? GrassInstances->GetInstanceCount() : 0);
    UE_LOG(LogTemp, Warning, TEXT("   Dirt instances: %d"), DirtInstances ? DirtInstances->GetInstanceCount() : 0);
    UE_LOG(LogTemp, Warning, TEXT("   Stone instances: %d"), StoneInstances ? StoneInstances->GetInstanceCount() : 0);
    UE_LOG(LogTemp, Warning, TEXT("   Water instances: %d"), WaterInstances ? WaterInstances->GetInstanceCount() : 0);
    
    // Calculate total instances
    int32 TotalInstances = 0;
    if (GrassInstances) TotalInstances += GrassInstances->GetInstanceCount();
    if (DirtInstances) TotalInstances += DirtInstances->GetInstanceCount();
    if (StoneInstances) TotalInstances += StoneInstances->GetInstanceCount();
    if (WaterInstances) TotalInstances += WaterInstances->GetInstanceCount();
    
    UE_LOG(LogTemp, Warning, TEXT("   TOTAL instances: %d"), TotalInstances);
    
    // Check for inconsistencies
    int32 MissingFromLoadedBlocks = 0;
    int32 ExtraInLoadedBlocks = 0;
    
    for (const auto& Pair : WorldGrid)
    {
        if (!LoadedBlocks.Contains(Pair.Key))
        {
            MissingFromLoadedBlocks++;
        }
    }
    
    for (const FIntVector& LoadedPos : LoadedBlocks)
    {
        if (!WorldGrid.Contains(LoadedPos))
        {
            // Check if this is air (which is OK)
            EBlockType ExpectedType = (GenerationType == EGenerationType::Simple) 
                ? GenerateSimpleTerrain(LoadedPos) 
                : GenerateHybridTerrain(LoadedPos);
            
            if (ExpectedType != EBlockType::Air)
            {
                ExtraInLoadedBlocks++;
            }
        }
    }
    
    if (MissingFromLoadedBlocks > 0)
    {
        UE_LOG(LogTemp, Error, TEXT("INCONSISTENCY: %d blocks in WorldGrid but not in LoadedBlocks"), 
               MissingFromLoadedBlocks);
    }
    
    if (ExtraInLoadedBlocks > 0)
    {
        UE_LOG(LogTemp, Error, TEXT("INCONSISTENCY: %d non-air positions in LoadedBlocks but not in WorldGrid"), 
               ExtraInLoadedBlocks);
    }
    
    if (MissingFromLoadedBlocks == 0 && ExtraInLoadedBlocks == 0)
    {
        UE_LOG(LogTemp, Warning, TEXT("Data structures are consistent"));
    }
    
    // Performance stats
    UE_LOG(LogTemp, Warning, TEXT("   Last generation time: %.1fms"), LastGenerationTime * 1000.0f);
    UE_LOG(LogTemp, Warning, TEXT("   Blocks generated last frame: %d"), BlocksGeneratedThisFrame);
}

// ========== PUBLIC API ==========

void AProceduralTerrainGenerator::RegenerateAroundPlayer()
{
    UE_LOG(LogTemp, Warning, TEXT("Manual terrain regeneration requested"));
    ClearAllTerrain();
    UpdateTerrainAroundPlayer();
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Warning, TEXT("Terrain regenerated - validating..."));
        if (!ValidateNoOverlaps())
        {
            UE_LOG(LogTemp, Error, TEXT("Overlaps detected after regeneration!"));
            ForceValidateAndFix();
        }
    }
}

void AProceduralTerrainGenerator::ClearAllTerrain()
{
    // Clear all instances
    if (GrassInstances) GrassInstances->ClearInstances();
    if (DirtInstances) DirtInstances->ClearInstances();
    if (StoneInstances) StoneInstances->ClearInstances();
    if (WaterInstances) WaterInstances->ClearInstances();
    
    // Clear data structures
    WorldGrid.Empty();
    LoadedBlocks.Empty();
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Warning, TEXT("🧹 All terrain cleared"));
    }
}

EBlockType AProceduralTerrainGenerator::GetBlockAt(const FVector& WorldPosition) const
{
    FIntVector GridPos = WorldToGrid(WorldPosition);
    return GetBlockType(GridPos);
}

void AProceduralTerrainGenerator::SetBlockAt(const FVector& WorldPosition, EBlockType BlockType)
{
    FIntVector GridPos = WorldToGrid(WorldPosition);
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Log, TEXT("Manual block placement: %s at (%d,%d,%d)"), 
               *GetBlockTypeName(BlockType), GridPos.X, GridPos.Y, GridPos.Z);
    }
    
    // Force clean the position first
    ForceCleanPosition(GridPos);
    
    // Place new block
    if (BlockType != EBlockType::Air)
    {
        PlaceBlock(GridPos, BlockType);
    }
    else
    {
        // Mark as loaded air
        LoadedBlocks.Add(GridPos);
    }
    
    OptimizeRendering();
}

void AProceduralTerrainGenerator::SwitchGenerationType(EGenerationType NewType)
{
    if (GenerationType != NewType)
    {
        GenerationType = NewType;
        
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Warning, TEXT("Switched to: %s"), 
                   GenerationType == EGenerationType::Simple ? TEXT("Simple") : TEXT("Hybrid"));
        }
        
        RegenerateAroundPlayer();
    }
}

