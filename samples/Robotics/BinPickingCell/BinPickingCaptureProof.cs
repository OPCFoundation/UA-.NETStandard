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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Vision.OpenUsd;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Renders one frame of the cell stage as soon as the server has
    /// started, saves it as a PNG outside the source tree, and reports
    /// how many distinct colours the frame contains and what the mean
    /// RGB in each of the five part regions looks like.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The purpose of the sample is to prove that the scene renders in
    /// colour and that the mixed parts are visually distinguishable —
    /// the demo it feeds is a language model looking at the frame and
    /// picking "the red part". A blank or white frame would silently
    /// destroy that. This service therefore also validates the guard
    /// the OpenUSD capture provider enforces on drawn-geometry counts,
    /// and treats a <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>
    /// result as a soft warning (typical of a CI host with no graphics
    /// device) rather than a fatal error.
    /// </para>
    /// <para>
    /// The service does not stop the host on any outcome. Set the
    /// <c>captureOnStartup=false</c> configuration key to skip the
    /// diagnostic entirely.
    /// </para>
    /// </remarks>
    internal sealed partial class BinPickingCaptureProof : BackgroundService
    {
        public BinPickingCaptureProof(
            ISceneCameraCaptureProvider capture,
            BinPickingCellStage stage,
            ILogger<BinPickingCaptureProof> logger,
            bool enabled,
            string? artifactDirectory)
        {
            m_capture = capture ?? throw new ArgumentNullException(nameof(capture));
            m_stage = stage ?? throw new ArgumentNullException(nameof(stage));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_enabled = enabled;
            m_artifactDirectory = string.IsNullOrEmpty(artifactDirectory)
                ? Path.Combine(Path.GetTempPath(), "OPCFoundation", "BinPickingCell")
                : artifactDirectory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!m_enabled)
            {
                m_logger.CaptureSkipped();
                return;
            }
            m_logger.CaptureProofStarted(m_capture.Backend);
            var request = new SceneCameraCaptureRequest
            {
                StageIdentifier = m_stage.CellStagePath,
                PrimPath = BinPickingVisionCell.CameraPrimPath,
                Width = ProofWidth,
                Height = ProofHeight,
                TimeCode = 0.0,
                Format = SceneCameraImageFormat.Png,
                TimestampUtc = DateTime.UtcNow
            };
            SceneCameraCaptureResult result = await m_capture
                .CaptureAsync(request, stoppingToken)
                .ConfigureAwait(false);
            if (result.Status != SceneCameraCaptureStatus.Succeeded)
            {
                m_logger.CaptureProofNoImage(result.Status, result.Reason ?? string.Empty);
                return;
            }
            byte[] png = result.Image.ToArray();
            Directory.CreateDirectory(m_artifactDirectory);
            string outPath = Path.Combine(m_artifactDirectory, "bin-picking-frame.png");
            await File.WriteAllBytesAsync(outPath, png, stoppingToken).ConfigureAwait(false);

            (byte[] Rgba, int W, int H) decoded;
            try
            {
                decoded = PngDecoder.Decode(png);
            }
            catch (Exception ex)
            {
                m_logger.CaptureProofDecodeFailed(outPath, ex.Message);
                return;
            }

            int distinct = CountDistinctColours(decoded.Rgba);
            (int mR, int mG, int mB) = MeanRgb(decoded.Rgba, decoded.W, decoded.H);
            List<(string Label, int R, int G, int B)> partSamples = SamplePartRegions(
                decoded.Rgba, decoded.W, decoded.H);

            m_logger.CaptureProofSaved(
                outPath, decoded.W, decoded.H, png.Length, distinct, mR, mG, mB);
            foreach ((string label, int r, int g, int b) in partSamples)
            {
                m_logger.CaptureProofPart(label, r, g, b);
            }

            AppendReport(m_artifactDirectory, outPath, decoded, distinct, mR, mG, mB, partSamples,
                m_capture.Backend, result.Elapsed);
        }

        private static int CountDistinctColours(byte[] rgba)
        {
            var seen = new HashSet<int>();
            for (int ii = 0; ii + 3 < rgba.Length; ii += 4)
            {
                int packed = rgba[ii] << 16 | rgba[ii + 1] << 8 | rgba[ii + 2];
                seen.Add(packed);
                if (seen.Count > 65536)
                {
                    return seen.Count;
                }
            }
            return seen.Count;
        }

        private static (int R, int G, int B) MeanRgb(byte[] rgba, int width, int height)
        {
            long r = 0, g = 0, b = 0, count = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = 4 * (y * width + x);
                    r += rgba[offset];
                    g += rgba[offset + 1];
                    b += rgba[offset + 2];
                    count++;
                }
            }
            if (count == 0)
            {
                return (0, 0, 0);
            }
            return ((int)(r / count), (int)(g / count), (int)(b / count));
        }

        private static List<(string Label, int R, int G, int B)> SamplePartRegions(
            byte[] rgba, int width, int height)
        {
            var samples = new List<(string, int, int, int)>();
            foreach ((string label, double fx, double fy) in s_partSampleFractions)
            {
                int cx = (int)Math.Clamp(Math.Round(fx * width), 0.0, width - 1);
                int cy = (int)Math.Clamp(Math.Round(fy * height), 0.0, height - 1);
                (int r, int g, int b) = AverageWindow(rgba, width, height, cx, cy, radius: 20);
                samples.Add((label, r, g, b));
            }
            return samples;
        }

        private static (int R, int G, int B) AverageWindow(
            byte[] rgba, int width, int height, int cx, int cy, int radius)
        {
            long r = 0, g = 0, b = 0, count = 0;
            int x0 = Math.Max(0, cx - radius);
            int x1 = Math.Min(width - 1, cx + radius);
            int y0 = Math.Max(0, cy - radius);
            int y1 = Math.Min(height - 1, cy + radius);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int offset = 4 * (y * width + x);
                    r += rgba[offset];
                    g += rgba[offset + 1];
                    b += rgba[offset + 2];
                    count++;
                }
            }
            if (count == 0)
            {
                return (0, 0, 0);
            }
            return ((int)(r / count), (int)(g / count), (int)(b / count));
        }

        private static void AppendReport(
            string directory,
            string imagePath,
            (byte[] Rgba, int W, int H) frame,
            int distinctColours,
            int meanR, int meanG, int meanB,
            List<(string Label, int R, int G, int B)> partSamples,
            SceneCameraCaptureBackend backend,
            TimeSpan elapsed)
        {
            string reportPath = Path.Combine(directory, "bin-picking-frame.report.txt");
            using StreamWriter writer = File.CreateText(reportPath);
            CultureInfo culture = CultureInfo.InvariantCulture;
            writer.WriteLine("BinPickingCell capture proof");
            writer.WriteLine("============================");
            writer.WriteLine(string.Format(culture, "Image      : {0}", imagePath));
            writer.WriteLine(string.Format(culture, "Dimensions : {0} x {1}", frame.W, frame.H));
            writer.WriteLine(string.Format(culture, "Backend    : {0}", backend));
            writer.WriteLine(string.Format(culture, "Elapsed    : {0:0.0} ms", elapsed.TotalMilliseconds));
            writer.WriteLine(string.Format(culture, "Distinct RGB colours : {0}", distinctColours));
            writer.WriteLine(string.Format(culture,
                "Mean RGB (whole frame): ({0}, {1}, {2})", meanR, meanG, meanB));
            writer.WriteLine();
            writer.WriteLine("Part samples (mean RGB of a 41x41 patch around the centroid):");
            foreach ((string label, int r, int g, int b) in partSamples)
            {
                writer.WriteLine(string.Format(culture,
                    "  {0,-15} : ({1,3}, {2,3}, {3,3})", label, r, g, b));
            }
        }

        // The proof renders at the same resolution the sensor declares and the cell
        // delivers, so what it checks is the picture an agent is actually handed.
        private const int ProofWidth = (int)BinPickingVisionCell.SensorWidth;
        private const int ProofHeight = (int)BinPickingVisionCell.SensorHeight;

        private static readonly (string Label, double Fx, double Fy)[] s_partSampleFractions =
        [
            ("RedCube",       0.419, 0.661),
            ("GreenCylinder", 0.564, 0.398),
            ("BlueSphere",    0.627, 0.600),
            ("YellowSlab",    0.437, 0.382),
            ("OrangeBrick",   0.563, 0.519)
        ];

        private readonly ISceneCameraCaptureProvider m_capture;
        private readonly BinPickingCellStage m_stage;
        private readonly ILogger<BinPickingCaptureProof> m_logger;
        private readonly bool m_enabled;
        private readonly string m_artifactDirectory;
    }

    internal static partial class BinPickingCaptureProofLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.Startup + 1,
            Level = LogLevel.Information,
            Message = "Capture-proof diagnostic starting; renderer backend={Backend}.")]
        public static partial void CaptureProofStarted(
            this ILogger<BinPickingCaptureProof> logger, SceneCameraCaptureBackend backend);

        [LoggerMessage(EventId = BinPickingCellEventIds.Startup + 2,
            Level = LogLevel.Information,
            Message = "Capture-proof diagnostic disabled by configuration (captureOnStartup=false).")]
        public static partial void CaptureSkipped(this ILogger<BinPickingCaptureProof> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.Startup + 3,
            Level = LogLevel.Warning,
            Message = "Capture-proof diagnostic did not produce a frame: {Status} - {Reason}.")]
        public static partial void CaptureProofNoImage(
            this ILogger<BinPickingCaptureProof> logger,
            SceneCameraCaptureStatus status,
            string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.Startup + 4,
            Level = LogLevel.Warning,
            Message = "Capture-proof frame saved to {Path} but PNG decoder failed: {Reason}.")]
        public static partial void CaptureProofDecodeFailed(
            this ILogger<BinPickingCaptureProof> logger, string path, string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.Startup + 5,
            Level = LogLevel.Information,
            Message = "Capture-proof frame {Width}x{Height} ({Bytes} bytes) saved to {Path}; " +
                "distinct RGB colours={Distinct}; mean RGB=({MeanR},{MeanG},{MeanB}).")]
        public static partial void CaptureProofSaved(
            this ILogger<BinPickingCaptureProof> logger,
            string path,
            int width, int height, int bytes,
            int distinct, int meanR, int meanG, int meanB);

        [LoggerMessage(EventId = BinPickingCellEventIds.Startup + 6,
            Level = LogLevel.Information,
            Message = "Part '{Label}' mean RGB = ({R},{G},{B}).")]
        public static partial void CaptureProofPart(
            this ILogger<BinPickingCaptureProof> logger,
            string label, int r, int g, int b);
    }
}
