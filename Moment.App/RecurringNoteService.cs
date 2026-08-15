using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Moment;

public sealed class RecurringNoteService
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly MomentSettings settings;

    public RecurringNoteService(MomentSettings settings) => this.settings = settings;

    public string WriteCapture(string text, DateTimeOffset timestamp)
    {
        var clean = text.Trim();
        if (clean.Length == 0) throw new InvalidOperationException("Type something before saving the capture.");
        if (string.IsNullOrWhiteSpace(settings.WorkspacePath) || !Directory.Exists(settings.WorkspacePath))
            throw new InvalidOperationException("Choose an existing workspace folder before writing a recurring note.");

        var provider = RecurringNoteProviderSettings.Load(settings.WorkspacePath);
        var local = timestamp.ToLocalTime();
        var filenameFormat = string.IsNullOrWhiteSpace(settings.RecurringNoteFilenameFormat)
            ? (string.IsNullOrWhiteSpace(provider.Format) ? "YYYY-MM-DD" : provider.Format)
            : settings.RecurringNoteFilenameFormat.Trim();
        var filename = MomentFormat.Format(local, filenameFormat);
        var prefix = settings.RecurringNoteFilenamePrefix?.Trim() ?? "";
        filename = WorkspacePath.SanitizeFilename(prefix + filename);
        var relative = WorkspacePath.Combine(provider.Folder, filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? filename : $"{filename}.md");
        var absolute = WorkspacePath.Resolve(settings.WorkspacePath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var gate = FileLocks.GetOrAdd(absolute, _ => new object());
        lock (gate)
        {
            // A generated name collision is intentional recurring-note behavior:
            // reuse the existing file and append the new capture rather than
            // overwriting it or silently creating a duplicate.
            if (!File.Exists(absolute))
                WriteAtomic(absolute, TemplateRenderer.Load(settings.WorkspacePath, provider.Template, filename, local, provider.Format));
            var current = File.ReadAllText(absolute, Encoding.UTF8);
            var heading = settings.IncludeTimestamp
                ? MomentFormat.Format(local, string.IsNullOrWhiteSpace(settings.RecurringNoteTimestampFormat) ? "HH:mm" : settings.RecurringNoteTimestampFormat)
                : "";
            WriteAtomic(absolute, NativeCaptureInsertion.Insert(current, heading, clean, settings));
        }
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public RecurringNoteProviderSettings ReadProviderSettings() => RecurringNoteProviderSettings.Load(settings.WorkspacePath);

    private static void WriteAtomic(string path, string content)
    {
        var temporary = $"{path}.moment-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(temporary, path, null); }
                catch (IOException) { File.Move(temporary, path, true); }
            }
            else File.Move(temporary, path);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
}

public sealed record RecurringNoteProviderSettings(string Folder, string Format, string Template)
{
    public static RecurringNoteProviderSettings Load(string workspace)
    {
        var periodic = Path.Combine(workspace, ".obsidian", "plugins", "periodic-notes", "data.json");
        if (Read(periodic, out var periodicDoc) && GetObject(periodicDoc, "daily", out var recurringProvider) && GetBool(recurringProvider, "enabled", out var enabled) && enabled)
            return From(recurringProvider);
        var core = Path.Combine(workspace, ".obsidian", "daily-notes.json");
        if (Read(core, out var coreDoc)) return From(coreDoc);
        return new RecurringNoteProviderSettings("", "YYYY-MM-DD", "");
    }

    private static RecurringNoteProviderSettings From(JsonElement value)
    {
        var format = GetString(value, "format");
        return new RecurringNoteProviderSettings(GetString(value, "folder"), string.IsNullOrWhiteSpace(format) ? "YYYY-MM-DD" : format, GetString(value, "template"));
    }

    private static bool Read(string path, out JsonElement value)
    {
        value = default;
        if (!File.Exists(path)) return false;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); value = doc.RootElement.Clone(); return value.ValueKind == JsonValueKind.Object; }
        catch { return false; }
    }

    private static string GetString(JsonElement value, string name)
    {
        foreach (var p in value.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString()?.Trim() ?? "";
        return "";
    }

    private static bool GetBool(JsonElement value, string name, out bool result)
    {
        result = false;
        foreach (var p in value.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False))
            { result = p.Value.GetBoolean(); return true; }
        return false;
    }

    private static bool GetObject(JsonElement value, string name, out JsonElement result)
    {
        foreach (var p in value.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object)
            { result = p.Value; return true; }
        result = default;
        return false;
    }
}

internal static class WorkspacePath
{
    public static string Combine(string folder, string name)
    {
        var safeFolder = Sanitize(folder);
        var safeName = name.Replace('\\', '/').Trim('/');
        if (safeName.Length == 0 || safeName.Split('/').Any(p => p is "" or "..")) throw new InvalidOperationException("The recurring note name is invalid.");
        return safeFolder.Length == 0 ? safeName : $"{safeFolder}/{safeName}";
    }

    public static string Resolve(string root, string relative)
    {
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The path escapes the selected workspace.");
        return full;
    }

    public static string Sanitize(string value)
    {
        var normalized = (value ?? "").Trim().Trim('/', '\\').Replace('\\', '/');
        if (normalized is "" or ".") return "";
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(p => p is "" or "..")) throw new InvalidOperationException("Workspace folders must remain relative to the selected workspace.");
        return normalized;
    }

    public static string SanitizeFilename(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\' };
        var clean = new string((value ?? "").Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim().TrimEnd('.', ' ');
        if (clean.Length == 0 || clean is "." or "..") throw new InvalidOperationException("The filename is empty or invalid.");
        return clean;
    }
}

internal static class MomentFilename
{
    public const string DefaultFormat = "YYYY-MM-DD HH-mm-ss-SSS";

    public static string Format(DateTimeOffset timestamp, string? format, string? prefix)
    {
        var pattern = string.IsNullOrWhiteSpace(format) ? DefaultFormat : format.Trim();
        var stem = MomentFormat.Format(timestamp.ToLocalTime(), pattern);
        return WorkspacePath.SanitizeFilename($"{prefix?.Trim() ?? ""}{stem}");
    }
}

internal static class TemplateRenderer
{
    private static readonly Regex Token = new(@"{{\s*(?<name>date|time|title|yesterday|tomorrow)\s*(?<offset>[+-]\d+)?(?<unit>[yqmwdhs])?\s*(?::\s*(?<format>[^}]+))?\s*}}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Load(string workspace, string templatePath, string filename, DateTimeOffset capture, string providerFormat)
    {
        if (string.IsNullOrWhiteSpace(templatePath) || templatePath.Trim() == "/") return "";
        try
        {
            var path = WorkspacePath.Resolve(workspace, templatePath.Trim());
            if (!File.Exists(path)) return "";
            return Token.Replace(File.ReadAllText(path, Encoding.UTF8), m => Render(m, filename, capture, providerFormat));
        }
        catch { return ""; }
    }

    private static string Render(Match match, string filename, DateTimeOffset capture, string providerFormat)
    {
        var name = match.Groups["name"].Value.ToLowerInvariant();
        if (name == "title") return filename;
        var value = capture;
        if (name == "yesterday") value = value.AddDays(-1);
        if (name == "tomorrow") value = value.AddDays(1);
        var offset = match.Groups["offset"].Value;
        if (offset.Length > 0 && int.TryParse(offset, out var amount))
        {
            value = match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "y" => value.AddYears(amount), "q" => value.AddMonths(amount * 3), "m" => value.AddMonths(amount),
                "w" => value.AddDays(amount * 7), "d" => value.AddDays(amount), "h" => value.AddHours(amount),
                "s" => value.AddSeconds(amount), _ => value
            };
        }
        var format = match.Groups["format"].Value.Trim();
        return name == "time" ? MomentFormat.Format(DateTimeOffset.Now, format.Length == 0 ? "HH:mm" : format) : MomentFormat.Format(value, format.Length == 0 ? providerFormat : format);
    }
}

internal static class MomentFormat
{
    private static readonly string[] Tokens = { "YYYY", "MMMM", "dddd", "MMM", "ddd", "SSS", "YY", "MM", "DD", "HH", "hh", "mm", "ss", "ZZ", "Do", "M", "D", "H", "h", "m", "s", "A", "a", "Z", "Q" };

    public static string Format(DateTimeOffset value, string format)
    {
        if (string.IsNullOrWhiteSpace(format)) format = "YYYY-MM-DD";
        var net = new StringBuilder();
        for (var i = 0; i < format.Length;)
        {
            if (format[i] == '[')
            {
                var end = format.IndexOf(']', i + 1);
                if (end >= 0) { Literal(net, format[(i + 1)..end]); i = end + 1; continue; }
            }
            var token = Tokens.FirstOrDefault(t => i + t.Length <= format.Length && format.AsSpan(i, t.Length).SequenceEqual(t));
            if (token is null) { Literal(net, format[i].ToString()); i++; continue; }
            if (token == "Q") Literal(net, (((value.Month - 1) / 3) + 1).ToString(CultureInfo.InvariantCulture));
            else if (token == "Do") net.Append('d');
            else if (token is "Z" or "ZZ") net.Append("zzz");
            else if (token == "a") net.Append("tt");
            else net.Append(token switch
            {
                "YYYY" => "yyyy", "YY" => "yy", "MMMM" => "MMMM", "MMM" => "MMM", "dddd" => "dddd", "ddd" => "ddd",
                "SSS" => "fff", "MM" => "MM", "DD" => "dd", "HH" => "HH", "hh" => "hh", "mm" => "mm", "ss" => "ss",
                "M" => "M", "D" => "d", "H" => "H", "h" => "h", "m" => "m", "s" => "s", "A" => "tt", _ => "yyyy-MM-dd"
            });
            i += token.Length;
        }
        try { return value.ToString(net.ToString(), CultureInfo.CurrentCulture); }
        catch { return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }

    private static void Literal(StringBuilder builder, string value) => builder.Append('\'').Append(value.Replace("'", "''", StringComparison.Ordinal)).Append('\'');
}

internal static class NativeCaptureInsertion
{
    private sealed record Heading(int Start, int Level, string Text);

    public static string Insert(string current, string heading, string text, MomentSettings settings)
    {
        var block = string.IsNullOrWhiteSpace(heading) ? text.Trim() : $"## {heading}\n\n{text.Trim()}";
        return settings.RecurringNoteInsertionLocation switch
        {
            "beginning" => InsertAt(current, FrontmatterEnd(current), block),
            "under-heading" => UnderHeading(current, block, settings.RecurringNoteTargetHeading, settings.RecurringNoteMissingHeadingBehavior),
            _ => InsertAt(current, current.Length, block)
        };
    }

    private static string UnderHeading(string current, string block, string configured, string behavior)
    {
        var target = Normalize(configured);
        if (target.Text.Length == 0) throw new InvalidOperationException("Set a target heading before using under-heading insertion.");
        var headings = FindHeadings(current);
        var index = headings.FindIndex(h => string.Equals(h.Text, target.Text, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            if (behavior == "error") throw new InvalidOperationException($"The target heading \"{target.Text}\" was not found.");
            if (behavior == "end") return InsertAt(current, current.Length, block);
            var newHeading = $"{new string('#', target.Level)} {target.Text}";
            var withHeading = InsertAt(current, current.Length, newHeading);
            return InsertAt(withHeading, withHeading.Length, block);
        }
        var following = headings.Skip(index + 1).FirstOrDefault(h => h.Level <= headings[index].Level);
        return InsertAt(current, following?.Start ?? current.Length, block);
    }

    private static string InsertAt(string content, int index, string block)
    {
        var before = content[..index];
        var after = content[index..];
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = newline == "\n" ? block : block.Replace("\n", newline, StringComparison.Ordinal);
        return before + Suffix(before, newline) + normalized + Prefix(after, newline) + after;
    }

    private static string Suffix(string value, string newline) => value.Length == 0 || value.EndsWith(newline + newline, StringComparison.Ordinal) ? "" : value.EndsWith(newline, StringComparison.Ordinal) ? newline : newline + newline;
    private static string Prefix(string value, string newline) => value.Length == 0 ? newline : value.StartsWith(newline + newline, StringComparison.Ordinal) ? "" : value.StartsWith(newline, StringComparison.Ordinal) ? newline : newline + newline;
    private static int FrontmatterEnd(string content)
    {
        if (!content.StartsWith("---\n", StringComparison.Ordinal) && !content.StartsWith("---\r\n", StringComparison.Ordinal)) return 0;
        var match = Regex.Match(content, @"^---\r?\n[\s\S]*?\r?\n---[ \t]*\r?\n?");
        return match.Success ? match.Length : 0;
    }

    private static List<Heading> FindHeadings(string content)
    {
        var result = new List<Heading>();
        var fence = false;
        var marker = (char)0;
        var offset = 0;
        while (offset < content.Length)
        {
            var newline = content.IndexOf('\n', offset);
            var end = newline < 0 ? content.Length : newline + 1;
            var line = content[offset..end].TrimEnd('\r', '\n');
            var fenceMatch = Regex.Match(line, @"^\s*(?:\x60{3,}|~{3,})").Value;
            if (fenceMatch.Length > 0)
            {
                if (!fence) { fence = true; marker = fenceMatch.Contains('~') ? '~' : (char)96; }
                else if (fenceMatch.Contains(marker)) fence = false;
            }
            else if (!fence)
            {
                var parsed = Parse(line);
                if (parsed is not null) result.Add(new Heading(offset, parsed.Value.Level, parsed.Value.Text));
            }
            offset = end;
        }
        return result;
    }

    private static (int Level, string Text)? Parse(string line)
    {
        var match = Regex.Match(line, @"^(#{1,6})[ \t]+(.+?)[ \t]*#*[ \t]*$");
        return match.Success ? (match.Groups[1].Length, match.Groups[2].Value.Trim()) : null;
    }

    private static (int Level, string Text) Normalize(string value)
    {
        var trimmed = (value ?? "").Trim();
        var parsed = Parse(trimmed);
        return parsed ?? (2, Regex.Replace(trimmed, @"[ \t]+#+[ \t]*$", "").Trim());
    }
}
