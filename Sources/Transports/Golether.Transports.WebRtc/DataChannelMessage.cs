namespace Golether.Transports.WebRtc;

/// <summary>
/// One message of a data channel: binary (the streams of a session) or text (the proofs of the keys).
/// </summary>
/// <param name="Binary">The bytes of a binary message, or <see langword="null"/>.</param>
/// <param name="Text">The text of a text message, or <see langword="null"/>.</param>
public sealed record DataChannelMessage(byte[]? Binary, string? Text);
