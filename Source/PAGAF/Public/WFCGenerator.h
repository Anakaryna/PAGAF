// WFCGenerator.h
#pragma once

#include "CoreMinimal.h"
#include "Containers/BitArray.h"
#include "GameFramework/Actor.h"
#include "WFCGenerator.generated.h"

UENUM(BlueprintType)
enum class EBlockType : uint8
{
    Air    UMETA(DisplayName = "Air"),
    Grass  UMETA(DisplayName = "Grass"),
    Dirt   UMETA(DisplayName = "Dirt"),
    Stone  UMETA(DisplayName = "Stone"),
    Water  UMETA(DisplayName = "Water")
};

USTRUCT()
struct FWFCChunk
{
    GENERATED_BODY()

    static const int32 ChunkSize = 8;      
    static const int32 ChunkHeight = 16;   
    static const int32 NumCells  = ChunkSize * ChunkSize * ChunkHeight;
    static const int32 NumTypes  = 5;

    FIntVector ChunkCoords;
    
    // 🔧 SIMPLIFIED: Direct block type storage instead of wave function
    TArray<EBlockType> BlockTypes;
    bool bGenerated = false;
    bool bDrawn = false;

    void Initialize(const FIntVector& InCoords)
    {
        ChunkCoords = InCoords;
        bGenerated = false;
        bDrawn = false;
        
        // Initialize all cells as Air
        BlockTypes.SetNum(NumCells);
        for (int32 i = 0; i < NumCells; i++)
        {
            BlockTypes[i] = EBlockType::Air;
        }
    }

    int32 CoordToIndex(const FIntVector& c) const
    {
        // Ensure coordinates are within bounds
        if (c.X < 0 || c.X >= ChunkSize || c.Y < 0 || c.Y >= ChunkSize || c.Z < 0 || c.Z >= ChunkHeight)
            return -1;
        return c.Z * ChunkSize * ChunkSize + c.Y * ChunkSize + c.X;
    }
    
    FIntVector IndexToCoord(int32 idx) const
    {
        if (idx < 0 || idx >= NumCells) return FIntVector(-1, -1, -1);
        int32 z = idx / (ChunkSize * ChunkSize);
        int32 r = idx % (ChunkSize * ChunkSize);
        int32 y = r / ChunkSize;
        int32 x = r % ChunkSize;
        return {x, y, z};
    }
    
    bool IsValidCoord(const FIntVector& c) const
    {
        return c.X >= 0 && c.X < ChunkSize
            && c.Y >= 0 && c.Y < ChunkSize
            && c.Z >= 0 && c.Z < ChunkHeight;
    }
    
    EBlockType GetBlockType(const FIntVector& coord) const
    {
        int32 idx = CoordToIndex(coord);
        if (idx < 0 || idx >= BlockTypes.Num()) return EBlockType::Air;
        return BlockTypes[idx];
    }
    
    void SetBlockType(const FIntVector& coord, EBlockType type)
    {
        int32 idx = CoordToIndex(coord);
        if (idx >= 0 && idx < BlockTypes.Num())
        {
            BlockTypes[idx] = type;
        }
    }
};

UCLASS()
class PAGAF_API AWFCGenerator : public AActor
{
    GENERATED_BODY()

public:
    AWFCGenerator();

protected:
    virtual void BeginPlay() override;
    virtual void Tick(float Delta) override;

    UPROPERTY(EditAnywhere, Category="WFC|Generation")
    int32 RenderDistance = 1;

    UPROPERTY(EditAnywhere, Category="WFC|Terrain")
    int32 SeaLevel = 6;

    UPROPERTY(EditAnywhere, Category="WFC|Debug")
    bool bDebugGeneration = true;
    
    UPROPERTY(EditAnywhere, Category="WFC|Debug")
    bool bUseSimpleGeneration = true; // 🔧 NEW: Force simple generation

    UPROPERTY(EditAnywhere, Category="WFC|Meshes")
    UStaticMesh* CubeMesh;
    
    UPROPERTY(EditAnywhere, Category="WFC|Materials")
    UMaterialInterface* GrassMat;
    UPROPERTY(EditAnywhere, Category="WFC|Materials")
    UMaterialInterface* DirtMat;
    UPROPERTY(EditAnywhere, Category="WFC|Materials")
    UMaterialInterface* StoneMat;
    UPROPERTY(EditAnywhere, Category="WFC|Materials")
    UMaterialInterface* WaterMat;

    UPROPERTY()
    UInstancedStaticMeshComponent* GrassInst;
    UPROPERTY()
    UInstancedStaticMeshComponent* DirtInst;
    UPROPERTY()
    UInstancedStaticMeshComponent* StoneInst;
    UPROPERTY()
    UInstancedStaticMeshComponent* WaterInst;

private:
    TMap<FIntVector, FWFCChunk> Chunks;
    FVector LastPlayerPos;
    bool bFirstChunkGenerated = false;
    
    // 🔧 CRITICAL: Global position tracking with better precision
    TMap<FIntVector, EBlockType> GlobalBlockMap; // Maps world coordinates to block types
    
    void UpdateChunks();
    void GenerateChunk(const FIntVector& Coords);
    void GenerateSimpleTerrain(FWFCChunk& Chunk, const FIntVector& Coords);
    void DrawChunk(FWFCChunk& Chunk);
    void RemoveChunkInstances(FWFCChunk& Chunk);
    FVector GetPlayerLocation() const;
    
    // 🔧 NEW: Convert world coordinates to precise integer coordinates
    FIntVector WorldPosToIntCoord(const FVector& WorldPos) const;
    FVector IntCoordToWorldPos(const FIntVector& IntCoord) const;
    
    // 🔧 NEW: Get world coordinate for a local chunk position
    FIntVector GetWorldCoord(const FIntVector& ChunkCoords, const FIntVector& LocalCoords) const;
};