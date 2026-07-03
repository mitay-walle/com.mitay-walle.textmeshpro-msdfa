#if TMP_MSDFA_UGUI_PATCHED
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace TMPro.EditorUtilities
{
    public static class TMP_MSDFAStressTest
    {
        private const string MenuPath = "Tools/TextMeshPro MSDFA/Stress Test Glyph";
        private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 1024;
        private const string DefaultGlyph = "V";
        private const int MsdfaIterations = 250;
        private const int SdfIterations = 250;

        [MenuItem(MenuPath)]
        public static void Run()
        {
            string glyphText = EditorPrefs.GetString("TMP_MSDFAStressTest.Glyph", DefaultGlyph);
            if (string.IsNullOrEmpty(glyphText))
                glyphText = DefaultGlyph;

            char glyph = glyphText[0];
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[MSDFA Stress] Source font not found: {SourceFontPath}");
                return;
            }

            string outputDirectory = GetOutputDirectory();
            Directory.CreateDirectory(outputDirectory);

            RenderResult msdfaRender = RenderGlyph(sourceFont, glyph, outputDirectory, true, true, "msdfa");
            if (msdfaRender.Success == false)
                return;

            RenderResult sdfRender = RenderGlyph(sourceFont, glyph, outputDirectory, true, false, "sdf");
            if (sdfRender.Success == false)
                return;

            TimeSpan msdfaElapsed = BenchmarkGlyphRender(sourceFont, glyph, true, MsdfaIterations, out int msdfaSuccessCount);
            TimeSpan sdfElapsed = BenchmarkGlyphRender(sourceFont, glyph, false, SdfIterations, out int sdfSuccessCount);

            CompareResult compare = CompareGlyphs(msdfaRender, sdfRender, outputDirectory, glyph);
            string reportPath = Path.Combine(outputDirectory, $"msdfa-stress-{SafeGlyphName(glyph)}.json");
            File.WriteAllText(reportPath, BuildReportJson(glyph, SourceFontPath, msdfaRender, sdfRender, compare, msdfaElapsed, msdfaSuccessCount, sdfElapsed, sdfSuccessCount));

            Debug.Log(BuildConsoleSummary(glyph, reportPath, msdfaRender, sdfRender, compare, msdfaElapsed, msdfaSuccessCount, sdfElapsed, sdfSuccessCount));
        }

        private static string GetOutputDirectory()
        {
            string projectPath = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            return Path.Combine(projectPath, "MSDFA Stress Results");
        }

        private static TimeSpan BenchmarkGlyphRender(Font sourceFont, char glyph, bool isMsdfaEnabled, int iterations, out int successCount)
        {
            TMP_FontAsset font = CreateTemporaryFontAsset(sourceFont, isMsdfaEnabled, isMsdfaEnabled ? "CodexMsdfaBenchmarkFont" : "CodexSdfBenchmarkFont");
            successCount = 0;
            try
            {
                if (font == null)
                    return TimeSpan.Zero;

                string glyphText = glyph.ToString();
                font.ClearFontAssetData(true);
                font.TryAddCharacters(glyphText, out _);

                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    font.ClearFontAssetData(true);
                    if (font.TryAddCharacters(glyphText, out string missingCharacters) && string.IsNullOrEmpty(missingCharacters))
                        successCount++;
                }

                stopwatch.Stop();
                return stopwatch.Elapsed;
            }
            finally
            {
                ReleaseTemporaryFontAsset(font);
            }
        }

        private static RenderResult RenderGlyph(Font sourceFont, char glyph, string outputDirectory, bool saveImage, bool isMsdfaEnabled, string label)
        {
            TMP_FontAsset font = CreateTemporaryFontAsset(sourceFont, isMsdfaEnabled, isMsdfaEnabled ? "CodexMsdfaStressFont" : "CodexSdfStressFont");
            try
            {
                if (font == null)
                    return RenderResult.Failed;

                font.ClearFontAssetData(true);
                if (font.TryAddCharacters(glyph.ToString(), out string missingCharacters) == false || string.IsNullOrEmpty(missingCharacters) == false)
                {
                    Debug.LogError($"[MSDFA Stress] {label.ToUpperInvariant()} TMP render failed. Missing characters: {missingCharacters}");
                    return RenderResult.Failed;
                }

                TMP_Character character = font.characterLookupTable[glyph];
                GlyphRect glyphRect = character.glyph.glyphRect;
                int atlasIndex = character.glyph.atlasIndex;
                Texture2D atlasTexture = font.atlasTextures[atlasIndex];
                Texture2D readableTexture = CopyTexture(atlasTexture);
                string atlasPath = string.Empty;
                if (saveImage)
                {
                    atlasPath = Path.Combine(outputDirectory, $"{label}-atlas-{SafeGlyphName(glyph)}.png");
                    File.WriteAllBytes(atlasPath, readableTexture.EncodeToPNG());
                }

                return new RenderResult(true, readableTexture, atlasPath, glyphRect, atlasIndex, font.atlasPadding, atlasTexture.width, atlasTexture.height, atlasTexture.format.ToString());
            }
            finally
            {
                ReleaseTemporaryFontAsset(font);
            }
        }

        private static CompareResult CompareGlyphs(RenderResult msdfaRender, RenderResult sdfRender, string outputDirectory, char glyph)
        {
            RectInt msdfaRect = ExpandRect(msdfaRender.GlyphRect, msdfaRender.Padding, msdfaRender.AtlasWidth, msdfaRender.AtlasHeight);
            RectInt sdfRect = ExpandRect(sdfRender.GlyphRect, sdfRender.Padding, sdfRender.AtlasWidth, sdfRender.AtlasHeight);
            Texture2D msdfaCrop = CropTexture(msdfaRender.Texture, msdfaRect);
            Texture2D sdfCrop = CropTexture(sdfRender.Texture, sdfRect);
            int width = Math.Max(msdfaCrop.width, sdfCrop.width);
            int height = Math.Max(msdfaCrop.height, sdfCrop.height);
            Color32[] msdfaPixels = CenterOnCanvas(msdfaCrop, width, height);
            Color32[] sdfPixels = CenterOnCanvas(sdfCrop, width, height);
            Color32[] diffPixels = new Color32[width * height];

            long[] absoluteSum = new long[4];
            long[] squareSum = new long[4];
            int differentPixels = 0;
            int maxDifference = 0;
            int alphaDifferentPixels = 0;
            for (int i = 0; i < msdfaPixels.Length; i++)
            {
                Color32 msdfaPixel = msdfaPixels[i];
                Color32 sdfPixel = sdfPixels[i];
                int red = Math.Abs(msdfaPixel.r - sdfPixel.r);
                int green = Math.Abs(msdfaPixel.g - sdfPixel.g);
                int blue = Math.Abs(msdfaPixel.b - sdfPixel.b);
                int alpha = Math.Abs(msdfaPixel.a - sdfPixel.a);
                int pixelMax = Math.Max(Math.Max(red, green), Math.Max(blue, alpha));
                if (pixelMax > 4)
                    differentPixels++;
                if (alpha > 4)
                    alphaDifferentPixels++;

                maxDifference = Math.Max(maxDifference, pixelMax);
                absoluteSum[0] += red;
                absoluteSum[1] += green;
                absoluteSum[2] += blue;
                absoluteSum[3] += alpha;
                squareSum[0] += red * red;
                squareSum[1] += green * green;
                squareSum[2] += blue * blue;
                squareSum[3] += alpha * alpha;
                diffPixels[i] = new Color32((byte)Math.Min(255, red * 6), (byte)Math.Min(255, green * 6), (byte)Math.Min(255, blue * 6), (byte)Math.Min(255, alpha * 6));
            }

            string comparePath = Path.Combine(outputDirectory, $"compare-msdfa-sdf-{SafeGlyphName(glyph)}.png");
            SaveContactSheet(sdfCrop, msdfaCrop, diffPixels, width, height, comparePath);
            double pixelCount = msdfaPixels.Length;
            return new CompareResult(
                comparePath,
                msdfaCrop.width,
                msdfaCrop.height,
                sdfCrop.width,
                sdfCrop.height,
                differentPixels / pixelCount,
                alphaDifferentPixels / pixelCount,
                maxDifference,
                absoluteSum[0] / pixelCount,
                absoluteSum[1] / pixelCount,
                absoluteSum[2] / pixelCount,
                absoluteSum[3] / pixelCount,
                Math.Sqrt(squareSum[0] / pixelCount),
                Math.Sqrt(squareSum[1] / pixelCount),
                Math.Sqrt(squareSum[2] / pixelCount),
                Math.Sqrt(squareSum[3] / pixelCount));
        }

        private static void ReleaseTemporaryFontAsset(TMP_FontAsset font)
        {
            if (font == null)
                return;

            if (font.atlasTextures != null)
            {
                for (int i = 0; i < font.atlasTextures.Length; i++)
                {
                    if (font.atlasTextures[i] != null)
                        Object.DestroyImmediate(font.atlasTextures[i]);
                }
            }

            if (font.material != null)
                Object.DestroyImmediate(font.material);

            font.atlasTextures = Array.Empty<Texture2D>();
            font.material = null;
            Object.DestroyImmediate(font);
        }

        private static TMP_FontAsset CreateTemporaryFontAsset(Font sourceFont, bool isMsdfaEnabled, string name)
        {
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(sourceFont, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false);
            if (font == null)
                return null;

            font.name = name;
            font.isMsdfaAtlasEnabled = isMsdfaEnabled;
            if (font.atlasTextures != null)
            {
                for (int i = 0; i < font.atlasTextures.Length; i++)
                {
                    if (font.atlasTextures[i] != null)
                        TMP_MSDFAAtlasRenderer.ResizeAtlasTexture(font.atlasTextures[i], AtlasSize, AtlasSize, font.atlasRenderMode, isMsdfaEnabled);
                }
            }

            if (font.material != null)
            {
                font.material.shader = TMP_MSDFAAtlasRenderer.GetDistanceFieldShader(isMsdfaEnabled, ShaderUtilities.ShaderRef_MobileSDF);
                font.material.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
                font.material.SetFloat(ShaderUtilities.ID_TextureWidth, AtlasSize);
                font.material.SetFloat(ShaderUtilities.ID_TextureHeight, AtlasSize);
            }

            if (isMsdfaEnabled)
                font.RefreshMsdfaSourceFontDataFromEditor();
            else
                font.ClearMsdfaSourceFontData();

            return font;
        }

        private static string BuildConsoleSummary(char glyph, string reportPath, RenderResult msdfaRender, RenderResult sdfRender, CompareResult compare, TimeSpan msdfaElapsed, int msdfaSuccessCount, TimeSpan sdfElapsed, int sdfSuccessCount)
        {
            return $"[MSDFA Stress] Glyph '{glyph}' done. MSDFA {msdfaSuccessCount}/{MsdfaIterations}: {msdfaElapsed.TotalMilliseconds:0.00} ms total, {msdfaElapsed.TotalMilliseconds / Math.Max(1, msdfaSuccessCount):0.000} ms/run. SDF {sdfSuccessCount}/{SdfIterations}: {sdfElapsed.TotalMilliseconds:0.00} ms total, {sdfElapsed.TotalMilliseconds / Math.Max(1, sdfSuccessCount):0.000} ms/run. Diff>4={compare.DifferentPixelRatio:P2}, AlphaDiff>4={compare.AlphaDifferentPixelRatio:P2}, MAE RGBA=({compare.MeanAbsoluteRed:0.00}, {compare.MeanAbsoluteGreen:0.00}, {compare.MeanAbsoluteBlue:0.00}, {compare.MeanAbsoluteAlpha:0.00}). Report: {reportPath}. Compare: {compare.CompareImagePath}. MSDFA atlas: {msdfaRender.ImagePath}. SDF atlas: {sdfRender.ImagePath}";
        }

        private static string BuildReportJson(char glyph, string sourceFontPath, RenderResult msdfaRender, RenderResult sdfRender, CompareResult compare, TimeSpan msdfaElapsed, int msdfaSuccessCount, TimeSpan sdfElapsed, int sdfSuccessCount)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("{");
            AppendJsonString(report, "glyph", $"{glyph} U+{(int)glyph:X4}", true);
            AppendJsonString(report, "fontAsset", "temporary dynamic TMP_FontAsset", true);
            AppendJsonString(report, "sourceFont", sourceFontPath, true);
            AppendJsonString(report, "msdfaIterations", $"{msdfaSuccessCount}/{MsdfaIterations}", true);
            AppendJsonNumber(report, "msdfaTotalMs", msdfaElapsed.TotalMilliseconds, true);
            AppendJsonNumber(report, "msdfaMsPerRun", msdfaElapsed.TotalMilliseconds / Math.Max(1, msdfaSuccessCount), true);
            AppendJsonString(report, "sdfIterations", $"{sdfSuccessCount}/{SdfIterations}", true);
            AppendJsonNumber(report, "sdfTotalMs", sdfElapsed.TotalMilliseconds, true);
            AppendJsonNumber(report, "sdfMsPerRun", sdfElapsed.TotalMilliseconds / Math.Max(1, sdfSuccessCount), true);
            AppendJsonNumber(report, "msdfaAtlasIndex", msdfaRender.AtlasIndex, true);
            AppendJsonString(report, "msdfaGlyphRect", $"{msdfaRender.GlyphRect.x},{msdfaRender.GlyphRect.y},{msdfaRender.GlyphRect.width},{msdfaRender.GlyphRect.height}", true);
            AppendJsonString(report, "msdfaTextureFormat", msdfaRender.TextureFormat, true);
            AppendJsonString(report, "msdfaCropSize", $"{compare.MsdfaCropWidth}x{compare.MsdfaCropHeight}", true);
            AppendJsonNumber(report, "sdfAtlasIndex", sdfRender.AtlasIndex, true);
            AppendJsonString(report, "sdfGlyphRect", $"{sdfRender.GlyphRect.x},{sdfRender.GlyphRect.y},{sdfRender.GlyphRect.width},{sdfRender.GlyphRect.height}", true);
            AppendJsonString(report, "sdfTextureFormat", sdfRender.TextureFormat, true);
            AppendJsonString(report, "sdfCropSize", $"{compare.SdfCropWidth}x{compare.SdfCropHeight}", true);
            AppendJsonNumber(report, "differentPixelRatioMaxChannelGt4", compare.DifferentPixelRatio, true);
            AppendJsonNumber(report, "alphaDifferentPixelRatioGt4", compare.AlphaDifferentPixelRatio, true);
            AppendJsonNumber(report, "maxChannelDifference", compare.MaxDifference, true);
            AppendJsonString(report, "meanAbsoluteRgba", $"{FormatNumber(compare.MeanAbsoluteRed)}, {FormatNumber(compare.MeanAbsoluteGreen)}, {FormatNumber(compare.MeanAbsoluteBlue)}, {FormatNumber(compare.MeanAbsoluteAlpha)}", true);
            AppendJsonString(report, "rmseRgba", $"{FormatNumber(compare.RootMeanSquareRed)}, {FormatNumber(compare.RootMeanSquareGreen)}, {FormatNumber(compare.RootMeanSquareBlue)}, {FormatNumber(compare.RootMeanSquareAlpha)}", true);
            AppendJsonString(report, "msdfaAtlasImage", msdfaRender.ImagePath, true);
            AppendJsonString(report, "sdfAtlasImage", sdfRender.ImagePath, true);
            AppendJsonString(report, "compareImage", compare.CompareImagePath, true);
            AppendJsonNumber(report, "atlasPadding", AtlasPadding, true);
            AppendJsonString(report, "atlasSize", $"{AtlasSize}x{AtlasSize}", false);
            report.AppendLine("}");
            return report.ToString();
        }

        private static void AppendJsonString(StringBuilder report, string key, string value, bool trailingComma)
        {
            report.Append("  \"");
            report.Append(key);
            report.Append("\": \"");
            report.Append(EscapeJson(value));
            report.Append("\"");
            report.AppendLine(trailingComma ? "," : string.Empty);
        }

        private static void AppendJsonNumber(StringBuilder report, string key, double value, bool trailingComma)
        {
            report.Append("  \"");
            report.Append(key);
            report.Append("\": ");
            report.Append(FormatNumber(value));
            report.AppendLine(trailingComma ? "," : string.Empty);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static Texture2D CopyTexture(Texture2D source)
        {
            Texture2D formatMatchedCopy = new Texture2D(source.width, source.height, source.format, false);
            Graphics.CopyTexture(source, formatMatchedCopy);

            Texture2D rgbaCopy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            rgbaCopy.SetPixels32(formatMatchedCopy.GetPixels32());
            rgbaCopy.Apply(false, false);
            Object.DestroyImmediate(formatMatchedCopy);
            return rgbaCopy;
        }

        private static RectInt ExpandRect(GlyphRect rect, int padding, int textureWidth, int textureHeight)
        {
            int x = Math.Max(0, rect.x - padding);
            int y = Math.Max(0, rect.y - padding);
            int xMax = Math.Min(textureWidth, rect.x + rect.width + padding);
            int yMax = Math.Min(textureHeight, rect.y + rect.height + padding);
            return new RectInt(x, y, Math.Max(0, xMax - x), Math.Max(0, yMax - y));
        }

        private static Texture2D CropTexture(Texture2D source, RectInt rect)
        {
            Texture2D crop = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[rect.width * rect.height];
            for (int y = 0; y < rect.height; y++)
            {
                for (int x = 0; x < rect.width; x++)
                    pixels[y * rect.width + x] = source.GetPixel(rect.x + x, rect.y + y);
            }

            crop.SetPixels32(pixels);
            crop.Apply(false, false);
            return crop;
        }

        private static Color32[] CenterOnCanvas(Texture2D texture, int width, int height)
        {
            Color32[] canvas = new Color32[width * height];
            Color32[] pixels = texture.GetPixels32();
            int offsetX = (width - texture.width) / 2;
            int offsetY = (height - texture.height) / 2;
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                    canvas[(y + offsetY) * width + x + offsetX] = pixels[y * texture.width + x];
            }

            return canvas;
        }

        private static void SaveContactSheet(Texture2D sdfCrop, Texture2D msdfaCrop, Color32[] diffPixels, int diffWidth, int diffHeight, string outputPath)
        {
            int width = Math.Max(Math.Max(sdfCrop.width, msdfaCrop.width), diffWidth);
            int height = Math.Max(Math.Max(sdfCrop.height, msdfaCrop.height), diffHeight);
            Texture2D sheet = new Texture2D(width * 3, height, TextureFormat.RGBA32, false);
            Color32[] clear = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < clear.Length; i++)
                clear[i] = new Color32(30, 30, 30, 255);
            sheet.SetPixels32(clear);
            BlitCentered(sheet, sdfCrop, 0, width, height);
            BlitCentered(sheet, msdfaCrop, width, width, height);
            Texture2D diffTexture = new Texture2D(diffWidth, diffHeight, TextureFormat.RGBA32, false);
            diffTexture.SetPixels32(diffPixels);
            diffTexture.Apply(false, false);
            BlitCentered(sheet, diffTexture, width * 2, width, height);
            sheet.Apply(false, false);
            File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(diffTexture);
            Object.DestroyImmediate(sheet);
        }

        private static void BlitCentered(Texture2D target, Texture2D source, int targetX, int cellWidth, int cellHeight)
        {
            Color32[] pixels = source.GetPixels32();
            int offsetX = targetX + (cellWidth - source.width) / 2;
            int offsetY = (cellHeight - source.height) / 2;
            for (int y = 0; y < source.height; y++)
            {
                for (int x = 0; x < source.width; x++)
                    target.SetPixel(offsetX + x, offsetY + y, pixels[y * source.width + x]);
            }
        }

        private static string SafeGlyphName(char glyph)
        {
            return $"U{(int)glyph:X4}";
        }

        private readonly struct RenderResult
        {
            internal static readonly RenderResult Failed = new RenderResult(false, null, string.Empty, new GlyphRect(), 0, 0, 0, 0, string.Empty);

            internal readonly bool Success;
            internal readonly Texture2D Texture;
            internal readonly string ImagePath;
            internal readonly GlyphRect GlyphRect;
            internal readonly int AtlasIndex;
            internal readonly int Padding;
            internal readonly int AtlasWidth;
            internal readonly int AtlasHeight;
            internal readonly string TextureFormat;

            internal RenderResult(bool success, Texture2D texture, string imagePath, GlyphRect glyphRect, int atlasIndex, int padding, int atlasWidth, int atlasHeight, string textureFormat)
            {
                Success = success;
                Texture = texture;
                ImagePath = imagePath;
                GlyphRect = glyphRect;
                AtlasIndex = atlasIndex;
                Padding = padding;
                AtlasWidth = atlasWidth;
                AtlasHeight = atlasHeight;
                TextureFormat = textureFormat;
            }
        }

        private readonly struct CompareResult
        {
            internal readonly string CompareImagePath;
            internal readonly int MsdfaCropWidth;
            internal readonly int MsdfaCropHeight;
            internal readonly int SdfCropWidth;
            internal readonly int SdfCropHeight;
            internal readonly double DifferentPixelRatio;
            internal readonly double AlphaDifferentPixelRatio;
            internal readonly int MaxDifference;
            internal readonly double MeanAbsoluteRed;
            internal readonly double MeanAbsoluteGreen;
            internal readonly double MeanAbsoluteBlue;
            internal readonly double MeanAbsoluteAlpha;
            internal readonly double RootMeanSquareRed;
            internal readonly double RootMeanSquareGreen;
            internal readonly double RootMeanSquareBlue;
            internal readonly double RootMeanSquareAlpha;

            internal CompareResult(string compareImagePath, int msdfaCropWidth, int msdfaCropHeight, int sdfCropWidth, int sdfCropHeight, double differentPixelRatio, double alphaDifferentPixelRatio, int maxDifference, double meanAbsoluteRed, double meanAbsoluteGreen, double meanAbsoluteBlue, double meanAbsoluteAlpha, double rootMeanSquareRed, double rootMeanSquareGreen, double rootMeanSquareBlue, double rootMeanSquareAlpha)
            {
                CompareImagePath = compareImagePath;
                MsdfaCropWidth = msdfaCropWidth;
                MsdfaCropHeight = msdfaCropHeight;
                SdfCropWidth = sdfCropWidth;
                SdfCropHeight = sdfCropHeight;
                DifferentPixelRatio = differentPixelRatio;
                AlphaDifferentPixelRatio = alphaDifferentPixelRatio;
                MaxDifference = maxDifference;
                MeanAbsoluteRed = meanAbsoluteRed;
                MeanAbsoluteGreen = meanAbsoluteGreen;
                MeanAbsoluteBlue = meanAbsoluteBlue;
                MeanAbsoluteAlpha = meanAbsoluteAlpha;
                RootMeanSquareRed = rootMeanSquareRed;
                RootMeanSquareGreen = rootMeanSquareGreen;
                RootMeanSquareBlue = rootMeanSquareBlue;
                RootMeanSquareAlpha = rootMeanSquareAlpha;
            }
        }
    }
}
#endif
