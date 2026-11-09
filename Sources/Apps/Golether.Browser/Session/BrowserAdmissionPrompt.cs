using Golether.Security.Admission;

namespace Golether.Browser.Session;

/// <summary>
/// Asks the host on the page whether to let a guest in, showing the verification code to read aloud.
/// </summary>
internal sealed class BrowserAdmissionPrompt : IAdmissionPrompt
{
    /// <summary>
    /// The questions waiting for an answer, by number.
    /// </summary>
    private readonly Dictionary<int, TaskCompletionSource<bool>> _questions = [];

    /// <summary>
    /// Guards <see cref="_questions"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The number of the last question.
    /// </summary>
    private int _last;

    /// <summary>
    /// Raised with a new question. May be raised on any thread.
    /// </summary>
    public event EventHandler<AdmissionQuestion>? Asked;

    /// <summary>
    /// Raised with the number of a question taken back: the guest left or the time ran out. May be raised on any thread.
    /// </summary>
    public event EventHandler<int>? Withdrawn;

    /// <inheritdoc />
    public async Task<bool> AskAsync(AdmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int id;
        lock (_gate)
        {
            id = ++_last;
            _questions[id] = answer;
        }

        Asked?.Invoke(this, new AdmissionQuestion(id, request.DisplayName, request.VerificationCode.ToString()));
        try
        {
            using (cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken)))
            {
                return await answer.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            bool open;
            lock (_gate)
            {
                open = _questions.Remove(id);
            }

            if (open)
            {
                Withdrawn?.Invoke(this, id);
            }
        }
    }

    /// <summary>
    /// Answers a question.
    /// </summary>
    /// <param name="id">The number of the question.</param>
    /// <param name="approve">Whether to let the guest in.</param>
    public void Answer(int id, bool approve)
    {
        TaskCompletionSource<bool>? answer;
        lock (_gate)
        {
            _questions.Remove(id, out answer);
        }

        answer?.TrySetResult(approve);
    }
}
