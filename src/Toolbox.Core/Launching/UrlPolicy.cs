namespace Toolbox.Launching;

public sealed record UrlCheck(Uri? Uri, string? Error, bool IsInsecure)
{
    public bool IsValid => Uri is not null;
}

public static class UrlPolicy
{
    public static UrlCheck Check(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new UrlCheck(null, "No URL is configured.", false);
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return new UrlCheck(null, $"'{trimmed}' is not an absolute URL.", false);
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return new UrlCheck(null, $"Only http and https links are allowed (got '{uri.Scheme}:').", false);
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return new UrlCheck(null, "The URL has no host name.", false);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return new UrlCheck(null, "URLs must not contain a user name or password.", false);
        }

        return new UrlCheck(uri, null, uri.Scheme == Uri.UriSchemeHttp);
    }
}
