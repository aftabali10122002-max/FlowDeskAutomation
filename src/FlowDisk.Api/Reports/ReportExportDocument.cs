namespace FlowDisk.Api.Reports;

public sealed record ReportExportDocument(
    string Title,
    string FileNamePrefix,
    IReadOnlyList<ReportExportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    string Summary);

public sealed record ReportExportColumn(string Header, double Width);

public sealed record ReportExportFile(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed class ReportExportLimitExceededException(
    int maximumRows,
    int matchingRows)
    : Exception(
        $"This export matches {matchingRows:N0} rows. The configured limit is {maximumRows:N0} rows. Narrow the filters and try again.")
{
    public int MaximumRows { get; } = maximumRows;
    public int MatchingRows { get; } = matchingRows;
}

public sealed class ReportExportDataChangedException()
    : Exception(
        "Report data changed while the export was being prepared. Please try again.")
{
}
