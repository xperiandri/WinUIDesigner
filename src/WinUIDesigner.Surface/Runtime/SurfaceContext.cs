// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Names what a surface process of a project starts from: the files that Visual Studio staged for it, and the resources
/// of its application (App.xaml). A framework that stops at its first frame stops the same way in every new process that
/// starts from the same, so the extension restarts the process once for each context, and again when the staged files
/// or App.xaml have changed. It has no WinUI types of its own, so that the rule can be tested.
/// </summary>
internal static class SurfaceContext
{
    /// <summary>The name of the context: the same in every process that starts from the same staged files and App.xaml.</summary>
    public static string Identify(string contentDirectory, string stagedFiles, string? applicationXaml)
    {
        byte[] context = Encoding.UTF8.GetBytes($"{contentDirectory.ToUpperInvariant()}\n{stagedFiles}\n{applicationXaml}");

        return Convert.ToHexString(SHA256.HashData(context), 0, 8);
    }

    /// <summary>
    /// The files at the top of the staged directory, the output of the project among them: how many there are, and when
    /// the newest was written. A build of the project stages its output again.
    /// </summary>
    public static string DescribeStagedFiles(string directory)
    {
        try
        {
            int count = 0;
            DateTime newest = DateTime.MinValue;
            foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles())
            {
                count++;
                if (file.LastWriteTimeUtc > newest)
                {
                    newest = file.LastWriteTimeUtc;
                }
            }

            return FormattableString.Invariant($"{count} files, the newest written {newest:O}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            // Without them the context is named by App.xaml alone.
            return string.Empty;
        }
    }
}
