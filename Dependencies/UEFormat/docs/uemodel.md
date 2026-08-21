# UEModel

Static/Skeletal Models with LODs, Skeletons, and Convex Collision.

## Root

```csharp
FDataAttributeSet
{
    LODS : TArray<UEModelLOD>
    SKELETON : FDataAttributeSet
    COLLISION : TArray<FConvexMeshCollision>
}
```

## LOD

```csharp
struct UEModelLOD
{
    FString Name;
    FDataAttributeSet Attributes;
}

FDataAttributeSet
{
    VERTICES : TArray<FVector>
    NORMALS : TArray<FNormal>
    TANGENTS : TArray<FVector>
    TEXCOORDS : TArray<FTexCoordEntry>
    INDICES : TArray<uint>
    VERTEXCOLORS : TArray<FVertexColor>
    MATERIALS : TArray<FMaterial>
    WEIGHTS : TArray<FWeight>
    MORPHTARGETS : TArray<FMorphTarget>
}
```

## Skeleton

```csharp
FDataAttributeSet
{
    METADATA : FString
    BONES : TArray<FBone>
    SOCKETS : TArray<FSocket>
    VIRTUALBONES : TArray<FVirtualBone>
}
```

## Structs

```csharp
struct FNormal
{
    float BinormalSign;
    FVector Normal;
}

struct FTexCoordEntry
{
    FString Name;
    TArray<FMeshUVFloat> UVs;
}

struct FVertexColor
{
    FString Name;
    TArray<FColor> Data;
}

struct FMaterial
{
    FString MaterialName;
    FString MaterialPath;
    int FirstIndex;
    int NumFaces;
}

struct FWeight
{
    ushort Bone;
    int VertexIndex;
    float Weight;
}

struct FMorphTarget
{
    FString MorphName;
    TArray<FMorphData> MorphData;
}

struct FMorphData
{
    FVector PositionDelta;
    FVector TangentZDelta;
    uint VertexIndex;
}

struct FBone
{
    FString BoneName;
    int ParentIndex;
    FVector Position;
    FQuat Orientation;
    FVector Scale;
}

struct FSocket
{
    FString SocketName;
    FString BoneName;
    FVector RelativeLocation;
    FQuat RelativeRotation;
    FVector RelativeScale;
}

struct FVirtualBone
{
    FString SourceBoneName;
    FString TargetBoneName;
    FString VirtualBoneName;
}

struct FConvexMeshCollision
{
    FString Name;
    TArray<FVector> VertexData;
    TArray<int> IndexData;
}
```
