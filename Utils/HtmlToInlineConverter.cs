using System.Text.RegularExpressions;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using HtmlAgilityPack;

namespace ProductDescGen.Utils;

public static partial class HtmlToInlineConverter
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Ws();

    public static InlineCollection Build(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var result = new InlineCollection();
        foreach (var n in doc.DocumentNode.ChildNodes) Add(result, n);
        return result;
    }

    private static void Add(InlineCollection target, HtmlNode node)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = Ws().Replace(HtmlEntity.DeEntitize(node.InnerText), " ");
            if (text.Length > 0) target.Add(new Run(text));
            return;
        }
        if (node.NodeType != HtmlNodeType.Element) return;

        switch (node.Name)
        {
            case "br": target.Add(new LineBreak()); return;
            case "script" or "style" or "head": return;
        }

        var span = node.Name switch
        {
            "b" or "strong" => new Bold(),
            "i" or "em"     => new Italic(),
            "u"             => new Underline(),
            _               => new Span()
        };

        switch (node.Name)
        {
            case "h1": span.FontSize = 24; span.FontWeight = FontWeight.Bold; break;
            case "h2": span.FontSize = 20; span.FontWeight = FontWeight.Bold; break;
            case "h3": span.FontSize = 16; span.FontWeight = FontWeight.Bold; break;
            case "code": span.FontFamily = new FontFamily("Consolas, Menlo, monospace"); break;
            case "a":
                span.Foreground = Brushes.DodgerBlue;
                span.TextDecorations = TextDecorations.Underline;
                break;
            case "li": span.Inlines.Add(new Run("• ")); break;
        }

        foreach (var child in node.ChildNodes) Add(span.Inlines, child);
        target.Add(span);

        if (node.Name is "p" or "div" or "li" or "h1" or "h2" or "h3" or "ul" or "ol")
            target.Add(new LineBreak());
    }
}