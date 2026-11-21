using GameDataGuard;

try
{
    if (args.SequenceEqual(new[] { "--self-test" })) { Checks.Run(); return 0; }
    List<string> problems;
    string what;
    if (args.Length == 2 && args[0] == "--repo") { problems = Guard.Repo(Path.GetFullPath(args[1])); what = "repository"; }
    else if (args.Length >= 2 && args[0] == "--export") { problems = args.Skip(1).SelectMany(Guard.Export).ToList(); what = "export"; }
    else throw new ArgumentException("Use --repo ROOT, --export PATH..., or --self-test");
    if (problems.Count != 0)
    {
        Console.Error.WriteLine("GAME_DATA_GUARD BLOCKED: " + what + " contains game data");
        foreach (string problem in problems) Console.Error.WriteLine(problem);
        return 1;
    }
    Console.WriteLine("GAME_DATA_GUARD PASS: " + what + " holds no converted or original game data paths or headers.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("GAME_DATA_GUARD BLOCKED: " + error.Message);
    return 1;
}
