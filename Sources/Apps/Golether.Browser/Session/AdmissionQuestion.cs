namespace Golether.Browser.Session;

/// <summary>
/// A question to the host about a guest who asks to come in.
/// </summary>
/// <param name="Id">The number of the question, for <see cref="BrowserAdmissionPrompt.Answer"/>.</param>
/// <param name="Name">The name of the guest.</param>
/// <param name="Code">The verification code to compare with the guest.</param>
internal sealed record AdmissionQuestion(int Id, string Name, string Code);
