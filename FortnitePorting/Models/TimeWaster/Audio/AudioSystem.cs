using System;

namespace FortnitePorting.Models.TimeWaster.Audio;

public class AudioSystem : IDisposable
{
    public static readonly AudioSystem Instance = new();
    public int SampleRate = 44100;
    public int ChannelCount = 2;

    public void PlaySound(object sampleProvider) { }
    public void Stop() { }
    public void Dispose() { }
}

public static class AudioSystemExtensions
{
    extension(CachedSound sound)
    {
        public void Play() { }
    }
}