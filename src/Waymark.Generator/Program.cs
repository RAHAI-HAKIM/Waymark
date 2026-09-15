// Waymark.Generator — the synthetic store generator (W10, decisions.md D-046).
//
// Exit codes: 0 success, 1 bad arguments or invalid input, 2 the run failed.

using Waymark.Generator;

var arguments = GeneratorArguments.Parse(args, out var error);

if (arguments is null)
{
    if (error is null)
    {
        Console.WriteLine(GeneratorArguments.Usage);
        return 0;
    }

    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(GeneratorArguments.Usage);
    return 1;
}

try
{
    GeneratorRun.Execute(arguments, Console.Out);
    return 0;
}
catch (GeneratorInputException invalid)
{
    // Every problem, one per line, so the whole list can be fixed in one pass.
    Console.Error.WriteLine(invalid.Message);
    return 1;
}
