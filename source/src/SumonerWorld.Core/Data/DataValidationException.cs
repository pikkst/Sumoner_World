namespace SumonerWorld.Core.Data;

/// <summary>
/// Game data failed validation. Lists every problem found, not just the first, so all of them can be fixed in one pass.
/// The server must stop on this error; it never runs with half-loaded data (AI Bible §12, R36).
/// </summary>
public sealed class DataValidationException : Exception
{
    /// <summary>Creates the exception from the list of problems.</summary>
    public DataValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Creates an exception with a single message.</summary>
    public DataValidationException()
        : this(["Game data is invalid."])
    {
    }

    /// <summary>Creates an exception with a single message.</summary>
    public DataValidationException(string message)
        : this([message])
    {
    }

    /// <summary>Creates an exception with a single message and the underlying cause.</summary>
    public DataValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    /// <summary>Every problem found, each starting with the file path.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return $"Game data is invalid ({errors.Count} problem(s)):{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}";
    }
}
