/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Projects the runtime data channel state onto the AddressSpace
    /// types the Part 3 errata defines.
    /// </summary>
    /// <remarks>
    /// The <c>DataChannelCapabilities</c> Object is how a client learns
    /// that a server supports data channels at all: its <b>absence</b> is
    /// the server saying it does not. A server that hosts the engine but
    /// never publishes the Object is therefore indistinguishable from one
    /// that cannot stream, which is why this projection exists rather
    /// than being left to each server to invent.
    /// </remarks>
    public static class DataChannelModel
    {
        /// <summary>
        /// Builds the values of the <c>DataChannelCapabilities</c> Object
        /// from the server's limits and its live managers.
        /// </summary>
        /// <param name="capabilities">The server wide limits.</param>
        /// <param name="activeChannelCount">The number of channels open
        /// across the whole server.</param>
        public static DataChannelCapabilitiesValues BuildCapabilities(
            DataChannelServerCapabilities capabilities,
            int activeChannelCount)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            return new DataChannelCapabilitiesValues
            {
                MaxDataChannels = capabilities.MaxDataChannels,
                MaxFrameSize = capabilities.MaxFrameSize,
                SupportedDeliveryModes = [.. capabilities.SupportedDeliveryModes],
                SupportedTransportProfileUris = [.. capabilities.SupportedTransportProfileUris],
                MaxCreditPerChannel = capabilities.MaxCreditPerChannel,
                MaxTotalBitrate = capabilities.MaxTotalBitrate,
                SupportsUnreliableDatagrams = capabilities.SupportsUnreliableDatagrams,
                AllowInsecureDataChannels = capabilities.AllowInsecureDataChannels,
                ActiveChannelCount = activeChannelCount > ushort.MaxValue
                    ? ushort.MaxValue
                    : (ushort)activeChannelCount
            };
        }

        /// <summary>
        /// The status of every channel a source currently carries, in the
        /// shape the <c>Channels</c> Property publishes.
        /// </summary>
        /// <param name="manager">The manager owning the channels.</param>
        /// <param name="sourceNodeId">The endpoint to filter by.</param>
        public static List<DataChannelStatusDataType> BuildChannelStatus(
            DataChannelManager manager,
            NodeId sourceNodeId)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            var status = new List<DataChannelStatusDataType>();

            foreach (DataChannel channel in manager.Channels)
            {
                if (channel.SourceNodeId == sourceNodeId)
                {
                    status.Add(channel.GetStatus());
                }
            }

            return status;
        }

        /// <summary>
        /// The counters of every channel a source currently carries, in
        /// the shape the <c>Diagnostics</c> Property publishes.
        /// </summary>
        /// <param name="manager">The manager owning the channels.</param>
        /// <param name="sourceNodeId">The endpoint to filter by.</param>
        public static List<DataChannelDiagnosticsDataType> BuildDiagnostics(
            DataChannelManager manager,
            NodeId sourceNodeId)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            var diagnostics = new List<DataChannelDiagnosticsDataType>();

            foreach (DataChannel channel in manager.Channels)
            {
                if (channel.SourceNodeId == sourceNodeId)
                {
                    diagnostics.Add(channel.GetDiagnostics());
                }
            }

            return diagnostics;
        }

        /// <summary>
        /// The values a <c>DataChannelStateChangeEventType</c> Event
        /// carries.
        /// </summary>
        /// <param name="arguments">The transition.</param>
        public static DataChannelStateChangeValues BuildStateChangeEvent(
            DataChannelStateChangedEventArgs arguments)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            return new DataChannelStateChangeValues
            {
                ChannelId = arguments.ChannelId,
                State = arguments.State,
                Status = arguments.Status
            };
        }
    }

    /// <summary>
    /// The values of the <c>DataChannelCapabilities</c> Object.
    /// </summary>
    public sealed record DataChannelCapabilitiesValues
    {
        /// <summary>
        /// The most channels the server keeps open on one SecureChannel.
        /// </summary>
        public ushort MaxDataChannels { get; init; }

        /// <summary>
        /// The largest frame payload the server will emit or accept.
        /// </summary>
        public uint MaxFrameSize { get; init; }

        /// <summary>
        /// The delivery modes the server implements.
        /// </summary>
        public IReadOnlyList<DataChannelDeliveryMode> SupportedDeliveryModes { get; init; } = [];

        /// <summary>
        /// The transport profiles over which the server carries data
        /// channels.
        /// </summary>
        public IReadOnlyList<string> SupportedTransportProfileUris { get; init; } = [];

        /// <summary>
        /// The largest window granted to one channel.
        /// </summary>
        public uint MaxCreditPerChannel { get; init; }

        /// <summary>
        /// The aggregate rate across all channels of one SecureChannel.
        /// </summary>
        public uint MaxTotalBitrate { get; init; }

        /// <summary>
        /// True only where the server can genuinely lose a frame in
        /// flight. False over opc.tcp and opc.wss, and false over
        /// opc.quic on a runtime with no datagram API, where Unreliable
        /// would otherwise degrade silently to sender side discard.
        /// </summary>
        public bool SupportsUnreliableDatagrams { get; init; }

        /// <summary>
        /// True only where a data channel may be opened on a
        /// SecureChannel whose SecurityMode is None. Absence is read as
        /// false.
        /// </summary>
        public bool AllowInsecureDataChannels { get; init; }

        /// <summary>
        /// The channels open across the whole server.
        /// </summary>
        public ushort ActiveChannelCount { get; init; }
    }

    /// <summary>
    /// The values a <c>DataChannelStateChangeEventType</c> Event carries.
    /// </summary>
    public sealed record DataChannelStateChangeValues
    {
        /// <summary>
        /// The channel whose state changed.
        /// </summary>
        public uint ChannelId { get; init; }

        /// <summary>
        /// The state entered.
        /// </summary>
        public DataChannelState State { get; init; }

        /// <summary>
        /// The StatusCode that caused a transition into Closed or
        /// Faulted.
        /// </summary>
        public StatusCode Status { get; init; }
    }
}
