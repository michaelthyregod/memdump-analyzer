using System.Buffers.Binary;
using System.IO.Compression;
using MemDumpAnalyzer.Reporting.Skins;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace MemDumpAnalyzer.Tests;

/// <summary>
/// Logos that colour their shapes through CSS classes (Illustrator exports) came out black in the PDF,
/// because QuestPDF's SVG renderer ignores &lt;style&gt;. SvgStyleInliner copies the colours onto the shapes.
/// </summary>
public class SvgStyleInlinerTests
{
    private const string Magenta = "#E6007E";
    private const string IllustratorStyle =
        "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><style type='text/css'>.st0{fill:#E6007E;}</style>" +
        "<rect class='st0' width='10' height='10'/></svg>";

    [Fact]
    public void Inline_CopiesClassFillOntoTheElement()
    {
        var result = SvgStyleInliner.Inline(IllustratorStyle);

        Assert.Contains($"fill=\"{Magenta}\"", result);
    }

    [Fact]
    public void Inline_FollowsCssPrecedence()
    {
        const string svg =
            "<svg xmlns='http://www.w3.org/2000/svg'><style>/* comment */ .a,.b{fill:#111111} .b{fill:#222222;stroke:#333333}</style>" +
            "<rect id='both' class='a b'/>" +                                // later rule wins
            "<rect id='attr' class='a' fill='#999999'/>" +                    // class rule beats a presentation attribute
            "<rect id='inline' class='a' style='fill:#444444'/>" +            // inline style beats the class rule
            "<rect id='plain' fill='#555555'/></svg>";

        var doc = System.Xml.Linq.XDocument.Parse(SvgStyleInliner.Inline(svg));
        string? Fill(string id) => doc.Descendants().Single(e => (string?)e.Attribute("id") == id).Attribute("fill")?.Value;

        Assert.Equal("#222222", Fill("both"));
        Assert.Equal("#111111", Fill("attr"));
        Assert.Null(Fill("inline"));
        Assert.Equal("#555555", Fill("plain"));
        Assert.Equal("#333333", doc.Descendants().Single(e => (string?)e.Attribute("id") == "both").Attribute("stroke")?.Value);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><rect fill='#E6007E'/></svg>")]   // no <style>: untouched
    [InlineData("not xml at all")]                                                        // unparseable: untouched
    public void Inline_LeavesOtherSvgUnchanged(string svg)
    {
        Assert.Equal(svg, SvgStyleInliner.Inline(svg));
    }

    [Fact]
    public void Inline_IgnoresDtdEntities()
    {
        // A DOCTYPE with an external entity must not be resolved (no local file reads from a logo)
        const string svg =
            "<?xml version='1.0'?><!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///c:/windows/win.ini'>]>" +
            "<svg xmlns='http://www.w3.org/2000/svg'><style>.a{fill:#E6007E}</style><rect class='a'/><text>&x;</text></svg>";

        var result = SvgStyleInliner.Inline(svg);

        Assert.DoesNotContain("[fonts]", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuestPdf_DrawsTheBrandColourOnlyAfterInlining()
    {
        // Rendered the way the PDF report draws a logo; counts pixels in the logo's magenta
        Assert.Equal(0, MagentaPixels(IllustratorStyle));                          // documents QuestPDF's behaviour
        Assert.True(MagentaPixels(SvgStyleInliner.Inline(IllustratorStyle)) > 1000);
    }

    private static int MagentaPixels(string svg)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var png = Document.Create(c => c.Page(p => { p.Size(40, 40); p.Content().Svg(svg); }))
            .GenerateImages(new ImageGenerationSettings { RasterDpi = 72 }).First();
        var rgba = DecodePng(png);
        int count = 0;
        for (int i = 0; i < rgba.Length; i += 4)
            if (Math.Abs(rgba[i] - 0xE6) < 12 && rgba[i + 1] < 20 && Math.Abs(rgba[i + 2] - 0x7E) < 12) count++;
        return count;
    }

    /// <summary>Minimal PNG decoder (8-bit RGB/RGBA, all five scanline filters) — enough for QuestPDF's output.</summary>
    private static byte[] DecodePng(byte[] png)
    {
        int w = 0, h = 0, colorType = 0, bitDepth = 0;
        using var idat = new MemoryStream();
        for (int pos = 8; pos < png.Length;)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len);
            if (type == "IHDR") { w = BinaryPrimitives.ReadInt32BigEndian(data); h = BinaryPrimitives.ReadInt32BigEndian(data[4..]); bitDepth = data[8]; colorType = data[9]; }
            else if (type == "IDAT") idat.Write(data);
            pos += 12 + len;
        }
        Assert.True(bitDepth == 8 && colorType is 2 or 6, $"Unexpected PNG format: {bitDepth}-bit, colour type {colorType}");

        int bpp = colorType == 6 ? 4 : 3, stride = w * bpp;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var src = raw.ToArray();

        var cur = new byte[stride];
        var prev = new byte[stride];
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int filter = src[y * (stride + 1)];
            Array.Copy(src, y * (stride + 1) + 1, cur, 0, stride);
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? cur[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                cur[x] = (byte)(cur[x] + filter switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => Paeth(a, b, c), _ => 0 });
            }
            for (int x = 0; x < w; x++)
            {
                for (int k = 0; k < 3; k++) rgba[(y * w + x) * 4 + k] = cur[x * bpp + k];
                rgba[(y * w + x) * 4 + 3] = bpp == 4 ? cur[x * bpp + 3] : (byte)255;
            }
            (prev, cur) = (cur, prev);
        }
        return rgba;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
