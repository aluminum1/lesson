using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Colors;

using System.IO;

public class Program
{
    public static int betweenPagePadding = 20;

    public static int Main(string[] args)
    {
        string pdfPath;

        if (args.Length == 0)
        {
            // Try basic.pdf in the current working directory, then next to the executable.
            var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "box.pdf");
            var exeCandidate = Path.Combine(AppContext.BaseDirectory, "box.pdf");

            if (File.Exists(cwdCandidate))       pdfPath = cwdCandidate;
            else if (File.Exists(exeCandidate))  pdfPath = exeCandidate;
            else
            {
                Console.WriteLine("No argument supplied, and 'lecture.pdf' was not found " +
                                  "in the current directory or next to the executable.");
                return 1;
            }

            Console.WriteLine($"No argument supplied; using '{pdfPath}'.");
        }
        else
        {
            pdfPath = args[0];
        }

        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"File not found: {pdfPath}");
            return 1;
        }

        try
        {
            using var document = PdfDocument.Open(pdfPath);
            string filename = Path.GetFileNameWithoutExtension(pdfPath);
            ProcessDocument(document, filename);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing PDF: {ex.Message}");
            return 1;
        }
    }

    public static void ProcessDocument(PdfDocument document, string filename)
    {
        StringBuilder html = new StringBuilder();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine($"    <title>{filename}</title>");
        html.AppendLine("    <style>");
        html.AppendLine($"        .page {{ background-color: #bbbbbb; padding: 10px; }}");
        html.AppendLine("        svg { background-color: #ffffff; }");
        html.AppendLine("        svg { border: 1px solid #000000; }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");

        int pageNumber = 1;
        foreach (Page page in document.GetPages())
        {
            // Convert points to pixels (approximate conversion)
            double pixelWidth = page.Width * 1.33333;   // 1 point ≈ 1.33333 px
            double pixelHeight = page.Height * 1.33333;

            html.AppendLine($"<div class='page' id='page-{pageNumber}'>");
            html.AppendLine($"    <svg width='{pixelWidth:F0}px' height='{pixelHeight:F0}px' viewBox='0 0 {page.Width} {page.Height}'>");

            IEnumerable<Word> words = page.GetWords();
            Console.WriteLine($"Page {pageNumber}: {words.Count()} words");
            foreach (Word word in words)
            {
                
            }

            foreach (PdfPath path in page.ExperimentalAccess.Paths)
            {
                // Skip clipping paths for now; you could emit clipPath defs if you want to honor clipping.
                if (path.IsClipping) continue;

                // Build a single 'd' for the whole PdfPath (all its subpaths).
                var d = new StringBuilder();
                foreach (var subpath in path)
                {
                    foreach (var cmd in subpath.Commands)
                    {
                        // IMPORTANT: pass page.Height so WriteSvg can invert Y properly.
                        cmd.WriteSvg(d, page.Height);
                    }
                }

                // Compose SVG attributes based on actual graphics state
                string fill = path.IsFilled ? CssColor(path.FillColor) : "none";
                string stroke = path.IsStroked ? CssColor(path.StrokeColor) : "none";
                string? fillRule = path.IsFilled ? MapFillRule(path.FillingRule) : null;

                // Stroke styling (only emit when stroked)
                string? strokeWidth = path.IsStroked
                    ? ((double)path.LineWidth).ToString("G", CultureInfo.InvariantCulture)
                    : null;

                string? strokeLineCap = path.IsStroked ? MapLineCap(path.LineCapStyle) : null;
                string? strokeLineJoin = path.IsStroked ? MapLineJoin(path.LineJoinStyle) : null;

                // If a dash pattern exists and you want it, uncomment and implement GetDashArray if needed.
                // var dash = GetDashArray(path.LineDashPattern);
                // var dashAttr = dash is null ? "" : $" stroke-dasharray='{dash}'";

                var attrs = new List<string>
                {
                    $"d=\"{d}\"",
                    $"fill=\"{fill}\"",
                    $"stroke=\"{stroke}\""
                };

                if (!string.IsNullOrEmpty(fillRule)) attrs.Add($"fill-rule=\"{fillRule}\"");
                if (!string.IsNullOrEmpty(strokeWidth)) attrs.Add($"stroke-width=\"{strokeWidth}\"");
                if (!string.IsNullOrEmpty(strokeLineCap)) attrs.Add($"stroke-linecap=\"{strokeLineCap}\"");
                if (!string.IsNullOrEmpty(strokeLineJoin)) attrs.Add($"stroke-linejoin=\"{strokeLineJoin}\"");

                html.AppendLine($"        <path {string.Join(" ", attrs)} />");
            }

            html.AppendLine("    </svg>");
            html.AppendLine("</div>");

            pageNumber++;
        }

        html.AppendLine("</body>");
        html.AppendLine("</html>");

        try
        {
            File.WriteAllText("index.html", html.ToString());
            Console.WriteLine("Successfully created index.html");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving HTML file: {ex.Message}");
        }
    }

    private static string MapFillRule(FillingRule rule) =>
        rule == FillingRule.NonZeroWinding ? "nonzero" : "evenodd";

    private static string MapLineCap(LineCapStyle cap) => cap switch
    {
        LineCapStyle.Butt => "butt",
        LineCapStyle.Round => "round",
        LineCapStyle.ProjectingSquare => "square",
        _ => "butt"
    };

    private static string MapLineJoin(LineJoinStyle join) => join switch
    {
        LineJoinStyle.Miter => "miter",
        LineJoinStyle.Round => "round",
        LineJoinStyle.Bevel => "bevel",
        _ => "miter"
    };

    private static string CssColor(IColor? color)
    {
        if (color is null) return "black";

        // PdfPig converts whatever the original color space was to RGB for you
        var (r, g, b) = color.ToRGBValues();   // each in [0,1]
        return $"rgb({To255(r)},{To255(g)},{To255(b)})";
    }

    private static int To255(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255.0);


    private static int To255(decimal v) => To255((double)v);
    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}
