using System.Text;
using DOF.Application.Calendar;
using DOF.Contracts.Common;
using DOF.Infrastructure.Documents;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace DOF.UnitTests;

public class SchedulePdfTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    /// <summary>A realistic week: meetings, recordings, deadlines, a release and a multi-day event.</summary>
    public static ScheduleDocument Sample(string period, DateOnly first, DateOnly last, int repeatWeeks = 1)
    {
        var entries = new List<ScheduleEntry>();
        for (var w = 0; w < repeatWeeks; w++)
        {
            var monday = Monday.AddDays(7 * (w - (period == ScheduleValues.Year ? 40 : period == ScheduleValues.Month ? 3 : 0)));
            entries.AddRange(
            [
                new(monday, new TimeOnly(8, 30), new TimeOnly(9, 30), false, false, false, "Weekly staff meeting", "Meeting", "Event",
                    "Scheduled", "Normal", "Board room", "Samuel Kazi, Grace Wanjiru"),
                new(monday, new TimeOnly(10, 0), new TimeOnly(13, 0), false, false, false, "Recording · Faith Frequency S2E4: Grace in the storm",
                    "Recording", "Recording", "Scheduled", "Normal", "Studio A", "Peter Otieno, Mercy Achieng"),
                new(monday.AddDays(1), null, null, true, false, false, "Editing due · Faith Frequency S2E3: Rooted", "Editing due", "Deadline",
                    "Editing", "Normal", null, "Mercy Achieng"),
                new(monday.AddDays(2), new TimeOnly(17, 0), null, false, false, false, "YouTube · Faith Frequency S2E2: Living water", "Release",
                    "Release", w % 2 == 0 ? "Overdue" : "Scheduled", "Normal", "YouTube", "Grace Wanjiru"),
                new(monday.AddDays(3), null, null, true, false, true, "Camp meeting", "Ministry event", "Event", "Scheduled", "High",
                    "Kamagambo campus", "Samuel Kazi"),
                new(monday.AddDays(4), null, null, true, true, false, "Camp meeting", "Ministry event", "Event", "Scheduled", "High",
                    "Kamagambo campus", "Samuel Kazi"),
                new(monday.AddDays(4), new TimeOnly(15, 0), null, false, false, false, "Submit sponsorship report", "Task due", "Deadline",
                    "In progress", "High", "Covenant Chronicles", "Grace Wanjiru"),
                new(monday.AddDays(5), null, null, true, false, false, "Season 2 trailer approved · Covenant Chronicles", "Milestone", "Milestone",
                    "Completed", "Normal", null, "Samuel Kazi"),
                new(monday.AddDays(5), new TimeOnly(14, 0), new TimeOnly(16, 0), false, false, false, "Camera training", "Training", "Event",
                    "Cancelled", "Low", "Studio B", ""),
            ]);
        }
        var inRange = entries.Where(e => e.Day >= first && e.Day <= last).OrderBy(e => e.Day).ThenByDescending(e => e.IsAllDay).ThenBy(e => e.Start).ToList();
        return new ScheduleDocument(period, $"{period}ly schedule".Replace("Dayly", "Daily"), "Sample range", first, last, Monday.AddDays(2),
            "Africa/Nairobi", new DateTime(2026, 11, 23, 18, 5, 0), "Samuel Kazi", period == ScheduleValues.Week ? "assigned to Samuel Kazi" : null, inRange);
    }

    public static TheoryData<string> Periods => [.. ScheduleValues.Periods];

    [Theory]
    [MemberData(nameof(Periods))]
    public void Every_period_renders_a_pdf(string period)
    {
        var doc = period switch
        {
            ScheduleValues.Day => Sample(period, Monday, Monday),
            ScheduleValues.Week => Sample(period, Monday, Monday.AddDays(6)),
            ScheduleValues.Month => Sample(period, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), repeatWeeks: 6),
            _ => Sample(period, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), repeatWeeks: 52),
        };

        var renderer = new QuestPdfScheduleRenderer();
        var pdf = renderer.Render(doc);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 5_000);
        // Set DOF_PDF_SAMPLES to a folder to look at the output.
        if (Environment.GetEnvironmentVariable("DOF_PDF_SAMPLES") is { Length: > 0 } folder)
        {
            File.WriteAllBytes(Path.Combine(folder, $"schedule-{period.ToLowerInvariant()}.pdf"), pdf);
            var page = 1;
            foreach (var image in renderer.Compose(doc).GenerateImages(new ImageGenerationSettings { RasterDpi = 110 }))
            {
                File.WriteAllBytes(Path.Combine(folder, $"schedule-{period.ToLowerInvariant()}-{page++}.png"), image);
            }
        }
    }

    [Fact]
    public void An_empty_period_still_renders()
    {
        var doc = Sample(ScheduleValues.Week, Monday.AddDays(70), Monday.AddDays(76));

        Assert.Empty(doc.Entries);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(new QuestPdfScheduleRenderer().Render(doc), 0, 4));
    }
}
