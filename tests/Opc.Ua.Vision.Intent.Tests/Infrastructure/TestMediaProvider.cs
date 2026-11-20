// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// Deterministic media provider used by the loop tests. Returns a
    /// small fixed byte pattern (no image encoder required, no GPU
    /// required) so the tests exercise the §6.4 media-gating rules
    /// without pulling in a graphics backend.
    /// </summary>
    internal sealed class TestMediaProvider : IVisionMediaProvider
    {
        public ValueTask<VisionStreamLease> GetStreamAsync(
            VisionStreamRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = new VisionStreamSessionDataType
            {
                Uri = "opcua-test-stream://test-cell",
                SessionToken = ByteString.From(new byte[] { 0x1 }),
                Protocol = VisionStreamProtocolEnum.Rtsp,
                ExpiresAt = DateTimeUtc.From(System.DateTime.UtcNow.AddMinutes(5))
            };
            return new ValueTask<VisionStreamLease>(new VisionStreamLease(
                ServiceResult.Good, session, request.Endpoint));
        }

        public ValueTask<ServiceResult> ReleaseStreamAsync(
            ByteString sessionToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public ValueTask<ServiceResult> ConfigureStreamAsync(
            VisionStreamConfigurationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public ValueTask<ServiceResult> SelectEndpointAsync(
            NodeId streamEndpoint, NodeId clipEndpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public ValueTask<VisionClipResult> GetClipAsync(
            VisionClipRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = new VisionImageReferenceDataType
            {
                Uri = "opcua-inline://test-cell/clips/" + (request.ResultId ?? string.Empty),
                Digest = ByteString.Empty,
                DigestAlgorithm = string.Empty,
                Format = request.Format,
                PixelFormat = "Mono8",
                Width = 32u,
                Height = 32u,
                SizeBytes = s_clipBytes != null ? (uint)s_clipBytes.Length : 0u,
                Timestamp = request.Timestamp.IsNull
                    ? DateTimeUtc.From(System.DateTime.UtcNow)
                    : request.Timestamp
            };
            ByteString inline = request.RequestInline
                ? ByteString.From(s_clipBytes)
                : ByteString.Empty;
            return new ValueTask<VisionClipResult>(new VisionClipResult(
                ServiceResult.Good, image, request.Endpoint, inline));
        }

        /// <summary>
        /// Deliberately tiny — the tests do not consume the pixels, they
        /// only prove that inline delivery either arrives or is refused
        /// per the endpoint's InlineDeliveryEnabled flag.
        /// </summary>
        private static readonly byte[] s_clipBytes =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        ];
    }
}
