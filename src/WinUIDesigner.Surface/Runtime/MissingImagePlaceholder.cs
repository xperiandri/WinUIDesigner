// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.IO;
using System.Text;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// An empty image for an image of the project that the surface cannot find, so that the document that shows it can
/// still be drawn. Only images get one: an empty XBF, XAML or RESW file would hide a real error.
/// </summary>
internal static class MissingImagePlaceholder
{
    // The image formats that WinUI decodes.
    private static readonly string[] _ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp", ".svg"];

    // One transparent pixel (red, green, blue and alpha 0). WinUI decodes a bitmap by its content, so it stands in for
    // every format but SVG.
    private static readonly byte[] _Png =
    [
        // Signature.
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        // IHDR: 1 x 1 pixel, 8 bits per channel, RGBA.
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00,
        0x1F, 0x15, 0xC4, 0x89,
        // IDAT: the one scan line (filter 0, then 0, 0, 0, 0), deflated.
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0xDA, 0x63, 0x60, 0x60, 0x60, 0x60, 0x00, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x7A, 0xA8, 0x57, 0x50,
        // IEND.
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    // An SVG image of one pixel that draws nothing.
    private static readonly byte[] _Svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>""");

    /// <summary>
    /// Whether the file resource <paramref name="name"/> (<c>Files/Assets/logo.png</c>) is an image.
    /// </summary>
    public static bool IsImage(string name)
    {
        if (!StagedResourceLookup.IsFileResource(name))
        {
            return false;
        }

        ReadOnlySpan<char> extension = Path.GetExtension(name.AsSpan());
        foreach (string imageExtension in _ImageExtensions)
        {
            if (extension.Equals(imageExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The name of the placeholder file for the image <paramref name="name"/>: an SVG image for an SVG image, a PNG
    /// image for any other.
    /// </summary>
    public static string GetFileName(string name) => IsSvg(name) ? "missing-image.svg" : "missing-image.png";

    /// <summary>
    /// Writes the placeholder for the image <paramref name="name"/> into <paramref name="directory"/> and returns its
    /// path. A complete placeholder that is already there, from this or another surface process, is used as it is.
    /// </summary>
    public static string Write(string directory, string name)
    {
        byte[] content = IsSvg(name) ? _Svg : _Png;
        string path = Path.Combine(directory, GetFileName(name));

        // Written before, by this or another surface process.
        if (HasContent(path, content))
        {
            return path;
        }

        // Missing or different: written under a name of this process first, so that no process reads half a file.
        Directory.CreateDirectory(directory);
        string temporary = FormattableString.Invariant($"{path}.{Environment.ProcessId}.tmp");
        File.WriteAllBytes(temporary, content);

        try
        {
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another surface process has the file open: this process uses its own copy.
            return temporary;
        }
    }

    private static bool IsSvg(string name) => Path.GetExtension(name.AsSpan()).Equals(".svg", StringComparison.OrdinalIgnoreCase);

    private static bool HasContent(string path, byte[] content)
    {
        try
        {
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not there, or another process is writing it.
            return false;
        }
    }
}
