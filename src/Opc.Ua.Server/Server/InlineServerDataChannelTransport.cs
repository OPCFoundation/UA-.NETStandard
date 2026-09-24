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
using System.Threading;
using System.Threading.Tasks;


namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Carries data channels over the SecureChannel that already exists,
    /// using the inline framing of Part 6 errata clause 5 as it applies to
    /// <c>opc.tcp</c> and <c>opc.wss</c> (§6.1, §6.2).
    /// </summary>
    /// <remarks>
    /// Inline framing needs no transport of its own: a frame is one
    /// MessageChunk written to the connection the Client already holds, so
    /// this resolves the UASC channel behind the request and enables the
    /// data channel engine on it.
    /// <para>
    /// There are no transport streams to allocate or bind, which is why the
    /// stream operations are inert here. §7.4's <c>transportChannelId</c>
    /// rules belong to the outer-protocol transports; under inline framing
    /// the ChannelId in the frame header is the only demultiplexer.
    /// </para>
    /// </remarks>
    public sealed class InlineServerDataChannelTransport : IServerDataChannelTransport
    {
        /// <inheritdoc/>
        public bool TryGetManager(
            SecureChannelContext secureChannelContext,
            DataChannelServerCapabilities capabilities,
            ITelemetryContext telemetry,
            out DataChannelManager manager,
            out uint maxFrameSize,
            out bool isReliable)
        {
            if (secureChannelContext == null)
            {
                throw new ArgumentNullException(nameof(secureChannelContext));
            }

            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            manager = null!;
            maxFrameSize = 0;
            isReliable = true;

            if (!IsInlineFramed(secureChannelContext))
            {
                return false;
            }

            if (!UaSCSecureChannelRegistry.TryGet(
                    secureChannelContext.SecureChannelId,
                    out UaSCUaBinaryChannel? channel) ||
                channel == null)
            {
                return false;
            }

            manager = channel.EnableDataChannels(
                isServer: true,
                telemetry,
                capabilities.MaxDataChannels,
                capabilities.MaxCreditPerChannel);

            int body = channel.MaxDataChannelBodySize;
            maxFrameSize = body <= 0 ? 0 : (uint)body;

            // Inline framing rides the SecureChannel, which is ordered and
            // retransmitted by the connection beneath it, so a frame is never
            // genuinely lost in flight.
            isReliable = true;
            return true;
        }

        /// <inheritdoc/>
        public ValueTask<ulong> AllocateServerStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            DataChannelDirection direction,
            CancellationToken ct)
        {
            // Inline framing multiplexes on ChannelId rather than on streams,
            // so there is nothing to allocate and the sentinel is returned.
            return new ValueTask<ulong>(0UL);
        }

        /// <inheritdoc/>
        public ValueTask BindClientStreamAsync(
            SecureChannelContext secureChannelContext,
            uint channelId,
            ulong streamId,
            DataChannelDirection direction,
            CancellationToken ct)
        {
            return default;
        }

        /// <inheritdoc/>
        public void AbortSecureChannel(SecureChannelContext secureChannelContext, StatusCode reason)
        {
            if (secureChannelContext != null &&
                UaSCSecureChannelRegistry.TryGet(
                    secureChannelContext.SecureChannelId,
                    out UaSCUaBinaryChannel? channel) &&
                channel != null)
            {
                channel.DataChannels?.AbortAll(reason);
            }
        }

        private static bool IsInlineFramed(SecureChannelContext secureChannelContext)
        {
            string? profile = secureChannelContext.EndpointDescription?.TransportProfileUri;

            // An endpoint that names no profile is the binary UASC listener,
            // which is inline framed.
            return string.IsNullOrEmpty(profile) ||
                string.Equals(profile, Profiles.UaTcpTransport, StringComparison.Ordinal) ||
                string.Equals(profile, Profiles.UaWssTransport, StringComparison.Ordinal);
        }
    }
}
