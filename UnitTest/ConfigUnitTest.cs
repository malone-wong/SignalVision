using System.Text.Json;
using SignalVision;
using SixLabors.ImageSharp.PixelFormats;

namespace UnitTest;

[TestClass]
[DoNotParallelize]
public sealed class ConfigUnitTest
{
    [TestMethod]
    public void Constructor_WithFilePath_LoadsConfigurationAndStoresPath()
    {
        Config config = CreateConfig("""{"PDFPath":"document.pdf"}""");

        Assert.AreEqual("document.pdf", config.PDF);
        Assert.IsTrue(Path.IsPathFullyQualified(config.ConfigPath));
    }

    [TestMethod]
    public void DefaultConstructor_LoadsConfigJsonFromCurrentDirectory()
    {
        string originalDirectory = Environment.CurrentDirectory;
        string testDirectory = CreateTemporaryDirectory();

        try
        {
            File.WriteAllText(
                Path.Combine(testDirectory, "config.json"),
                """{"PDFPath":"default.pdf"}""");
            Environment.CurrentDirectory = testDirectory;

            Config config = new();

            Assert.AreEqual("config.json", config.ConfigPath);
            Assert.AreEqual("default.pdf", config.PDF);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Constructor_WhenFileDoesNotExist_ThrowsFileNotFoundException()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.json");

        Assert.ThrowsExactly<FileNotFoundException>(() => new Config(missingPath));
    }

    [TestMethod]
    public void Constructor_WhenJsonIsMalformed_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => CreateConfig("""{"PDFPath":"""));
    }

    [TestMethod]
    public void Constructor_WithJsonComments_LoadsConfiguration()
    {
        Config config = CreateConfig("""
            {
                // The input document to process.
                "PDFPath": "C:\\reports\\case.pdf",
                /* Comments may also span multiple lines. */
                "OutputBasePath": "output"
            }
            """);

        Assert.AreEqual(@"C:\reports\case.pdf", config.PDF);
        Assert.AreEqual("output", config.OutputBasePath);
    }

    [TestMethod]
    public void PDF_WhenPresent_ReturnsConfiguredPath()
    {
        Config config = CreateConfig("""{"PDFPath":"C:\\reports\\case.pdf"}""");

        Assert.AreEqual(@"C:\reports\case.pdf", config.PDF);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"PDFPath":null}""")]
    [DataRow("null")]
    public void PDF_WhenMissingOrNull_ThrowsDescriptiveException(string json)
    {
        Config config = CreateConfig(json);

        Exception exception = Assert.ThrowsExactly<Exception>(() => _ = config.PDF);

        Assert.AreEqual(
            $"PDF path not found in {config.ConfigPath}",
            exception.Message);
    }

    [TestMethod]
    public void TitleColorTolerance_WhenPresent_ReturnsConfiguredValue()
    {
        Config config = CreateConfig(
            """{"WindowsPanel":{"TitleColorTolerance":42}}""");

        Assert.AreEqual(42, config.WindowsPanelTitleColorTolerance);
    }

    [TestMethod]
    public void OCRProvider_WhenMissing_DefaultsToMicrosoft()
    {
        Config config = CreateConfig("{}");

        Assert.AreEqual(OcrProvider.Microsoft, config.OCRProvider);
    }

    [TestMethod]
    [DataRow("Microsoft", OcrProvider.Microsoft)]
    [DataRow("MS", OcrProvider.Microsoft)]
    [DataRow("Paddle", OcrProvider.Paddle)]
    public void OCRProvider_WhenConfigured_ReturnsSelectedProvider(
        string value,
        OcrProvider expected)
    {
        Config config = CreateConfig(JsonSerializer.Serialize(new
        {
            OCR = new { Provider = value }
        }));

        Assert.AreEqual(expected, config.OCRProvider);
    }

    [TestMethod]
    public void OCRProvider_WhenUnsupported_ThrowsDescriptiveException()
    {
        Config config = CreateConfig("""{"OCR":{"Provider":"Unknown"}}""");

        Exception exception = Assert.ThrowsExactly<Exception>(
            () => _ = config.OCRProvider);

        StringAssert.Contains(exception.Message, "Unsupported OCR provider 'Unknown'");
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"WindowsPanel":{}}""")]
    [DataRow("""{"WindowsPanel":{"TitleColorTolerance":null}}""")]
    [DataRow("null")]
    public void TitleColorTolerance_WhenMissingOrNull_ThrowsDescriptiveException(
        string json)
    {
        Config config = CreateConfig(json);

        Exception exception = Assert.ThrowsExactly<Exception>(
            () => _ = config.WindowsPanelTitleColorTolerance);

        Assert.AreEqual(
            $"TitleColorTolerance not found in {config.ConfigPath}",
            exception.Message);
    }

    private static Config CreateConfig(string json)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        try
        {
            return new Config(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ConfigUnitTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
