using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MapManager.App;

// Renders the markdown used in map READMEs and release notes: headings, paragraphs, lists, tables,
// bold, italic, code and links. Images are omitted; relative links resolve against a base URL.
public static class MarkdownView
{
    private static readonly Regex Inline = new(
        @"\\(?<esc>[\\`*_{}\[\]()#+\-.!|])|!\[(?<alt>[^\]]*)\]\((?<img>[^)\s]+)\)|\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<text>[^\]]+)\]\((?<url>[^)\s]+)\)|(?<![\w*])[*_](?<italic>[^*_\s][^*_]*?)[*_](?![\w*])",
        RegexOptions.Compiled);
    private static readonly Color LinkColor = Color.FromArgb(0, 104, 118);

    private sealed class State(string markdown, Uri? baseUri, string? plainSuffix)
    {
        public string Markdown { get; } = markdown;
        public Uri? BaseUri { get; } = baseUri;
        public string? PlainSuffix { get; } = plainSuffix;
        public List<(int Start, int Length, string Url)> Links { get; } = [];
    }

    // Plain-text notes (README.txt) are shown as written; only markdown files are formatted.
    public static void RenderNotes(RichTextBox box, string text, MapManager.Core.MapEntry map)
    {
        if (map.NotesPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) { box.Tag = null;box.Clear();box.SelectionIndent = box.SelectionHangingIndent = 0;box.Text = text; }
        else Render(box, text, NotesBase(map));
    }

    // plainSuffix is appended unformatted after the markdown, for plain-text notes under a formatted heading.
    public static void Render(RichTextBox box, string markdown, Uri? baseUri = null, string? plainSuffix = null)
    {
        // Character formatting applied before the native control exists is discarded when it is created.
        if (!box.IsHandleCreated) _ = box.Handle;
        var state = new State(markdown, baseUri, plainSuffix);
        box.Tag = state;
        box.SuspendLayout();
        box.Clear();
        var font = box.Font;
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var text = new StringBuilder();
        (string Prefix, int Indent, int Hanging)? item = null;
        bool needGap = false, inCode = false;

        void Gap() { if (needGap && box.TextLength > 0) Append(box, "\n", font); needGap = false; }
        void Flush()
        {
            if (text.Length == 0) return;
            Gap();
            var (prefix, indent, hanging) = item ?? ("", 0, 0);
            StartParagraph(box, indent, hanging);
            if (prefix.Length > 0) Append(box, prefix, font);
            WriteInline(box, text.ToString(), font, state);
            Append(box, "\n", font);
            text.Clear();item = null;
            // List items sit together; other blocks get a blank line before the next one.
            needGap = indent == 0;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal)) { Flush();inCode = !inCode;if (!inCode) needGap = true;continue; }
            if (inCode)
            {
                Gap();StartParagraph(box, 12, 0);
                Append(box, raw.TrimEnd() + "\n", new Font("Consolas", font.Size * 0.95f));continue;
            }
            if (line.Length == 0) { Flush();needGap = true;continue; }
            if (Regex.Match(line, @"^(#{1,6})\s+(.*?)\s*#*$") is { Success: true } heading)
            {
                Flush();needGap = box.TextLength > 0;Gap();StartParagraph(box, 0, 0);
                var size = heading.Groups[1].Length switch { 1 => 1.35f, 2 => 1.2f, _ => 1.05f };
                WriteInline(box, heading.Groups[2].Value, new Font(font.FontFamily, font.Size * size, FontStyle.Bold), state);
                Append(box, "\n", font);needGap = false;continue;
            }
            if (Regex.IsMatch(line, @"^(-{3,}|\*{3,}|_{3,})$") || Regex.IsMatch(line, @"^!\[[^\]]*\]\([^)]*\)$")) { Flush();needGap = true;continue; }
            if (line.StartsWith('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                Flush();
                var header = Cells(line);var rows = new List<List<string>>();
                for (i += 2; i < lines.Length && lines[i].Trim().StartsWith('|'); i++) rows.Add(Cells(lines[i].Trim()));
                i--;
                Gap();WriteTable(box, header, rows, font, state);needGap = true;continue;
            }
            if (Regex.Match(raw, @"^(\s*)[-*+]\s+(.*)$") is { Success: true } bullet)
            {
                Flush();item = ("•  ", 12 + bullet.Groups[1].Length * 6, 14);text.Append(bullet.Groups[2].Value.Trim());continue;
            }
            if (Regex.Match(raw, @"^(\s*)(\d+)[.)]\s+(.*)$") is { Success: true } numbered)
            {
                Flush();item = (numbered.Groups[2].Value + ".  ", 12 + numbered.Groups[1].Length * 6, 20);text.Append(numbered.Groups[3].Value.Trim());continue;
            }
            // Hard-wrapped lines continue the current paragraph or list item.
            if (text.Length > 0) text.Append(' ');
            text.Append(line);
        }
        Flush();
        if (plainSuffix != null) { needGap = true;Gap();StartParagraph(box, 0, 0);Append(box, plainSuffix.Replace("\r\n", "\n"), font); }
        box.Select(0, 0);
        box.ResumeLayout();
    }

    // Narrow panels cannot fit wide tables, so two-column tables become a list and wider ones become labelled records.
    private static void WriteTable(RichTextBox box, List<string> header, List<List<string>> rows, Font font, State state)
    {
        var bold = new Font(font, FontStyle.Bold);
        foreach (var row in rows)
        {
            if (header.Count <= 2)
            {
                StartParagraph(box, 12, 14);Append(box, "•  ", font);
                WriteInline(box, row.ElementAtOrDefault(0) ?? "", bold, state);
                if (row.ElementAtOrDefault(1) is { Length: > 0 } detail) { Append(box, " — ", font);WriteInline(box, detail, font, state); }
                Append(box, "\n", font);
                continue;
            }
            StartParagraph(box, 0, 0);WriteInline(box, row.ElementAtOrDefault(0) ?? "", bold, state);Append(box, "\n", font);
            StartParagraph(box, 14, 0);
            bool first = true;
            for (int c = 1; c < header.Count && c < row.Count; c++)
            {
                if (row[c].Length == 0) continue;
                if (!first) Append(box, "   ", font);
                Append(box, StripInline(header[c]) + ": ", font, Color.DimGray);
                WriteInline(box, row[c], font, state);first = false;
            }
            Append(box, "\n", font);
        }
    }

    private static void WriteInline(RichTextBox box, string text, Font font, State state)
    {
        int position = 0;
        foreach (Match m in Inline.Matches(text))
        {
            Append(box, text[position..m.Index], font);
            if (m.Groups["esc"].Success) Append(box, m.Groups["esc"].Value, font);
            else if (m.Groups["img"].Success) { }
            else if (m.Groups["bold"].Success) WriteInline(box, m.Groups["bold"].Value, new Font(font, font.Style | FontStyle.Bold), state);
            else if (m.Groups["code"].Success) Append(box, m.Groups["code"].Value, new Font("Consolas", font.Size * 0.95f, font.Style & ~FontStyle.Underline));
            else if (m.Groups["italic"].Success) WriteInline(box, m.Groups["italic"].Value, new Font(font, font.Style | FontStyle.Italic), state);
            else
            {
                var start = box.TextLength;
                Append(box, StripInline(m.Groups["text"].Value), new Font(font, font.Style | FontStyle.Underline), LinkColor);
                if (Resolve(m.Groups["url"].Value, state.BaseUri) is { } url) state.Links.Add((start, box.TextLength - start, url));
            }
            position = m.Index + m.Length;
        }
        Append(box, text[position..], font);
    }

    private static string StripInline(string text) => Inline.Replace(text, m =>
        m.Groups["esc"].Success ? m.Groups["esc"].Value : m.Groups["bold"].Success ? m.Groups["bold"].Value : m.Groups["code"].Success ? m.Groups["code"].Value
        : m.Groups["italic"].Success ? m.Groups["italic"].Value : m.Groups["text"].Success ? m.Groups["text"].Value : "");

    private static string? Resolve(string url, Uri? baseUri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return absolute.Scheme == "https" ? absolute.AbsoluteUri : null;
        return baseUri != null && Uri.TryCreate(baseUri, url, out var relative) && relative.Scheme == "https" ? relative.AbsoluteUri : null;
    }

    // Map and pack notes live in the maps repository at the catalog's pinned commit; relative links resolve there.
    public static Uri NotesBase(MapManager.Core.MapEntry map) => new(
        $"{MapManager.Core.RepositoryClient.RepositoryUrl}/blob/{map.Commit}/{string.Join('/', map.NotesPath.Split('/').Select(Uri.EscapeDataString))}");

    private static bool IsTableSeparator(string line) => Regex.IsMatch(line.Trim(), @"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$");
    private static List<string> Cells(string line) => line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();

    private static void StartParagraph(RichTextBox box, int indent, int hanging)
    {
        box.Select(box.TextLength, 0);
        box.SelectionIndent = indent;box.SelectionHangingIndent = hanging;
    }

    // Opens rendered https links, and re-renders after font or DPI changes, which reset RichTextBox formatting.
    public static void EnableLinks(RichTextBox box)
    {
        box.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && LinkAt(box, e.Location) is { } url)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };
        box.MouseMove += (_, e) => box.Cursor = LinkAt(box, e.Location) != null ? Cursors.Hand : Cursors.IBeam;
        // Bare URLs detected by the control; skip ones that are also rendered links so a click opens once.
        box.LinkClicked += (_, e) =>
        {
            if (box.Tag is State state && state.Links.Any(l => e.LinkStart >= l.Start && e.LinkStart < l.Start + l.Length)) return;
            if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        };
        box.FontChanged += (_, _) => { if (box.Tag is State state) Render(box, state.Markdown, state.BaseUri, state.PlainSuffix); };
    }

    private static string? LinkAt(RichTextBox box, Point location)
    {
        if (box.Tag is not State state || box.TextLength == 0) return null;
        int index = box.GetCharIndexFromPosition(location);
        var bounds = box.GetPositionFromCharIndex(index);
        if (Math.Abs(location.Y - bounds.Y) > box.Font.Height * 2) return null;
        foreach (var link in state.Links)
            if (index >= link.Start && index < link.Start + link.Length) return link.Url;
        return null;
    }

    private static void Append(RichTextBox box, string text, Font font, Color? color = null)
    {
        if (text.Length == 0) return;
        // Format after inserting; formatting an empty selection is not reliably applied to appended text.
        int start = box.TextLength;box.AppendText(text);
        box.Select(start, box.TextLength - start);
        box.SelectionFont = font;box.SelectionColor = color ?? box.ForeColor;
        box.Select(box.TextLength, 0);
    }
}
