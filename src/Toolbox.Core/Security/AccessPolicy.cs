namespace Toolbox.Security;

public sealed record AccessDecision(bool Allowed, string Reason);

public static class AccessPolicy
{
    public static AccessDecision Evaluate(bool enabled, IEnumerable<string>? allowedGroups, Func<string, bool> isMember)
    {
        ArgumentNullException.ThrowIfNull(isMember);
        if (!enabled)
        {
            return new AccessDecision(true, "Access control is disabled.");
        }

        var groups = (allowedGroups ?? Enumerable.Empty<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groups.Count == 0)
        {
            return new AccessDecision(false, "Access control is enabled but no allowed groups are configured.");
        }

        var unresolved = new List<string>();
        foreach (var group in groups)
        {
            try
            {
                if (isMember(group))
                {
                    return new AccessDecision(true, $"Member of '{group}'.");
                }
            }
            catch (Exception ex)
            {
                unresolved.Add($"{group} ({ex.GetType().Name})");
            }
        }

        var reason = unresolved.Count == 0
            ? "Not a member of any allowed group."
            : "Not a member of any allowed group. Could not evaluate: " + string.Join(", ", unresolved) + ".";
        return new AccessDecision(false, reason);
    }

    public static bool LooksLikeSid(string value) =>
        value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase);
}
