// WFCGenerator.cpp
#include "../Public/WFCGenerator.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Engine/World.h"
#include "Engine/Engine.h"

AWFCGenerator::AWFCGenerator()
{
    PrimaryActorTick.bCanEverTick = true;

    // Create instanced mesh components for each block type
    GrassInst = CreateDefaultSubobject<UInstancedStaticMeshComponent>("GrassInst");
    DirtInst  = CreateDefaultSubobject<UInstancedStaticMeshComponent>("DirtInst");
    StoneInst = CreateDefaultSubobject<UInstancedStaticMeshComponent>("StoneInst");
    WaterInst = CreateDefaultSubobject<UInstancedStaticMeshComponent>("WaterInst");

    RootComponent = GrassInst;
    DirtInst->SetupAttachment(RootComponent);
    StoneInst->SetupAttachment(RootComponent);
    WaterInst->SetupAttachment(RootComponent);
    
    // Optimize for large instance counts
    for (auto* Inst : {GrassInst, DirtInst, StoneInst, WaterInst})
    {
        Inst->SetCastShadow(false);
        Inst->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Inst->SetMobility(EComponentMobility::Static); // Performance optimization
    }
}

void AWFCGenerator::BeginPlay()
{
    Super::BeginPlay();

    // Setup mesh and materials
    for (auto* Inst : {GrassInst, DirtInst, StoneInst, WaterInst})
    {
        if (CubeMesh)
            Inst->SetStaticMesh(CubeMesh);
    }

    if (GrassMat) GrassInst->SetMaterial(0, GrassMat);
    if (DirtMat)  DirtInst->SetMaterial(0, DirtMat);
    if (StoneMat) StoneInst->SetMaterial(0, StoneMat);
    if (WaterMat) WaterInst->SetMaterial(0, WaterMat);

    LastPlayerPos = GetPlayerLocation();
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Warning, TEXT("🚀 SIMPLIFIED Terrain Generator Initialized!"));
        UE_LOG(LogTemp, Warning, TEXT("📊 Chunk Size: %dx%dx%d"), 
               FWFCChunk::ChunkSize, FWFCChunk::ChunkSize, FWFCChunk::ChunkHeight);
    }
    
    UpdateChunks();
}

void AWFCGenerator::Tick(float Delta)
{
    Super::Tick(Delta);

    FVector PlayerPos = GetPlayerLocation();
    float MoveDistance = FVector::DistXY(PlayerPos, LastPlayerPos);
    float ChunkWorldSize = FWFCChunk::ChunkSize * 100.0f;
    
    if (MoveDistance > ChunkWorldSize * 0.8f)
    {
        LastPlayerPos = PlayerPos;
        
        double StartTime = FPlatformTime::Seconds();
        UpdateChunks();
        double GenerationTime = FPlatformTime::Seconds() - StartTime;
        
        if (bDebugGeneration && GenerationTime > 0.01f)
        {
            UE_LOG(LogTemp, Warning, TEXT("⚡ Chunk update took %.2fms"), GenerationTime * 1000.0f);
        }
    }
}

FVector AWFCGenerator::GetPlayerLocation() const
{
    if (APlayerController* PC = GetWorld()->GetFirstPlayerController())
    {
        if (APawn* Pawn = PC->GetPawn())
        {
            return Pawn->GetActorLocation();
        }
    }
    return FVector::ZeroVector;
}

FIntVector AWFCGenerator::WorldPosToIntCoord(const FVector& WorldPos) const
{
    const float BlockSize = 100.0f;
    return FIntVector(
        FMath::FloorToInt(WorldPos.X / BlockSize),
        FMath::FloorToInt(WorldPos.Y / BlockSize),
        FMath::FloorToInt(WorldPos.Z / BlockSize)
    );
}

FVector AWFCGenerator::IntCoordToWorldPos(const FIntVector& IntCoord) const
{
    const float BlockSize = 100.0f;
    return FVector(
        IntCoord.X * BlockSize,
        IntCoord.Y * BlockSize,
        IntCoord.Z * BlockSize
    );
}

FIntVector AWFCGenerator::GetWorldCoord(const FIntVector& ChunkCoords, const FIntVector& LocalCoords) const
{
    return FIntVector(
        ChunkCoords.X * FWFCChunk::ChunkSize + LocalCoords.X,
        ChunkCoords.Y * FWFCChunk::ChunkSize + LocalCoords.Y,
        ChunkCoords.Z * FWFCChunk::ChunkHeight + LocalCoords.Z
    );
}

void AWFCGenerator::UpdateChunks()
{
    FVector PlayerPos = GetPlayerLocation();
    float ChunkWorldSize = FWFCChunk::ChunkSize * 100.0f;
    
    int32 ChunkX, ChunkY;
    
    // 🔧 FIX: Ensure first chunk is always at (0,0,0)
    if (!bFirstChunkGenerated)
    {
        ChunkX = 0;
        ChunkY = 0;
        bFirstChunkGenerated = true;
        
        FIntVector OriginCoords(0, 0, 0);
        if (!Chunks.Contains(OriginCoords))
        {
            GenerateChunk(OriginCoords);
            
            if (bDebugGeneration)
            {
                UE_LOG(LogTemp, Warning, TEXT("🎯 Generated origin chunk at (0,0,0)"));
            }
        }
    }
    else
    {
        ChunkX = FMath::FloorToInt(PlayerPos.X / ChunkWorldSize);
        ChunkY = FMath::FloorToInt(PlayerPos.Y / ChunkWorldSize);
    }

    // 🧹 MEMORY MANAGEMENT: Clean up distant chunks
    TArray<FIntVector> ChunksToRemove;
    for (auto& ChunkPair : Chunks)
    {
        const FIntVector& ChunkCoord = ChunkPair.Key;
        int32 DistanceX = FMath::Abs(ChunkCoord.X - ChunkX);
        int32 DistanceY = FMath::Abs(ChunkCoord.Y - ChunkY);
        
        if (DistanceX > RenderDistance + 1 || DistanceY > RenderDistance + 1)
        {
            ChunksToRemove.Add(ChunkCoord);
        }
    }
    
    // 🗑️ CLEANUP: Remove distant chunks
    for (const FIntVector& ChunkToRemove : ChunksToRemove)
    {
        if (bDebugGeneration)
        {
            UE_LOG(LogTemp, Log, TEXT("🗑️ Unloading chunk (%d,%d,%d)"), 
                   ChunkToRemove.X, ChunkToRemove.Y, ChunkToRemove.Z);
        }
        
        if (Chunks.Contains(ChunkToRemove))
        {
            FWFCChunk& ChunkToCleanup = Chunks[ChunkToRemove];
            RemoveChunkInstances(ChunkToCleanup);
        }
        
        Chunks.Remove(ChunkToRemove);
    }

    int32 NewChunksGenerated = 0;
    
    // 🌍 Generate chunks in controlled pattern
    for (int32 dx = -RenderDistance; dx <= RenderDistance; dx++)
    {
        for (int32 dy = -RenderDistance; dy <= RenderDistance; dy++)
        {
            FIntVector ChunkCoords(ChunkX + dx, ChunkY + dy, 0);
            
            if (!Chunks.Contains(ChunkCoords))
            {
                GenerateChunk(ChunkCoords);
                NewChunksGenerated++;
                
                if (NewChunksGenerated >= 2)
                {
                    if (bDebugGeneration)
                    {
                        UE_LOG(LogTemp, Warning, TEXT("🚦 Throttling generation: %d chunks this frame"), NewChunksGenerated);
                    }
                    return;
                }
            }
        }
    }
    
    if (bDebugGeneration && NewChunksGenerated > 0)
    {
        UE_LOG(LogTemp, Warning, TEXT("🌍 Generated %d new chunks | Total chunks: %d"), 
               NewChunksGenerated, Chunks.Num());
    }
}

void AWFCGenerator::GenerateChunk(const FIntVector& Coords)
{
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Warning, TEXT("🔧 Generating chunk at (%d, %d, %d)"), Coords.X, Coords.Y, Coords.Z);
    }

    FWFCChunk& Chunk = Chunks.Add(Coords);
    Chunk.Initialize(Coords);

    // 🔧 SIMPLIFIED: Always use deterministic generation
    GenerateSimpleTerrain(Chunk, Coords);
    
    DrawChunk(Chunk);
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("✅ Chunk (%d,%d,%d) generation complete"), Coords.X, Coords.Y, Coords.Z);
    }
}

void AWFCGenerator::GenerateSimpleTerrain(FWFCChunk& Chunk, const FIntVector& Coords)
{
    const float NoiseScale = 0.03f;
    const int32 BaseHeight = 8;
    const int32 HeightVariation = 4;
    const int32 DirtDepth = 3;
    
    // 🔧 CRITICAL: Track what we're placing to prevent duplicates
    TSet<FIntVector> PlacedPositions;
    int32 BlockCounts[5] = {0}; // Air, Grass, Dirt, Stone, Water
    
    for (int32 x = 0; x < FWFCChunk::ChunkSize; x++)
    {
        for (int32 y = 0; y < FWFCChunk::ChunkSize; y++)
        {
            // Calculate world coordinates for noise
            FIntVector WorldCoord = GetWorldCoord(Coords, FIntVector(x, y, 0));
            int32 WorldX = WorldCoord.X;
            int32 WorldY = WorldCoord.Y;
            
            // Generate height using multi-octave noise
            float Noise1 = FMath::PerlinNoise2D(FVector2D(WorldX, WorldY) * NoiseScale);
            float Noise2 = FMath::PerlinNoise2D(FVector2D(WorldX, WorldY) * (NoiseScale * 2.0f)) * 0.3f;
            float CombinedNoise = (Noise1 + Noise2) / 1.3f;
            
            int32 SurfaceHeight = BaseHeight + FMath::RoundToInt(CombinedNoise * HeightVariation);
            SurfaceHeight = FMath::Clamp(SurfaceHeight, 3, FWFCChunk::ChunkHeight - 3);
            
            // Generate vertical column
            for (int32 z = 0; z < FWFCChunk::ChunkHeight; z++)
            {
                FIntVector LocalCoord(x, y, z);
                FIntVector WorldBlockCoord = GetWorldCoord(Coords, LocalCoord);
                
                // 🔧 CRITICAL: Check for existing placement
                if (PlacedPositions.Contains(LocalCoord))
                {
                    if (bDebugGeneration)
                    {
                        UE_LOG(LogTemp, Error, TEXT("🚨 DUPLICATE LOCAL POSITION: (%d,%d,%d) in chunk (%d,%d,%d)"), 
                               x, y, z, Coords.X, Coords.Y, Coords.Z);
                    }
                    continue;
                }
                
                if (GlobalBlockMap.Contains(WorldBlockCoord))
                {
                    if (bDebugGeneration)
                    {
                        UE_LOG(LogTemp, Error, TEXT("🚨 DUPLICATE WORLD POSITION: (%d,%d,%d)"), 
                               WorldBlockCoord.X, WorldBlockCoord.Y, WorldBlockCoord.Z);
                    }
                    continue;
                }
                
                EBlockType BlockType = EBlockType::Air;
                
                // 🌊 WATER GENERATION
                if (z <= SeaLevel && SurfaceHeight <= SeaLevel)
                {
                    if (z > SurfaceHeight)
                        BlockType = EBlockType::Water;
                    else if (z == SurfaceHeight)
                        BlockType = EBlockType::Dirt;
                    else if (z > SurfaceHeight - DirtDepth)
                        BlockType = EBlockType::Dirt;
                    else if (z > 2)
                        BlockType = EBlockType::Dirt;
                    else
                        BlockType = EBlockType::Stone;
                }
                // 🌍 LAND GENERATION
                else if (z > SurfaceHeight)
                {
                    BlockType = EBlockType::Air;
                }
                else if (z == SurfaceHeight)
                {
                    BlockType = EBlockType::Grass;
                }
                else if (z > SurfaceHeight - DirtDepth)
                {
                    BlockType = EBlockType::Dirt;
                }
                else if (z > 2)
                {
                    BlockType = EBlockType::Dirt;
                }
                else
                {
                    BlockType = EBlockType::Stone;
                }
                
                // 🔧 CRITICAL: Only place if not Air and not duplicate
                if (BlockType != EBlockType::Air)
                {
                    Chunk.SetBlockType(LocalCoord, BlockType);
                    PlacedPositions.Add(LocalCoord);
                    GlobalBlockMap.Add(WorldBlockCoord, BlockType);
                    BlockCounts[(int32)BlockType]++;
                }
            }
        }
    }
    
    Chunk.bGenerated = true;
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("🔄 Simple terrain generated for chunk (%d,%d,%d): %d Grass, %d Dirt, %d Stone, %d Water"), 
               Coords.X, Coords.Y, Coords.Z, BlockCounts[1], BlockCounts[2], BlockCounts[3], BlockCounts[4]);
    }
}

void AWFCGenerator::DrawChunk(FWFCChunk& Chunk)
{
    if (Chunk.bDrawn)
    {
        if (bDebugGeneration)
            UE_LOG(LogTemp, Warning, TEXT("⚠️ Chunk (%d,%d,%d) already rendered, skipping"), 
                   Chunk.ChunkCoords.X, Chunk.ChunkCoords.Y, Chunk.ChunkCoords.Z);
        return;
    }
    
    if (!Chunk.bGenerated)
    {
        UE_LOG(LogTemp, Error, TEXT("❌ Trying to draw ungenerated chunk (%d,%d,%d)"), 
               Chunk.ChunkCoords.X, Chunk.ChunkCoords.Y, Chunk.ChunkCoords.Z);
        return;
    }
    
    TArray<FTransform> GrassTransforms, DirtTransforms, StoneTransforms, WaterTransforms;
    int32 BlockCounts[5] = {0}; // Air, Grass, Dirt, Stone, Water
    
    // Pre-allocate arrays
    GrassTransforms.Reserve(FWFCChunk::NumCells / 10);
    DirtTransforms.Reserve(FWFCChunk::NumCells / 8);
    StoneTransforms.Reserve(FWFCChunk::NumCells / 4);
    WaterTransforms.Reserve(FWFCChunk::NumCells / 20);
    
    // Track render positions to detect duplicates during rendering
    TSet<FVector> RenderPositions;
    int32 OverlapCount = 0;
    
    for (int32 x = 0; x < FWFCChunk::ChunkSize; x++)
    {
        for (int32 y = 0; y < FWFCChunk::ChunkSize; y++)
        {
            for (int32 z = 0; z < FWFCChunk::ChunkHeight; z++)
            {
                FIntVector LocalCoord(x, y, z);
                EBlockType BlockType = Chunk.GetBlockType(LocalCoord);
                
                if (BlockType == EBlockType::Air) continue;
                
                BlockCounts[(int32)BlockType]++;
                
                // Calculate precise world position
                FIntVector WorldCoord = GetWorldCoord(Chunk.ChunkCoords, LocalCoord);
                FVector WorldPos = IntCoordToWorldPos(WorldCoord);
                
                // 🔧 CRITICAL: Final overlap check during rendering
                if (RenderPositions.Contains(WorldPos))
                {
                    OverlapCount++;
                    if (bDebugGeneration)
                    {
                        UE_LOG(LogTemp, Error, TEXT("🚨 RENDER OVERLAP #%d at world (%f,%f,%f) from chunk (%d,%d,%d) local (%d,%d,%d)"), 
                               OverlapCount, WorldPos.X, WorldPos.Y, WorldPos.Z,
                               Chunk.ChunkCoords.X, Chunk.ChunkCoords.Y, Chunk.ChunkCoords.Z, x, y, z);
                    }
                    continue;
                }
                RenderPositions.Add(WorldPos);
                
                FTransform BlockTransform(WorldPos);
                
                switch (BlockType)
                {
                case EBlockType::Grass:
                    GrassTransforms.Add(BlockTransform);
                    break;
                case EBlockType::Dirt:
                    DirtTransforms.Add(BlockTransform);
                    break;
                case EBlockType::Stone:
                    StoneTransforms.Add(BlockTransform);
                    break;
                case EBlockType::Water:
                    WaterTransforms.Add(BlockTransform);
                    break;
                default:
                    break;
                }
            }
        }
    }
    
    // 🚀 BATCH INSTANCING: Add all instances
    if (GrassInst && GrassTransforms.Num() > 0) 
    {
        GrassInst->AddInstances(GrassTransforms, false);
        GrassInst->MarkRenderStateDirty();
    }
    if (DirtInst && DirtTransforms.Num() > 0) 
    {
        DirtInst->AddInstances(DirtTransforms, false);
        DirtInst->MarkRenderStateDirty();
    }
    if (StoneInst && StoneTransforms.Num() > 0) 
    {
        StoneInst->AddInstances(StoneTransforms, false);
        StoneInst->MarkRenderStateDirty();
    }
    if (WaterInst && WaterTransforms.Num() > 0) 
    {
        WaterInst->AddInstances(WaterTransforms, false);
        WaterInst->MarkRenderStateDirty();
    }
    
    Chunk.bDrawn = true;
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("🎨 Chunk (%d,%d,%d) rendered: %d Grass, %d Dirt, %d Stone, %d Water | Render Overlaps: %d"), 
               Chunk.ChunkCoords.X, Chunk.ChunkCoords.Y, Chunk.ChunkCoords.Z,
               BlockCounts[1], BlockCounts[2], BlockCounts[3], BlockCounts[4], OverlapCount);
    }
}

void AWFCGenerator::RemoveChunkInstances(FWFCChunk& Chunk)
{
    if (!Chunk.bDrawn) return;
    
    // 🔧 FIX: Remove from global tracking
    for (int32 x = 0; x < FWFCChunk::ChunkSize; x++)
    {
        for (int32 y = 0; y < FWFCChunk::ChunkSize; y++)
        {
            for (int32 z = 0; z < FWFCChunk::ChunkHeight; z++)
            {
                FIntVector LocalCoord(x, y, z);
                FIntVector WorldCoord = GetWorldCoord(Chunk.ChunkCoords, LocalCoord);
                GlobalBlockMap.Remove(WorldCoord);
            }
        }
    }
    
    Chunk.bDrawn = false;
    Chunk.bGenerated = false;
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("🧹 Cleaned up chunk (%d,%d,%d) from global tracking"), 
               Chunk.ChunkCoords.X, Chunk.ChunkCoords.Y, Chunk.ChunkCoords.Z);
    }
}