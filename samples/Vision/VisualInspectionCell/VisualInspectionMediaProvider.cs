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
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Vision.VisualInspectionCell
{
    internal sealed partial class VisualInspectionMediaProvider : IVisionMediaProvider
    {
        public VisualInspectionMediaProvider(
            VisualInspectionAnalysisService analysis,
            VisualInspectionResultPublisher publisher,
            ILogger<VisualInspectionMediaProvider> logger)
        {
            m_analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            m_publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValueTask<VisionStreamLease> GetStreamAsync(
            VisionStreamRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = new VisionStreamSessionDataType
            {
                SessionToken = ByteString.Empty,
                Uri = string.Empty,
                Protocol = request.PreferredProtocol,
                ExpiresAt = DateTimeUtc.MinValue
            };
            return ValueTask.FromResult(new VisionStreamLease(
                new ServiceResult(StatusCodes.BadNotSupported,
                    LocalizedText.From("The visual-inspection sample serves still PNG fixture clips only.")),
                session,
                request.Endpoint));
        }

        public ValueTask<ServiceResult> ReleaseStreamAsync(
            ByteString sessionToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        public ValueTask<ServiceResult> ConfigureStreamAsync(
            VisionStreamConfigurationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ServiceResult(StatusCodes.BadNotSupported,
                LocalizedText.From("Fixture clips are static and cannot be reconfigured.")));
        }

        public ValueTask<ServiceResult> SelectEndpointAsync(
            NodeId streamEndpoint, NodeId clipEndpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        public ValueTask<VisionClipResult> GetClipAsync(
            VisionClipRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_publisher.TryGetPublished(request.ResultId, out PublishedInspectionResult? published) ||
                published == null ||
                string.IsNullOrEmpty(published.FixtureName))
            {
                return ValueTask.FromResult(new VisionClipResult(
                    new ServiceResult(StatusCodes.BadNodeIdUnknown,
                        LocalizedText.From("The requested inspection result has no fixture clip.")),
                    new VisionImageReferenceDataType(),
                    request.Endpoint,
                    ByteString.Empty));
            }
            string fixture = published.FixtureName;
            string path = Path.Combine(m_analysis.FixtureDirectory, fixture);
            ByteString png = ByteString.From(File.ReadAllBytes(path));
            byte[] digest = SHA256.HashData(png.Span);
            DateTimeUtc timestamp = request.Timestamp.IsNull
                ? DateTimeUtc.From(DateTime.UnixEpoch)
                : request.Timestamp;
            var image = new VisionImageReferenceDataType
            {
                Uri = FormattableString.Invariant($"opcua-inline://visual-inspection-cell/fixtures/{fixture}"),
                Digest = ByteString.From(digest),
                DigestAlgorithm = "SHA-256",
                Format = VisionClipFormatEnum.Png,
                PixelFormat = PixelFormat,
                Width = Width,
                Height = Height,
                SizeBytes = (uint)png.Length,
                Timestamp = timestamp
            };
            m_logger.FixtureClipServed(fixture, png.Length);
            return ValueTask.FromResult(new VisionClipResult(
                ServiceResult.Good,
                image,
                request.Endpoint,
                request.RequestInline ? png : ByteString.Empty));
        }

        public const string PixelFormat = "RGB8";
        public const uint Width = 800;
        public const uint Height = 600;

        private readonly VisualInspectionAnalysisService m_analysis;
        private readonly VisualInspectionResultPublisher m_publisher;
        private readonly ILogger<VisualInspectionMediaProvider> m_logger;
    }

    internal static partial class VisualInspectionMediaProviderLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Media + 1,
            Level = LogLevel.Information,
            Message = "Served fixture clip {FixtureName} ({Bytes} bytes).")]
        public static partial void FixtureClipServed(
            this ILogger<VisualInspectionMediaProvider> logger,
            string fixtureName,
            int bytes);
    }
}
