namespace DataBoundsToCsv;

internal sealed record DataBoundsFileName(string CsvFileName)
{
    private const string InputPrefix = "databounds_";

    public static bool TryParse(string path, out DataBoundsFileName? result)
    {
        string fileName = Path.GetFileName(path);
        if (!fileName.StartsWith(InputPrefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase))
        {
            result = null;
            return false;
        }

        string suffix = Path.GetFileNameWithoutExtension(fileName)[InputPrefix.Length..];
        result = new DataBoundsFileName($"curves_{suffix}.csv");
        return true;
    }
}
