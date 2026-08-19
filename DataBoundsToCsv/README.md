# DataBoundsToCsv

This console project regenerates SignalVision curve CSV files from corrected
databounds PNG images. It calls the same `DataBoundsCsvGenerator` used by the
main SignalVision PDF workflow.

The input argument is a directory. The project reads only top-level `.png`
files whose names start with `databounds_`, for example:

```text
databounds_page_7_image_1_panel_18_Data_0.png
```

The corresponding output is:

```text
curves_page_7_image_1_panel_18_Data_0.csv
```

Run it for every matching PNG directly inside a folder:

```powershell
dotnet run --project .\DataBoundsToCsv -- "C:\temp\CaseSummaryData"
```

Pass a second argument to write all CSV files to another folder. Otherwise each
CSV is written beside its source image. Existing CSV files are replaced.
