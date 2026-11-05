using System.Globalization;
using System.Reflection;
using System.Text;
using ClosedXML.Excel;
using NexCrm.Application.Common;
using NexCrm.Application.Common.Querying;

namespace NexCrm.Infrastructure.Services;

/// <summary>Writes the rows a grid is showing, with its visible columns in their order.</summary>
public sealed class ExportService : IExportService
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public FileResultDto Export<T>(IReadOnlyList<T> rows, ExportRequest request, string defaultName)
    {
        var columns = ResolveColumns<T>(request.Columns);
        var name = $"{SafeName(request.FileName ?? defaultName)}-{DateTime.Now:yyyyMMdd-HHmm}";

        return request.Format.Equals("csv", StringComparison.OrdinalIgnoreCase)
            ? new FileResultDto(Csv(rows, columns), "text/csv", $"{name}.csv")
            : new FileResultDto(Xlsx(rows, columns, request.Title ?? defaultName), XlsxType, $"{name}.xlsx");
    }

    private static List<(PropertyInfo Property, string Header)> ResolveColumns<T>(IEnumerable<ExportColumn> requested)
    {
        var props = QueryEngine.PropertiesOf(typeof(T));
        var columns = requested
            .Where(c => props.ContainsKey(c.Field))
            .Select(c => (props[c.Field], string.IsNullOrWhiteSpace(c.Header) ? c.Field : c.Header))
            .ToList();

        if (columns.Count == 0) throw new BadRequestException("Pick at least one column to export.");
        return columns;
    }

    private static byte[] Csv<T>(IReadOnlyList<T> rows, List<(PropertyInfo Property, string Header)> columns)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', columns.Select(c => Escape(c.Header))));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', columns.Select(c => Escape(Format(c.Property.GetValue(row))))));

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static byte[] Xlsx<T>(IReadOnlyList<T> rows, List<(PropertyInfo Property, string Header)> columns, string title)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(SafeName(title)[..Math.Min(31, SafeName(title).Length)]);

        for (var c = 0; c < columns.Count; c++) sheet.Cell(1, c + 1).Value = columns[c].Header;

        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (columns[c].Property.GetValue(rows[r]))
                {
                    case null: break;
                    case decimal m: cell.Value = m; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case int i: cell.Value = i; break;
                    case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = "dd mmm yyyy hh:mm"; break;
                    case bool b: cell.Value = b ? "Yes" : "No"; break;
                    case var v: cell.Value = Format(v); break;
                }
            }

        var header = sheet.Range(1, 1, 1, columns.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1EAFF");
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    // Leading = + - @ would run as a formula when the file is opened in Excel.
    private static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string SafeName(string value)
    {
        var clean = new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ').ToArray()).Trim();
        return clean.Length == 0 ? "export" : clean;
    }
}
