using System.Collections.Immutable;
using System.Diagnostics;

static class Helper
{

    public static int Run(string cmd)
    {
        var parts = cmd.Split(' ', 2);
        return Run(parts[0], parts.ElementAtOrDefault(1) ?? "");
    }

    public static int Run(string exe, string args)
    {
        return Run(exe, args, ImmutableDictionary<string, string>.Empty);
    }

    public static int Run(string exe, string args, IDictionary<string, string> env)
    {
        var startinfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args
        };

        foreach (var (k, v) in env)
            startinfo.Environment[k] = v;

        using var proc = Process.Start(startinfo)!;
        Console.WriteLine($"start: ({proc.Id}) {exe} {args}");
        proc.WaitForExit();
        return proc.ExitCode;
    }


}