using Toolbox.Ldap;
using Toolbox.Models;

namespace Toolbox.Tests;

public class LdapFilterTests
{
    [Theory]
    [InlineData("PC-1", "PC-1")]
    [InlineData("a*b", @"a\2ab")]
    [InlineData("(x)", @"\28x\29")]
    [InlineData(@"back\slash", @"back\5cslash")]
    [InlineData("nul\0", @"nul\00")]
    [InlineData("*)(objectClass=*", @"\2a\29\28objectClass=\2a")]
    public void EscapeValue_escapes_rfc4515_specials(string input, string expected) =>
        Assert.Equal(expected, LdapFilter.EscapeValue(input));

    [Theory]
    [InlineData("(objectCategory=computer)", true)]
    [InlineData("(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))", true)]
    [InlineData(@"(cn=a\2ab)", true)]
    [InlineData("objectCategory=computer", false)]
    [InlineData("(objectCategory=computer", false)]
    [InlineData("(a=1))(b=2", false)]
    [InlineData("(a=1)(b=2)", false)]
    [InlineData("()", false)]
    [InlineData(@"(cn=\zz)", false)]
    [InlineData("", false)]
    public void IsWellFormed(string filter, bool expected) => Assert.Equal(expected, LdapFilter.IsWellFormed(filter));

    [Theory]
    [InlineData("cn", true)]
    [InlineData("ms-Mcs-AdmPwd", true)]
    [InlineData("1.2.840.113556.1.4.221", true)]
    [InlineData("cn)(x", false)]
    [InlineData("", false)]
    [InlineData("9abc", false)]
    public void IsValidAttributeName(string name, bool expected) =>
        Assert.Equal(expected, LdapFilter.IsValidAttributeName(name));
}

public class ComputerSearchQueryTests
{
    private static readonly LdapSettings Ldap = new();
    private static readonly string[] Prefixes = { "PC-", "WS-" };

    [Fact]
    public void Prefix_match_searches_name_only()
    {
        Assert.True(ComputerSearchQueryBuilder.TryBuild(" pc-12 ", Ldap, Prefixes, out var query, out _));
        Assert.Equal("(&(objectCategory=computer)(cn=*pc-12*))", query!.Filter);
        Assert.True(query.IsNameSearch);
    }

    [Fact]
    public void Other_text_searches_name_or_description()
    {
        Assert.True(ComputerSearchQueryBuilder.TryBuild("jdoe", Ldap, Prefixes, out var query, out _));
        Assert.Equal("(&(objectCategory=computer)(|(cn=*jdoe*)(description=*jdoe*)))", query!.Filter);
        Assert.False(query.IsNameSearch);
    }

    [Fact]
    public void Injection_attempt_is_escaped_into_a_literal()
    {
        Assert.True(ComputerSearchQueryBuilder.TryBuild("*)(objectClass=user", Ldap, Prefixes, out var query, out _));
        Assert.DoesNotContain("(objectClass=user", query!.Filter, StringComparison.Ordinal);
        Assert.Contains(@"\2a\29\28objectClass=user", query.Filter, StringComparison.Ordinal);
        Assert.True(LdapFilter.IsWellFormed(query.Filter));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\u0001b")]
    public void Rejects_empty_or_control_characters(string input) =>
        Assert.False(ComputerSearchQueryBuilder.TryBuild(input, Ldap, Prefixes, out _, out _));

    [Fact]
    public void Rejects_overlong_terms() =>
        Assert.False(ComputerSearchQueryBuilder.TryBuild(new string('a', 257), Ldap, Prefixes, out _, out _));

    [Fact]
    public void Rejects_malformed_base_filter()
    {
        var ldap = new LdapSettings { ComputerObjectFilter = "(objectCategory=computer" };
        Assert.False(ComputerSearchQueryBuilder.TryBuild("x", ldap, Prefixes, out _, out var error));
        Assert.Contains("computerObjectFilter", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_attribute_names_that_would_alter_the_filter()
    {
        var ldap = new LdapSettings { NameAttribute = "cn=*)(objectClass" };
        Assert.False(ComputerSearchQueryBuilder.TryBuild("x", ldap, Prefixes, out _, out _));
    }
}

public class LdapPathTests
{
    [Fact]
    public void Serverless_signed_bind_by_default() =>
        Assert.Equal("LDAP://OU=PCs,DC=corp,DC=example", LdapPath.ForSearchRoot(new LdapSettings { RootOu = "OU=PCs,DC=corp,DC=example" }));

    [Fact]
    public void Explicit_server_is_used() =>
        Assert.Equal("LDAP://dc01.corp.example/OU=PCs,DC=corp,DC=example",
            LdapPath.ForSearchRoot(new LdapSettings { RootOu = "OU=PCs,DC=corp,DC=example", Server = "dc01.corp.example" }));

    [Fact]
    public void Ldaps_derives_domain_and_port() =>
        Assert.Equal("LDAP://corp.example:636/OU=PCs,DC=corp,DC=example",
            LdapPath.ForSearchRoot(new LdapSettings { RootOu = "OU=PCs,DC=corp,DC=example", UseLdaps = true }));

    [Fact]
    public void Ldaps_keeps_explicit_port() =>
        Assert.Equal("LDAP://dc01:3269/OU=PCs,DC=corp,DC=example",
            LdapPath.ForSearchRoot(new LdapSettings { RootOu = "OU=PCs,DC=corp,DC=example", Server = "dc01:3269", UseLdaps = true }));

    [Fact]
    public void Full_adspath_root_is_kept_verbatim()
    {
        var ldap = new LdapSettings { RootOu = "LDAP://dc02/OU=PCs,DC=corp,DC=example" };
        Assert.Equal(ldap.RootOu, LdapPath.ForSearchRoot(ldap));
        Assert.Equal("LDAP://dc02/CN=PC-1,OU=PCs,DC=corp,DC=example", LdapPath.ForObject("CN=PC-1,OU=PCs,DC=corp,DC=example", ldap));
    }

    [Fact]
    public void Slash_in_dn_is_escaped() =>
        Assert.Equal(@"LDAP://CN=A\/B,DC=x", LdapPath.ForObject("CN=A/B,DC=x", new LdapSettings()));

    [Theory]
    [InlineData("OU=a,DC=corp,DC=example,DC=com", "corp.example.com")]
    [InlineData("OU=a", null)]
    public void DomainFromDn(string dn, string? expected) => Assert.Equal(expected, LdapPath.DomainFromDn(dn));
}

public class HostNameRulesTests
{
    [Theory]
    [InlineData("PC-1234", true)]
    [InlineData("pc-1.corp.example.com", true)]
    [InlineData("SQL_01", true)]
    [InlineData("PC 1", false)]
    [InlineData("PC-1 /admin", false)]
    [InlineData("PC\"1", false)]
    [InlineData("-PC", false)]
    [InlineData("PC..corp", false)]
    [InlineData("", false)]
    [InlineData(@"\\evil\share", false)]
    [InlineData("pc&calc", false)]
    public void IsValidComputerName(string name, bool expected) =>
        Assert.Equal(expected, HostNameRules.IsValidComputerName(name));

    [Theory]
    [InlineData("dc01", true)]
    [InlineData("dc01.corp.example:636", true)]
    [InlineData("dc01:0", false)]
    [InlineData("dc01:99999", false)]
    [InlineData("dc 01", false)]
    public void IsValidServer(string server, bool expected) => Assert.Equal(expected, HostNameRules.IsValidServer(server));
}

public class WindowsVersionFormatterTests
{
    [Fact]
    public void Windows11_is_detected_from_build()
    {
        var (os, version) = WindowsVersionFormatter.Format("Windows 10 Enterprise", "23H2", "22631", 4460, "6.3");
        Assert.Equal("Windows 11 Enterprise", os);
        Assert.Equal("23H2 (build 22631.4460)", version);
    }

    [Fact]
    public void Windows10_is_left_alone()
    {
        var (os, version) = WindowsVersionFormatter.Format("Windows 10 Pro", "22H2", "19045", null, "6.3");
        Assert.Equal("Windows 10 Pro", os);
        Assert.Equal("22H2 (build 19045)", version);
    }

    [Fact]
    public void Falls_back_to_legacy_version_without_build()
    {
        var (_, version) = WindowsVersionFormatter.Format("Windows Server", null, null, null, "6.1");
        Assert.Equal("6.1", version);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    [InlineData(-5L)]
    public void FromFileTime_never_values_are_null(long value) => Assert.Null(WindowsVersionFormatter.FromFileTime(value));
}
