namespace Waymark.Generator;

/// <summary>
/// The configuration or the catalogue is wrong. Carries every problem found, not just the
/// first, so one run of the validator produces the whole list to fix (D-046 §9).
/// </summary>
internal sealed class GeneratorInputException : Exception
{
    public GeneratorInputException(IReadOnlyList<string> problems)
        : base(Describe(problems))
    {
        Problems = problems;
    }

    public GeneratorInputException(string problem)
        : this([problem])
    {
    }

    public GeneratorInputException(string problem, Exception innerException)
        : base(Describe([problem]), innerException)
    {
        Problems = [problem];
    }

    public GeneratorInputException()
        : this("The generator input is invalid.")
    {
    }

    public GeneratorInputException(string message, IReadOnlyList<string> problems, Exception innerException)
        : base(message, innerException)
    {
        Problems = problems;
    }

    /// <summary>Each problem, one sentence, naming the file and line or field it concerns.</summary>
    public IReadOnlyList<string> Problems { get; }

    private static string Describe(IReadOnlyList<string> problems) =>
        problems.Count == 1
            ? $"The generator input is invalid: {problems[0]}"
            : $"The generator input has {problems.Count} problems:\n  - " + string.Join("\n  - ", problems);
}
