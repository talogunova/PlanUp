using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PlanUp.Engine;

namespace PlanUp.Reports
{
    public class ReportGenerator
    {
        private static readonly XColor Charcoal = XColor.FromArgb(44, 62, 80);
        private static readonly XColor LightGrey = XColor.FromArgb(149, 165, 166);
        private static readonly XColor MediumGrey = XColor.FromArgb(127, 140, 141);
        private static readonly XColor Green = XColor.FromArgb(39, 174, 96);
        private static readonly XColor Yellow = XColor.FromArgb(243, 156, 18);
        private static readonly XColor Red = XColor.FromArgb(231, 76, 60);
        private static readonly XColor OffWhite = XColor.FromArgb(250, 250, 250);
        private static readonly XFont TitleFont = new XFont("Montserrat", 20, XFontStyle.Bold);
        private static readonly XFont SubtitleFont = new XFont("Montserrat", 10, XFontStyle.Regular);
        private static readonly XFont HeadingFont = new XFont("Montserrat", 13, XFontStyle.Bold);
        private static readonly XFont BodyFont = new XFont("Montserrat", 10, XFontStyle.Regular);
        private static readonly XFont SmallFont = new XFont("Montserrat", 8, XFontStyle.Regular);
        private static readonly XFont BoldBodyFont = new XFont("Montserrat", 10, XFontStyle.Bold);
        private static readonly XFont StatusFont = new XFont("Montserrat", 9, XFontStyle.Bold);
        private static readonly XFont NoteFont = new XFont("Montserrat", 9, XFontStyle.Italic);
        private static double PW;
        private static double PH;
        private const double ML = 50;
        private const double MR = 50;
        private const double MB = 60;
        private static double CW => PW - ML - MR;

        public static void Generate(List<CheckResult> results, string projectName, string comunaZone, string filePath, bool isA4 = true)
        {
            var ps = isA4 ? PdfSharpCore.PageSize.A4 : PdfSharpCore.PageSize.Letter;
            PW = isA4 ? 595 : 612; PH = isA4 ? 842 : 792;
            var doc = new PdfDocument(); doc.Info.Title = $"PlanUp - {projectName}"; doc.Info.Author = "PlanUp";
            var page = doc.AddPage(); page.Size = ps; var gfx = XGraphics.FromPdfPage(page);
            double y = DrawHeader(gfx, projectName, comunaZone); y = DrawSummary(gfx, y, results);
            foreach (var r in results) { if (y > PH - MB - 160) { DrawFooter(gfx, doc.PageCount); page = doc.AddPage(); page.Size = ps; gfx = XGraphics.FromPdfPage(page); y = 50; } y = DrawCheck(gfx, y, r); }
            DrawFooter(gfx, doc.PageCount); doc.Save(filePath);
        }

        private static double DrawHeader(XGraphics g, string project, string zone)
        {
            g.DrawRectangle(new XSolidBrush(Charcoal), 0, 0, PW, 100);
            g.DrawString("Compliance Check", TitleFont, XBrushes.White, new XPoint(ML, 42));
            g.DrawString("by", SubtitleFont, XBrushes.White, new XPoint(ML, 62));
            try { string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ""; string lp = Path.Combine(dir, "Resources", "PlanUp_logo_white_transparent.png"); if (File.Exists(lp)) { var logo = XImage.FromFile(lp); double lh = 20; double lw = lh * ((double)logo.PixelWidth / logo.PixelHeight); g.DrawImage(logo, ML + 18, 49, lw, lh); } else { g.DrawString("PlanUp", SubtitleFont, XBrushes.White, new XPoint(ML + 18, 62)); } } catch { g.DrawString("PlanUp", SubtitleFont, XBrushes.White, new XPoint(ML + 18, 62)); }
            g.DrawString(DateTime.Now.ToString("MMMM d, yyyy"), SubtitleFont, new XSolidBrush(LightGrey), new XPoint(PW - MR, 42), new XStringFormat { Alignment = XStringAlignment.Far });
            double y = 120;
            g.DrawString("Project:", BoldBodyFont, new XSolidBrush(Charcoal), new XPoint(ML, y)); g.DrawString(project, BodyFont, new XSolidBrush(Charcoal), new XPoint(ML + 55, y)); y += 18;
            g.DrawString("Zone:", BoldBodyFont, new XSolidBrush(Charcoal), new XPoint(ML, y)); g.DrawString(zone, BodyFont, new XSolidBrush(Charcoal), new XPoint(ML + 55, y)); return y + 30;
        }

        private static double DrawSummary(XGraphics g, double y, List<CheckResult> results)
        {
            g.DrawRectangle(new XSolidBrush(OffWhite), ML, y, CW, 40);
            int gc = 0, yc = 0, rc = 0; foreach (var r in results) { if (r.Status == ComplianceStatus.Green) gc++; else if (r.Status == ComplianceStatus.Yellow) yc++; else rc++; }
            double sx = ML + 15, sy = y + 25;
            g.DrawString($"{results.Count} checks completed", BoldBodyFont, new XSolidBrush(Charcoal), new XPoint(sx, sy));
            sx = ML + 200; g.DrawEllipse(new XSolidBrush(Green), sx, sy - 9, 10, 10); g.DrawString(gc.ToString(), BodyFont, new XSolidBrush(Charcoal), new XPoint(sx + 14, sy)); sx += 40;
            g.DrawEllipse(new XSolidBrush(Yellow), sx, sy - 9, 10, 10); g.DrawString(yc.ToString(), BodyFont, new XSolidBrush(Charcoal), new XPoint(sx + 14, sy)); sx += 40;
            g.DrawEllipse(new XSolidBrush(Red), sx, sy - 9, 10, 10); g.DrawString(rc.ToString(), BodyFont, new XSolidBrush(Charcoal), new XPoint(sx + 14, sy));
            return y + 55;
        }

        private static double DrawCheck(XGraphics g, double y, CheckResult r)
        {
            XColor sc; string sl; if (r.Status == ComplianceStatus.Green) { sc = Green; sl = "PASS"; } else if (r.Status == ComplianceStatus.Yellow) { sc = Yellow; sl = "WARNING"; } else { sc = Red; sl = "FAIL"; }
            g.DrawEllipse(new XSolidBrush(sc), ML, y - 2, 12, 12); g.DrawString(r.RuleName, HeadingFont, new XSolidBrush(Charcoal), new XPoint(ML + 20, y + 8));
            double bw = 65, bx = PW - MR - bw; g.DrawRectangle(new XSolidBrush(sc), bx, y - 2, bw, 16);
            g.DrawString(sl, StatusFont, XBrushes.White, new XRect(bx, y - 2, bw, 16), new XStringFormat { Alignment = XStringAlignment.Center, LineAlignment = XLineAlignment.Center });
            y += 22; g.DrawString(r.ArticleReference, SmallFont, new XSolidBrush(LightGrey), new XPoint(ML + 20, y));
            y += 16; g.DrawString(r.ValueSummary, BodyFont, new XSolidBrush(Charcoal), new XPoint(ML + 20, y));
            y += 16; WrapText(g, r.StatusMessage, BodyFont, MediumGrey, ML + 20, ref y, CW - 20);
            if (!string.IsNullOrEmpty(r.DetailDescription)) { y += 4; foreach (string line in r.DetailDescription.Split('\n')) { if (string.IsNullOrWhiteSpace(line)) { y += 4; continue; } if (y > PH - MB - 20) break; string t = line.Trim(); if (t.StartsWith("Note:")) { y += 4; WrapText(g, t, NoteFont, Charcoal, ML + 20, ref y, CW - 20); } else { WrapText(g, t, SmallFont, LightGrey, ML + 20, ref y, CW - 20); } } }
            y += 8; g.DrawLine(new XPen(XColor.FromArgb(224, 224, 224), 0.5), ML, y, PW - MR, y); return y + 14;
        }

        private static void WrapText(XGraphics g, string text, XFont font, XColor color, double x, ref double y, double maxW)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] words = text.Split(' '); string cur = ""; double lh = font.Size * 1.4;
            foreach (string w in words) { string test = cur.Length == 0 ? w : cur + " " + w; if (g.MeasureString(test, font).Width > maxW && cur.Length > 0) { g.DrawString(cur, font, new XSolidBrush(color), new XPoint(x, y)); y += lh; cur = w; } else { cur = test; } }
            if (cur.Length > 0) { g.DrawString(cur, font, new XSolidBrush(color), new XPoint(x, y)); y += lh; }
        }

        private static void DrawFooter(XGraphics g, int pn)
        {
            double fy = PH - 35; g.DrawString("Generated by PlanUp", SmallFont, new XSolidBrush(LightGrey), new XPoint(ML, fy));
            g.DrawString($"Page {pn}", SmallFont, new XSolidBrush(LightGrey), new XPoint(PW - MR, fy), new XStringFormat { Alignment = XStringAlignment.Far });
        }
    }
}
