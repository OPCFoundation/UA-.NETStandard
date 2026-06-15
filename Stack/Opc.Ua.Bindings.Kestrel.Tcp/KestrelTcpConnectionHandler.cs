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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Kestrel <see cref="ConnectionHandler"/> that pumps an accepted
    /// <see cref="ConnectionContext"/> through a UA-SC binary channel
    /// (<see cref="TcpServerChannel"/> in forward mode,
    /// <see cref="TcpReverseConnectChannel"/> when the listener was
    /// opened with <c>settings.ReverseConnectListener = true</c>). One
    /// handler instance per <see cref="KestrelTcpTransportListener"/>;
    /// one connection per invocation of <see cref="OnConnectedAsync"/>.
    /// </summary>
    internal sealed class KestrelTcpConnectionHandler : ConnectionHandler
    {
        public KestrelTcpConnectionHandler(KestrelTcpTransportListener owner)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            PipeByteTransport? transport = new PipeByteTransport(
                connection,
                m_owner.BufferManager,
                m_owner.Quotas.MaxBufferSize,
                m_owner.Telemetry);
            try
            {
                uint channelId = m_owner.NextChannelId();
                TcpListenerChannel channel = m_owner.CreateChannel();
                try
                {
                    m_owner.RegisterChannel(channelId, channel);
                    channel.Attach(channelId, transport);
                    // Ownership of the transport has been transferred to the
                    // channel; null it out so the finally block below does
                    // not dispose what the channel now owns.
                    transport = null;

                    // Hold the connection open until either side tears it down,
                    // OR (in reverse-connect mode) until TransferListenerChannelAsync
                    // hands the transport off to the application AND the new
                    // owner closes the underlying pipe (Kestrel disposes the
                    // ConnectionContext the instant this method returns).
                    await m_owner.WaitForConnectionAsync(channelId, connection.ConnectionClosed)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_owner.Logger.LogDebug(ex, "Kestrel TCP connection {Id} ended with exception.", channelId);
                }
                finally
                {
                    m_owner.UnregisterChannel(channelId);
                    // channel.Dispose() closes the transport if the channel
                    // still owns it. In reverse-connect mode after a successful
                    // handoff the transport has been detached and a NEW owner
                    // is responsible for it; channel.Dispose() is a no-op for
                    // that transport in that case.
                    channel.Dispose();
                }
            }
            finally
            {
                // Disposed only on the rare path where channel.Attach throws
                // before ownership transfer; in the happy path 'transport' is
                // already null and the channel owns it.
                transport?.Dispose();
            }
        }

        private readonly KestrelTcpTransportListener m_owner;
    }
}
