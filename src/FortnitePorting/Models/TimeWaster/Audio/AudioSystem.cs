using System;

namespace FortnitePorting.Models.TimeWaster.Audio;

public class AudioSystem : IDisposable
{
    private static readonly Lazy<AudioSystem> LazyInstance = new(() => new AudioSystem());
    public static AudioSystem Instance => LazyInstance.Value;

    public int SampleRate;
    public int ChannelCount;

    public AudioSystem(int sampleRate = 44100, int channelCount = 2)
    {
        SampleRate = sampleRate;
        ChannelCount = channelCount;
    }

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
