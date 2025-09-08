using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Colors;

using System.IO;
using System.Security.Cryptography;
using UglyToad.PdfPig.Geometry;

enum SpecialRectType
{
    Video,
    Demo,
    Example
}


public class Program
{
    public static int BETWEEN_PAGE_PADDING = 20;
    public static double ANNOTATION_GAP = 20;

    public static int Main(string[] args)
    {
        string pdfPath;
        string defaultFile = "boxes.pdf";

        if (args.Length == 0)
        {
            // Try basic.pdf in the current working directory, then next to the executable.
            var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), defaultFile);

            if (File.Exists(cwdCandidate))       pdfPath = cwdCandidate;
            else
            {
                Console.WriteLine("No argument supplied, and the default'" + defaultFile + "' was not found " +
                                  "in the current directory,");
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
        IEnumerable<Word> wordsOnPage;
        var dString = new StringBuilder();
        PdfPath curPath;
        PdfSubpath curSubPath;
        Page curPage;
        var overlays = new StringBuilder(); // holds <foreignObject> inserts

        string initialHTML = $$$"""
                                <!DOCTYPE html>
                                <html>
                                <head>
                                   <title>{filename}</title>
                                   <style>
                                body {
                                  margin: 0;
                                  padding: 0;
                                  background-color: #999;   /* gray workspace background */
                                }
                                
                                .pageholder {
                                  display: flex;
                                  flex-direction: column;   /* stack pages vertically */
                                  align-items: center;      /* center each page horizontally */
                                  width: 100%;
                                  padding: 5px 0;
                                  box-sizing: border-box;
                                }
                                
                                .page {
                                  background: white;        /* white paper */
                                  box-shadow: 0 0 8px rgba(0,0,0,0.4);
                                  display: flex;
                                  justify-content: center;
                                  position: relative;
                                }
                                
                                .page svg {
                                  width: 100%;              /* scale to browser width */
                                  height: auto;             /* keep aspect ratio */
                                  display: block;
                                  border: 1px solid #000;
                                  background: white;        /* ensure white background */
                                  }
                                      video { position: absolute; object-fit: contain; margin: 0px; padding: 0px; border-style: none; }
                                      iframe { position: absolute; object-fit: contain; margin: 0px; padding: 0px; border-style: none;}
                                   </style>
                                </head>
                                <body>
                                    <script>
                                    const observer = new IntersectionObserver((entries) => {
                                        entries.forEach(entry => {
                                            if (entry.isIntersecting) {
                                             console.log("target is visible. Asked main loop to resume.");
                                             entry.target.contentWindow.postMessage("resume");
                                            } else {
                                             console.log("target is not visible. Asked main loop to pause.");
                                             entry.target.contentWindow.postMessage("pause");
                                            }});
                                     });
                                
                                    document.addEventListener("DOMContentLoaded", () => {
                                        var demos = document.getElementsByClassName("unity-demo");   
                                        for (let i = 0; i < demos.length; i++) 
                                        {
                                            observer.observe(demos[i]);
                                        }
                                    });
                                    </script>   
                                """;
        html.AppendLine(initialHTML);
       
        for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            curPage = document.GetPage(pageNumber);
            
            html.AppendLine("<div class='pageholder'>");
            html.AppendLine($"<div class='page' id='page-{pageNumber}'>");
            html.AppendLine($"    <svg width='{curPage.Width}px' height='{curPage.Height}px' viewBox='0 0 {curPage.Width} {curPage.Height}'>");

            wordsOnPage = curPage.GetWords();


            for (int pathIndex = 0; pathIndex < curPage.ExperimentalAccess.Paths.Count; pathIndex++)
            {
                curPath = curPage.ExperimentalAccess.Paths[pathIndex];
                if (curPath.IsClipping) continue;
                
                for (int subpathIndex = 0; subpathIndex < curPath.Count; subpathIndex++)
                {
                    curSubPath = curPath[subpathIndex];
                    ProcessIfSubpathIsSpecialRectangle();

                    foreach (var cmd in curSubPath.Commands)
                    {
                        cmd.WriteSvg(dString, curPage.Height);
                    }
                }

                string attributes = GetAttributes(curPath);
                html.AppendLine($"        <path d=\"{dString}\" {attributes} />");
                dString.Clear();

            }
            html.AppendLine("    </svg>");
            html.AppendLine(overlays.ToString());
            overlays.Clear();
            html.AppendLine("</div>");
            html.AppendLine("</div>");
            
            
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

        return;
        
        // End of ProcessDocument function.
        // Now for internal helper functions

        void ProcessIfSubpathIsSpecialRectangle()
        {
            if (curSubPath.Commands.Count != 5) return;
            if (curSubPath.Commands[0] is not PdfSubpath.Move) return;
            if (curSubPath.Commands[1] is not PdfSubpath.Line) return;
            if (curSubPath.Commands[2] is not PdfSubpath.Line) return;
            if (curSubPath.Commands[3] is not PdfSubpath.Line) return;
            if (curSubPath.Commands[4] is not PdfSubpath.Close) return;
            
            // At this stage we assume that curSubPath is a rectangle.
            PdfRectangle boundingRect = curSubPath.GetBoundingRectangle().Value;
            
            foreach (var word in wordsOnPage)
            {
                string text = word.Text;
                if (text.EndsWith(".mp4") && (word.BoundingBox.IntersectsWith(boundingRect)))
                {
                    Console.WriteLine($"Found video: {text}");
                    double left = boundingRect.Left;
                    double top = curPage.Height - boundingRect.Top;
                    double width = boundingRect.Width;
                    double height = boundingRect.Height;
                    
                    double leftPercent = left / curPage.Width * 100;
                    double topPercent = top / curPage.Height * 100;
                    double widthPercent = width / curPage.Width * 100;
                    double heightPercent = height / curPage.Height * 100;

                    string line = $"""
                                        <video src="{text}" controls playsinline style="left: {leftPercent}%; top: {topPercent}%; width: {widthPercent}%; 
                                    height: {heightPercent}%;">
                                        </video>
                                    """;
                    overlays.AppendLine(line);
                }
                if (text.StartsWith("unity:") && (word.BoundingBox.IntersectsWith(boundingRect)))
                {
                    string location = text.Substring(6);
                    Console.WriteLine($"Found unity demo: {location}");
                    double left = boundingRect.Left;
                    double top = curPage.Height - boundingRect.Top;
                    double width = boundingRect.Width;
                    double height = boundingRect.Height;

                    double leftPercent = left / curPage.Width * 100;
                    double topPercent = top / curPage.Height * 100;
                    double widthPercent = width / curPage.Width * 100;
                    double heightPercent = height / curPage.Height * 100;
                    

                    string line = $"""
                                        <iframe src="{location}" class="unity-demo" style="left: {leftPercent}%; top: {topPercent}%; width: {widthPercent}%; 
                                        height: {heightPercent}%;"
                                        allow="fullscreen; autoplay; ">
                                        </iframe>
                                   """;
                    overlays.AppendLine(line);
                }
            }
            
        }
        
        
        string GetAttributes(PdfPath path)
        {

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
                $"fill=\"{fill}\"",
                $"stroke=\"{stroke}\""
            };

            if (!string.IsNullOrEmpty(fillRule)) attrs.Add($"fill-rule=\"{fillRule}\"");
            if (!string.IsNullOrEmpty(strokeWidth)) attrs.Add($"stroke-width=\"{strokeWidth}\"");
            if (!string.IsNullOrEmpty(strokeLineCap)) attrs.Add($"stroke-linecap=\"{strokeLineCap}\"");
            if (!string.IsNullOrEmpty(strokeLineJoin)) attrs.Add($"stroke-linejoin=\"{strokeLineJoin}\"");

            return string.Join(" ", attrs);

        }
        static string MapFillRule(FillingRule rule)
        {
            if (rule == FillingRule.NonZeroWinding)
                return "nonzero";
            else
                return "evenodd";
        }

        static string MapLineCap(LineCapStyle cap)
        {
            switch (cap)
            {
                case LineCapStyle.Butt:             return "butt";
                case LineCapStyle.Round:            return "round";
                case LineCapStyle.ProjectingSquare: return "square";
                default:                            return "butt";
            }
        }

        static string MapLineJoin(LineJoinStyle join)
        {
            switch (join)
            {
                case LineJoinStyle.Miter:   return "miter";
                case LineJoinStyle.Round:   return "round";
                case LineJoinStyle.Bevel:   return "bevel";
                default:                    return "miter";
            }
        }

        static string CssColor(IColor? color)
        {
            if (color is null) return "black";

            // PdfPig converts whatever the original color space was to RGB for you
            var (r, g, b) = color.ToRGBValues();   // each in [0,1]
            return $"rgb({To255(r)},{To255(g)},{To255(b)})";
        }

        static int To255(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255.0);
        
    }
    





 
}
