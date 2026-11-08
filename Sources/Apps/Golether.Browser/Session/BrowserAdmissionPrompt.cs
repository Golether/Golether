using Golether.Browser.Interop;
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
    /// The number of the last question.
    /// </summary>
    private int _last;

    /// <inheritdoc />
    public async Task<bool> AskAsync(AdmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = ++_last;
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _questions[id] = answer;
        PageInterop.Ask(id, request.DisplayName, request.VerificationCode.ToString());

        try
        {
            using (cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken)))
            {
                return await answer.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            if (_questions.Remove(id))
            {
                PageInterop.CancelAsk(id);
            }
        }
    }

    /// <summary>
    /// Takes the answer of the host.
    /// </summary>
    /// <param name="id">The number of the question.</param>
    /// <param name="approve">Whether the guest is let in.</param>
    public void Answer(int id, bool approve)
    {
        if (_questions.Remove(id, out var answer))
        {
            answer.TrySetResult(approve);
        }
    }
}
