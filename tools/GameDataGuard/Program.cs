using GameDataGuard;

try
{
    if (args.SequenceEqual(new[] { "--self-test" })) { Checks.Run(); return 0; }
    if (args.Length >= 2 && args[0] == "--doorway")
    {
        string? baseFile = null, renamesFile = null;
        for (int i = 2; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--base" or "--renames")) throw new ArgumentException("Use --doorway ROOT [--base FILE] [--renames FILE]");
            if (args[i] == "--base") baseFile = args[i + 1]; else renamesFile = args[i + 1];
        }
        List<string> bypasses = Doorway.Repo(Path.GetFullPath(args[1]), baseFile, renamesFile);
        if (bypasses.Count == 0) { Console.WriteLine("DOORWAY_GUARD PASS: tracked sources add no Radicle doorway bypass beyond " + Doorway.BaselinePath + "."); return 0; }
        Console.Error.WriteLine("DOORWAY_GUARD BLOCKED: game data is read around Radicle (baseline " + Doorway.BaselinePath + " only shrinks)");
        foreach (string bypass in bypasses) Console.Error.WriteLine(bypass);
        return 1;
    }
    List<string> problems;
    string what;
    if (args.Length == 2 && args[0] == "--repo") { problems = Guard.Repo(Path.GetFullPath(args[1])); what = "repository"; }
    else if (args.Length >= 2 && args[0] == "--export") { problems = args.Skip(1).SelectMany(Guard.Export).ToList(); what = "export"; }
    else throw new ArgumentException("Use --repo ROOT, --export PATH..., --doorway ROOT, or --self-test");
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
