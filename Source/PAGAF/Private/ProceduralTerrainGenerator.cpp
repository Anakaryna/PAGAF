// ProceduralTerrainGenerator.cpp
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
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Warning, TEXT("🚀 Production Terrain Generator Online!"));
        UE_LOG(LogTemp, Warning, TEXT("📊 Mode: %s | View: %d blocks | Block Size: %.0f"), 
               GenerationType == EGenerationType::Simple ? TEXT("Simple") : TEXT("Hybrid"),
               ViewDistance, BlockSize);
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
            UE_LOG(LogTemp, Warning, TEXT("⚡ Generation: %.1fms | Blocks: %d"), 
                   LastGenerationTime * 1000.0f, LoadedBlocks.Num());
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

FIntVector AProceduralTerrainGenerator::WorldToGrid(const FVector& WorldPos) const
{
    return FIntVector(
        FMath::FloorToInt(WorldPos.X / BlockSize),
        FMath::FloorToInt(WorldPos.Y / BlockSize),
        FMath::FloorToInt(WorldPos.Z / BlockSize)
    );
}

FVector AProceduralTerrainGenerator::GridToWorld(const FIntVector& GridPos) const
{
    return FVector(
        GridPos.X * BlockSize + BlockSize * 0.5f,
        GridPos.Y * BlockSize + BlockSize * 0.5f,
        GridPos.Z * BlockSize + BlockSize * 0.5f
    );
}

void AProceduralTerrainGenerator::UpdateTerrainAroundPlayer()
{
    FIntVector PlayerGrid = WorldToGrid(GetPlayerPosition());
    BlocksGeneratedThisFrame = 0;
    
    // Clean up distant blocks first
    RemoveDistantBlocks(PlayerGrid, ViewDistance + 3);
    
    // Generate new blocks in spherical pattern
    GenerateBlocksInRadius(PlayerGrid, ViewDistance);
    
    // Finalize rendering
    OptimizeRendering();
}

void AProceduralTerrainGenerator::GenerateBlocksInRadius(const FIntVector& Center, int32 Radius)
{
    // Pre-calculate all positions to generate
    TArray<FIntVector> Positions;
    Positions.Reserve((Radius * 2 + 1) * (Radius * 2 + 1) * (MaxHeight - MinHeight + 1));
    
    for (int32 x = Center.X - Radius; x <= Center.X + Radius; x++)
    {
        for (int32 y = Center.Y - Radius; y <= Center.Y + Radius; y++)
        {
            for (int32 z = Center.Z + MinHeight; z <= Center.Z + MaxHeight; z++)
            {
                FIntVector GridPos(x, y, z);
                
                if (!IsInRadius(Center, GridPos, Radius) || IsBlockLoaded(GridPos))
                    continue;
                
                Positions.Add(GridPos);
            }
        }
    }
    
    // Sort by distance for smooth loading
    Positions.Sort([Center](const FIntVector& A, const FIntVector& B) {
        float DistA = FVector::DistSquared(FVector(A), FVector(Center));
        float DistB = FVector::DistSquared(FVector(B), FVector(Center));
        return DistA < DistB;
    });
    
    // Generate with performance throttling
    for (const FIntVector& GridPos : Positions)
    {
        if (BlocksGeneratedThisFrame >= MaxBlocksPerFrame)
            break;
        
        EBlockType BlockType = (GenerationType == EGenerationType::Simple) 
            ? GenerateSimpleTerrain(GridPos) 
            : GenerateHybridTerrain(GridPos);
        
        if (BlockType != EBlockType::Air)
        {
            PlaceBlock(GridPos, BlockType);
            BlocksGeneratedThisFrame++;
        }
        else
        {
            LoadedBlocks.Add(GridPos);
        }
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
        UE_LOG(LogTemp, Log, TEXT("🗑️ Removed %d distant blocks"), ToRemove.Num());
    }
}

EBlockType AProceduralTerrainGenerator::GenerateSimpleTerrain(const FIntVector& GridPos) const
{
    int32 TerrainHeight = GetTerrainHeight(GridPos.X, GridPos.Y);
    
    // Above terrain
    if (GridPos.Z > TerrainHeight)
    {
        // Water generation in low areas
        if (GridPos.Z <= SeaLevel && TerrainHeight <= SeaLevel)
        {
            return EBlockType::Water;
        }
        return EBlockType::Air;
    }
    
    // Surface layer
    if (GridPos.Z == TerrainHeight)
    {
        return (TerrainHeight <= SeaLevel) ? EBlockType::Dirt : EBlockType::Grass;
    }
    
    // Subsurface layers
    if (GridPos.Z > TerrainHeight - DirtDepth)
    {
        return EBlockType::Dirt;
    }
    
    // Deep layers
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
        // Could return a different stone type or ore here
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

void AProceduralTerrainGenerator::PlaceBlock(const FIntVector& GridPos, EBlockType BlockType)
{
    // Prevent duplicate placement
    if (WorldGrid.Contains(GridPos))
    {
        if (bDebugLogs)
        {
            UE_LOG(LogTemp, Error, TEXT("🚨 Overlap prevented at (%d,%d,%d)"), 
                   GridPos.X, GridPos.Y, GridPos.Z);
        }
        return;
    }
    
    // Get instance component
    UInstancedStaticMeshComponent* InstanceComp = GetInstanceComponent(BlockType);
    if (!InstanceComp)
    {
        UE_LOG(LogTemp, Error, TEXT("❌ No component for block type %d"), (int32)BlockType);
        return;
    }
    
    // Create and place block
    FBlockData BlockData(BlockType);
    BlockData.bGenerated = true;
    
    FVector WorldPos = GridToWorld(GridPos);
    FTransform BlockTransform(WorldPos);
    
    BlockData.InstanceIndex = InstanceComp->AddInstance(BlockTransform);
    
    // Store in world grid
    WorldGrid.Add(GridPos, BlockData);
    LoadedBlocks.Add(GridPos);
}

void AProceduralTerrainGenerator::RemoveBlock(const FIntVector& GridPos)
{
    if (!WorldGrid.Contains(GridPos))
        return;
    
    FBlockData& BlockData = WorldGrid[GridPos];
    
    // Remove from instance component
    UInstancedStaticMeshComponent* InstanceComp = GetInstanceComponent(BlockData.BlockType);
    if (InstanceComp && BlockData.InstanceIndex >= 0)
    {
        InstanceComp->RemoveInstance(BlockData.InstanceIndex);
        
        // Update other instance indices (UE shifts indices on removal)
        for (auto& Pair : WorldGrid)
        {
            FBlockData& OtherBlock = Pair.Value;
            if (OtherBlock.BlockType == BlockData.BlockType && 
                OtherBlock.InstanceIndex > BlockData.InstanceIndex)
            {
                OtherBlock.InstanceIndex--;
            }
        }
    }
    
    WorldGrid.Remove(GridPos);
    LoadedBlocks.Remove(GridPos);
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

// ========== PUBLIC API ==========

void AProceduralTerrainGenerator::RegenerateAroundPlayer()
{
    ClearAllTerrain();
    UpdateTerrainAroundPlayer();
    
    if (bDebugLogs)
    {
        UE_LOG(LogTemp, Warning, TEXT("🔄 Terrain regenerated"));
    }
}

void AProceduralTerrainGenerator::ClearAllTerrain()
{
    // Clear all instances
    GrassInstances->ClearInstances();
    DirtInstances->ClearInstances();
    StoneInstances->ClearInstances();
    WaterInstances->ClearInstances();
    
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
    
    // Remove existing block
    if (IsBlockLoaded(GridPos))
    {
        RemoveBlock(GridPos);
    }
    
    // Place new block
    if (BlockType != EBlockType::Air)
    {
        PlaceBlock(GridPos, BlockType);
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
            UE_LOG(LogTemp, Warning, TEXT("🔄 Switched to: %s"), 
                   GenerationType == EGenerationType::Simple ? TEXT("Simple") : TEXT("Hybrid"));
        }
        
        RegenerateAroundPlayer();
    }
}