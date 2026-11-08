using System.Text.Json;
using System.Text.Json.Serialization;

namespace Golether.Transports.WebRtc;

/// <summary>
/// What a <c>signal</c> frame carries in its <c>data</c>, in the form the browser writes: <c>{ "offer": … }</c>,
/// <c>{ "answer": … }</c> or <c>{ "candidate": … }</c>. It comes from the other side through the server, so it is
/// checked before it reaches the connection.
/// </summary>
/// <param name="Offer">An offer, or <see langword="null"/>.</param>
/// <param name="Answer">An answer, or <see langword="null"/>.</param>
/// <param name="Candidate">A candidate, or <see langword="null"/>.</param>
public sealed record SignalEnvelope(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SignalDescription? Offer = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SignalDescription? Answer = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SignalCandidate? Candidate = null)
{
    /// <summary>
    /// The longest description accepted, in characters.
    /// </summary>
    public const int MAX_DESCRIPTION = 12 * 1024;

    /// <summary>
    /// The longest candidate accepted, in characters.
    /// </summary>
    public const int MAX_CANDIDATE = 1024;

    /// <summary>
    /// The JSON options of the envelope.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Wraps an offer.
    /// </summary>
    /// <param name="sdp">The description.</param>
    /// <returns>The data of the frame.</returns>
    public static string ForOffer(string sdp) => Write(new SignalEnvelope(Offer: new SignalDescription("offer", sdp)));

    /// <summary>
    /// Wraps an answer.
    /// </summary>
    /// <param name="sdp">The description.</param>
    /// <returns>The data of the frame.</returns>
    public static string ForAnswer(string sdp) => Write(new SignalEnvelope(Answer: new SignalDescription("answer", sdp)));

    /// <summary>
    /// Wraps a candidate.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns>The data of the frame.</returns>
    public static string ForCandidate(IceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Write(new SignalEnvelope(Candidate: new SignalCandidate(candidate.Candidate, candidate.Line)));
    }

    /// <summary>
    /// Reads the data of a frame and checks what it carries: one description that is SDP, or one candidate line.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="envelope">The envelope read.</param>
    /// <returns><see langword="true"/> when the data is well-formed.</returns>
    public static bool TryRead(string? data, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SignalEnvelope? envelope)
    {
        envelope = null;
        if (string.IsNullOrEmpty(data) || data.Length > MAX_DESCRIPTION * 2)
        {
            return false;
        }

        SignalEnvelope? read;
        try
        {
            read = JsonSerializer.Deserialize<SignalEnvelope>(data, _json);
        }
        catch (JsonException)
        {
            return false;
        }

        var valid = read switch
        {
            { Offer: { } offer, Answer: null, Candidate: null } => IsDescription(offer, "offer"),
            { Answer: { } answer, Offer: null, Candidate: null } => IsDescription(answer, "answer"),
            { Candidate: { } candidate, Offer: null, Answer: null } => IsCandidate(candidate),
            _ => false,
        };
        envelope = valid ? read : null;
        return valid;
    }

    /// <summary>
    /// Writes an envelope.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The JSON.</returns>
    private static string Write(SignalEnvelope envelope) => JsonSerializer.Serialize(envelope, _json);

    /// <summary>
    /// Checks a description.
    /// </summary>
    /// <param name="description">The description.</param>
    /// <param name="type">The type it must have.</param>
    /// <returns><see langword="true"/> when it is SDP of that type.</returns>
    private static bool IsDescription(SignalDescription description, string type)
        => description.Type == type &&
           description.Sdp is { Length: <= MAX_DESCRIPTION } sdp &&
           sdp.StartsWith("v=0", StringComparison.Ordinal) && sdp.Contains("\nm=", StringComparison.Ordinal);

    /// <summary>
    /// Checks a candidate.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns><see langword="true"/> when it is one candidate line.</returns>
    private static bool IsCandidate(SignalCandidate candidate)
        => candidate.Candidate is { Length: > 0 and <= MAX_CANDIDATE } line &&
           line.StartsWith("candidate:", StringComparison.Ordinal) &&
           !line.Contains('\r') && !line.Contains('\n') &&
           candidate.SdpMLineIndex is null or <= 64;
}
