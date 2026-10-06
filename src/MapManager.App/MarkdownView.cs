using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MapManager.App;

// Renders the small markdown subset used in release notes: headings, bullets, numbered lists, bold, italic, code and links.
public static class MarkdownView
{
    private static readonly Regex Inline = new(@"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<text>[^\]]+)\]\((?<url>[^)\s]+)\)|(?<![\w*])[*_](?<italic>[^*_\s][^*_]*?)[*_](?![\w*])", RegexOptions.Compiled);

    public static void Render(RichTextBox box, string markdown)
    {
        var links = new List<(int Start, int Length, string Url)>();
        // Character formatting applied before the native control exists is discarded when it is created.
        if (!box.IsHandleCreated) _ = box.Handle;
        box.Clear();
        var baseFont = box.Font;
        bool previousBlank = true;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { if (!previousBlank) Append(box, "\n", baseFont);previousBlank = true;continue; }
            previousBlank = false;
            int indent = 0, hanging = 0;var font = baseFont;string prefix = "";
            if (Regex.Match(line, @"^(#{1,6})\s+(.*)$") is { Success: true } heading)
            {
                line = heading.Groups[2].Value;
                font = new Font(baseFont.FontFamily, baseFont.Size * (heading.Groups[1].Length <= 2 ? 1.3f : 1.1f), FontStyle.Bold);
            }
            else if (Regex.Match(raw, @"^(\s*)[-*+]\s+(.*)$") is { Success: true } bullet)
            {
                line = bullet.Groups[2].Value;prefix = "•  ";
                indent = 12 + bullet.Groups[1].Length * 8;hanging = 14;
            }
            else if (Regex.Match(raw, @"^(\s*)(\d+)[.)]\s+(.*)$") is { Success: true } numbered)
            {
                line = numbered.Groups[3].Value;prefix = numbered.Groups[2].Value + ".  ";
                indent = 12 + numbered.Groups[1].Length * 8;hanging = 18;
            }
            box.SelectionStart = box.TextLength;box.SelectionIndent = indent;box.SelectionHangingIndent = hanging;
            if (prefix.Length > 0) Append(box, prefix, font);
            int position = 0;
            foreach (Match m in Inline.Matches(line))
            {
                Append(box, line[position..m.Index], font);
                if (m.Groups["bold"].Success) Append(box, m.Groups["bold"].Value, new Font(font, font.Style | FontStyle.Bold));
                else if (m.Groups["code"].Success) Append(box, m.Groups["code"].Value, new Font("Consolas", font.Size * 0.95f, font.Style));
                else if (m.Groups["italic"].Success) Append(box, m.Groups["italic"].Value, new Font(font, font.Style | FontStyle.Italic));
                else
                {
                    var start = box.TextLength;
                    Append(box, m.Groups["text"].Value, new Font(font, font.Style | FontStyle.Underline), Color.FromArgb(0, 104, 118));
                    links.Add((start, box.TextLength - start, m.Groups["url"].Value));
                }
                position = m.Index + m.Length;
            }
            Append(box, line[position..] + "\n", font);
        }
        box.SelectionStart = 0;box.SelectionLength = 0;
        box.Tag = links;
    }

    // Opens rendered https links; DetectUrls still handles bare URLs.
    public static void EnableLinks(RichTextBox box)
    {
        box.MouseClick += (_, e) =>
        {
            if (box.Tag is not List<(int Start, int Length, string Url)> links) return;
            int index = box.GetCharIndexFromPosition(e.Location);
            foreach (var link in links)
                if (index >= link.Start && index < link.Start + link.Length && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });return; }
        };
        box.MouseMove += (_, e) =>
        {
            int index = box.GetCharIndexFromPosition(e.Location);
            box.Cursor = box.Tag is List<(int Start, int Length, string Url)> links && links.Any(l => index >= l.Start && index < l.Start + l.Length) ? Cursors.Hand : Cursors.IBeam;
        };
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
