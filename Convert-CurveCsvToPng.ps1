<#
.SYNOPSIS
Reconstructs a PNG image from a SignalVision curve CSV.

.EXAMPLE
.\Convert-CurveCsvToPng.ps1 -CsvPath 'C:\Temp\CaseSummaryData _20260222 (masked)\curves_page_7_image_1_panel_18_Data_0.csv' -PngPath 'c:\temp\reconstructed.png' -LineWidth 1

.EXAMPLE
.\Convert-CurveCsvToPng.ps1 -CsvPath 'C:\Temp\CaseSummaryData _20260222 (masked)' -PngPath 'C:\Temp\Png'
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateScript({ Test-Path -LiteralPath $_ })]
    [string] $CsvPath,

    [Parameter(Position = 1)]
    [string] $PngPath,

    [ValidateRange(1, 20)]
    [int] $LineWidth = 1,

    [ValidateRange(0, 10000)]
    [int] $Padding = 0,

    [switch] $TransparentBackground
)

$ErrorActionPreference = 'Stop'

function Convert-CurveCsvFile {
param(
    [System.IO.FileInfo] $CsvFile,
    [string] $OutputPngPath
)

if ($CsvFile.Extension -ine '.csv') {
    throw "'$($CsvFile.FullName)' is not a CSV file."
}

if ([string]::IsNullOrWhiteSpace($OutputPngPath)) {
    $OutputPngPath = [System.IO.Path]::ChangeExtension($CsvFile.FullName, '.png')
}

$rows = @(Import-Csv -LiteralPath $CsvFile.FullName)
if ($rows.Count -eq 0) {
    throw "The CSV contains no curve rows."
}

$xColumns = @(
    $rows[0].PSObject.Properties.Name |
        Where-Object { $_ -ne 'Color' } |
        ForEach-Object {
            $x = 0
            if (-not [int]::TryParse($_, [ref]$x)) {
                throw "CSV column '$_' is not a valid integer X coordinate."
            }
            [pscustomobject]@{ Name = $_; X = $x }
        } |
        Sort-Object X
)

if ($xColumns.Count -eq 0) {
    throw "The CSV contains no X-coordinate columns."
}

$maxX = ($xColumns | Measure-Object -Property X -Maximum).Maximum
$maxY = -1

foreach ($row in $rows) {
    foreach ($column in $xColumns) {
        $value = $row.($column.Name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $y = 0
            if (-not [int]::TryParse($value, [ref]$y)) {
                throw "Value '$value' at X=$($column.X) is not a valid integer Y coordinate."
            }
            if ($y -lt 0) {
                throw "Negative Y coordinate $y at X=$($column.X) is not supported."
            }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

if ($maxY -lt 0) {
    throw "The CSV contains no Y-coordinate values."
}

Add-Type -AssemblyName System.Drawing

# Current CSV exports store the detected curve category in the Color column
# instead of an HTML/RGB value. Keep these colors aligned with Graph.cs, while
# continuing to support older exports containing HTML color values.
$categoryColors = @{
    Baseline = '#7fffff'
    Curve    = '#bd51a7'
    Marker   = '#00ffff'
}

$width = [int]($maxX + 1 + (2 * $Padding))
$height = [int]($maxY + 1 + (2 * $Padding))
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$pens = [System.Collections.Generic.Dictionary[string, System.Drawing.Pen]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)

try {
    if ($TransparentBackground) {
        $graphics.Clear([System.Drawing.Color]::Transparent)
    }
    else {
        $graphics.Clear([System.Drawing.Color]::Black)
    }

    # Integer-coordinate data looks closest to the source with antialiasing disabled.
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

    foreach ($row in $rows) {
        $colorValue = ([string]$row.Color).Trim()
        if ([string]::IsNullOrWhiteSpace($colorValue)) {
            throw "A curve row has no Color value."
        }

        if (-not $pens.ContainsKey($colorValue)) {
            $colorText = if ($categoryColors.ContainsKey($colorValue)) {
                $categoryColors[$colorValue]
            }
            else {
                $colorValue
            }

            try {
                $color = [System.Drawing.ColorTranslator]::FromHtml($colorText)
            }
            catch {
                throw "Color '$colorValue' is not a recognized curve category or valid HTML color."
            }
            $pens[$colorValue] = [System.Drawing.Pen]::new($color, $LineWidth)
        }
        $pen = $pens[$colorValue]
        $previousPoint = $null

        foreach ($column in $xColumns) {
            $value = $row.($column.Name)
            if ([string]::IsNullOrWhiteSpace($value)) {
                # End the current segment so missing samples remain visibly blank.
                # The next available coordinate starts a new segment rather than
                # being connected to the point before the gap.
                $previousPoint = $null
                continue
            }

            $y = [int]$value
            $point = [System.Drawing.Point]::new(
                $column.X + $Padding,
                $y + $Padding
            )

            if ($null -ne $previousPoint) {
                $graphics.DrawLine($pen, $previousPoint, $point)
            }
            else {
                # Preserve isolated samples that have no adjacent point.
                $graphics.DrawLine($pen, $point, $point)
            }

            $previousPoint = $point
        }
    }

    $fullOutputPath = [System.IO.Path]::GetFullPath($OutputPngPath)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($fullOutputPath)
    if (-not [string]::IsNullOrEmpty($outputDirectory)) {
        [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }

    $bitmap.Save($fullOutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output $fullOutputPath
}
finally {
    foreach ($pen in $pens.Values) { $pen.Dispose() }
    $graphics.Dispose()
    $bitmap.Dispose()
}
}

$csvItem = Get-Item -LiteralPath $CsvPath
if ($csvItem -is [System.IO.DirectoryInfo]) {
    $csvFiles = @(
        Get-ChildItem -LiteralPath $csvItem.FullName -File -Filter '*.csv' |
            Sort-Object Name
    )
    if ($csvFiles.Count -eq 0) {
        throw "The folder '$($csvItem.FullName)' contains no CSV files."
    }

    $outputDirectory = if ([string]::IsNullOrWhiteSpace($PngPath)) {
        $csvItem.FullName
    }
    else {
        [System.IO.Path]::GetFullPath($PngPath)
    }

    if (Test-Path -LiteralPath $outputDirectory -PathType Leaf) {
        throw "When CsvPath is a folder, PngPath must also be a folder."
    }

    foreach ($csvFile in $csvFiles) {
        $outputFileName = [System.IO.Path]::ChangeExtension($csvFile.Name, '.png')
        $outputPath = Join-Path $outputDirectory $outputFileName
        Convert-CurveCsvFile -CsvFile $csvFile -OutputPngPath $outputPath
    }
}
else {
    Convert-CurveCsvFile -CsvFile $csvItem -OutputPngPath $PngPath
}
