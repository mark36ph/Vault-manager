using System.Globalization;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace FactVaultManager.Desktop;

public enum SimpleIconColourMode
{
    Brand,
    Black,
}

public sealed record SimpleIconDownloadResult(bool Found, string ImagePath = "", string Title = "");

public static class SimpleIconsCatalog
{
    public static string CreateSlug(string brand)
    {
        var value = (brand ?? "").Trim().Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
                slug.Append(char.ToLowerInvariant(character));
        }

        return slug.ToString();
    }

    public static Uri BuildIconUri(string brand, SimpleIconColourMode colourMode)
    {
        var slug = CreateSlug(brand);
        if (slug.Length == 0)
            throw new ArgumentException("Enter a brand name before finding its icon.", nameof(brand));

        var suffix = colourMode == SimpleIconColourMode.Black ? "/000000" : "";
        return new Uri($"https://cdn.simpleicons.org/{Uri.EscapeDataString(slug)}{suffix}");
    }
}

public static class SimpleIconsService
{
    private const int OutputSize = 1024;
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 1_000_000,
    };

    public static async Task<SimpleIconDownloadResult> DownloadPngAsync(
        string brand,
        SimpleIconColourMode colourMode,
        CancellationToken cancellationToken = default)
    {
        var slug = SimpleIconsCatalog.CreateSlug(brand);
        var uri = SimpleIconsCatalog.BuildIconUri(brand, colourMode);
        using var response = await Client.GetAsync(uri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new SimpleIconDownloadResult(false);
        response.EnsureSuccessStatusCode();

        var svg = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = ParseSvg(svg, brand, colourMode);
        if (!string.Equals(
                SimpleIconsCatalog.CreateSlug(parsed.Title),
                slug,
                StringComparison.Ordinal))
        {
            return new SimpleIconDownloadResult(false);
        }

        var cacheDirectory = Path.Combine(Path.GetTempPath(), "FactVaultManager", "simple-icons");
        Directory.CreateDirectory(cacheDirectory);
        var colourName = colourMode == SimpleIconColourMode.Black ? "black" : "brand";
        var destination = Path.Combine(cacheDirectory, $"{slug}-{colourName}.png");
        RenderPng(parsed, destination);
        return new SimpleIconDownloadResult(true, destination, parsed.Title);
    }

    private static ParsedSimpleIcon ParseSvg(string svg, string brand, SimpleIconColourMode colourMode)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_000_000,
        };
        using var text = new StringReader(svg);
        using var reader = XmlReader.Create(text, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("Simple Icons returned an empty SVG document.");
        var title = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "title")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = brand.Trim();

        var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
        var paths = root.Descendants()
            .Where(element => element.Name.LocalName == "path")
            .Select(element => element.Attribute("d")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Geometry.Parse(value!))
            .ToArray();
        if (paths.Length == 0)
            throw new InvalidDataException("Simple Icons returned an SVG without a usable logo path.");

        var colour = colourMode == SimpleIconColourMode.Black
            ? Colors.Black
            : ParseHexColour(
                root.Attribute("fill")?.Value ??
                root.Descendants().FirstOrDefault(element => element.Name.LocalName == "path")?.Attribute("fill")?.Value);
        return new ParsedSimpleIcon(title, viewBox, paths, colour);
    }

    private static Rect ParseViewBox(string? value)
    {
        var parts = (value ?? "")
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Simple Icons returned an SVG with an invalid viewBox.");
        }

        return new Rect(x, y, width, height);
    }

    private static Color ParseHexColour(string? value)
    {
        var hex = (value ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
            return Colors.Black;
        return Color.FromRgb((byte)(number >> 16), (byte)(number >> 8), (byte)number);
    }

    private static void RenderPng(ParsedSimpleIcon icon, string destination)
    {
        const double padding = 90;
        var scale = Math.Min(
            (OutputSize - padding * 2) / icon.ViewBox.Width,
            (OutputSize - padding * 2) / icon.ViewBox.Height);
        var offsetX = (OutputSize - icon.ViewBox.Width * scale) / 2;
        var offsetY = (OutputSize - icon.ViewBox.Height * scale) / 2;
        var transform = new TransformGroup();
        transform.Children.Add(new TranslateTransform(-icon.ViewBox.X, -icon.ViewBox.Y));
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(offsetX, offsetY));

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(transform);
            var brush = new SolidColorBrush(icon.Colour);
            foreach (var path in icon.Paths)
                drawing.DrawGeometry(brush, null, path);
            drawing.Pop();
        }

        var bitmap = new RenderTargetBitmap(OutputSize, OutputSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var output = File.Create(temporary))
                encoder.Save(output);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private sealed record ParsedSimpleIcon(string Title, Rect ViewBox, Geometry[] Paths, Color Colour);
}
