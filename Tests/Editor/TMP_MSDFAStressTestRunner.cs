#if TMP_MSDFA_UGUI_PATCHED
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro.EditorUtilities;
using Unity.PerformanceTesting;
using UnityEditor;
using UnityEngine;

namespace TMPro.EditorUtilities.Tests
{
    public class TMP_MSDFAStressTestRunner
    {
        private const string Glyph = "V";
        private const string GlyphName = "U0056";
        private const string GlyphEditorPrefKey = "TMP_MSDFAStressTest.Glyph";

        [Test]
        [Performance]
        public void LiberationSansGlyphComparesMsdfaAndSdfRenderer()
        {
            EditorPrefs.SetString(GlyphEditorPrefKey, Glyph);
            string reportPath = Path.Combine(GetOutputDirectory(), $"msdfa-stress-{GlyphName}.json");
            if (File.Exists(reportPath))
                File.Delete(reportPath);

            TMP_MSDFAStressTest.Run();

            Assert.That(File.Exists(reportPath), Is.True, $"MSDFA stress report was not created: {reportPath}");
            string report = File.ReadAllText(reportPath);
            Assert.That(ReadValue(report, "msdfaIterations"), Is.EqualTo("250/250"));
            Assert.That(ReadValue(report, "sdfIterations"), Is.EqualTo("250/250"));
            Assert.That(File.Exists(ReadValue(report, "msdfaAtlasImage")), Is.True);
            Assert.That(File.Exists(ReadValue(report, "sdfAtlasImage")), Is.True);
            Assert.That(File.Exists(ReadValue(report, "compareImage")), Is.True);

            Measure.Custom(new SampleGroup("MSDFA.MsdfaMsPerRun", SampleUnit.Millisecond, false), ReadDouble(report, "msdfaMsPerRun"));
            Measure.Custom(new SampleGroup("MSDFA.SdfMsPerRun", SampleUnit.Millisecond, false), ReadDouble(report, "sdfMsPerRun"));
            Measure.Custom("MSDFA.DifferentPixelRatioMaxChannelGt4", ReadDouble(report, "differentPixelRatioMaxChannelGt4"));
            Measure.Custom("MSDFA.AlphaDifferentPixelRatioGt4", ReadDouble(report, "alphaDifferentPixelRatioGt4"));
            Measure.Custom("MSDFA.MaxChannelDifference", ReadDouble(report, "maxChannelDifference"));
            Measure.Custom("MSDFA.MeanAbsoluteAlpha", ReadTupleValue(report, "meanAbsoluteRgba", 3));
            Measure.Custom("MSDFA.RmseAlpha", ReadTupleValue(report, "rmseRgba", 3));
        }

        private static string GetOutputDirectory()
        {
            string projectPath = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
            return Path.Combine(projectPath, "MSDFA Stress Results");
        }

        private static string ReadValue(string report, string key)
        {
            Match match = Regex.Match(report, $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*(?:\\\"(?<string>(?:\\\\.|[^\\\"])*)\\\"|(?<number>-?[0-9]+(?:\\.[0-9]+)?))", RegexOptions.Multiline);
            Assert.That(match.Success, Is.True, $"Missing report key: {key}");
            if (match.Groups["string"].Success)
                return match.Groups["string"].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");

            return match.Groups["number"].Value.Trim();
        }

        private static double ReadDouble(string report, string key)
        {
            return ParseNumber(ReadValue(report, key));
        }

        private static double ReadTupleValue(string report, string key, int index)
        {
            string[] values = ReadValue(report, key).Split(',');
            Assert.That(index, Is.LessThan(values.Length), $"Report key {key} has too few values.");
            return ParseNumber(values[index]);
        }

        private static double ParseNumber(string value)
        {
            string normalized = value.Trim().Replace(',', '.');
            return double.Parse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
#endif
