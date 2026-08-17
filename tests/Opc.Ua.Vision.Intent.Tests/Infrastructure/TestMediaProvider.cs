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

using System.Threading;
using System.Threading.Tasks;
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
                ? ByteString.From(s_clipBytes!)
                : ByteString.Empty;
            return new ValueTask<VisionClipResult>(new VisionClipResult(
                ServiceResult.Good, image, request.Endpoint, inline));
        }

        // Deliberately tiny — the tests do not consume the pixels, they
        // only prove that inline delivery either arrives or is refused
        // per the endpoint's InlineDeliveryEnabled flag.
        private static readonly byte[] s_clipBytes =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        ];
    }
}
