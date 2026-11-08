namespace Golether.Transports.WebRtc;

/// <summary>
/// The signalling refused or broke a request: the code is the name the server gave (<c>RoomNotFound</c>,
/// <c>RoomTaken</c>, <c>Busy</c>, …) or one of this side (<c>Unreachable</c>, <c>UnsupportedVersion</c>).
/// </summary>
public sealed class SignalException : IOException
{
    /// <summary>
    /// The code of a server that did not answer or broke the connection.
    /// </summary>
    public const string UNREACHABLE = "Unreachable";

    /// <summary>
    /// The code of a server that speaks another version of the signalling.
    /// </summary>
    public const string UNSUPPORTED_VERSION = "UnsupportedVersion";

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalException"/> class.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="innerException">The cause, or <see langword="null"/>.</param>
    public SignalException(string code, Exception? innerException = null)
        : base("The signalling refused the request: " + code + ".", innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the code.
    /// </summary>
    public string Code { get; }
}
