namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The elements of Linux: the automatic ones by default, V4L2 and PulseAudio for a chosen device.
/// </summary>
internal sealed class LinuxPipelineSources : IPipelineSources
{
    /// <inheritdoc />
    public string AudioSink => "autoaudiosink";

    /// <summary>
    /// Gets the monitor of the default output of PulseAudio (and of PipeWire, which speaks its protocol): the sound
    /// the film and the voices play on.
    /// </summary>
    public string EchoReference => "pulsesrc device=@DEFAULT_MONITOR@";

    /// <inheritdoc />
    public string VideoSource(CaptureDevice? camera)
        => camera is null ? "autovideosrc" : "v4l2src device=" + PipelineText.Quote(camera.Id);

    /// <inheritdoc />
    public string AudioSource(CaptureDevice? microphone)
        => microphone is null ? "autoaudiosrc" : "pulsesrc device=" + PipelineText.Quote(microphone.Id);
}
