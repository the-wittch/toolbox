using Toolbox.Models;

namespace Toolbox.Configuration;

public static class SettingsLimits
{
    public static readonly (int Min, int Max) PageSizeRange = (1, 1000);
    public static readonly (int Min, int Max) MaxResultsRange = (1, 5000);
    public static readonly (int Min, int Max) SearchTimeoutRange = (5, 300);
    public static readonly (int Min, int Max) PingTimeoutRange = (100, 60000);
    public static readonly (int Min, int Max) PingBufferRange = (1, 65500);
    public static readonly (int Min, int Max) RegistryTimeoutRange = (2, 120);
    public static readonly (int Min, int Max) ClipboardClearRange = (0, 600);
    public static readonly (int Min, int Max) RevealRange = (3, 300);
    public static readonly (int Min, int Max) LapsTimeoutRange = (5, 120);
    public static readonly (int Min, int Max) HistoryLimitRange = (1, 500);
    public static readonly (int Min, int Max) StatusLogRange = (20, 5000);

    public static int PageSize(LdapSettings l) => Clamp(l.PageSize, PageSizeRange);
    public static int MaxResults(LdapSettings l) => Clamp(l.MaxResults, MaxResultsRange);
    public static TimeSpan SearchTimeout(LdapSettings l) => TimeSpan.FromSeconds(Clamp(l.SearchTimeoutSeconds, SearchTimeoutRange));
    public static TimeSpan PingTimeout(PingSettings p) => TimeSpan.FromMilliseconds(Clamp(p.TimeoutMs, PingTimeoutRange));
    public static int PingBuffer(PingSettings p) => Clamp(p.BufferSize, PingBufferRange);
    public static TimeSpan RegistryTimeout(DetailSettings d) => TimeSpan.FromSeconds(Clamp(d.RemoteRegistryTimeoutSeconds, RegistryTimeoutRange));
    public static TimeSpan ClipboardClear(LapsSettings l) => TimeSpan.FromSeconds(Clamp(l.ClipboardClearSeconds, ClipboardClearRange));
    public static TimeSpan Reveal(LapsSettings l) => TimeSpan.FromSeconds(Clamp(l.RevealSeconds, RevealRange));
    public static TimeSpan LapsTimeout(LapsSettings l) => TimeSpan.FromSeconds(Clamp(l.LookupTimeoutSeconds, LapsTimeoutRange));
    public static int HistoryLimit(SearchSettings s) => Clamp(s.HistoryLimit, HistoryLimitRange);
    public static int StatusLogLimit(UiSettings u) => Clamp(u.StatusLogLimit, StatusLogRange);

    public static int Clamp(int value, (int Min, int Max) range) => Math.Clamp(value, range.Min, range.Max);
}
