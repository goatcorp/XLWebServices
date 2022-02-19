using System.Reflection;

namespace XLWebServices;

public class Util
{
    private static string? _gitHashInternal;

    /// <summary>
    /// Gets the git hash value from the assembly
    /// or null if it cannot be found.
    /// </summary>
    /// <returns>The git hash of the assembly.</returns>
    public static string GetGitHash()
    {
        if (_gitHashInternal != null)
            return _gitHashInternal;

        var asm = typeof(Util).Assembly;
        var attrs = asm.GetCustomAttributes<AssemblyMetadataAttribute>();

        _gitHashInternal = attrs.First(a => a.Key == "GitHash").Value!;

        return _gitHashInternal;
    }
}