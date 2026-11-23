// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

/// <summary>
/// Finds the surface that was built next to the tests. The surface needs WinUI and cannot run in a test, so the tests
/// that check how it is wired read its compiled code.
/// </summary>
internal static class SurfaceBuild
{
    /// <summary>
    /// Returns the path of <c>WinUISurface.dll</c> under the build output of the surface project. The newest build wins
    /// when several configurations or platforms were built.
    /// </summary>
    /// <exception cref="FileNotFoundException">The solution or the build of the surface was not found.</exception>
    public static string GetAssemblyPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "WinUIDesigner.slnx")))
            {
                continue;
            }

            string output = Path.Combine(directory.FullName, "src", "WinUIDesigner.Surface", "bin");
            string? path = Directory.Exists(output)
                ? Directory.EnumerateFiles(output, "WinUISurface.dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            return path ?? throw new FileNotFoundException($"Could not locate WinUISurface.dll under '{output}'. Build WinUIDesigner.Surface first.");
        }

        throw new FileNotFoundException("Could not locate WinUIDesigner.slnx from the test output directory.");
    }
}