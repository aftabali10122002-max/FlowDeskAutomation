using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FlowDesk.Application.DTOs;
using FlowDesk.Application.Services;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace FlowDisk.Api.Reports;

public sealed class ReportsExportService(
    ReportsService reportsService,
    IConfiguration configuration,
    ILogger<ReportsExportService> logger)
{
    private const int BatchSize = 100;
    private const int DefaultMaximumRows = 10_000;
    private const int AbsoluteMaximumRows = 50_000;
    private const string ExcelContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfContentType = "application/pdf";

    private readonly int _maximumRows = GetMaximumRows(configuration);

    static ReportsExportService()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public async Task<ReportExportFile> CreateAsync(
        string report,
        string format,
        int actorUserId,
        string role,
        ReportQueryInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reportDocument = report switch
        {
            "requests" => await CreateRequestsDocumentAsync(
                actorUserId, role, input, cancellationToken),
            "approval-history" => await CreateApprovalHistoryDocumentAsync(
                actorUserId, role, input, cancellationToken),
            "workflow-performance" => await CreateWorkflowDocumentAsync(
                actorUserId, role, input, cancellationToken),
            "leave" => await CreateLeaveDocumentAsync(
                actorUserId, role, input, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(report))
        };
        var generatedAtUtc = DateTime.UtcNow;
        var fileExtension = format.ToLowerInvariant();
        var fileName = $"FlowDesk_{reportDocument.FileNamePrefix}_{generatedAtUtc:yyyyMMdd-HHmmss}.{fileExtension}";
        var filters = FormatFilters(input, role);
        var content = fileExtension switch
        {
            "xlsx" => CreateExcel(reportDocument, generatedAtUtc, filters),
            "pdf" => CreatePdf(reportDocument, generatedAtUtc, filters),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        return new ReportExportFile(
            content,
            fileExtension == "xlsx" ? ExcelContentType : PdfContentType,
            fileName);
    }

    private async Task<ReportExportDocument> CreateRequestsDocumentAsync(
        int actorUserId,
        string role,
        ReportQueryInput input,
        CancellationToken cancellationToken)
    {
        var rows = await GetAllRowsAsync(
            input,
            page => reportsService.GetRequestsAsync(actorUserId, role, page),
            cancellationToken);
        var items = rows.Select(row => new object?[]
        {
            row.RequestId,
            row.Title,
            row.RequestTypeName ?? row.RequestTypeCode ?? "Unspecified",
            row.Status,
            row.Priority ?? "Unspecified",
            row.EmployeeName ?? "Legacy request",
            row.DepartmentName ?? "Unassigned",
            row.CreatedAtUtc
        }).ToArray();

        return new ReportExportDocument(
            "Request Reports",
            "Requests_Report",
            [
                new("Request ID", 12),
                new("Request", 30),
                new("Type", 22),
                new("Status", 14),
                new("Priority", 14),
                new("Employee", 24),
                new("Department", 22),
                new("Submitted", 21)
            ],
            items,
            SummarizeStatuses(rows.Select(row => row.Status), rows.Count));
    }

    private async Task<ReportExportDocument>
        CreateApprovalHistoryDocumentAsync(
            int actorUserId,
            string role,
            ReportQueryInput input,
            CancellationToken cancellationToken)
    {
        var rows = await GetAllRowsAsync(
            input,
            page => reportsService.GetApprovalHistoryAsync(
                actorUserId, role, page),
            cancellationToken);
        var items = rows.Select(row => new object?[]
        {
            row.RequestId,
            row.RequestTitle,
            row.Decision,
            row.ManagerName,
            row.StageSequence is int sequence
                ? $"Stage {sequence}"
                : "Manual",
            row.EmployeeName ?? "Legacy request",
            row.DepartmentName ?? "Unassigned",
            row.DecidedAtUtc
        }).ToArray();

        return new ReportExportDocument(
            "Approval History",
            "Approval_History",
            [
                new("Request ID", 12),
                new("Request", 30),
                new("Decision", 14),
                new("Manager", 24),
                new("Stage", 14),
                new("Employee", 24),
                new("Department", 22),
                new("Decided", 21)
            ],
            items,
            SummarizeStatuses(rows.Select(row => row.Decision), rows.Count));
    }

    private async Task<ReportExportDocument> CreateWorkflowDocumentAsync(
        int actorUserId,
        string role,
        ReportQueryInput input,
        CancellationToken cancellationToken)
    {
        var rows = await GetAllRowsAsync(
            input,
            page => reportsService.GetWorkflowPerformanceAsync(
                actorUserId, role, page),
            cancellationToken);
        var elapsed = rows
            .Where(row => row.ElapsedHours.HasValue)
            .Select(row => row.ElapsedHours!.Value)
            .ToArray();
        var items = rows.Select(row => new object?[]
        {
            row.RequestId,
            row.RequestTitle,
            row.WorkflowName,
            row.WorkflowVersion,
            row.WorkflowStatus,
            row.RequestStatus,
            row.StageCount,
            row.ApprovedStageCount,
            row.RejectedStageCount,
            row.ActiveStageCount,
            row.StartedAtUtc,
            row.CompletedAtUtc,
            row.ElapsedHours,
            row.EmployeeName ?? "Unspecified",
            row.DepartmentName ?? "Unassigned"
        }).ToArray();
        var averageElapsed = elapsed.Length == 0
            ? "unavailable"
            : $"{elapsed.Average().ToString("N1", CultureInfo.InvariantCulture)} hours";
        var summary =
            $"{rows.Count:N0} workflow instances; {rows.Count(row => row.CompletedAtUtc.HasValue):N0} completed; average completed duration: {averageElapsed}.";

        return new ReportExportDocument(
            "Workflow Performance",
            "Workflow_Performance",
            [
                new("Request ID", 11),
                new("Request", 26),
                new("Workflow", 22),
                new("Version", 10),
                new("Workflow State", 15),
                new("Request Status", 15),
                new("Stages", 10),
                new("Approved", 11),
                new("Rejected", 11),
                new("Active", 10),
                new("Started", 20),
                new("Completed", 20),
                new("Elapsed Hours", 14),
                new("Employee", 22),
                new("Department", 20)
            ],
            items,
            summary);
    }

    private async Task<ReportExportDocument> CreateLeaveDocumentAsync(
        int actorUserId,
        string role,
        ReportQueryInput input,
        CancellationToken cancellationToken)
    {
        var rows = await GetAllRowsAsync(
            input,
            page => reportsService.GetLeaveReportAsync(actorUserId, role, page),
            cancellationToken);
        var requestedUnits = rows.Sum(row =>
            row.CalculatedLeaveUnits ?? row.TotalLeaveDays);
        var items = rows.Select(row => new object?[]
        {
            row.RequestId,
            row.EmployeeName,
            row.DepartmentName ?? "Unassigned",
            row.LeaveType,
            row.StartDate,
            row.EndDate,
            row.CalculatedLeaveUnits ?? row.TotalLeaveDays,
            row.RequestStatus,
            row.Used,
            row.Available
        }).ToArray();

        return new ReportExportDocument(
            "Leave Reports",
            "Leave_Report",
            [
                new("Request ID", 12),
                new("Employee", 24),
                new("Department", 22),
                new("Leave Type", 20),
                new("Start Date", 16),
                new("End Date", 16),
                new("Requested Units", 16),
                new("Status", 14),
                new("Used", 14),
                new("Available", 14)
            ],
            items,
            $"{rows.Count:N0} leave requests; {requestedUnits.ToString("N2", CultureInfo.InvariantCulture)} requested leave units.");
    }

    private async Task<IReadOnlyList<T>> GetAllRowsAsync<T>(
        ReportQueryInput input,
        Func<ReportQueryInput, Task<ReportPage<T>>> getPage,
        CancellationToken cancellationToken)
    {
        var pageNumber = 1;
        var firstPage = await getPage(input with
        {
            Page = pageNumber,
            PageSize = BatchSize
        });
        if (firstPage.TotalCount > _maximumRows)
        {
            throw new ReportExportLimitExceededException(
                _maximumRows,
                firstPage.TotalCount);
        }

        var rows = new List<T>(firstPage.TotalCount);
        rows.AddRange(firstPage.Items);
        while (rows.Count < firstPage.TotalCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageNumber++;
            var nextPage = await getPage(input with
            {
                Page = pageNumber,
                PageSize = BatchSize
            });
            if (nextPage.TotalCount != firstPage.TotalCount ||
                nextPage.Items.Count == 0)
            {
                var exception = new ReportExportDataChangedException(
                    pageNumber,
                    firstPage.TotalCount,
                    nextPage.TotalCount,
                    rows.Count,
                    nextPage.Items.Count);
                logger.LogWarning(
                    "Report export pagination changed while collecting rows. " +
                    "Page {PageNumber}; expected count {ExpectedTotalCount}; " +
                    "actual count {ActualTotalCount}; collected rows " +
                    "{CollectedRowCount}; page rows {ReceivedPageRowCount}.",
                    exception.PageNumber,
                    exception.ExpectedTotalCount,
                    exception.ActualTotalCount,
                    exception.CollectedRowCount,
                    exception.ReceivedPageRowCount);
                throw exception;
            }

            rows.AddRange(nextPage.Items);
        }

        return rows;
    }

    private static byte[] CreateExcel(
        ReportExportDocument report,
        DateTime generatedAtUtc,
        string filters)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(
            report.Title.Length > 31
                ? report.Title[..31]
                : report.Title);
        var lastColumn = report.Columns.Count;
        const int headerRow = 6;

        worksheet.Range(1, 1, 1, lastColumn).Merge();
        worksheet.Cell(1, 1).Value = $"FlowDesk | {report.Title}";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 18;
        worksheet.Cell(1, 1).Style.Font.FontColor =
            XLColor.FromHtml("#0F766E");
        worksheet.Row(1).Height = 30;

        worksheet.Range(2, 1, 2, lastColumn).Merge();
        worksheet.Cell(2, 1).Value =
            $"Generated: {generatedAtUtc:yyyy-MM-dd HH:mm} UTC";
        worksheet.Cell(2, 1).Style.Font.FontColor =
            XLColor.FromHtml("#526574");

        worksheet.Range(3, 1, 3, lastColumn).Merge();
        worksheet.Cell(3, 1).Value = $"Summary: {report.Summary}";

        worksheet.Range(4, 1, 4, lastColumn).Merge();
        worksheet.Cell(4, 1).Value = $"Applied filters: {filters}";
        worksheet.Cell(4, 1).Style.Alignment.WrapText = true;
        worksheet.Row(4).Height = 30;

        for (var index = 0; index < lastColumn; index++)
        {
            var cell = worksheet.Cell(headerRow, index + 1);
            cell.Value = report.Columns[index].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
            cell.Style.Alignment.WrapText = true;
            worksheet.Column(index + 1).Width =
                Math.Clamp(report.Columns[index].Width, 10, 42);
        }

        var firstDataRow = headerRow + 1;
        for (var rowIndex = 0; rowIndex < report.Rows.Count; rowIndex++)
        {
            var excelRow = firstDataRow + rowIndex;
            for (var columnIndex = 0; columnIndex < lastColumn; columnIndex++)
            {
                var cell = worksheet.Cell(excelRow, columnIndex + 1);
                WriteExcelValue(
                    cell,
                    report.Rows[rowIndex][columnIndex]);
                if (report.Rows[rowIndex][columnIndex] is DateTime)
                {
                    cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                }
                else if (report.Rows[rowIndex][columnIndex] is DateOnly)
                {
                    cell.Style.DateFormat.Format = "yyyy-mm-dd";
                }
                else if (report.Rows[rowIndex][columnIndex] is decimal or double)
                {
                    cell.Style.NumberFormat.Format = "#,##0.00;[Red](#,##0.00);-";
                }
            }

            if (rowIndex % 2 == 1)
            {
                worksheet.Range(
                    excelRow,
                    1,
                    excelRow,
                    lastColumn).Style.Fill.BackgroundColor =
                    XLColor.FromHtml("#F1F8F7");
            }
        }

        var lastDataRow = Math.Max(headerRow, firstDataRow + report.Rows.Count - 1);
        worksheet.Range(headerRow, 1, lastDataRow, lastColumn)
            .SetAutoFilter();
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Range(
            firstDataRow,
            1,
            Math.Max(firstDataRow, lastDataRow),
            lastColumn).Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;
        worksheet.PageSetup.PagesWide = 1;
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.Margins.Top = 0.5;
        worksheet.PageSetup.Margins.Bottom = 0.5;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteExcelValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Clear(XLClearOptions.Contents);
                break;
            case string text:
                cell.Value = text;
                break;
            case DateTime dateTime:
                cell.Value = dateTime;
                break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                break;
            case decimal number:
                cell.Value = number;
                break;
            case double number:
                cell.Value = number;
                break;
            case int number:
                cell.Value = number;
                break;
            case long number:
                cell.Value = number;
                break;
            default:
                cell.Value = Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }
    }

    private static byte[] CreatePdf(
        ReportExportDocument report,
        DateTime generatedAtUtc,
        string filters)
    {
        using var document = new PdfDocument();
        document.Info.Title = $"FlowDesk - {report.Title}";
        document.Info.Subject = report.Summary;

        var regularFont = new XFont("Arial", 8, XFontStyleEx.Regular);
        var headerFont = new XFont("Arial", 8, XFontStyleEx.Bold);
        var titleFont = new XFont("Arial", 17, XFontStyleEx.Bold);
        var brandFont = new XFont("Arial", 11, XFontStyleEx.Bold);
        var metadataFont = new XFont("Arial", 8, XFontStyleEx.Regular);
        var pages = new List<PdfPage>();
        var rowIndex = 0;
        var continuation = false;

        while (rowIndex < report.Rows.Count || !continuation)
        {
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A3;
            page.Orientation = PdfSharp.PageOrientation.Landscape;
            pages.Add(page);
            using var graphics = XGraphics.FromPdfPage(page);
            var tableY = DrawPdfPageHeader(
                graphics,
                report,
                generatedAtUtc,
                filters,
                brandFont,
                titleFont,
                metadataFont,
                continuation);
            DrawPdfTableHeader(
                graphics,
                report.Columns,
                headerFont,
                tableY,
                out var widths,
                out var headerHeight);
            var y = tableY + headerHeight;
            var bottom = page.Height.Point - 42;

            while (rowIndex < report.Rows.Count)
            {
                var row = report.Rows[rowIndex];
                var lines = row.Select((value, index) =>
                    WrapPdfText(
                        FormatPdfValue(value),
                        regularFont,
                        graphics,
                        Math.Max(30, widths[index] - 12)))
                    .ToArray();
                var lineCount = Math.Max(1, lines.Max(item => item.Count));
                var rowHeight = Math.Max(25, lineCount * 10 + 8);
                if (y + rowHeight > bottom)
                {
                    break;
                }

                DrawPdfRow(
                    graphics,
                    lines,
                    widths,
                    regularFont,
                    rowIndex,
                    y,
                    rowHeight);
                y += rowHeight;
                rowIndex++;
            }

            if (report.Rows.Count == 0)
            {
                graphics.DrawString(
                    "No matching records.",
                    regularFont,
                    XBrushes.Gray,
                    new XRect(28, y + 12, page.Width.Point - 56, 20),
                    XStringFormats.TopLeft);
            }

            continuation = true;
        }

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            using var graphics = XGraphics.FromPdfPage(
                pages[pageIndex],
                XGraphicsPdfPageOptions.Append);
            graphics.DrawLine(
                new XPen(XColor.FromArgb(218, 228, 228), 0.6),
                28,
                pages[pageIndex].Height.Point - 34,
                pages[pageIndex].Width.Point - 28,
                pages[pageIndex].Height.Point - 34);
            graphics.DrawString(
                $"FlowDesk | Page {pageIndex + 1} of {pages.Count}",
                metadataFont,
                XBrushes.Gray,
                new XRect(
                    28,
                    pages[pageIndex].Height.Point - 28,
                    pages[pageIndex].Width.Point - 56,
                    14),
                XStringFormats.TopRight);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static double DrawPdfPageHeader(
        XGraphics graphics,
        ReportExportDocument report,
        DateTime generatedAtUtc,
        string filters,
        XFont brandFont,
        XFont titleFont,
        XFont metadataFont,
        bool continuation)
    {
        const double margin = 28;
        var width = graphics.PageSize.Width;
        graphics.DrawString(
            "FlowDesk",
            brandFont,
            XBrushes.DarkSlateGray,
            margin,
            28);
        graphics.DrawString(
            continuation ? $"{report.Title} (continued)" : report.Title,
            titleFont,
            XBrushes.DarkSlateGray,
            margin,
            53);
        graphics.DrawString(
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC",
            metadataFont,
            XBrushes.Gray,
            margin,
            70);
        graphics.DrawString(
            $"Summary: {report.Summary}",
            metadataFont,
            XBrushes.DarkSlateGray,
            margin,
            86);

        var filterFont = new XFont("Arial", 7.5, XFontStyleEx.Regular);
        var filterLines = WrapPdfText(
            $"Applied filters: {filters}",
            filterFont,
            graphics,
            width - margin * 2 - 18);
        var filterHeight = Math.Max(25, filterLines.Count * 10 + 8);
        graphics.DrawRoundedRectangle(
            new XPen(XColor.FromArgb(218, 232, 230), 0.6),
            new XSolidBrush(XColor.FromArgb(242, 249, 248)),
            margin,
            96,
            width - margin * 2,
            filterHeight,
            5,
            5);
        for (var index = 0; index < filterLines.Count; index++)
        {
            graphics.DrawString(
                filterLines[index],
                filterFont,
                XBrushes.DarkSlateGray,
                margin + 9,
                108 + index * 10);
        }

        return 104 + filterHeight;
    }

    private static void DrawPdfTableHeader(
        XGraphics graphics,
        IReadOnlyList<ReportExportColumn> columns,
        XFont font,
        double y,
        out double[] widths,
        out double headerHeight)
    {
        const double margin = 28;
        var tableWidth = graphics.PageSize.Width - margin * 2;
        var totalWeight = columns.Sum(column => column.Width);
        widths = columns
            .Select(column => tableWidth * column.Width / totalWeight)
            .ToArray();
        headerHeight = 29;
        var x = margin;
        for (var index = 0; index < columns.Count; index++)
        {
            var rect = new XRect(x, y, widths[index], headerHeight);
            graphics.DrawRectangle(
                new XSolidBrush(XColor.FromArgb(15, 118, 110)),
                rect);
            var lines = WrapPdfText(
                columns[index].Header,
                font,
                graphics,
                widths[index] - 8);
            for (var line = 0; line < Math.Min(lines.Count, 2); line++)
            {
                graphics.DrawString(
                    lines[line],
                    font,
                    XBrushes.White,
                    x + 4,
                    y + 10 + line * 9);
            }

            x += widths[index];
        }
    }

    private static void DrawPdfRow(
        XGraphics graphics,
        IReadOnlyList<IReadOnlyList<string>> lines,
        IReadOnlyList<double> widths,
        XFont font,
        int rowIndex,
        double y,
        double height)
    {
        const double margin = 28;
        var background = rowIndex % 2 == 0
            ? XColor.FromArgb(255, 255, 255)
            : XColor.FromArgb(241, 248, 247);
        var x = margin;
        for (var index = 0; index < widths.Count; index++)
        {
            var rect = new XRect(x, y, widths[index], height);
            graphics.DrawRectangle(new XSolidBrush(background), rect);
            graphics.DrawRectangle(
                new XPen(XColor.FromArgb(224, 232, 232), 0.4),
                rect);
            for (var line = 0; line < lines[index].Count; line++)
            {
                graphics.DrawString(
                    lines[index][line],
                    font,
                    XBrushes.DarkSlateGray,
                    x + 4,
                    y + 12 + line * 10);
            }

            x += widths[index];
        }
    }

    private static IReadOnlyList<string> WrapPdfText(
        string value,
        XFont font,
        XGraphics graphics,
        double maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in value.Replace("\r", string.Empty)
                     .Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(
                         ' ',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0
                    ? word
                    : $"{line} {word}";
                if (line.Length > 0 &&
                    graphics.MeasureString(candidate, font).Width > maxWidth)
                {
                    result.Add(line.ToString());
                    line.Clear();
                }

                if (graphics.MeasureString(word, font).Width <= maxWidth)
                {
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                    continue;
                }

                foreach (var part in SplitLongWord(
                             word,
                             font,
                             graphics,
                             maxWidth))
                {
                    if (line.Length > 0)
                    {
                        result.Add(line.ToString());
                        line.Clear();
                    }

                    result.Add(part);
                }
            }

            if (line.Length > 0 || paragraph.Length == 0)
            {
                result.Add(line.ToString());
            }
        }

        return result.Count == 0 ? [string.Empty] : result;
    }

    private static IEnumerable<string> SplitLongWord(
        string word,
        XFont font,
        XGraphics graphics,
        double maxWidth)
    {
        var part = new StringBuilder();
        foreach (var character in word)
        {
            part.Append(character);
            if (graphics.MeasureString(part.ToString(), font).Width <= maxWidth)
            {
                continue;
            }

            part.Length--;
            if (part.Length > 0) yield return part.ToString();
            part.Clear();
            part.Append(character);
        }

        if (part.Length > 0) yield return part.ToString();
    }

    private static string FormatPdfValue(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString(
            "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture),
        DateOnly date => date.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture),
        decimal number => number.ToString(
            "N2",
            CultureInfo.InvariantCulture),
        double number => number.ToString(
            "N2",
            CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(
            null,
            CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string FormatFilters(
        ReportQueryInput input,
        string role)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var filters = new List<string>
        {
            $"From: {(input.From ?? today.AddDays(-29)):yyyy-MM-dd}",
            $"To: {(input.To ?? today):yyyy-MM-dd}"
        };
        AddFilter(filters, "Department ID", input.DepartmentId);
        AddFilter(filters, "Request type ID", input.RequestTypeId);
        AddFilter(filters, "Status", input.Status);
        AddFilter(filters, "Priority", input.Priority);
        if (role == "Employee")
        {
            filters.Add("Employee scope: Current user");
        }
        else
        {
            AddFilter(
                filters,
                "Employee",
                input.EmployeeUserId is int employeeId
                    ? $"User #{employeeId}"
                    : null);
        }

        if (role == "Manager")
        {
            filters.Add("Manager scope: Authorized assignments");
        }
        else
        {
            AddFilter(
                filters,
                "Manager",
                input.ManagerUserId is int managerId
                    ? $"User #{managerId}"
                    : null);
        }
        AddFilter(filters, "Search", input.Search?.Trim());
        filters.Add(
            $"Sort: {input.SortBy ?? "default"} {input.SortDirection ?? "desc"}");
        return string.Join(" | ", filters);
    }

    private static void AddFilter(
        ICollection<string> filters,
        string label,
        object? value)
    {
        if (value is not null &&
            !string.IsNullOrWhiteSpace(Convert.ToString(
                value,
                CultureInfo.InvariantCulture)))
        {
            filters.Add(
                $"{label}: {Convert.ToString(value, CultureInfo.InvariantCulture)}");
        }
    }

    private static string SummarizeStatuses(
        IEnumerable<string> statuses,
        int total)
    {
        var counts = statuses
            .GroupBy(status => status, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}: {group.Count():N0}");
        return $"{total:N0} records; {string.Join(", ", counts)}.";
    }

    private static int GetMaximumRows(IConfiguration configuration)
    {
        var maximumRows = configuration.GetValue<int?>(
            "Reports:ExportMaxRows") ?? DefaultMaximumRows;
        if (maximumRows is < 1 or > AbsoluteMaximumRows)
        {
            throw new InvalidOperationException(
                $"Reports:ExportMaxRows must be between 1 and {AbsoluteMaximumRows:N0}.");
        }

        return maximumRows;
    }
}
