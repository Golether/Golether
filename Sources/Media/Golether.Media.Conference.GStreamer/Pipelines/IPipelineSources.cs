namespace Golether.Media.Conference.GStreamer.Pipelines;

/// <summary>
/// The capture and playback elements of one platform: the only part of the pipelines that differs between systems.
/// </summary>
public interface IPipelineSources
{
    /// <summary>
    /// Gets the element that plays the mixed sound of the other participants.
    /// </summary>
    string AudioSink { get; }

    /// <summary>
    /// Gets the element that captures what the loudspeakers play (the film, the voices, any other sound), the
    /// reference of the echo canceller, or <see langword="null"/> when the system cannot give it: then only the voices
    /// played through <see cref="AudioSink"/> are taken out of the microphone.
    /// </summary>
    string? EchoReference { get; }

    /// <summary>
    /// Builds the element that captures the camera.
    /// </summary>
    /// <param name="camera">The camera or <see langword="null"/> for the default one.</param>
    /// <returns>The element with its properties, as a part of a pipeline description.</returns>
    string VideoSource(CaptureDevice? camera);

    /// <summary>
    /// Builds the element that captures the microphone.
    /// </summary>
    /// <param name="microphone">The microphone or <see langword="null"/> for the default one.</param>
    /// <returns>The element with its properties, as a part of a pipeline description.</returns>
    string AudioSource(CaptureDevice? microphone);
}
