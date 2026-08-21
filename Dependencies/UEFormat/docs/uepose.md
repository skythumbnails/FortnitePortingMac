# UEPose

Pose Assets with Named Poses and Curves.

## Root

```csharp
FDataAttributeSet
{
    POSES : TArray<FPoseData>
    CURVES : TArray<FString>
}
```

## Structs

```csharp
struct FPoseData
{
    FString PoseName;
    TArray<FPoseKey> Keys;
    TArray<FPoseCurveInfluence> Curves;
}

struct FPoseKey
{
    FString BoneName;
    FVector Location;
    FQuat Rotation;
    FVector Scale;
}

struct FPoseCurveInfluence
{
    int CurveIndex;
    float Influence;
}
```
