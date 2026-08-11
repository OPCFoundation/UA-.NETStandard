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

using System.Collections.Concurrent;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Inline data channel framing over the UASC binary channel, expressed as
    /// an extension that owns the STR MessageType.
    /// </summary>
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// The data channels multiplexed onto this SecureChannel, or null
        /// when the feature is not enabled.
        /// </summary>
        /// <remarks>
        /// Experimental. Until <see cref="EnableDataChannels"/> is called the
        /// STR dispatch is inert and an incoming frame closes the
        /// SecureChannel, which is what the interoperability rule of the
        /// Part 6 errata 5.16 requires of a peer that does not implement this
        /// specification.
        /// </remarks>
        public DataChannelManager? DataChannels => m_dataChannels?.Manager;

        /// <summary>
        /// Enables the data channel feature on this SecureChannel.
        /// </summary>
        /// <param name="isServer">True on the server side, which
        /// allocates ChannelIds.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="maxDataChannels">The most channels kept open at
        /// once on this SecureChannel.</param>
        /// <param name="maxCreditPerChannel">The largest window granted
        /// to one channel.</param>
        public DataChannelManager EnableDataChannels(
            bool isServer,
            ITelemetryContext telemetry,
            ushort maxDataChannels = 16,
            uint maxCreditPerChannel = 1024 * 1024)
        {
            DataChannelExtension? existing = m_dataChannels;

            if (existing != null)
            {
                return existing.Manager;
            }

            var extension = new DataChannelExtension(
                this,
                isServer,
                telemetry,
                maxDataChannels,
                maxCreditPerChannel);

            var registered = (DataChannelExtension)RegisterMessageExtension(extension);

            if (ReferenceEquals(registered, extension))
            {
                m_isDataChannelSource = isServer;
            }
            else
            {
                _ = extension.Manager.DisposeAsync().AsTask();
            }

            m_dataChannels = registered;
            return registered.Manager;
        }

        /// <summary>
        /// Tracks how much of the SecureChannel SequenceNumber space
        /// remains under the current SecurityToken. STR, MSG, OPN and CLO
        /// chunks all consume the same sender SequenceNumber space.
        /// </summary>
        public DataChannelSequenceBudget SequenceBudget
        {
            get
            {
                m_sequenceBudget.ObserveConsumed(SequenceNumbersIssuedUnderCurrentToken);
                return m_sequenceBudget;
            }
        }

        /// <summary>
        /// True when the SequenceNumber space remaining under the current
        /// token has fallen below the renewal threshold, so the owning
        /// channel should initiate OpenSecureChannel with
        /// RenewalRequest ahead of the normal lifetime based renewal.
        /// </summary>
        public bool IsSequenceRenewalDue => SequenceBudget.ShouldRenew;

        /// <summary>
        /// The largest secured body a data channel frame may occupy on
        /// this SecureChannel.
        /// </summary>
        /// <remarks>
        /// Public because a Server has to advertise this ceiling when it
        /// negotiates a channel, and the negotiation happens in the Server
        /// assembly rather than here.
        /// </remarks>
        public int MaxDataChannelBodySize
            => DataChannelFrameCodec.MaxPayload(
                DataChannelFramingMode.Inline,
                SendBufferSize,
                SymmetricSignatureSize + 2,
                withDeadline: true);

        private readonly DataChannelSequenceBudget m_sequenceBudget = new();
        private DataChannelExtension? m_dataChannels;
        private bool m_isDataChannelSource;
    }

    /// <summary>
    /// Maps server-side SecureChannel identifiers to their UASC channels so
    /// the DataChannel Service Set can bind an accepted OpenDataChannel to the
    /// transport that will carry its STR frames.
    /// </summary>
    public static class UaSCDataChannelSecureChannelRegistry
    {
        /// <summary>
        /// Finds the server-side UASC channel that owns a SecureChannel.
        /// </summary>
        /// <param name="secureChannelId">The SecureChannel identifier.</param>
        /// <param name="channel">The channel that owns it.</param>
        public static bool TryGet(
            string secureChannelId,
            out UaSCUaBinaryChannel? channel)
        {
            return s_channels.TryGetValue(secureChannelId, out channel);
        }

        internal static void Bind(string secureChannelId, UaSCUaBinaryChannel channel)
        {
            if (!string.IsNullOrEmpty(secureChannelId))
            {
                s_channels[secureChannelId] = channel;
            }
        }

        internal static void Unbind(string secureChannelId, UaSCUaBinaryChannel channel)
        {
            if (!string.IsNullOrEmpty(secureChannelId) &&
                s_channels.TryGetValue(secureChannelId, out UaSCUaBinaryChannel? current) &&
                ReferenceEquals(current, channel))
            {
                s_channels.TryRemove(secureChannelId, out _);
            }
        }

        private static readonly ConcurrentDictionary<string, UaSCUaBinaryChannel> s_channels = new();
    }
}
