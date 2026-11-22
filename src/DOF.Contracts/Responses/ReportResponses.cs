namespace DOF.Contracts.Responses;

public sealed record ReportFigure(string Label, string Value);

/// <summary>The record behind a report row (for opening it from the desktop).</summary>
public sealed record ReportRowLink(string SourceType, Guid SourceId);

/// <summary>
/// Every report has the same shape: headline figures plus a table. Cells are text already formatted in
/// the requested timezone (times as <c>yyyy-MM-dd HH:mm</c>), so the table and its CSV export agree.
/// <see cref="Links"/> is parallel to <see cref="Rows"/>.
/// </summary>
public sealed record ReportResponse(
    string Key,
    string Title,
    string Description,
    string Period,
    string TimeZone,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReportFigure> Summary,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<ReportRowLink?> Links);

/// <summary>A global search hit. <see cref="SourceType"/> is what <c>OpenRecordAsync</c> expects.</summary>
public sealed record SearchResultResponse(string SourceType, Guid Id, string Title, string Subtitle);
