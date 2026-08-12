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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Attaches inline data channel framing to a UASC binary channel.
    /// </summary>
    /// <remarks>
    /// A frame is one STR MessageChunk, written to the connection exactly as a
    /// MSG chunk is. The channel itself knows nothing of the frame format; it
    /// carries the MessageType on behalf of the extension registered here.
    /// </remarks>
    public static class UaSCDataChannelExtensions
    {
        /// <summary>
        /// Enables the data channel feature on a SecureChannel.
        /// </summary>
        /// <remarks>
        /// Experimental. Until this is called the STR dispatch is inert and an
        /// incoming frame closes the SecureChannel, which is what the
        /// interoperability rule of the Part 6 errata 5.16 requires of a peer
        /// that does not implement this specification.
        /// </remarks>
        /// <param name="channel">The SecureChannel that carries the frames.</param>
        /// <param name="isServer">True on the server side, which allocates
        /// ChannelIds.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="maxDataChannels">The most channels kept open at once
        /// on this SecureChannel.</param>
        /// <param name="maxCreditPerChannel">The largest window granted to one
        /// channel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="channel"/> is <c>null</c>.</exception>
        public static DataChannelManager EnableDataChannels(
            this UaSCUaBinaryChannel channel,
            bool isServer,
            ITelemetryContext telemetry,
            ushort maxDataChannels = 16,
            uint maxCreditPerChannel = 1024 * 1024)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            if (channel.TryGetDataChannelExtension(out DataChannelExtension? existing) &&
                existing != null)
            {
                return existing.Manager;
            }

            var extension = new DataChannelExtension(
                channel,
                isServer,
                telemetry,
                maxDataChannels,
                maxCreditPerChannel);

            var registered = (DataChannelExtension)channel.RegisterMessageExtension(extension);

            if (!ReferenceEquals(registered, extension))
            {
                _ = extension.Manager.DisposeAsync().AsTask();
            }

            return registered.Manager;
        }

        /// <summary>
        /// Returns the data channels multiplexed onto a SecureChannel, or
        /// <c>null</c> when the feature has not been enabled on it.
        /// </summary>
        /// <param name="channel">The SecureChannel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="channel"/> is <c>null</c>.</exception>
        public static DataChannelManager? GetDataChannels(this UaSCUaBinaryChannel channel)
        {
            return channel.TryGetDataChannelExtension(out DataChannelExtension? extension)
                ? extension?.Manager
                : null;
        }

        /// <summary>
        /// Returns the extension that owns the STR MessageType on a
        /// SecureChannel, if one is registered.
        /// </summary>
        /// <remarks>
        /// The extension is an implementation detail of the inline framing:
        /// a consumer reaches the channels through
        /// <see cref="GetDataChannels"/>, which returns the engine rather
        /// than the adapter that drives it.
        /// </remarks>
        /// <param name="channel">The SecureChannel.</param>
        /// <param name="extension">The extension.</param>
        /// <exception cref="ArgumentNullException"><paramref name="channel"/> is <c>null</c>.</exception>
        internal static bool TryGetDataChannelExtension(
            this UaSCUaBinaryChannel channel,
            out DataChannelExtension? extension)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            if (channel.TryGetMessageExtension(
                    TcpMessageType.Stream,
                    out ISecureChannelMessageExtension? registered) &&
                registered is DataChannelExtension dataChannels)
            {
                extension = dataChannels;
                return true;
            }

            extension = null;
            return false;
        }

        /// <summary>
        /// The largest secured body a data channel frame may occupy on a
        /// SecureChannel.
        /// </summary>
        /// <param name="channel">The SecureChannel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="channel"/> is <c>null</c>.</exception>
        public static int GetMaxDataChannelBodySize(this UaSCUaBinaryChannel channel)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            ISecureChannelMessageHost host = channel;

            return DataChannelFrameCodec.MaxPayload(
                DataChannelFramingMode.Inline,
                host.SendBufferSize,
                host.SymmetricSignatureSize + 2,
                withDeadline: true);
        }
    }
}
