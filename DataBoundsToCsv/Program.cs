using DataBoundsToCsv;
using SignalVision;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

const int OcrScale = 5;
const int OcrMaximumPreparedDimension = 2400;

if (args.Any(argument => argument is "-h" or "--help" or "/?"))
{
    PrintUsage();
    return 0;
}

bool detectLabels = true;
List<string> positional = [];
foreach (string argument in args)
{
    if (string.Equals(argument, "--no-labels", StringComparison.OrdinalIgnoreCase))
    {
        detectLabels = false;
        continue;
    }

    if (argument.StartsWith('-'))
    {
        Console.Error.WriteLine($"Unknown option: {argument}");
        PrintUsage();
        return 2;
    }

    positional.Add(argument);
}

if (positional.Count > 2)
{
    Console.Error.WriteLine("Too many arguments.");
    PrintUsage();
    return 2;
}

string inputPath = Path.GetFullPath(positional.Count > 0 ? positional[0] : Environment.CurrentDirectory);
string? outputFolder = positional.Count > 1 ? Path.GetFullPath(positional[1]) : null;

IReadOnlyList<string> inputImages;
if (File.Exists(inputPath))
{
    inputImages = [inputPath];
}
else if (Directory.Exists(inputPath))
{
    inputImages = Directory
        .EnumerateFiles(inputPath, "*.png", SearchOption.TopDirectoryOnly)
        .Where(path => DataBoundsFileName.TryParse(path, out _))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
else
{
    Console.Error.WriteLine($"Input path does not exist: {inputPath}");
    return 2;
}

if (inputImages.Count == 0)
{
    Console.Error.WriteLine(
        $"No databounds PNG files were found in: {inputPath}{Environment.NewLine}" +
        "Expected names such as databounds_page_7_image_1_panel_18_Data_0.png.");
    return 1;
}

// SignalVision's config selects Paddle, and Paddle is the only provider that
// needs no per-run configuration, so the standalone tool uses it directly.
Logger logger = new(
    "DataBoundsToCsv",
    Path.Combine(outputFolder ?? Path.GetDirectoryName(inputImages[0])!, "DataBoundsToCsv.log"));

int succeeded = 0;
int failed = 0;
foreach (string imagePath in inputImages)
{
    if (!DataBoundsFileName.TryParse(imagePath, out DataBoundsFileName? fileName) || fileName is null)
    {
        Console.Error.WriteLine($"Skipped file with an unsupported name: {imagePath}");
        failed++;
        continue;
    }

    string destinationFolder = outputFolder ?? Path.GetDirectoryName(imagePath)!;
    string csvPath = Path.Combine(destinationFolder, fileName.CsvFileName);

    try
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
        IReadOnlyList<OcrTextRegion>? textRegions = detectLabels
            ? PaddleOCRHelper.DetectTextRegions(
                image,
                OcrScale,
                OcrMaximumPreparedDimension,
                logger)
            : null;

        List<Curve> curves = DataBoundsCsvGenerator.Generate(image, csvPath, textRegions);
        Console.WriteLine($"Created {csvPath} ({curves.Count} curves)");
        succeeded++;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Failed {imagePath}: {exception.Message}");
        failed++;
    }
}

Console.WriteLine($"Complete: {succeeded} succeeded, {failed} failed.");
return failed == 0 ? 0 : 1;

static void PrintUsage()
{
    Console.WriteLine(
        "Usage: DataBoundsToCsv [input-image-or-folder] [output-folder] [--no-labels]\n" +
        "\n" +
        "Reads databounds PNG images and creates curves CSV files using the same\n" +
        "curve extraction implementation as SignalVision. If no input is supplied,\n" +
        "the current folder is processed. Existing CSV files are replaced.\n" +
        "\n" +
        "  --no-labels  Skip OCR label detection. Labels printed inside the plot are\n" +
        "               drawn in the same color as the baseline trace, so skipping\n" +
        "               detection can report a label as an extra curve.\n" +
        "\n" +
        "Example:\n" +
        "  DataBoundsToCsv C:\\temp\\CaseSummaryData\\databounds_page_7_image_1_panel_18_Data_0.png\n" +
        "  DataBoundsToCsv C:\\temp\\CaseSummaryData C:\\temp\\CorrectedCsv");
}
