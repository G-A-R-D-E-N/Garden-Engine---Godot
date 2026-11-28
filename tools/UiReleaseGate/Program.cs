using UiReleaseGate;

try
{
    if (args.SequenceEqual(new[] { "--self-test" })) { Checks.Run(); return 0; }
    string root = Directory.GetCurrentDirectory();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
        else throw new ArgumentException("Use --root DIRECTORY, or --self-test");
    }
    var problems = Provenance.Check(Path.GetFullPath(root));
    if (problems.Count != 0)
    {
        Console.Error.WriteLine("UI_PROVENANCE BLOCKED");
        foreach (string problem in problems) Console.Error.WriteLine(problem);
        return 1;
    }
    Console.WriteLine("UI_PROVENANCE PASS: source review records match; package and legal review remain separate.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("UI_PROVENANCE BLOCKED: " + error.Message);
    return 1;
}
