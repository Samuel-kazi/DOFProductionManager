using DOF.Application.Calendar;
using DOF.Application.Common.Abstractions;
using DOF.Contracts.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DOF.Infrastructure.Documents;

/// <summary>
/// Draws schedule PDFs. Every period starts with an overview that suits it — a day's timeline, a
/// seven-day board, a month grid or a twelve-month heat map — followed by the full list of entries
/// grouped by day. Colours mark the kind of item and are explained in a legend on every page.
/// </summary>
public sealed class QuestPdfScheduleRenderer : ISchedulePdfRenderer
{
    private const string Ink = "#1C2333";
    private const string Muted = "#6B7280";
    private const string Faint = "#9CA3AF";
    private const string Line = "#E5E7EB";
    private const string Paper = "#FBF8F3";
    private const string Accent = "#C2410C";
    private const string AccentSoft = "#FFEDD5";

    private static readonly IReadOnlyList<(string Category, string Label, string Colour, string Tint)> Categories =
    [
        ("Event", "Events & meetings", "#2563EB", "#DBEAFE"),
        ("Recording", "Recordings", "#DC2626", "#FEE2E2"),
        ("Deadline", "Deadlines", "#D97706", "#FEF3C7"),
        ("Milestone", "Milestones", "#7C3AED", "#EDE9FE"),
        ("Release", "Releases", "#059669", "#D1FAE5"),
    ];

    private static readonly string[] WeekdayShort = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    static QuestPdfScheduleRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(ScheduleDocument doc) => Compose(doc).GeneratePdf();

    /// <summary>The document before it is drawn (tests also render it to images for review).</summary>
    public IDocument Compose(ScheduleDocument doc)
    {
        var landscape = doc.Period is ScheduleValues.Week or ScheduleValues.Month;
        return Document.Create(container => container.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(26);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontFamily("Lato").FontSize(8.5f).FontColor(Ink));
            page.Header().Element(c => Header(c, doc));
            page.Content().PaddingVertical(10).Element(c => Body(c, doc));
            page.Footer().Element(c => Footer(c, doc));
        }))
        .WithMetadata(new DocumentMetadata
        {
            Title = $"{doc.Title} · {doc.RangeLabel}",
            Author = doc.GeneratedBy,
            Creator = "DOF Production Manager",
            Subject = "DOF schedule",
        });
    }

    private static void Header(IContainer container, ScheduleDocument doc) => container.Column(col =>
    {
        col.Item().Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("DOF PRODUCTION MANAGER").FontSize(7.5f).Bold().LetterSpacing(0.18f).FontColor(Accent);
                left.Item().PaddingTop(2).Text(doc.Title).FontSize(20).Bold();
                left.Item().Text(doc.RangeLabel).FontSize(11).FontColor(Muted);
            });
            row.AutoItem().AlignBottom().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Element(c => Totals(c, doc));
                right.Item().PaddingTop(4).AlignRight().Text($"Times in {doc.TimeZone}").FontSize(7.5f).FontColor(Muted);
            });
        });
        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Accent);
        col.Item().PaddingTop(5).Row(row =>
        {
            foreach (var (_, label, colour, _) in Categories)
            {
                row.AutoItem().PaddingRight(12).Row(chip =>
                {
                    chip.AutoItem().AlignMiddle().Width(7).Height(7).CornerRadius(2).Background(colour);
                    chip.AutoItem().AlignMiddle().PaddingLeft(4).Text(label).FontSize(7.5f).FontColor(Muted);
                });
            }
            if (doc.FilterSummary is { } filters)
            {
                row.RelativeItem().AlignRight().Text($"Filtered: {filters}").FontSize(7.5f).Italic().FontColor(Muted);
            }
        });
    });

    /// <summary>Distinct items and the busiest kinds, e.g. "14 items · 3 recordings · 2 releases".</summary>
    private static void Totals(IContainer container, ScheduleDocument doc)
    {
        var distinct = doc.Entries.Where(e => !e.ContinuesFromPreviousDay || e.Day == doc.FirstDay).ToList();
        var parts = new List<string> { distinct.Count == 1 ? "1 item" : $"{distinct.Count} items" };
        parts.AddRange(Categories
            .Select(c => (c.Category, Count: distinct.Count(e => e.Category == c.Category)))
            .Where(c => c.Count > 0)
            .Select(c => $"{c.Count} {Noun(c.Category, c.Count)}"));
        container.Background(AccentSoft).CornerRadius(4).PaddingVertical(3).PaddingHorizontal(8)
            .Text(string.Join("  ·  ", parts)).FontSize(8).SemiBold().FontColor(Accent);
    }

    private static string Noun(string category, int count) => (category, count == 1) switch
    {
        ("Event", true) => "event",
        ("Event", false) => "events",
        (_, true) => category.ToLowerInvariant(),
        _ => $"{category.ToLowerInvariant()}s",
    };

    private static void Footer(IContainer container, ScheduleDocument doc) => container.BorderTop(0.5f).BorderColor(Line).PaddingTop(5).Row(row =>
    {
        row.RelativeItem().Text($"Generated {doc.GeneratedAt.ToString("d MMM yyyy, HH:mm", ScheduleExportService.Culture)} by {doc.GeneratedBy}")
            .FontSize(7).FontColor(Faint);
        row.AutoItem().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(7).FontColor(Faint));
            t.Span("Page ");
            t.CurrentPageNumber();
            t.Span(" of ");
            t.TotalPages();
        });
    });

    private static void Body(IContainer container, ScheduleDocument doc) => container.Column(col =>
    {
        col.Spacing(14);
        if (doc.Entries.Count == 0)
        {
            col.Item().PaddingTop(60).AlignCenter().Text("Nothing is scheduled in this period.").FontSize(13).FontColor(Muted);
            return;
        }

        switch (doc.Period)
        {
            case ScheduleValues.Week:
                col.Item().Element(c => WeekBoard(c, doc));
                col.Item().PageBreak();
                break;
            case ScheduleValues.Month:
                col.Item().Element(c => MonthGrid(c, doc));
                col.Item().PageBreak();
                break;
            case ScheduleValues.Year:
                col.Item().Element(c => YearOverview(c, doc));
                col.Item().PageBreak();
                break;
        }
        col.Item().Element(c => Agenda(c, doc));
    });

    // ----- Overviews -----

    private static void WeekBoard(IContainer container, ScheduleDocument doc) => container.Row(row =>
    {
        row.Spacing(5);
        for (var day = doc.FirstDay; day <= doc.LastDay; day = day.AddDays(1))
        {
            var entries = doc.Entries.Where(e => e.Day == day).ToList();
            var current = day;
            row.RelativeItem().Border(0.5f).BorderColor(Line).CornerRadius(4).Column(col =>
            {
                col.Item().Background(current == doc.Today ? AccentSoft : Paper).Padding(5).Row(head =>
                {
                    head.RelativeItem().Text(WeekdayShort[(int)current.DayOfWeek == 0 ? 6 : (int)current.DayOfWeek - 1]).Bold()
                        .FontColor(current == doc.Today ? Accent : Ink);
                    head.AutoItem().Text(current.ToString("d MMM", ScheduleExportService.Culture)).FontColor(Muted);
                });
                col.Item().MinHeight(380).Padding(4).Column(list =>
                {
                    list.Spacing(4);
                    if (entries.Count == 0)
                    {
                        list.Item().PaddingTop(6).AlignCenter().Text("Free").FontSize(7.5f).FontColor(Faint);
                    }
                    const int shown = 16;
                    foreach (var e in entries.Take(shown))
                    {
                        list.Item().Element(c => Card(c, e, compact: false));
                    }
                    if (entries.Count > shown)
                    {
                        list.Item().Text($"+{entries.Count - shown} more (see details)").FontSize(7).FontColor(Muted);
                    }
                });
            });
        }
    });

    private static void MonthGrid(IContainer container, ScheduleDocument doc)
    {
        var gridStart = doc.FirstDay.AddDays(-(((int)doc.FirstDay.DayOfWeek + 6) % 7));
        var weeks = (int)Math.Ceiling((doc.LastDay.DayNumber - gridStart.DayNumber + 1) / 7.0);
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                for (var i = 0; i < 7; i++)
                {
                    c.RelativeColumn();
                }
            });
            table.Header(h =>
            {
                foreach (var name in WeekdayShort)
                {
                    h.Cell().Background(Paper).BorderBottom(0.5f).BorderColor(Line).Padding(4).AlignCenter()
                        .Text(name.ToUpperInvariant()).FontSize(7).Bold().LetterSpacing(0.1f).FontColor(Muted);
                }
            });
            var cellHeight = weeks > 5 ? 66f : 80f;
            for (var i = 0; i < weeks * 7; i++)
            {
                var day = gridStart.AddDays(i);
                var inMonth = day >= doc.FirstDay && day <= doc.LastDay;
                var entries = inMonth ? doc.Entries.Where(e => e.Day == day).ToList() : [];
                table.Cell().Border(0.5f).BorderColor(Line).Background(inMonth ? Colors.White : Paper).Height(cellHeight).Padding(3).Column(col =>
                {
                    col.Spacing(1.5f);
                    col.Item().Row(r =>
                    {
                        var number = r.AutoItem();
                        if (day == doc.Today)
                        {
                            number.Background(Accent).CornerRadius(3).PaddingHorizontal(3)
                                .Text(day.Day.ToString()).FontSize(8).Bold().FontColor(Colors.White);
                        }
                        else
                        {
                            number.Text(day.Day.ToString()).FontSize(8).Bold().FontColor(inMonth ? Ink : Faint);
                        }
                    });
                    var limit = cellHeight > 70 ? 5 : 4;
                    foreach (var e in entries.Take(entries.Count > limit ? limit - 1 : limit))
                    {
                        col.Item().Element(c => Card(c, e, compact: true));
                    }
                    if (entries.Count > limit)
                    {
                        col.Item().Text($"+{entries.Count - limit + 1} more").FontSize(6.5f).FontColor(Muted);
                    }
                });
            }
        });
    }

    private static void YearOverview(IContainer container, ScheduleDocument doc)
    {
        var counts = doc.Entries.GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.Count());
        var busiest = Math.Max(1, counts.Values.DefaultIfEmpty(0).Max());
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("The year at a glance — darker days are busier").FontSize(9).FontColor(Muted);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });
                for (var month = 1; month <= 12; month++)
                {
                    var first = new DateOnly(doc.FirstDay.Year, month, 1);
                    var monthCount = doc.Entries.Count(e => e.Day.Month == month && (!e.ContinuesFromPreviousDay || e.Day.Day == 1));
                    table.Cell().Padding(6).Element(c => MiniMonth(c, first, counts, busiest, doc.Today, monthCount));
                }
            });
        });
    }

    private static void MiniMonth(IContainer container, DateOnly first, Dictionary<DateOnly, int> counts, int busiest, DateOnly today, int total) =>
        container.Border(0.5f).BorderColor(Line).CornerRadius(4).Padding(6).Column(col =>
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Text(first.ToString("MMMM", ScheduleExportService.Culture)).Bold().FontSize(9.5f);
                r.AutoItem().Text(total == 1 ? "1 item" : $"{total} items").FontSize(7).FontColor(total > 0 ? Accent : Faint);
            });
            col.Item().PaddingTop(4).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    for (var i = 0; i < 7; i++)
                    {
                        c.RelativeColumn();
                    }
                });
                foreach (var name in WeekdayShort)
                {
                    t.Cell().AlignCenter().Text(name[..1]).FontSize(6).FontColor(Faint);
                }
                var offset = ((int)first.DayOfWeek + 6) % 7;
                for (var i = 0; i < offset; i++)
                {
                    t.Cell().Text(string.Empty);
                }
                for (var day = first; day.Month == first.Month; day = day.AddDays(1))
                {
                    var count = counts.GetValueOrDefault(day);
                    var shade = count == 0 ? null : HeatColour((double)count / busiest);
                    var cell = t.Cell().Padding(0.8f).Height(13);
                    if (shade is not null)
                    {
                        cell = cell.Background(shade).CornerRadius(2);
                    }
                    if (day == today)
                    {
                        cell = cell.Border(1).BorderColor(Ink).CornerRadius(2);
                    }
                    cell.AlignCenter().AlignMiddle().Text(day.Day.ToString()).FontSize(6.5f)
                        .FontColor(count > 0 && (double)count / busiest > 0.5 ? Colors.White : Ink);
                }
            });
        });

    private static string HeatColour(double ratio) => ratio switch
    {
        <= 0.25 => "#FED7AA",
        <= 0.5 => "#FDBA74",
        <= 0.75 => "#F97316",
        _ => "#C2410C",
    };

    // ----- Detailed list -----

    private static void Agenda(IContainer container, ScheduleDocument doc) => container.Column(col =>
    {
        if (doc.Period != ScheduleValues.Day)
        {
            col.Item().PaddingBottom(6).Text("Details").FontSize(13).Bold();
        }

        foreach (var monthGroup in doc.Entries.GroupBy(e => (e.Day.Year, e.Day.Month)))
        {
            if (doc.Period == ScheduleValues.Year)
            {
                var first = new DateOnly(monthGroup.Key.Year, monthGroup.Key.Month, 1);
                col.Item().PaddingTop(10).PaddingBottom(2).BorderBottom(1).BorderColor(Accent)
                    .Text(first.ToString("MMMM yyyy", ScheduleExportService.Culture).ToUpperInvariant())
                    .FontSize(9).Bold().LetterSpacing(0.12f).FontColor(Accent);
            }

            foreach (var dayGroup in monthGroup.GroupBy(e => e.Day))
            {
                // A day's heading never sits alone at the bottom of a page: it moves with its first rows.
                col.Item().EnsureSpace(90).PaddingTop(8).Element(c => Day(c, doc, dayGroup.Key, [.. dayGroup]));
            }
        }
    });

    private static void Day(IContainer container, ScheduleDocument doc, DateOnly date, IReadOnlyList<ScheduleEntry> entries) => container.Column(day =>
    {
        day.Item().Row(r =>
        {
            r.AutoItem().Text(date.ToString("dddd d MMMM", ScheduleExportService.Culture)).FontSize(10).Bold()
                .FontColor(date == doc.Today ? Accent : Ink);
            if (date == doc.Today)
            {
                r.AutoItem().PaddingLeft(6).AlignMiddle().Background(AccentSoft).CornerRadius(3).PaddingHorizontal(4)
                    .Text("TODAY").FontSize(6.5f).Bold().FontColor(Accent);
            }
            r.RelativeItem().AlignRight().AlignMiddle()
                .Text(entries.Count == 1 ? "1 item" : $"{entries.Count} items").FontSize(7.5f).FontColor(Muted);
        });
        day.Item().PaddingTop(3).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(74);
                c.RelativeColumn(5);
                c.RelativeColumn(3);
                c.ConstantColumn(70);
            });
            table.Header(h =>
            {
                foreach (var title in new[] { "TIME", "ITEM", "PEOPLE", "STATUS" })
                {
                    h.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(4)
                        .Text(title).FontSize(6.5f).Bold().LetterSpacing(0.1f).FontColor(Faint);
                }
            });
            foreach (var e in entries)
            {
                AgendaRow(table, e);
            }
        });
    });

    private static void AgendaRow(TableDescriptor table, ScheduleEntry e)
    {
        var (colour, _) = Colour(e.Category);
        IContainer Cell() => table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(4);

        Cell().Text(TimeLabel(e)).SemiBold().FontColor(e.IsAllDay ? Muted : Ink);
        Cell().Row(r =>
        {
            r.ConstantItem(3).Background(colour).CornerRadius(1.5f);
            r.RelativeItem().PaddingLeft(6).Column(c =>
            {
                c.Item().Text(e.Title).SemiBold();
                var details = string.Join("  ·  ", new[] { e.Kind, e.Location }.Where(s => !string.IsNullOrWhiteSpace(s)));
                c.Item().Text(details).FontSize(7.5f).FontColor(Muted);
            });
        });
        Cell().Text(string.IsNullOrEmpty(e.People) ? "—" : e.People).FontColor(string.IsNullOrEmpty(e.People) ? Faint : Ink);
        Cell().Element(c => Status(c, e));
    }

    private static void Status(IContainer container, ScheduleEntry e)
    {
        var (fore, back) = e.Status.ToLowerInvariant() switch
        {
            "overdue" => ("#B91C1C", "#FEE2E2"),
            "cancelled" => (Muted, "#F3F4F6"),
            "completed" or "published" or "approved" => ("#047857", "#D1FAE5"),
            "in progress" or "editing" or "in review" or "recorded" => ("#1D4ED8", "#DBEAFE"),
            _ => ("#374151", "#F3F4F6"),
        };
        container.AlignLeft().Background(back).CornerRadius(3).PaddingHorizontal(5).PaddingVertical(1.5f)
            .Text(e.Status).FontSize(7).SemiBold().FontColor(fore);
    }

    /// <summary>A small coloured card: used on the week board and (compact) inside month cells.</summary>
    private static void Card(IContainer container, ScheduleEntry e, bool compact)
    {
        var (colour, tint) = Colour(e.Category);
        var cancelled = string.Equals(e.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
        container.Background(tint).CornerRadius(2.5f).Row(r =>
        {
            r.ConstantItem(2.5f).Background(colour);
            r.RelativeItem().PaddingHorizontal(compact ? 2.5f : 4).PaddingVertical(compact ? 1 : 2.5f).Column(c =>
            {
                if (compact)
                {
                    var time = e.Start is { } s ? $"{s:HH\\:mm} " : string.Empty;
                    c.Item().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(6.5f));
                        if (time.Length > 0)
                        {
                            t.Span(time).Bold().FontColor(colour);
                        }
                        var title = t.Span(e.Title);
                        if (cancelled)
                        {
                            title.Strikethrough().FontColor(Muted);
                        }
                        t.ClampLines(1);
                    });
                    return;
                }
                c.Item().Text(TimeLabel(e)).FontSize(6.5f).Bold().FontColor(colour);
                var name = c.Item().Text(e.Title).FontSize(7.5f).SemiBold();
                name.ClampLines(3);
                if (cancelled)
                {
                    name.Strikethrough().FontColor(Muted);
                }
                if (!string.IsNullOrWhiteSpace(e.Location))
                {
                    c.Item().Text(e.Location).FontSize(6.5f).FontColor(Muted).ClampLines(1);
                }
            });
        });
    }

    private static string TimeLabel(ScheduleEntry e) => (e.IsAllDay, e.Start, e.End) switch
    {
        (true, _, _) => e.ContinuesFromPreviousDay ? "All day (cont.)" : "All day",
        (false, { } s, { } end) => $"{s:HH\\:mm}–{end:HH\\:mm}",
        (false, { } s, null) when e.ContinuesToNextDay => $"From {s:HH\\:mm}",
        (false, { } s, null) => $"{s:HH\\:mm}",
        (false, null, { } end) => $"Until {end:HH\\:mm}",
        _ => "All day (cont.)",
    };

    private static (string Colour, string Tint) Colour(string category)
    {
        var match = Categories.FirstOrDefault(c => c.Category == category);
        return match.Colour is null ? ("#6B7280", "#F3F4F6") : (match.Colour, match.Tint);
    }
}
