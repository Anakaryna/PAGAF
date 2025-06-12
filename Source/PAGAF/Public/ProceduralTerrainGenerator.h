// ProceduralTerrainGenerator.h
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "ProceduralTerrainGenerator.generated.h"

UENUM(BlueprintType)
enum class EBlockType : uint8
{
    Air    UMETA(DisplayName = "Air"),
    Grass  UMETA(DisplayName = "Grass"),
    Dirt   UMETA(DisplayName = "Dirt"),
    Stone  UMETA(DisplayName = "Stone"),
    Water  UMETA(DisplayName = "Water")
};

UENUM(BlueprintType)
enum class EGenerationType : uint8
{
    Simple     UMETA(DisplayName = "Simple Height-based"),
    Hybrid     UMETA(DisplayName = "Hybrid (Simple + Structures)")
};

USTRUCT(BlueprintType)
struct FBlockData
{
    GENERATED_BODY()

    UPROPERTY()
    EBlockType BlockType = EBlockType::Air;
    
    UPROPERTY()
    int32 InstanceIndex = -1;
    
    UPROPERTY()
    bool bGenerated = false;
    
    FBlockData()
    {
        BlockType = EBlockType::Air;
        InstanceIndex = -1;
        bGenerated = false;
    }
    
    FBlockData(EBlockType InType)
    {
        BlockType = InType;
        InstanceIndex = -1;
        bGenerated = false;
    }
};

UCLASS()
class PAGAF_API AProceduralTerrainGenerator : public AActor
{
    GENERATED_BODY()

public:
    AProceduralTerrainGenerator();

protected:
    virtual void BeginPlay() override;
    virtual void Tick(float DeltaTime) override;

    // ========== GENERATION SETTINGS ==========
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Generation")
    EGenerationType GenerationType = EGenerationType::Simple;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Generation")
    int32 ViewDistance = 50;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Generation")
    float BlockSize = 100.0f;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Generation")
    int32 MaxBlocksPerFrame = 100;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Generation")
    int32 MaxHeight = 24;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Generation")
    int32 MinHeight = -8;
    
    // ========== TERRAIN SETTINGS ==========
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terrain")
    float NoiseScale = 0.015f;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terrain")
    int32 BaseHeight = 6;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terrain")
    int32 HeightVariation = 12;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terrain")
    int32 DirtDepth = 4;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Terrain")
    int32 SeaLevel = 4;
    
    // ========== RENDERING ==========
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rendering")
    UStaticMesh* BlockMesh;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rendering")
    UMaterialInterface* GrassMaterial;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rendering")
    UMaterialInterface* DirtMaterial;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rendering")
    UMaterialInterface* StoneMaterial;
    
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Rendering")
    UMaterialInterface* WaterMaterial;
    
    // ========== DEBUG ==========
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Debug")
    bool bDebugLogs = true;

private:
    // ========== COMPONENTS ==========
    UPROPERTY()
    UInstancedStaticMeshComponent* GrassInstances;
    
    UPROPERTY()
    UInstancedStaticMeshComponent* DirtInstances;
    
    UPROPERTY()
    UInstancedStaticMeshComponent* StoneInstances;
    
    UPROPERTY()
    UInstancedStaticMeshComponent* WaterInstances;
    
    // ========== CORE DATA ==========
    TMap<FIntVector, FBlockData> WorldGrid;
    TSet<FIntVector> LoadedBlocks;
    
    // Player tracking
    FVector LastPlayerPos;
    FIntVector LastPlayerGrid;
    
    // Performance tracking
    int32 BlocksGeneratedThisFrame;
    double LastGenerationTime;
    
    // ========== CORE GENERATION ==========
    void UpdateTerrainAroundPlayer();
    void GenerateBlocksInRadius(const FIntVector& Center, int32 Radius);
    void RemoveDistantBlocks(const FIntVector& Center, int32 MaxDistance);
    
    // ========== BLOCK MANAGEMENT ==========
    void PlaceBlock(const FIntVector& GridPos, EBlockType BlockType);
    void RemoveBlock(const FIntVector& GridPos);
    bool IsBlockLoaded(const FIntVector& GridPos) const;
    EBlockType GetBlockType(const FIntVector& GridPos) const;
    
    // ========== TERRAIN GENERATION METHODS ==========
    EBlockType GenerateSimpleTerrain(const FIntVector& GridPos) const;
    EBlockType GenerateHybridTerrain(const FIntVector& GridPos) const;
    
    // ========== NOISE & HEIGHTMAP ==========
    int32 GetTerrainHeight(int32 WorldX, int32 WorldY) const;
    float GetNoise(float X, float Y, float Scale = 1.0f) const;
    float GetMultiOctaveNoise(float X, float Y) const;
    
    // ========== COORDINATE CONVERSION ==========
    FIntVector WorldToGrid(const FVector& WorldPos) const;
    FVector GridToWorld(const FIntVector& GridPos) const;
    FVector GetPlayerPosition() const;
    
    // ========== INSTANCED MESH MANAGEMENT ==========
    UInstancedStaticMeshComponent* GetInstanceComponent(EBlockType BlockType) const;
    void SetupInstancedMeshComponents();
    void OptimizeRendering();
    
    // ========== UTILITY ==========
    bool IsInRadius(const FIntVector& Center, const FIntVector& Point, int32 Radius) const;
    float GetDistance3D(const FIntVector& A, const FIntVector& B) const;

public:
    // ========== PUBLIC API ==========
    UFUNCTION(BlueprintCallable, Category = "Terrain")
    void RegenerateAroundPlayer();
    
    UFUNCTION(BlueprintCallable, Category = "Terrain")
    void ClearAllTerrain();
    
    UFUNCTION(BlueprintCallable, Category = "Terrain")
    EBlockType GetBlockAt(const FVector& WorldPosition) const;
    
    UFUNCTION(BlueprintCallable, Category = "Terrain")
    void SetBlockAt(const FVector& WorldPosition, EBlockType BlockType);
    
    UFUNCTION(BlueprintCallable, Category = "Terrain")
    int32 GetLoadedBlockCount() const { return LoadedBlocks.Num(); }
    
    UFUNCTION(BlueprintCallable, Category = "Terrain")
    void SwitchGenerationType(EGenerationType NewType);
};