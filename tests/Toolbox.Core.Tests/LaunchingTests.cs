using Toolbox.Launching;

namespace Toolbox.Tests;

public class CommandTemplateTests
{
    [Fact]
    public void Tokenizes_whitespace_and_quotes() =>
        Assert.Equal(new[] { "/offerRA", "PC 1 x", "z" }, CommandTemplate.Tokenize("/offerRA  \"PC 1 x\" z"));

    [Fact]
    public void Backslashes_are_literal() =>
        Assert.Equal(new[] { @"\\{computerName}\c$" }, CommandTemplate.Tokenize(@"\\{computerName}\c$"));

    [Fact]
    public void Unmatched_quote_is_rejected() =>
        Assert.Throws<FormatException>(() => CommandTemplate.Tokenize("/v:\"abc"));

    [Fact]
    public void Substituted_value_stays_inside_its_argument()
    {
        var args = CommandTemplate.Expand("/v:{computerName} /f", "PC1 /admin \"x\"", "me");
        Assert.Equal(new[] { "/v:PC1 /admin \"x\"", "/f" }, args);
    }

    [Fact]
    public void Tokens_are_case_insensitive() =>
        Assert.Equal(new[] { "a=PC-1&u=jdoe" }, CommandTemplate.Expand("a={COMPUTERNAME}&u={username}", "PC-1", "jdoe"));

    [Fact]
    public void Empty_template_gives_no_arguments() => Assert.Empty(CommandTemplate.Expand("  ", "x", "y"));

    [Fact]
    public void Reports_unknown_tokens() =>
        Assert.Equal(new[] { "{host}" }, CommandTemplate.FindUnknownTokens("/v:{host} {computerName}"));
}

public class ExecutableResolverTests
{
    private const string System32 = @"C:\Windows\System32";
    private const string Windows = @"C:\Windows";

    private static ExecutableResolver Resolver(params string[] existing)
    {
        var files = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return new ExecutableResolver(
            files.Contains,
            new[] { System32, Windows },
            s => s.Replace("%ProgramFiles%", @"C:\Program Files", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bare_name_resolves_only_from_system_folders()
    {
        var result = Resolver(@"C:\Windows\explorer.exe").Resolve("explorer.exe", "me");
        Assert.Equal(@"C:\Windows\explorer.exe", result.Path);
    }

    [Fact]
    public void Bare_name_without_extension_gets_exe()
    {
        var result = Resolver(@"C:\Windows\System32\mstsc.exe").Resolve("mstsc", "me");
        Assert.Equal(@"C:\Windows\System32\mstsc.exe", result.Path);
    }

    [Fact]
    public void Bare_name_outside_system_folders_is_not_found() =>
        Assert.False(Resolver(@"D:\share\explorer.exe").Resolve("explorer.exe", "me").Succeeded);

    [Fact]
    public void Full_path_with_user_token_and_environment()
    {
        var r = Resolver(@"C:\Program Files\Tool\jdoe\t.exe").Resolve(@"%ProgramFiles%\Tool\{userName}\t.exe", "jdoe");
        Assert.True(r.Succeeded);
        Assert.False(r.IsNetworkPath);
    }

    [Fact]
    public void Unc_path_is_allowed_but_flagged()
    {
        var r = Resolver(@"\\srv\tools\t.exe").Resolve(@"\\srv\tools\t.exe", "me");
        Assert.True(r.Succeeded);
        Assert.True(r.IsNetworkPath);
    }

    [Theory]
    [InlineData(@"tools\t.exe")]
    [InlineData(@".\t.exe")]
    [InlineData(@"C:\t.cmd")]
    [InlineData(@"C:\t.ps1")]
    [InlineData(@"\\{computerName}\c$\t.exe")]
    [InlineData(@"C:\a""b.exe")]
    [InlineData("")]
    public void Rejected_paths(string configured) =>
        Assert.False(Resolver(@"C:\t.cmd", @"C:\t.ps1", @"tools\t.exe").Resolve(configured, "me").Succeeded);

    [Fact]
    public void Missing_file_fails() => Assert.False(Resolver().Resolve(@"C:\missing.exe", "me").Succeeded);
}

public class UrlPolicyTests
{
    [Theory]
    [InlineData("https://stream.example.com/view", false)]
    [InlineData("http://10.0.0.5:8080/", true)]
    public void Accepts_http_and_https(string url, bool insecure)
    {
        var check = UrlPolicy.Check(url);
        Assert.True(check.IsValid);
        Assert.Equal(insecure, check.IsInsecure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("stream.example.com")]
    [InlineData("file:///C:/x.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:")]
    [InlineData("https://admin:secret@stream.example.com/")]
    public void Rejects_everything_else(string url) => Assert.False(UrlPolicy.Check(url).IsValid);
}

public class WindowsPathTests
{
    [Theory]
    [InlineData(@"C:\a.exe", true)]
    [InlineData(@"\\srv\share\a.exe", true)]
    [InlineData(@"\\srv", false)]
    [InlineData(@"\\?\C:\a.exe", false)]
    [InlineData(@"a.exe", false)]
    [InlineData(@"C:a.exe", false)]
    public void IsFullyQualified(string path, bool expected) => Assert.Equal(expected, WindowsPath.IsFullyQualified(path));
}

public class WindowsCommandLineTests
{
    private static List<string> Split(string commandLine)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasArg = false;
        var i = 0;
        while (i < commandLine.Length)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                var start = i;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    i++;
                }

                var count = i - start;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                        i++;
                    }
                }
                else
                {
                    current.Append('\\', count);
                }

                hasArg = true;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasArg = true;
                i++;
                continue;
            }

            if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (hasArg)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasArg = false;
                }

                i++;
                continue;
            }

            current.Append(c);
            hasArg = true;
            i++;
        }

        if (hasArg)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with space")]
    [InlineData("")]
    [InlineData("quote\"inside")]
    [InlineData(@"trailing\")]
    [InlineData(@"C:\Program Files\x\")]
    [InlineData(@"\\PC-1\c$")]
    [InlineData("a=1&b=2")]
    [InlineData("\\\"")]
    public void Round_trips_through_argv_rules(string argument)
    {
        var args = new[] { "/first", argument, "last" };
        Assert.Equal(args, Split(WindowsCommandLine.Join(args)));
    }

    [Fact]
    public void Plain_arguments_are_not_quoted() =>
        Assert.Equal(@"/offerRA PC-1 \\PC-1\c$", WindowsCommandLine.Join(new[] { "/offerRA", "PC-1", @"\\PC-1\c$" }));
}
