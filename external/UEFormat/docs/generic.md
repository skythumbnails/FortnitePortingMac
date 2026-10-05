# UEFormat

Shared binary layout for all UEFormat files.

## File Types
- Models: [.uemodel](uemodel.md)
- Animations: [.ueanim](ueanim.md)
- Pose Assets: [.uepose](uepose.md)

## Generic Structs

```csharp
struct FVector
{
    float X, Y, Z;
}

struct FQuat
{
    float X, Y, Z, W;
}

struct FColor
{
    byte R, G, B, A;
}

struct FMeshUVFloat
{
    float U, V;
}

struct FString
{
    int Length;
    byte[] Data; // utf-8, no null terminator
}

struct TArray<T>
{
    int Count;
    T[Count] Elements;
}
```

## Header

```csharp
struct FUEFormatHeader
{
    byte[8] Magic; // "UEFORMAT"
    FString Identifier; // "UEMODEL" | "UEANIM" | "UEPOSE"
    byte FileVersion;
    FString ObjectName;
    FString ObjectPath;
    bool IsCompressed;
}
```

If `IsCompressed=true`, we serialize some extra compression info:

```csharp
FString CompressionFormat; // "GZIP" | "ZSTD"
int UncompressedSize;
int CompressedSize;
```

## Body

The file body (after decompress) is one root-level `FDataAttributeSet`.

```csharp
struct FDataAttributeSet
{
    int Count;
    FDataAttribute[Count] Attributes;
}

struct FDataAttribute
{
    FString Name;
    int ByteSize;
    byte[ByteSize] Data;
}
```

Each attribute is `Name : Data`. `ByteSize` is the length of `Data` so unknown attributes can be skipped. Order does not matter. Optional attributes may be absent.

`Data` is a serialized struct, a `TArray<T>`, or another `FDataAttributeSet`.
