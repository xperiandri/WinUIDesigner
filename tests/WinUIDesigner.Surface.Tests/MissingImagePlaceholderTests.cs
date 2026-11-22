// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class MissingImagePlaceholderTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinUIDesigner.Surface.Tests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("Files/Assets/logo.png")]
    [DataRow("Files/Assets/logo.PNG")]
    [DataRow("Files/Assets/photo.jpg")]
    [DataRow("Files/Assets/photo.jpeg")]
    [DataRow("Files/Assets/spinner.gif")]
    [DataRow("Files/Assets/logo.bmp")]
    [DataRow("Files/Assets/app.ico")]
    [DataRow("Files/Assets/scan.tif")]
    [DataRow("Files/Assets/scan.tiff")]
    [DataRow("Files/Assets/photo.webp")]
    [DataRow("Files/Assets/Images/svg/theme-light/ecierge_waves_logo.svg")]
    [DataRow("Files/Assets/logo.SVG")]
    public void ImagesAreRecognizedInAnyCase(string name) => Assert.IsTrue(MissingImagePlaceholder.IsImage(name));

    [TestMethod]
    [DataRow("Files/Ecierge.Uno.Controls/Styles.xbf")]
    [DataRow("Files/Ecierge.Uno.Controls/Styles.xaml")]
    [DataRow("Files/Strings/en-US/Resources.resw")]
    [DataRow("Files/Assets/logo")]
    [DataRow("Files/Assets/logo.png.xbf")]
    [DataRow("Assets/logo.png")]
    [DataRow("Resources/logo.png")]
    public void OtherResourcesAreNoImages(string name)
    {
        // XBF, XAML and RESW files, a file without an extension, and names that are no file resources.
        Assert.IsFalse(MissingImagePlaceholder.IsImage(name));
    }

    [TestMethod]
    [DataRow("Files/Assets/Images/svg/theme-dark/ecierge_waves_logo.svg", "missing-image.svg")]
    [DataRow("Files/Assets/logo.SVG", "missing-image.svg")]
    [DataRow("Files/Assets/Images/back.png", "missing-image.png")]
    [DataRow("Files/Assets/photo.jpg", "missing-image.png")]
    public void SvgImagesGetAnSvgPlaceholderAndOtherImagesAPng(string name, string fileName)
    {
        Assert.AreEqual(fileName, MissingImagePlaceholder.GetFileName(name));
        Assert.AreEqual(Path.Combine(_directory, fileName), MissingImagePlaceholder.Write(_directory, name));
    }

    [TestMethod]
    public void PngPlaceholderIsOneTransparentPixel()
    {
        byte[] png = File.ReadAllBytes(MissingImagePlaceholder.Write(_directory, "Files/Assets/Images/back.png"));

        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);

        var chunks = new List<(string Type, byte[] Data)>();
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            byte[] typeAndData = png[(offset + 4)..(offset + 8 + length)];
            Assert.AreEqual(BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length)), Crc32(typeAndData), "chunk CRC");
            chunks.Add((Encoding.ASCII.GetString(typeAndData, 0, 4), typeAndData[4..]));
            offset += 12 + length;
        }

        CollectionAssert.AreEqual(new[] { "IHDR", "IDAT", "IEND" }, chunks.Select(chunk => chunk.Type).ToArray());

        // 1 x 1 pixel, 8 bits per channel, RGBA, no interlacing.
        byte[] header = chunks[0].Data;
        Assert.AreEqual(1, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.AreEqual(1, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        CollectionAssert.AreEqual(new byte[] { 8, 6, 0, 0, 0 }, header[8..]);

        // The scan line: filter 0, then red, green, blue and alpha 0.
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 0 }, inflated.ToArray());
        Assert.IsEmpty(chunks[2].Data);
    }

    [TestMethod]
    public void SvgPlaceholderIsAnEmptyImageOfOnePixel()
    {
        XElement svg = XElement.Load(MissingImagePlaceholder.Write(_directory, "Files/Assets/Images/svg/theme-light/ecierge_waves_logo.svg"));

        Assert.AreEqual(XName.Get("svg", "http://www.w3.org/2000/svg"), svg.Name);
        Assert.AreEqual("1", (string?)svg.Attribute("width"));
        Assert.AreEqual("1", (string?)svg.Attribute("height"));
        Assert.IsTrue(svg.IsEmpty);
    }

    [TestMethod]
    public void CompletePlaceholderIsUsedAsItIs()
    {
        // Another surface process wrote it before.
        string path = MissingImagePlaceholder.Write(_directory, "Files/Assets/logo.png");
        var written = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        Assert.AreEqual(path, MissingImagePlaceholder.Write(_directory, "Files/Assets/other.png"));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
        Assert.HasCount(1, Directory.GetFiles(_directory));
    }

    [TestMethod]
    public void PlaceholderWithOtherContentIsReplaced()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "missing-image.png");
        File.WriteAllText(path, "not an image");

        Assert.AreEqual(path, MissingImagePlaceholder.Write(_directory, "Files/Assets/logo.png"));
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(path)[..4]);
        Assert.HasCount(1, Directory.GetFiles(_directory));
    }

    [TestMethod]
    [DataRow("Files/Assets/logo.svg", "missing-image.svg")]
    [DataRow("Files/Assets/logo.png", "missing-image.png")]
    public void PlaceholderThatCannotBeReplacedNowIsLeftAlone(string name, string fileName)
    {
        // A file with other content that another process holds open: this process uses a copy of its own.
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, "other content");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            string own = MissingImagePlaceholder.Write(_directory, name);

            // The copy is a file of the same kind in the same folder, in case the extension decides how it is decoded.
            Assert.AreNotEqual(path, own);
            Assert.AreEqual(_directory, Path.GetDirectoryName(own));
            Assert.AreEqual(Path.GetExtension(fileName), Path.GetExtension(own));
            CollectionAssert.AreEqual(File.ReadAllBytes(MissingImagePlaceholder.Write(Path.Combine(_directory, "reference"), name)), File.ReadAllBytes(own));
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
