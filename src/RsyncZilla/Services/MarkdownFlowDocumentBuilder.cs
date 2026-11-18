using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RsyncZilla.Services
{
    /// <summary>
    /// Converts GitHub-flavored Markdown release notes into a cleanly formatted native WPF FlowDocument
    /// without external dependencies or WebView2 overhead.
    /// </summary>
    public static class MarkdownFlowDocumentBuilder
    {
        private static readonly FontFamily BodyFont = new("Segoe UI, Arial");
        private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Courier New");

        private static readonly Brush Heading1Brush = CreateFrozenBrush(0, 90, 158);    // #005A9E
        private static readonly Brush Heading2Brush = CreateFrozenBrush(15, 23, 42);    // #0F172A
        private static readonly Brush Heading3Brush = CreateFrozenBrush(30, 64, 175);   // #1E40AF
        private static readonly Brush BodyBrush = CreateFrozenBrush(30, 41, 59);        // #1E293B
        private static readonly Brush MutedBrush = CreateFrozenBrush(100, 116, 139);    // #64748B
        private static readonly Brush DividerBrush = CreateFrozenBrush(226, 232, 240);  // #E2E8F0
        private static readonly Brush CodeBgBrush = CreateFrozenBrush(241, 245, 249);   // #F1F5F9
        private static readonly Brush CodeFgBrush = CreateFrozenBrush(15, 76, 129);     // #0F4C81
        private static readonly Brush CodeBlockBgBrush = CreateFrozenBrush(248, 250, 252); // #F8FAFC
        private static readonly Brush LinkBrush = CreateFrozenBrush(0, 102, 204);       // #0066CC

        private static readonly Regex HeadingRegex = new(@"^(#{1,4})\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex BulletRegex = new(@"^(\s*)[-*+]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex OrderedRegex = new(@"^(\s*)(\d+)\.\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex HorizontalRuleRegex = new(@"^\s*([-*_])\1{2,}\s*$", RegexOptions.Compiled);

        // Inline token regex: links, bold, inline code, <kbd>, italic
        private static readonly Regex InlineTokenRegex = new(
            @"(\[([^\]]+)\]\((https?://[^\s\)]+|[^\s\)]+)\))" +   // Group 1,2,3: [text](url)
            @"|(\*\*(.+?)\*\*)" +                                 // Group 4,5: **bold**
            @"|(`([^`]+)`)" +                                     // Group 6,7: `code`
            @"|(<kbd>(.+?)</kbd>)" +                              // Group 8,9: <kbd>key</kbd>
            @"|(\*([^\*\n]+)\*)",                                 // Group 10,11: *italic*
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static FlowDocument Build(string? markdown)
        {
            var doc = new FlowDocument
            {
                FontFamily = BodyFont,
                FontSize = 12.5,
                Foreground = BodyBrush,
                PagePadding = new Thickness(6, 4, 10, 8),
                TextAlignment = TextAlignment.Left
            };

            if (string.IsNullOrWhiteSpace(markdown))
            {
                doc.Blocks.Add(new Paragraph(new Run("No detailed release notes provided."))
                {
                    Foreground = MutedBrush,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                return doc;
            }

            var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');

            bool inCodeBlock = false;
            var codeBuffer = new StringBuilder();
            var paragraphBuffer = new List<string>();

            void FlushParagraph()
            {
                if (paragraphBuffer.Count == 0) return;
                var joined = string.Join(" ", paragraphBuffer).Trim();
                paragraphBuffer.Clear();
                if (string.IsNullOrEmpty(joined)) return;

                var p = new Paragraph
                {
                    Margin = new Thickness(0, 3, 0, 6),
                    LineHeight = 19
                };
                AppendFormattedInlines(p.Inlines, joined);
                doc.Blocks.Add(p);
            }

            void FlushCodeBlock()
            {
                var codeText = codeBuffer.ToString().TrimEnd('\n');
                codeBuffer.Clear();
                if (string.IsNullOrEmpty(codeText)) return;

                var p = new Paragraph(new Run(codeText))
                {
                    FontFamily = MonoFont,
                    FontSize = 11.5,
                    Foreground = BodyBrush,
                    Background = CodeBlockBgBrush,
                    BorderBrush = DividerBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 4, 0, 8),
                    LineHeight = 17
                };
                doc.Blocks.Add(p);
            }

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();

                // Fenced code blocks ```
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    if (inCodeBlock)
                    {
                        FlushCodeBlock();
                        inCodeBlock = false;
                    }
                    else
                    {
                        inCodeBlock = true;
                        codeBuffer.Clear();
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBuffer.AppendLine(rawLine);
                    continue;
                }

                // Blank line
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    FlushParagraph();
                    continue;
                }

                // Horizontal rule (---)
                if (HorizontalRuleRegex.IsMatch(rawLine))
                {
                    FlushParagraph();
                    var sep = new Border
                    {
                        Height = 1,
                        Background = DividerBrush,
                        Margin = new Thickness(0, 8, 0, 8)
                    };
                    doc.Blocks.Add(new BlockUIContainer(sep) { Margin = new Thickness(0) });
                    continue;
                }

                // Heading (#, ##, ###, ####)
                var headingMatch = HeadingRegex.Match(trimmed);
                if (headingMatch.Success)
                {
                    FlushParagraph();
                    int level = headingMatch.Groups[1].Value.Length;
                    var text = headingMatch.Groups[2].Value.Trim();

                    var p = new Paragraph
                    {
                        FontWeight = FontWeights.Bold,
                        FontSize = level switch
                        {
                            1 => 16.5,
                            2 => 14.5,
                            3 => 13.0,
                            _ => 12.5
                        },
                        Foreground = level switch
                        {
                            1 => Heading1Brush,
                            2 => Heading2Brush,
                            _ => Heading3Brush
                        },
                        Margin = level switch
                        {
                            1 => new Thickness(0, 2, 0, 8),
                            2 => new Thickness(0, 10, 0, 5),
                            _ => new Thickness(0, 7, 0, 4)
                        }
                    };
                    AppendFormattedInlines(p.Inlines, text);
                    doc.Blocks.Add(p);
                    continue;
                }

                // Unordered list item (-, *, +) with indentation nesting
                var bulletMatch = BulletRegex.Match(rawLine);
                if (bulletMatch.Success)
                {
                    FlushParagraph();
                    int indentSpaces = bulletMatch.Groups[1].Value.Replace("\t", "  ").Length;
                    int depth = Math.Min(indentSpaces / 2, 3);
                    string bulletChar = depth switch
                    {
                        0 => "•  ",
                        1 => "◦  ",
                        _ => "▪  "
                    };
                    double leftMargin = 16 + (depth * 16);

                    var p = new Paragraph
                    {
                        Margin = new Thickness(leftMargin, 1.5, 0, 3),
                        TextIndent = -12,
                        LineHeight = 19
                    };
                    p.Inlines.Add(new Run(bulletChar) { Foreground = MutedBrush, FontWeight = FontWeights.Bold });
                    AppendFormattedInlines(p.Inlines, bulletMatch.Groups[2].Value.Trim());
                    doc.Blocks.Add(p);
                    continue;
                }

                // Ordered list item (1. 2. ...)
                var orderedMatch = OrderedRegex.Match(rawLine);
                if (orderedMatch.Success)
                {
                    FlushParagraph();
                    int indentSpaces = orderedMatch.Groups[1].Value.Replace("\t", "  ").Length;
                    int depth = Math.Min(indentSpaces / 2, 3);
                    double leftMargin = 20 + (depth * 16);

                    var p = new Paragraph
                    {
                        Margin = new Thickness(leftMargin, 1.5, 0, 3),
                        TextIndent = -16,
                        LineHeight = 19
                    };
                    p.Inlines.Add(new Run($"{orderedMatch.Groups[2].Value}. ") { Foreground = MutedBrush, FontWeight = FontWeights.SemiBold });
                    AppendFormattedInlines(p.Inlines, orderedMatch.Groups[3].Value.Trim());
                    doc.Blocks.Add(p);
                    continue;
                }

                // Normal paragraph text
                paragraphBuffer.Add(trimmed);
            }

            if (inCodeBlock)
            {
                FlushCodeBlock();
            }
            FlushParagraph();

            return doc;
        }

        private static void AppendFormattedInlines(InlineCollection inlines, string text, bool isBold = false, bool isItalic = false)
        {
            if (string.IsNullOrEmpty(text)) return;

            int lastIndex = 0;
            foreach (Match match in InlineTokenRegex.Matches(text))
            {
                if (match.Index > lastIndex)
                {
                    AddStyledRun(inlines, text.Substring(lastIndex, match.Index - lastIndex), isBold, isItalic);
                }

                if (match.Groups[1].Success)
                {
                    // [text](url)
                    var linkText = match.Groups[2].Value;
                    var linkUrl = match.Groups[3].Value;

                    if (Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        var hyperlink = new Hyperlink
                        {
                            NavigateUri = uri,
                            Foreground = LinkBrush,
                            ToolTip = linkUrl
                        };
                        hyperlink.RequestNavigate += (s, e) =>
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = e.Uri.AbsoluteUri,
                                    UseShellExecute = true
                                });
                                e.Handled = true;
                            }
                            catch { }
                        };
                        AppendFormattedInlines(hyperlink.Inlines, linkText, isBold, isItalic);
                        inlines.Add(hyperlink);
                    }
                    else
                    {
                        AppendFormattedInlines(inlines, linkText, isBold, isItalic);
                    }
                }
                else if (match.Groups[4].Success)
                {
                    // **bold**
                    AppendFormattedInlines(inlines, match.Groups[5].Value, isBold: true, isItalic: isItalic);
                }
                else if (match.Groups[6].Success)
                {
                    // `code`
                    var codeRun = new Run($" {match.Groups[7].Value} ")
                    {
                        FontFamily = MonoFont,
                        FontSize = 11.5,
                        Background = CodeBgBrush,
                        Foreground = CodeFgBrush,
                        FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal
                    };
                    inlines.Add(codeRun);
                }
                else if (match.Groups[8].Success)
                {
                    // <kbd>key</kbd>
                    var kbdRun = new Run($" {match.Groups[9].Value} ")
                    {
                        FontFamily = MonoFont,
                        FontSize = 11,
                        Background = CodeBgBrush,
                        Foreground = Heading2Brush,
                        FontWeight = FontWeights.SemiBold
                    };
                    inlines.Add(kbdRun);
                }
                else if (match.Groups[10].Success)
                {
                    // *italic*
                    AppendFormattedInlines(inlines, match.Groups[11].Value, isBold: isBold, isItalic: true);
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                AddStyledRun(inlines, text.Substring(lastIndex), isBold, isItalic);
            }
        }

        private static void AddStyledRun(InlineCollection inlines, string text, bool isBold, bool isItalic)
        {
            if (string.IsNullOrEmpty(text)) return;
            var run = new Run(text);
            if (isBold) run.FontWeight = FontWeights.SemiBold;
            if (isItalic) run.FontStyle = FontStyles.Italic;
            inlines.Add(run);
        }
    }
}
