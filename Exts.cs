using System.Diagnostics;

static class Exts
{
    public static string Unix(this string it) { return it.ReplaceLineEndings("\n"); }
    public static string NoEOL(this string it) { return it.ReplaceLineEndings(" "); }
    public static void Z(this int it) { if (it != 0) throw new Exception("result not zero"); }

    public static string? GetFileNameOrDefault(this Process p)
    {
        try
        {
            return p.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}