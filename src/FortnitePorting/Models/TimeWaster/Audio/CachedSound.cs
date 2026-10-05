using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Platform;
// using NAudio.Vorbis;
// using NAudio.Wave;

namespace FortnitePorting.Models.TimeWaster.Audio;

public class CachedSound
{
    public readonly float[] AudioData = [];
    public readonly object WaveFormat = null;
    
    public CachedSound(string resourcePath)
    {
        // Audio disabled on macOS
    }
}