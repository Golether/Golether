using Golether.Media.Conference.Browser.Enums;

namespace Golether.Media.Conference.Browser;

/// <summary>
/// What became of the camera and the microphone after the page was asked to capture them.
/// </summary>
/// <param name="Camera">The camera.</param>
/// <param name="Microphone">The microphone.</param>
public readonly record struct CaptureResult(CaptureOutcome Camera, CaptureOutcome Microphone);
