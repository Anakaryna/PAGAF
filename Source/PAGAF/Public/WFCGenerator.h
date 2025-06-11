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

    static const int32 ChunkSize = 8;      // REDUCED for performance testing
    static const int32 ChunkHeight = 16;   // REDUCED for performance testing
    static const int32 NumCells  = ChunkSize * ChunkSize * ChunkHeight;
    static const int32 NumTypes  = 5; // Air,Grass,Dirt,Stone,Water

    FIntVector       ChunkCoords;
    TArray<TBitArray<>> Wave;
    TArray<int32>    PropQueue;
    bool             bCollapsed = false;
    bool             bDrawn = false;
    int32            AttemptCount = 0;
    static const int32 MaxAttempts = 3;

    void Initialize(const FIntVector& InCoords)
    {
        ChunkCoords = InCoords;
        bCollapsed  = false;
        bDrawn      = false;
        AttemptCount = 0;
        Wave.SetNum(NumCells);
        for (int32 i = 0; i < NumCells; i++)
            Wave[i].Init(true, NumTypes);
        PropQueue.Empty();
    }

    template<typename FAdjFunc>
    bool Run(const FAdjFunc& GetAllowedNeighbors)
    {
        UE_LOG(LogTemp, Warning, TEXT("Starting WFC for chunk (%d,%d,%d)"), 
               ChunkCoords.X, ChunkCoords.Y, ChunkCoords.Z);
        
        int32 IterationCount = 0;
        const int32 MaxIterations = NumCells * 2; // Safety limit
               
        while (!bCollapsed && AttemptCount < MaxAttempts && IterationCount < MaxIterations)
        {
            IterationCount++; // Prevent infinite loops
            
            // 1) OBSERVE - Find cell with minimum entropy
            int32 BestCell = -1, BestCount = INT_MAX;
            for (int32 i = 0; i < NumCells; i++)
            {
                int32 Count = Wave[i].CountSetBits();
                if (Count > 1 && Count < BestCount)
                {
                    BestCount = Count;
                    BestCell  = i;
                }
                else if (Count == 0)
                {
                    // CONTRADICTION DETECTED - Restart with different seed
                    UE_LOG(LogTemp, Warning, TEXT("WFC Contradiction at cell %d, restarting attempt %d"), i, AttemptCount + 1);
                    AttemptCount++;
                    if (AttemptCount >= MaxAttempts)
                    {
                        UE_LOG(LogTemp, Error, TEXT("WFC Failed after %d attempts, falling back to deterministic generation"), MaxAttempts);
                        return false;
                    }
                    Initialize(ChunkCoords);
                    BestCell = -1;
                    break;
                }
            }
            
            if (BestCell < 0)
            {
                if (AttemptCount >= MaxAttempts) break;
                continue; // Retry the observation phase
            }

            // Check if we're fully collapsed
            bool bFullyCollapsed = true;
            for (int32 i = 0; i < NumCells; i++)
            {
                if (Wave[i].CountSetBits() > 1)
                {
                    bFullyCollapsed = false;
                    break;
                }
            }
            
            if (bFullyCollapsed)
            {
                bCollapsed = true;
                UE_LOG(LogTemp, Log, TEXT("WFC Successfully completed for chunk (%d,%d,%d)"), 
                       ChunkCoords.X, ChunkCoords.Y, ChunkCoords.Z);
                break;
            }

            // COLLAPSE - Choose random state from valid options
            TArray<int32> Choices;
            for (int32 t = 0; t < NumTypes; t++)
                if (Wave[BestCell][t]) Choices.Add(t);

            if (Choices.Num() == 0) continue; // Safety check

            // Weight choices based on terrain logic
            int32 Pick = ChooseWeightedOption(BestCell, Choices);
            
            for (int32 t = 0; t < NumTypes; t++)
                if (t != Pick && Wave[BestCell][t])
                    Ban(BestCell, t);

            // 2) PROPAGATE - Update constraints
            while (PropQueue.Num())
            {
                int32 Idx = PropQueue.Pop(EAllowShrinking::No);
                FIntVector Cell = IndexToCoord(Idx);
                
                // 6-directional adjacency (including vertical)
                static const FIntVector Offsets[6] = {
                    {1,0,0},{-1,0,0},{0,1,0},
                    {0,-1,0},{0,0,1},{0,0,-1}
                };
                
                for (int32 d = 0; d < 6; d++)
                {
                    FIntVector NC = Cell + Offsets[d];
                    if (!IsValidCoord(NC)) continue;
                    
                    int32 NIdx = CoordToIndex(NC);
                    for (int32 t = 0; t < NumTypes; t++)
                    {
                        if (!Wave[NIdx][t]) continue;
                        
                        bool bAllowed = false;
                        for (int32 q = 0; q < NumTypes && !bAllowed; q++)
                        {
                            if (Wave[Idx][q] && GetAllowedNeighbors(q, d).Contains(t))
                                bAllowed = true;
                        }
                        
                        if (!bAllowed) Ban(NIdx, t);
                    }
                }
            }
        }
        
        // 🚨 EMERGENCY EXIT - Prevent engine freeze
        if (IterationCount >= MaxIterations)
        {
            UE_LOG(LogTemp, Error, TEXT("❌ WFC hit iteration limit (%d), forcing fallback"), MaxIterations);
            return false;
        }
        
        return bCollapsed;
    }

    int32 CoordToIndex(const FIntVector& c) const
    {
        return c.Z * ChunkSize * ChunkSize
             + c.Y * ChunkSize
             + c.X;
    }
    
    FIntVector IndexToCoord(int32 idx) const
    {
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
    
    int32 FindFirstAllowed(int32 CellIdx) const
    {
        for (int32 t = 0; t < NumTypes; ++t)
            if (Wave[CellIdx][t]) return t;
        return INDEX_NONE;
    }

private:
    void Ban(int32 CellIdx, int32 Type)
    {
        if (Wave[CellIdx][Type])
        {
            Wave[CellIdx][Type] = false;
            PropQueue.AddUnique(CellIdx); // Avoid duplicates
        }
    }
    
    int32 ChooseWeightedOption(int32 CellIdx, const TArray<int32>& Choices)
    {
        FIntVector Coord = IndexToCoord(CellIdx);
        
        // Terrain-aware weighting system
        TArray<float> Weights;
        Weights.SetNum(Choices.Num());
        
        for (int32 i = 0; i < Choices.Num(); i++)
        {
            EBlockType Type = (EBlockType)Choices[i];
            float Weight = 1.0f;
            
            // Height-based preferences
            float HeightRatio = (float)Coord.Z / ChunkHeight;
            
            switch (Type)
            {
            case EBlockType::Stone:
                Weight = HeightRatio < 0.3f ? 3.0f : 0.5f; // Prefer stone deep underground
                break;
            case EBlockType::Dirt:
                Weight = HeightRatio > 0.2f && HeightRatio < 0.7f ? 2.0f : 0.8f;
                break;
            case EBlockType::Grass:
                Weight = HeightRatio > 0.5f ? 2.5f : 0.3f; // Surface preference
                break;
            case EBlockType::Air:
                Weight = HeightRatio > 0.6f ? 3.0f : 1.0f; // Sky preference
                break;
            case EBlockType::Water:
                Weight = HeightRatio < 0.4f ? 1.5f : 0.2f; // Low areas
                break;
            }
            
            Weights[i] = Weight;
        }
        
        // Weighted random selection
        float TotalWeight = 0.0f;
        for (float W : Weights) TotalWeight += W;
        
        float Random = FMath::FRand() * TotalWeight;
        float Accumulator = 0.0f;
        
        for (int32 i = 0; i < Choices.Num(); i++)
        {
            Accumulator += Weights[i];
            if (Random <= Accumulator)
                return Choices[i];
        }
        
        return Choices[FMath::RandRange(0, Choices.Num() - 1)];
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
    int32 RenderDistance = 1;  // REDUCED: Start with 3x3 grid only

    UPROPERTY(EditAnywhere, Category="WFC|Terrain")
    int32 SeaLevel = 8;

    UPROPERTY(EditAnywhere, Category="WFC|Debug")
    bool bDebugGeneration = true;

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
    
    static TArray<int32> Allowed[FWFCChunk::NumTypes][6];
    static bool bAdjacencyBuilt;

    void UpdateChunks();
    void GenerateChunk(const FIntVector& Coords);
    void SeedHeightConstraints(FWFCChunk& Chunk, const FIntVector& Coords);
    void DrawChunk(const FWFCChunk& Chunk);
    void FallbackGeneration(FWFCChunk& Chunk, const FIntVector& Coords);
    FVector GetPlayerLocation() const;
    
    static void BuildAdjacency();
};