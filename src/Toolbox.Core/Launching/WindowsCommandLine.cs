using System.Text;

namespace Toolbox.Launching;

public static class WindowsCommandLine
{
    private static readonly System.Buffers.SearchValues<char> NeedsQuoting = System.Buffers.SearchValues.Create(" \t\n\v\"");

    public static string Join(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            AppendQuoted(builder, argument ?? string.Empty);
        }

        return builder.ToString();
    }

    private static void AppendQuoted(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(NeedsQuoting) < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
    }
}
