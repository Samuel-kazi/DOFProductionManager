using System.Text;
using DOF.Contracts.Responses;

namespace DOF.Application.Reports;

/// <summary>
/// RFC 4180 CSV for reports (UTF-8 with BOM so spreadsheet programs detect the encoding). Cells that a
/// spreadsheet would run as a formula (starting with =, +, -, @, tab or CR and not a plain number) are
/// prefixed with an apostrophe to prevent CSV injection.
/// </summary>
public static class CsvWriter
{
    public static byte[] Write(ReportResponse report)
    {
        var sb = new StringBuilder();
        Line(sb, report.Columns);
        foreach (var row in report.Rows)
        {
            Line(sb, row);
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public static string FileName(ReportResponse report) =>
        $"dof-{report.Key}-{report.GeneratedAt:yyyyMMdd-HHmm}.csv";

    private static void Line(StringBuilder sb, IEnumerable<string> cells)
    {
        sb.AppendJoin(',', cells.Select(Escape));
        sb.Append("\r\n");
    }

    internal static string Escape(string? value)
    {
        var cell = value ?? string.Empty;
        if (cell.Length > 0 && "=+-@\t\r".Contains(cell[0]) && !double.TryParse(cell, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            cell = "'" + cell;
        }
        return cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
    }
}
