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
using Opc.Ua.DataChannels;

namespace Opc.Ua.PubSub.Transports.DataChannels
{
    /// <summary>
    /// Owns one flow and, optionally, a dedicated connector lifetime. Shared associations remain caller-owned.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelPubSubFlow : IAsyncDisposable
    {
        /// <summary>
        /// Transfers ownership of a ready flow and an optional dedicated owner.
        /// </summary>
        public DataChannelPubSubFlow(
            DataChannel channel,
            DataChannelCarrierKind carrierKind,
            IAsyncDisposable? owner = null)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _ = DataChannelPubSubConfiguration.GetTransportProfile(carrierKind);
            if (channel.CarrierKind != carrierKind)
            {
                throw new ArgumentException(
                    "The supplied carrier does not match the actual flow.", nameof(carrierKind));
            }
            CarrierKind = carrierKind;
            m_owner = owner;
        }

        /// <summary>
        /// Transfers ownership of configured inbound route cleanup to this flow.
        /// </summary>
        internal DataChannelPubSubFlow(
            DataChannel channel,
            DataChannelCarrierKind carrierKind,
            Action routeCleanup)
            : this(channel, carrierKind, (IAsyncDisposable?)null)
        {
            m_routeCleanup = routeCleanup ?? throw new ArgumentNullException(nameof(routeCleanup));
        }

        /// <summary>
        /// Gets the actual ready flow, not a replacement stream or a private channel registry.
        /// </summary>
        public DataChannel Channel { get; }

        /// <summary>
        /// Gets the carrier confirmed by the provider's authenticated context.
        /// </summary>
        public DataChannelCarrierKind CarrierKind { get; }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            try
            {
                await Channel.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    m_routeCleanup?.Invoke();
                }
                finally
                {
                    if (m_owner is not null)
                    {
                        await m_owner.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private readonly IAsyncDisposable? m_owner;
        private Action? m_routeCleanup;
        private int m_disposed;
    }
}
