# UEAnim

Skeletal Animation Sequences with Bone Transforms and Curves.

## Root

```csharp
FDataAttributeSet
{
    METADATA : FAnimMetadata
    TRACKS : TArray<FTrack>
    CURVES : TArray<FCurve>
}
```

## Structs

```csharp
struct FAnimMetadata
{
    int NumFrames;
    float FramesPerSecond;
    FString RefPosePath;
    byte AdditiveAnimType;
    byte RefPoseType;
    int RefFrameIndex;
}

struct FTrack
{
    FString BoneName;
    TArray<FVectorKey> PositionKeys;
    TArray<FQuatKey> RotationKeys;
    TArray<FVectorKey> ScaleKeys;
}

struct FCurve
{
    FString CurveName;
    TArray<FFloatKey> Keys;
}

struct FVectorKey
{
    int Frame;
    FVector Value;
}

struct FQuatKey
{
    int Frame;
    FQuat Value;
}

struct FFloatKey
{
    int Frame;
    float Value;
}

enum EAdditiveAnimationType : byte
{
    AAT_None = 0,
    AAT_LocalSpaceBase = 1,
    AAT_RotationOffsetMeshSpace = 2,
}

enum EAdditiveBasePoseType : byte
{
    ABPT_None = 0,
    ABPT_RefPose = 1,
    ABPT_AnimScaled = 2,
    ABPT_AnimFrame = 3,
    ABPT_LocalAnimFrame = 4,
}
```
