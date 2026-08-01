using System.Text.Json.Nodes;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

namespace SignalVision;

public class Config
{
    private readonly JsonNode? _root;

    public string ConfigPath { get; }

    // Constructor chaining: The default constructor points to the main one
    public Config() : this("config.json") { }

    public Config(string filePath)
    {
        ConfigPath = filePath;
        _root = JsonNode.Parse(
            File.ReadAllText(filePath),
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip
            });
    }

    public string PDF => _root?["PDFPath"]?.GetValue<string>()
        ?? throw new Exception($"PDF path not found in {ConfigPath}");

    public string OutputBasePath => _root?["OutputBasePath"]?.GetValue<string>()
        ?? throw new Exception($"OutputBasePath not found in {ConfigPath}");

    public bool DeleteExistingOutputFolder => _root?["DeleteExistingOutputFolder"]?.GetValue<bool>()
        ?? throw new Exception($"DeleteExistingOutputFolder not found in {ConfigPath}");

    public bool JPEGVerticalFlip => _root?["JPEGVerticalFlip"]?.GetValue<bool>()
        ?? throw new Exception($"JPEGVerticalFlip not found in {ConfigPath}");

    public List<Rgba32> WindowsPanelActiveTitleColors =>
        _root?["WindowsPanel"]?["ActiveTitleColor"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? throw new Exception("Color value is null"))
            .Select(Rgba32.ParseHex)
            .ToList() ?? [];

    public List<Rgba32> WindowsPanelInactiveTitleColors =>
        _root?["WindowsPanel"]?["InactiveTitleColor"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? throw new Exception("Color value is null"))
            .Select(Rgba32.ParseHex)
            .ToList() ?? [];

    public int WindowsPanelTitleColorTolerance =>
        _root?["WindowsPanel"]?["TitleColorTolerance"]?.GetValue<int>()
            ?? throw new Exception($"TitleColorTolerance not found in {ConfigPath}");

    public float WindowsPanelTitleDensity =>
        _root?["WindowsPanel"]?["TitleDensity"]?.GetValue<float>()
            ?? throw new Exception($"TitleDensity not found in {ConfigPath}");

    public int WindowsPanelMinimumHeight =>
        _root?["WindowsPanel"]?["MinimumHeight"]?.GetValue<int>()
            ?? throw new Exception($"MinimumHeight not found in {ConfigPath}");

    public int WindowsPanelMinimumWidth =>
        _root?["WindowsPanel"]?["MinimumWidth"]?.GetValue<int>()
            ?? throw new Exception($"MinimumWidth not found in {ConfigPath}");  

    public float WindowsPanelBlurRadius =>
        _root?["WindowsPanel"]?["BlurRadius"]?.GetValue<float>()
            ?? throw new Exception($"BlurRadius not found in {ConfigPath}");

    public List<Rgba32> WindowsPanelGraphTitleColor=>
         _root?["WindowsPanel"]?["GraphTitleColor"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? throw new Exception("Color value is null"))
            .Select(Rgba32.ParseHex)
            .ToList() ?? [];

    public List<string> TargetGraphTitles =>
        _root?["TargetGraphTitles"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? throw new Exception("TargetGraphTitles value is null"))
            .ToList() ?? [];

    public List<Rgba32> WindowsPanelGraphSeparatorColor =>
         _root?["WindowsPanel"]?["GraphSeparatorColor"]?.AsArray()
            .Select(node => node?.GetValue<string>() ?? throw new Exception("Color value is null"))
            .Select(Rgba32.ParseHex)
            .ToList() ?? [];

    public int WindowsPanelGraphSeparatorMinWidth =>
        _root?["WindowsPanel"]?["GraphSeparatorMinWidth"]?.GetValue<int>()
            ?? throw new Exception($"GraphSeparatorMinWidth not found in {ConfigPath}");

    public int OCRScale =>
        _root?["OCR"]?["Scale"]?.GetValue<int>()
            ?? throw new Exception($"Scale not found in {ConfigPath}");

    public int OCRMaxPreparedDimension =>
        _root?["OCR"]?["MaxPreparedDimension"]?.GetValue<int>()
            ?? throw new Exception($"MaxPreparedDimension not found in {ConfigPath}");

    public OcrProvider OCRProvider
    {
        get
        {
            string? value = _root?["OCR"]?["Provider"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
                return OcrProvider.Microsoft;

            if (string.Equals(value, "MS", StringComparison.OrdinalIgnoreCase))
                return OcrProvider.Microsoft;

            return Enum.TryParse(value, ignoreCase: true, out OcrProvider provider)
                ? provider
                : throw new Exception(
                    $"Unsupported OCR provider '{value}' in {ConfigPath}. " +
                    $"Use '{OcrProvider.Microsoft}' or '{OcrProvider.Paddle}'.");
        }
    }
}
