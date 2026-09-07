/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.VisualInspection.Tests
{
    [TestFixture]
    [Category("Vision")]
    public sealed class VisualInspectionFixtureGeometryTests
    {
        [TestCaseSource(nameof(FixtureGeometryCases))]
        public void FixtureImagesMeasureTheDesignedBoreAndSlotGeometry(
            string imageName,
            double expectedBoreMm,
            double expectedSlotMm)
        {
            PngImage image = PngImage.Load(GetFixturePath(imageName));
            FeatureMeasurements measurements = MeasureFeatures(image);

            // These assertions prevent regenerated fixtures from silently changing the sample's verdicts.
            Assert.Multiple(() =>
            {
                Assert.That(measurements.BoreDiameterMm, Is.EqualTo(expectedBoreMm).Within(0.000_001),
                    $"the decoded bore diameter must remain {FormatMm(expectedBoreMm)} mm");
                Assert.That(measurements.SlotWidthMm, Is.EqualTo(expectedSlotMm).Within(0.000_001),
                    $"the decoded slot width must remain {FormatMm(expectedSlotMm)} mm");
            });
        }

        [TestCase("bracket-ok.png", VisualInspectionVerdict.Ok)]
        [TestCase("bracket-not-ok.png", VisualInspectionVerdict.NotOk)]
        [TestCase("bracket-ambiguous.png", VisualInspectionVerdict.NotDecidable)]
        public void FixtureMeasurementsProduceTheDocumentedPartVerdicts(
            string imageName,
            VisualInspectionVerdict expectedVerdict)
        {
            PngImage image = PngImage.Load(GetFixturePath(imageName));
            FeatureMeasurements measurements = MeasureFeatures(image);

            IReadOnlyList<CharacteristicMeasurement> actual = new[]
            {
                new CharacteristicMeasurement(BoreDiameter, measurements.BoreDiameterMm, PixelPitchMm),
                new CharacteristicMeasurement(SlotWidth, measurements.SlotWidthMm, PixelPitchMm),
                new CharacteristicMeasurement(EdgeOffset, 20.00, PixelPitchMm)
            };

            // The fixture pixels are the real camera measurements that drive the sample's intended outcome.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(expectedVerdict));
        }

        [Test]
        public void IntervalTouchingLimitFromInsideIsOk()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = ReplaceOkMeasurement(
                new CharacteristicMeasurement(BoreDiameter, 12.10, 0.10));

            // Inclusive tolerance limits make an exactly in-band measurement a pass, not an ambiguity.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.Ok));
        }

        [Test]
        public void IntervalTouchingLimitFromOutsideIsNotDecidable()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = ReplaceOkMeasurement(
                new CharacteristicMeasurement(BoreDiameter, 12.30, 0.10));

            // The interval still contains the upper limit, so it is not wholly outside and must escalate.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotDecidable));
        }

        [Test]
        public void NegativeUncertaintyCannotNarrowIntervalIntoPass()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = ReplaceOkMeasurement(
                new CharacteristicMeasurement(SlotWidth, 8.20, -0.05));

            // A caller controls uncertainty; treating it as signed could report this out-of-tolerance slot as good.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotDecidable));
        }

        [Test]
        public void EnormousUncertaintyMakesVerdictNotDecidable()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = ReplaceOkMeasurement(
                new CharacteristicMeasurement(BoreDiameter, 12.00, 100.00));

            // Large uncertainty must widen the interval and force escalation rather than being trusted as a pass.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotDecidable));
        }

        [Test]
        public void MissingRequiredCharacteristicMakesVerdictNotDecidable()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = new[]
            {
                new CharacteristicMeasurement(BoreDiameter, 12.00, PixelPitchMm),
                new CharacteristicMeasurement(SlotWidth, 8.00, PixelPitchMm)
            };

            // The recipe requires EdgeOffset; no measurement is an escalation, not a silent pass.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotDecidable));
        }

        [Test]
        public void WorstOfOrderingNotOkAmongOkCharacteristicsWins()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = ReplaceOkMeasurement(
                new CharacteristicMeasurement(BoreDiameter, 12.60, PixelPitchMm));

            // Any confirmed failing characteristic makes the whole part fail.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotOk));
        }

        [Test]
        public void WorstOfOrderingNotDecidableAmongOkCharacteristicsWins()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = ReplaceOkMeasurement(
                new CharacteristicMeasurement(SlotWidth, 8.10, PixelPitchMm));

            // One ambiguous characteristic among otherwise good measurements must still escalate the whole part.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotDecidable));
        }

        [Test]
        public void WorstOfOrderingNotOkBeatsNotDecidable()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = new[]
            {
                new CharacteristicMeasurement(BoreDiameter, 12.60, PixelPitchMm),
                new CharacteristicMeasurement(SlotWidth, 8.10, PixelPitchMm),
                new CharacteristicMeasurement(EdgeOffset, 20.00, PixelPitchMm)
            };

            // Reading of "worst": a confirmed failure is more severe than an escalation request.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotOk));
        }

        [Test]
        public void FloatingPointStressAtLowerToleranceLimitIsNotDecidable()
        {
            IReadOnlyList<CharacteristicMeasurement> actual = ReplaceOkMeasurement(
                new CharacteristicMeasurement(BoreDiameter, 11.70, 0.10));

            // This reachable 11.7 +/- 0.1 case touches 11.8; integer micrometres avoid double drift to NotOk.
            Assert.That(LocalVerdictRule.EvaluatePart(actual), Is.EqualTo(VisualInspectionVerdict.NotDecidable));
        }

        private static IEnumerable<TestCaseData> FixtureGeometryCases()
        {
            yield return new TestCaseData("bracket-ok.png", 12.00, 8.00);
            yield return new TestCaseData("bracket-not-ok.png", 12.60, 8.00);
            yield return new TestCaseData("bracket-ambiguous.png", 12.00, 8.10);
        }

        private static FeatureMeasurements MeasureFeatures(PngImage image)
        {
            IReadOnlyList<PixelRun> boreRuns = FindDarkRuns(image, BoreCenterY, BoreSearchStartX, BoreSearchEndX);
            IReadOnlyList<PixelRun> slotRuns = FindDarkRuns(image, SlotCenterY, SlotSearchStartX, SlotSearchEndX);

            PixelRun boreRun = boreRuns.OrderByDescending(run => run.Length).First();
            PixelRun slotRun = slotRuns.OrderByDescending(run => run.Length).First();

            return new FeatureMeasurements(boreRun.Length / PixelsPerMillimetre, slotRun.Length / PixelsPerMillimetre);
        }

        private static IReadOnlyList<PixelRun> FindDarkRuns(PngImage image, int y, int startX, int endX)
        {
            var runs = new List<PixelRun>();
            int? runStart = null;
            for (int x = startX; x <= endX; x++)
            {
                bool isDark = image.IsDark(x, y);
                if (isDark && runStart is null)
                {
                    runStart = x;
                }
                else if (!isDark && runStart is not null)
                {
                    runs.Add(new PixelRun(runStart.Value, x - 1));
                    runStart = null;
                }
            }

            if (runStart is not null)
            {
                runs.Add(new PixelRun(runStart.Value, endX));
            }

            Assert.That(runs, Is.Not.Empty, "each fixture must contain the dark feature being measured");
            return runs;
        }

        private static IReadOnlyList<CharacteristicMeasurement> ReplaceOkMeasurement(
            CharacteristicMeasurement replacement)
        {
            List<CharacteristicMeasurement> measurements = CreateOkMeasurements().ToList();
            int index = measurements.FindIndex(measurement => measurement.Name == replacement.Name);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "test setup must replace a recipe characteristic");
            measurements[index] = replacement;
            return measurements;
        }

        private static IReadOnlyList<CharacteristicMeasurement> CreateOkMeasurements()
        {
            return new[]
            {
                new CharacteristicMeasurement(BoreDiameter, 12.00, PixelPitchMm),
                new CharacteristicMeasurement(SlotWidth, 8.00, PixelPitchMm),
                new CharacteristicMeasurement(EdgeOffset, 20.00, PixelPitchMm)
            };
        }

        private static string GetFixturePath(string imageName)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", imageName);
        }

        private static string FormatMm(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private const double PixelsPerMillimetre = 10.00;
        private const double PixelPitchMm = 0.10;
        private const int BoreCenterY = 300;
        private const int SlotCenterY = 300;
        private const int BoreSearchStartX = 200;
        private const int BoreSearchEndX = 380;
        private const int SlotSearchStartX = 400;
        private const int SlotSearchEndX = 560;
        private const string BoreDiameter = "BoreDiameter";
        private const string SlotWidth = "SlotWidth";
        private const string EdgeOffset = "EdgeOffset";
    }

    public enum VisualInspectionVerdict
    {
        Ok,
        NotDecidable,
        NotOk
    }

    internal static class LocalVerdictRule
    {
        public static VisualInspectionVerdict EvaluatePart(IReadOnlyList<CharacteristicMeasurement> measurements)
        {
            var lookup = measurements.ToDictionary(measurement => measurement.Name, StringComparer.Ordinal);
            VisualInspectionVerdict worst = VisualInspectionVerdict.Ok;
            for (int ii = 0; ii < s_recipe.Length; ii++)
            {
                VisualInspectionVerdict verdict = lookup.TryGetValue(
                    s_recipe[ii].Name,
                    out CharacteristicMeasurement measurement)
                    ? EvaluateCharacteristic(s_recipe[ii], measurement)
                    : VisualInspectionVerdict.NotDecidable;

                if (verdict > worst)
                {
                    worst = verdict;
                }
            }

            return worst;
        }

        private static VisualInspectionVerdict EvaluateCharacteristic(
            CharacteristicRecipe recipe,
            CharacteristicMeasurement measurement)
        {
            int actual = ToMicrometres(measurement.ActualMm);
            int uncertainty = Math.Abs(ToMicrometres(measurement.UncertaintyMm));
            int actualLower = actual - uncertainty;
            int actualUpper = actual + uncertainty;
            int toleranceLower = ToMicrometres(recipe.NominalMm - recipe.LowerToleranceMm);
            int toleranceUpper = ToMicrometres(recipe.NominalMm + recipe.UpperToleranceMm);

            if (actualLower >= toleranceLower && actualUpper <= toleranceUpper)
            {
                return VisualInspectionVerdict.Ok;
            }

            if (actualUpper < toleranceLower || actualLower > toleranceUpper)
            {
                return VisualInspectionVerdict.NotOk;
            }

            return VisualInspectionVerdict.NotDecidable;
        }

        private static int ToMicrometres(double value)
        {
            return checked((int)Math.Round(value * 1000.00, MidpointRounding.AwayFromZero));
        }

        private static readonly CharacteristicRecipe[] s_recipe =
        {
            new CharacteristicRecipe("BoreDiameter", 12.00, 0.20, 0.20),
            new CharacteristicRecipe("SlotWidth", 8.00, 0.15, 0.15),
            new CharacteristicRecipe("EdgeOffset", 20.00, 0.25, 0.25)
        };
    }

    internal sealed class PngImage
    {
        private PngImage(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            m_pixels = pixels;
        }

        public int Width { get; }

        public int Height { get; }

        public static PngImage Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int position = PngSignature.Length;
            int width = 0;
            int height = 0;
            byte[] idat = Array.Empty<byte>();

            while (position < bytes.Length)
            {
                int length = ReadBigEndianInt32(bytes, position);
                string chunkType = System.Text.Encoding.ASCII.GetString(bytes, position + 4, 4);
                int dataOffset = position + 8;

                if (chunkType == "IHDR")
                {
                    width = ReadBigEndianInt32(bytes, dataOffset);
                    height = ReadBigEndianInt32(bytes, dataOffset + 4);
                    Assert.That(bytes[dataOffset + 8], Is.EqualTo(8), "fixtures must be 8-bit PNGs");
                    Assert.That(bytes[dataOffset + 9], Is.EqualTo(2), "fixtures must be true-colour RGB PNGs");
                    Assert.That(bytes[dataOffset + 12], Is.EqualTo(0), "fixtures must not be interlaced");
                }
                else if (chunkType == "IDAT")
                {
                    byte[] next = new byte[idat.Length + length];
                    Buffer.BlockCopy(idat, 0, next, 0, idat.Length);
                    Buffer.BlockCopy(bytes, dataOffset, next, idat.Length, length);
                    idat = next;
                }
                else if (chunkType == "IEND")
                {
                    break;
                }

                position = dataOffset + length + PngCrcLength;
            }

            return new PngImage(width, height, DecodePixels(idat, width, height));
        }

        public bool IsDark(int x, int y)
        {
            int offset = ((y * Width) + x) * BytesPerPixel;
            return m_pixels[offset] < DarkThreshold &&
                m_pixels[offset + 1] < DarkThreshold &&
                m_pixels[offset + 2] < DarkThreshold;
        }

        private static byte[] DecodePixels(byte[] compressed, int width, int height)
        {
            byte[] raw = InflateZlib(compressed);
            int stride = width * BytesPerPixel;
            var pixels = new byte[height * stride];
            var previous = new byte[stride];
            int sourceOffset = 0;

            for (int y = 0; y < height; y++)
            {
                byte filter = raw[sourceOffset++];
                var scanline = new byte[stride];
                Buffer.BlockCopy(raw, sourceOffset, scanline, 0, stride);
                sourceOffset += stride;
                Unfilter(scanline, previous, filter);
                Buffer.BlockCopy(scanline, 0, pixels, y * stride, stride);
                previous = scanline;
            }

            return pixels;
        }

        private static byte[] InflateZlib(byte[] compressed)
        {
            using var source = new MemoryStream(compressed, ZlibHeaderLength, compressed.Length - ZlibWrapperLength);
            using var inflater = new DeflateStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflater.CopyTo(output);
            return output.ToArray();
        }

        private static void Unfilter(byte[] scanline, byte[] previous, byte filter)
        {
            for (int x = 0; x < scanline.Length; x++)
            {
                int left = x >= BytesPerPixel ? scanline[x - BytesPerPixel] : 0;
                int up = previous[x];
                int upLeft = x >= BytesPerPixel ? previous[x - BytesPerPixel] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => PaethPredictor(left, up, upLeft),
                    _ => throw new InvalidDataException("Unsupported PNG filter type.")
                };

                scanline[x] = unchecked((byte)(scanline[x] + predictor));
            }
        }

        private static int PaethPredictor(int left, int up, int upLeft)
        {
            int estimate = left + up - upLeft;
            int leftDistance = Math.Abs(estimate - left);
            int upDistance = Math.Abs(estimate - up);
            int upLeftDistance = Math.Abs(estimate - upLeft);

            if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
            {
                return left;
            }

            return upDistance <= upLeftDistance ? up : upLeft;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) |
                (bytes[offset + 1] << 16) |
                (bytes[offset + 2] << 8) |
                bytes[offset + 3];
        }

        private readonly byte[] m_pixels;
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private const int BytesPerPixel = 3;
        private const int DarkThreshold = 64;
        private const int PngCrcLength = 4;
        private const int ZlibHeaderLength = 2;
        private const int ZlibWrapperLength = 6;
    }

    internal readonly struct CharacteristicRecipe
    {
        public CharacteristicRecipe(
            string name,
            double nominalMm,
            double lowerToleranceMm,
            double upperToleranceMm)
        {
            Name = name;
            NominalMm = nominalMm;
            LowerToleranceMm = lowerToleranceMm;
            UpperToleranceMm = upperToleranceMm;
        }

        public string Name { get; }

        public double NominalMm { get; }

        public double LowerToleranceMm { get; }

        public double UpperToleranceMm { get; }
    }

    internal readonly struct CharacteristicMeasurement
    {
        public CharacteristicMeasurement(string name, double actualMm, double uncertaintyMm)
        {
            Name = name;
            ActualMm = actualMm;
            UncertaintyMm = uncertaintyMm;
        }

        public string Name { get; }

        public double ActualMm { get; }

        public double UncertaintyMm { get; }
    }

    internal readonly struct FeatureMeasurements
    {
        public FeatureMeasurements(double boreDiameterMm, double slotWidthMm)
        {
            BoreDiameterMm = boreDiameterMm;
            SlotWidthMm = slotWidthMm;
        }

        public double BoreDiameterMm { get; }

        public double SlotWidthMm { get; }
    }

    internal readonly struct PixelRun
    {
        public PixelRun(int startX, int endX)
        {
            StartX = startX;
            EndX = endX;
        }

        public int StartX { get; }

        public int EndX { get; }

        public int Length => EndX - StartX + 1;
    }
}
