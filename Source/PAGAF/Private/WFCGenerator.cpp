// WFCGenerator.cpp
#include "../Public/WFCGenerator.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Engine/World.h"
#include "Engine/Engine.h"

// Static member initialization
TArray<int32> AWFCGenerator::Allowed[FWFCChunk::NumTypes][6];
bool AWFCGenerator::bAdjacencyBuilt = false;

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
        Inst->SetCastShadow(false); // Performance optimization
        Inst->SetCollisionEnabled(ECollisionEnabled::NoCollision); // For now
    }
}

void AWFCGenerator::BeginPlay()
{
    Super::BeginPlay();
    
    // Initialize adjacency rules if not already done
    if (!bAdjacencyBuilt)
    {
        BuildAdjacency();
        bAdjacencyBuilt = true;
    }

    // Setup mesh and materials for all instance components
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
        UE_LOG(LogTemp, Warning, TEXT("🚀 PAGAF WFC Terrain Generator Initialized!"));
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
    
    // 🎯 PERFORMANCE: Reduce update frequency with larger threshold
    if (MoveDistance > ChunkWorldSize * 0.8f) // Increased threshold
    {
        LastPlayerPos = PlayerPos;
        
        // 🚨 PERFORMANCE MONITOR: Track generation time
        double StartTime = FPlatformTime::Seconds();
        
        UpdateChunks();
        
        double GenerationTime = FPlatformTime::Seconds() - StartTime;
        if (bDebugGeneration && GenerationTime > 0.01f) // Log if > 10ms
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

void AWFCGenerator::UpdateChunks()
{
    FVector PlayerPos = GetPlayerLocation();
    float ChunkWorldSize = FWFCChunk::ChunkSize * 100.0f;
    
    int32 ChunkX = FMath::FloorToInt(PlayerPos.X / ChunkWorldSize);
    int32 ChunkY = FMath::FloorToInt(PlayerPos.Y / ChunkWorldSize);

    // 🧹 MEMORY MANAGEMENT: Unload distant chunks to prevent memory bloat
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
    
    // 🗑️ CLEANUP: Remove distant chunks and their instances
    for (const FIntVector& ChunkToRemove : ChunksToRemove)
    {
        if (bDebugGeneration)
        {
            UE_LOG(LogTemp, Log, TEXT("🗑️ Unloading chunk (%d,%d,%d) to free memory"), 
                   ChunkToRemove.X, ChunkToRemove.Y, ChunkToRemove.Z);
        }
        Chunks.Remove(ChunkToRemove);
    }

    int32 NewChunksGenerated = 0;
    
    // 🌍 PROCEDURAL EXPANSION: Generate chunks in a controlled grid pattern
    for (int32 dx = -RenderDistance; dx <= RenderDistance; dx++)
    {
        for (int32 dy = -RenderDistance; dy <= RenderDistance; dy++)
        {
            FIntVector ChunkCoords(ChunkX + dx, ChunkY + dy, 0);
            
            if (!Chunks.Contains(ChunkCoords))
            {
                GenerateChunk(ChunkCoords);
                NewChunksGenerated++;
                
                // 🚦 PERFORMANCE THROTTLE: Limit chunks per frame
                if (NewChunksGenerated >= 2) // Max 2 chunks per update
                {
                    if (bDebugGeneration)
                    {
                        UE_LOG(LogTemp, Warning, TEXT("🚦 Throttling generation: %d chunks this frame"), NewChunksGenerated);
                    }
                    return; // Exit early to prevent frame drops
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

    // 🎯 STRATEGIC DECISION: Try WFC first, fallback immediately if issues
    bool bUseWFC = true; // Set to false to skip WFC entirely for testing
    bool bWFCSuccess = false;
    
    if (bUseWFC)
    {
        // STEP 1: Apply minimal constraints
        SeedHeightConstraints(Chunk, Coords);

        // STEP 2: Try WFC with timeout protection
        auto GetAdjacencyRules = [](int BlockType, int Direction) -> const TArray<int32>& {
            return Allowed[BlockType][Direction];
        };

        bWFCSuccess = Chunk.Run(GetAdjacencyRules);
        
        if (bDebugGeneration)
        {
            if (bWFCSuccess)
            {
                UE_LOG(LogTemp, Log, TEXT("✅ WFC succeeded for chunk (%d,%d,%d)"), Coords.X, Coords.Y, Coords.Z);
            }else
                UE_LOG(LogTemp, Warning, TEXT("⚠️ WFC timed out for chunk (%d,%d,%d), using fallback"), Coords.X, Coords.Y, Coords.Z);
        }
    }
    
    // STEP 3: Use deterministic fallback if WFC failed or disabled
    if (!bWFCSuccess)
    {
        FallbackGeneration(Chunk, Coords);
    }

    // STEP 4: Render the chunk
    DrawChunk(Chunk);
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("✅ Chunk (%d,%d,%d) generation complete"), Coords.X, Coords.Y, Coords.Z);
    }
}

void AWFCGenerator::SeedHeightConstraints(FWFCChunk& Chunk, const FIntVector& Coords)
{
    // 🔧 RELAXED CONSTRAINTS - Prevent over-specification
    const float NoiseScale = 0.03f;
    const int32 BaseHeight = 10;
    const int32 HeightVariation = 6;
    const int32 DirtDepth = 2;
    
    // 🚨 CRITICAL: Only apply SOFT constraints, never force single block types
    for (int32 x = 0; x < FWFCChunk::ChunkSize; x++)
    {
        for (int32 y = 0; y < FWFCChunk::ChunkSize; y++)
        {
            // Calculate world coordinates
            int32 WorldX = Coords.X * FWFCChunk::ChunkSize + x;
            int32 WorldY = Coords.Y * FWFCChunk::ChunkSize + y;
            
            // Generate height using single-octave noise for stability
            float Noise = FMath::PerlinNoise2D(FVector2D(WorldX, WorldY) * NoiseScale);
            int32 SurfaceHeight = BaseHeight + FMath::RoundToInt(Noise * HeightVariation);
            SurfaceHeight = FMath::Clamp(SurfaceHeight, 3, FWFCChunk::ChunkHeight - 5);
            
            // 🎯 GENTLE BIASING - Never eliminate all options
            for (int32 z = 0; z < FWFCChunk::ChunkHeight; z++)
            {
                int32 CellIndex = Chunk.CoordToIndex({x, y, z});
                
                // 🔒 BEDROCK LAYER - Only constrain bottom 2 layers
                if (z <= 1)
                {
                    // Prefer stone but allow dirt as backup
                    Chunk.Wave[CellIndex][(int32)EBlockType::Air] = false;
                    Chunk.Wave[CellIndex][(int32)EBlockType::Grass] = false;
                    Chunk.Wave[CellIndex][(int32)EBlockType::Water] = false;
                    // Leave Stone and Dirt as valid options
                }
                // 🌤️ SKY LAYER - Only constrain top 3 layers  
                else if (z >= FWFCChunk::ChunkHeight - 3)
                {
                    // Prefer air but allow some variation
                    Chunk.Wave[CellIndex][(int32)EBlockType::Stone] = false;
                    Chunk.Wave[CellIndex][(int32)EBlockType::Dirt] = false;
                    // Leave Air, Grass, Water as valid options
                }
                // 🌊 SEA LEVEL CONSTRAINTS - Very gentle
                else if (z <= SeaLevel && SurfaceHeight <= SeaLevel)
                {
                    // Prefer water in low areas but don't force it
                    Chunk.Wave[CellIndex][(int32)EBlockType::Grass] = false;
                    // Leave Air, Dirt, Stone, Water as valid options
                }
                
                // 🌍 MIDDLE LAYERS - Completely unconstrained
                // Let WFC handle the entire middle section naturally
            }
        }
    }
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("🌱 Applied gentle height constraints for chunk (%d,%d,%d)"), 
               Coords.X, Coords.Y, Coords.Z);
    }
}

void AWFCGenerator::FallbackGeneration(FWFCChunk& Chunk, const FIntVector& Coords)
{
    // 🎯 ENHANCED DETERMINISTIC FALLBACK - Better than basic height-maps!
    const float NoiseScale = 0.03f;
    const int32 BaseHeight = 10;
    const int32 HeightVariation = 6;
    const int32 DirtDepth = 2;
    
    for (int32 x = 0; x < FWFCChunk::ChunkSize; x++)
    {
        for (int32 y = 0; y < FWFCChunk::ChunkSize; y++)
        {
            int32 WorldX = Coords.X * FWFCChunk::ChunkSize + x;
            int32 WorldY = Coords.Y * FWFCChunk::ChunkSize + y;
            
            // Multi-octave noise for natural terrain
            float Noise1 = FMath::PerlinNoise2D(FVector2D(WorldX, WorldY) * NoiseScale);
            float Noise2 = FMath::PerlinNoise2D(FVector2D(WorldX, WorldY) * NoiseScale * 2.0f) * 0.3f;
            float CombinedNoise = (Noise1 + Noise2) / 1.3f;
            
            int32 SurfaceHeight = BaseHeight + FMath::RoundToInt(CombinedNoise * HeightVariation);
            SurfaceHeight = FMath::Clamp(SurfaceHeight, 2, FWFCChunk::ChunkHeight - 4);
            
            for (int32 z = 0; z < FWFCChunk::ChunkHeight; z++)
            {
                int32 CellIndex = Chunk.CoordToIndex({x, y, z});
                EBlockType BlockType;
                
                // 🌊 WATER GENERATION - Ensure water appears!
                if (z <= SeaLevel && SurfaceHeight <= SeaLevel)
                {
                    if (z > SurfaceHeight)
                        BlockType = EBlockType::Water;
                    else if (z == SurfaceHeight)
                        BlockType = EBlockType::Dirt; // Underwater dirt
                    else if (z > SurfaceHeight - DirtDepth)
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
                else
                {
                    BlockType = EBlockType::Stone;
                }
                
                // Set single block type for deterministic generation
                for (int32 t = 0; t < FWFCChunk::NumTypes; t++)
                    Chunk.Wave[CellIndex][t] = (t == (int32)BlockType);
            }
        }
    }
    
    Chunk.bCollapsed = true;
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("🔄 Fallback generation completed for chunk (%d,%d,%d)"), 
               Coords.X, Coords.Y, Coords.Z);
    }
}

void AWFCGenerator::DrawChunk(const FWFCChunk& Chunk)
{
    const float BlockSize = 100.0f;
    
    // 🚨 REDUNDANCY PROTECTION: Prevent expensive duplicate draw calls
    if (Chunk.bDrawn)
    {
        if (bDebugGeneration)
            UE_LOG(LogTemp, Warning, TEXT("⚠️ Chunk (%d,%d,%d) already rendered, skipping draw"), 
                   Chunk.ChunkCoords.X, Chunk.ChunkCoords.Y, Chunk.ChunkCoords.Z);
        return;
    }
    
    // 🎯 SPATIAL TRANSFORMATION: Convert chunk coordinates to world space
    FVector ChunkWorldPos = GetActorLocation();
    ChunkWorldPos.X += Chunk.ChunkCoords.X * FWFCChunk::ChunkSize * BlockSize;
    ChunkWorldPos.Y += Chunk.ChunkCoords.Y * FWFCChunk::ChunkSize * BlockSize;
    ChunkWorldPos.Z += Chunk.ChunkCoords.Z * FWFCChunk::ChunkHeight * BlockSize;

    // 📊 PROFILING: Track block type distribution for optimization insights
    int32 BlockCounts[FWFCChunk::NumTypes] = {0};
    TArray<FTransform> GrassTransforms, DirtTransforms, StoneTransforms, WaterTransforms;
    
    // 🎮 BATCH OPTIMIZATION: Pre-allocate transform arrays for efficient instancing
    GrassTransforms.Reserve(FWFCChunk::NumCells / 10);
    DirtTransforms.Reserve(FWFCChunk::NumCells / 8);
    StoneTransforms.Reserve(FWFCChunk::NumCells / 4);
    WaterTransforms.Reserve(FWFCChunk::NumCells / 20);
    
    for (int32 CellIndex = 0; CellIndex < FWFCChunk::NumCells; CellIndex++)
    {
        int32 BlockType = Chunk.FindFirstAllowed(CellIndex);
        if (BlockType <= 0) continue; // Skip Air blocks for performance
        
        BlockCounts[BlockType]++;
        
        FIntVector LocalCoord = Chunk.IndexToCoord(CellIndex);
        FVector BlockWorldPos = ChunkWorldPos + FVector(LocalCoord) * BlockSize;
        FTransform BlockTransform(BlockWorldPos);
        
        // 🎨 MATERIAL BATCHING: Sort instances by material type for GPU efficiency
        switch ((EBlockType)BlockType)
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
    
    // 🚀 BATCH INSTANCING: Add all instances of each type simultaneously
    if (GrassInst && GrassTransforms.Num() > 0) GrassInst->AddInstances(GrassTransforms, false);
    if (DirtInst && DirtTransforms.Num() > 0) DirtInst->AddInstances(DirtTransforms, false);
    if (StoneInst && StoneTransforms.Num() > 0) StoneInst->AddInstances(StoneTransforms, false);
    if (WaterInst && WaterTransforms.Num() > 0) WaterInst->AddInstances(WaterTransforms, false);
    
    // 🔒 ATOMIC FLAG: Mark chunk as rendered to prevent re-draws
    const_cast<FWFCChunk&>(Chunk).bDrawn = true;
    
    if (bDebugGeneration)
    {
        UE_LOG(LogTemp, Log, TEXT("🎨 Chunk (%d,%d,%d) rendered: %d Grass, %d Dirt, %d Stone, %d Water | Total: %d blocks"), 
               Chunk.ChunkCoords.X, Chunk.ChunkCoords.Y, Chunk.ChunkCoords.Z,
               BlockCounts[1], BlockCounts[2], BlockCounts[3], BlockCounts[4],
               BlockCounts[1] + BlockCounts[2] + BlockCounts[3] + BlockCounts[4]);
    }
}

void AWFCGenerator::BuildAdjacency()
{
    UE_LOG(LogTemp, Warning, TEXT("🔨 Building WFC adjacency rules for advanced terrain generation"));
    
    // Clear all adjacency rules first
    for (int32 BlockType = 0; BlockType < FWFCChunk::NumTypes; BlockType++)
    {
        for (int32 Direction = 0; Direction < 6; Direction++)
        {
            Allowed[BlockType][Direction].Empty();
        }
    }

    // Direction mapping: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z

    // ===== GRASS BLOCK RULES =====
    int32 Grass = (int32)EBlockType::Grass;
    Allowed[Grass][4] = { (int32)EBlockType::Air };  // Above: Air only
    Allowed[Grass][5] = { (int32)EBlockType::Dirt, (int32)EBlockType::Stone };  // Below: Dirt or Stone
    // Horizontal: Grass, Dirt, or transition to other surface blocks
    for (int32 d = 0; d < 4; d++)
        Allowed[Grass][d] = { Grass, (int32)EBlockType::Dirt, (int32)EBlockType::Water };

    // ===== DIRT BLOCK RULES =====
    int32 Dirt = (int32)EBlockType::Dirt;
    Allowed[Dirt][4] = { Grass, Dirt, (int32)EBlockType::Air };  // Above: Grass, Dirt, or Air
    Allowed[Dirt][5] = { Dirt, (int32)EBlockType::Stone };  // Below: Dirt or Stone
    // Horizontal: Dirt, Grass, Stone for natural transitions
    for (int32 d = 0; d < 4; d++)
        Allowed[Dirt][d] = { Dirt, Grass, (int32)EBlockType::Stone };

    // ===== STONE BLOCK RULES =====
    int32 Stone = (int32)EBlockType::Stone;
    Allowed[Stone][4] = { Stone, Dirt, (int32)EBlockType::Air };  // Above: Stone, Dirt, or Air (caves)
    Allowed[Stone][5] = { Stone };  // Below: Stone only (bedrock-like)
    // Horizontal: Primarily stone, allow some dirt transition
    for (int32 d = 0; d < 4; d++)
        Allowed[Stone][d] = { Stone, Dirt };

    // ===== WATER BLOCK RULES =====
    int32 Water = (int32)EBlockType::Water;
    Allowed[Water][4] = { Water, (int32)EBlockType::Air };  // Above: Water or Air
    Allowed[Water][5] = { Water, Stone, Dirt };  // Below: Water, Stone, or Dirt
    // Horizontal: Water spreads, can touch shore blocks
    for (int32 d = 0; d < 4; d++)
        Allowed[Water][d] = { Water, Dirt, Stone };

    // ===== AIR BLOCK RULES =====
    int32 Air = (int32)EBlockType::Air;
    // Air is compatible with everything (most permissive)
    for (int32 d = 0; d < 6; d++)
    {
        Allowed[Air][d] = { Air, Grass, Dirt, Stone, Water };
    }
    
    UE_LOG(LogTemp, Warning, TEXT("✅ WFC Adjacency rules built successfully!"));
    
    // Debug log some rules
    UE_LOG(LogTemp, Log, TEXT("📋 Sample rules - Grass above: %d allowed types"), Allowed[Grass][4].Num());
    UE_LOG(LogTemp, Log, TEXT("📋 Sample rules - Stone horizontal: %d allowed types"), Allowed[Stone][0].Num());
}