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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace UaLens.Plugins.Companions.Providers
{
    internal static class VisionOverlayExport
    {
        public static string ValidateDestination(string? path)
        {
            if (!string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Choose a new local .svg file.", nameof(path));
            }
            return OpenUsdCompanionExporter.ValidateDestination(path);
        }

        public static ByteString Render(VisionDetectionResultSnapshot snapshot)
        {
            VisionWorkflowResults.ValidateDetection(snapshot);
            VisionImageReferenceDataType image = snapshot.Frame ??
                throw new ServiceResultException(StatusCodes.BadNotFound,
                    "An overlay requires the published image's dimensions and digest.");
            VisionWorkflowValues.ValidateImage(image, default);
            if (string.IsNullOrWhiteSpace(snapshot.ResultId) ||
                snapshot.ResultId.Length > 256 ||
                snapshot.NodeId.IsNull ||
                snapshot.SensorId.IsNull ||
                snapshot.PipelineId.IsNull ||
                snapshot.CreationTime == default)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The overlay source identity is missing.");
            }
            var text = new StringBuilder();
            using (var writer = XmlWriter.Create(text, new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = true
            }))
            {
                writer.WriteStartElement("svg", SvgNamespace);
                Attribute(writer, "width", image.Width);
                Attribute(writer, "height", image.Height);
                writer.WriteAttributeString("viewBox",
                    FormattableString.Invariant($"0 0 {image.Width} {image.Height}"));
                writer.WriteAttributeString("overflow", "hidden");
                writer.WriteElementString("title", SvgNamespace, "Vision result " + snapshot.ResultId);
                writer.WriteElementString("desc", SvgNamespace,
                    $"Result: {snapshot.NodeId}; sensor: {snapshot.SensorId}; pipeline: {snapshot.PipelineId}; " +
                    $"frame SHA-256: {Convert.ToHexString(image.Digest.Span)}; " +
                    $"acquired: {image.Timestamp.ToDateTime().ToString("O", CultureInfo.InvariantCulture)}. " +
                    "Pixel-space geometry only; no image or remote media is embedded.");
                foreach (VisionDetectionDataType detection in snapshot.Detections)
                {
                    if (detection.HasBoundingBox2D)
                    {
                        WriteBox(writer, detection, image);
                    }
                }
                writer.WriteEndElement();
            }
            if (Encoding.UTF8.GetByteCount(text.ToString()) > VisionWorkflowValues.MaximumBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "The SVG exceeds 1 MiB.");
            }
            return ByteString.From(Encoding.UTF8.GetBytes(text.ToString()));
        }

        public static async Task WriteAsync(
            string destination, ByteString svg, Action requireCurrent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(requireCurrent);
            string path = ValidateDestination(destination);
            if (svg.IsEmpty || svg.Length > VisionWorkflowValues.MaximumBytes)
            {
                throw new ArgumentException("A bounded, nonempty SVG is required.", nameof(svg));
            }
            cancellationToken.ThrowIfCancellationRequested();
            requireCurrent();
            string temporary = Path.Combine(Path.GetDirectoryName(path)!,
                ".ualens-vision-" + Guid.NewGuid().ToString("N") + ".pending");
            using var pending = new PendingFile(temporary);
            try
            {
                var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                    FileOptions.Asynchronous);
                pending.Owned = true;
                await using (file.ConfigureAwait(false))
                {
                    await file.WriteAsync(svg.Memory, cancellationToken).ConfigureAwait(false);
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                requireCurrent();
                _ = ValidateDestination(path);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path);
                pending.Owned = false;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                OperationCanceledException or InvalidOperationException or ArgumentException)
            {
                pending.Failure = error;
                throw;
            }
        }

        private static void WriteBox(
            XmlWriter writer, VisionDetectionDataType detection, VisionImageReferenceDataType image)
        {
            VisionBoundingBox2DDataType box = detection.BoundingBox2D;
            if (Math.Abs(box.CenterX) > 2d * image.Width ||
                Math.Abs(box.CenterY) > 2d * image.Height ||
                box.Width > 2d * image.Width ||
                box.Height > 2d * image.Height)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange,
                    "Overlay geometry must fit within twice the source image's extent.");
            }
            double left = box.CenterX - (box.Width / 2);
            double top = box.CenterY - (box.Height / 2);
            writer.WriteStartElement("g", SvgNamespace);
            writer.WriteAttributeString("transform",
                FormattableString.Invariant($"rotate({box.Rotation % 360:R} {box.CenterX:R} {box.CenterY:R})"));
            writer.WriteStartElement("rect", SvgNamespace);
            Attribute(writer, "x", left);
            Attribute(writer, "y", top);
            Attribute(writer, "width", box.Width);
            Attribute(writer, "height", box.Height);
            writer.WriteAttributeString("fill", "none");
            writer.WriteAttributeString("stroke", "#00a870");
            writer.WriteAttributeString("stroke-width", "2");
            writer.WriteElementString("title", SvgNamespace, detection.DetectionId);
            writer.WriteEndElement();
            writer.WriteStartElement("text", SvgNamespace);
            Attribute(writer, "x", left);
            Attribute(writer, "y", Math.Max(12, top - 2));
            writer.WriteAttributeString("fill", "#00a870");
            writer.WriteAttributeString("font-size", "12");
            writer.WriteString(detection.ClassLabel +
                " " +
                detection.Confidence.ToString("P1", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private static void Attribute(XmlWriter writer, string name, double value)
        {
            writer.WriteAttributeString(name, value.ToString("R", CultureInfo.InvariantCulture));
        }

        private const string SvgNamespace = "http://www.w3.org/2000/svg";

        private sealed class PendingFile(string path) : IDisposable
        {
            public bool Owned { get; set; }

            public Exception? Failure { get; set; }

            public void Dispose()
            {
                if (!Owned)
                {
                    return;
                }
                try
                {
                    File.Delete(path);
                    Owned = false;
                }
                catch (Exception cleanup) when (Failure is not null &&
                    cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new AggregateException("Overlay export and owned-file cleanup failed.", Failure, cleanup);
                }
            }
        }
    }
}
