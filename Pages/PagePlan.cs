namespace HamiPdf.Pages;

internal sealed record PageEntry(Guid Id, Guid SourceId, int SourcePage, int Rotation = 0);
internal sealed record SourceRecord(Guid Id, string Name, byte[] Bytes);

internal static class PagePlan
{
    public static int Normalize(int degrees) => (degrees % 360 + 360) % 360;

    public static List<PageEntry> Move(IReadOnlyList<PageEntry> pages, HashSet<Guid> selected, bool up)
    {
        var result = pages.ToList();
        if (up)
        {
            for (int i = 1; i < result.Count; i++)
                if (selected.Contains(result[i].Id) && !selected.Contains(result[i-1].Id))
                    (result[i-1], result[i]) = (result[i], result[i-1]);
        }
        else
        {
            for (int i = result.Count-2; i >= 0; i--)
                if (selected.Contains(result[i].Id) && !selected.Contains(result[i+1].Id))
                    (result[i+1], result[i]) = (result[i], result[i+1]);
        }
        return result;
    }

    public static List<PageEntry> Rotate(IReadOnlyList<PageEntry> pages, HashSet<Guid> selected) =>
        pages.Select(p => selected.Contains(p.Id) ? p with { Rotation = Normalize(p.Rotation + 90) } : p).ToList();

    public static List<PageEntry> Extract(IReadOnlyList<PageEntry> pages, HashSet<Guid> selected) =>
        pages.Where(p => selected.Contains(p.Id)).ToList();
}
