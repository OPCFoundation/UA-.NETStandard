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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebTransport.Tests;
using Opc.Ua.Server.DataChannels;

namespace Opc.Ua.DataChannels.ClientServer.Tests
{
    /// <summary>
    /// Pauses one already decoded Service before its normal StandardServer implementation runs.
    /// </summary>
    internal sealed class LaneResponsePause
    {
        /// <summary>
        /// Gets observation that the real UASC dispatcher reached the Service.
        /// </summary>
        internal Task Entered => m_entered.Task;

        /// <summary>
        /// Arms one Service pause.
        /// </summary>
        internal void Arm() => Interlocked.Exchange(ref m_armed, 1);

        /// <summary>
        /// Allows the normal Service implementation to produce its response.
        /// </summary>
        internal void Release() => m_release.TrySetResult(true);

        /// <summary>
        /// Waits only when the test explicitly armed this pause.
        /// </summary>
        internal async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref m_armed, 2, 1) == 1)
            {
                m_entered.TrySetResult(true);
                await m_release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private readonly TaskCompletionSource<bool> m_entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_armed;
    }

    /// <summary>
    /// Delays one positive policy recheck through the supported application-policy seam.
    /// Authentication and all native/UASC processing remain real.
    /// </summary>
    internal sealed class LaneRecheckPolicy : IDataChannelSourcePolicy
    {
        /// <summary>
        /// Gets observation of the policy pause.
        /// </summary>
        internal Task Entered => m_entered.Task;

        /// <summary>
        /// Arms the next positive recheck, without changing its authorization result.
        /// </summary>
        internal void Arm() => Interlocked.Exchange(ref m_armed, 1);

        /// <summary>
        /// Completes the positive authorization decision.
        /// </summary>
        internal void Release() => m_release.TrySetResult(true);

        /// <inheritdoc/>
        public async ValueTask<DataChannelResult> AuthorizeAsync(
            DataChannelSourceAccess access, CancellationToken cancellationToken = default)
        {
            DataChannelResult result = await m_inner.AuthorizeAsync(access, cancellationToken).ConfigureAwait(false);
            if (result == DataChannelResult.Good && access.Purpose == DataChannelAuthorizationPurpose.Recheck &&
                Interlocked.CompareExchange(ref m_armed, 2, 1) == 1)
            {
                m_entered.TrySetResult(true);
                await m_release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        private readonly DataChannelApplicationPolicy m_inner = new([WebTransportTestEnvironment.ClientUri]);
        private readonly TaskCompletionSource<bool> m_entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_armed;
    }

    /// <summary>
    /// Observes the existing byte-transport send boundary without replacing framing or pool ownership.
    /// </summary>
    internal sealed class LaneSendObservation
    {
        /// <summary>
        /// Gets whether the observed send completed before the held policy decision.
        /// </summary>
        internal Task<bool> Attempted => m_attempted.Task;

        /// <summary>
        /// Gets the actual result of the observed send.
        /// </summary>
        internal Task<StatusCode> Outcome => m_outcome.Task;

        /// <summary>
        /// Arms the next server response send.
        /// </summary>
        internal void Arm() => Interlocked.Exchange(ref m_armed, 1);

        /// <summary>
        /// Decorates only observation; the original transport still owns every byte and buffer.
        /// </summary>
        internal IUaSCByteTransport Decorate(IUaSCByteTransport transport) => new ObservedTransport(this, transport);

        /// <summary>
        /// Signals only after invoking the actual send, avoiding a signal-before-send race in the test.
        /// </summary>
        private async ValueTask ObserveAsync(ValueTask operation)
        {
            bool observing = Interlocked.Exchange(ref m_armed, 0) == 1;
            if (observing)
            {
                m_attempted.TrySetResult(operation.IsCompleted);
            }
            try
            {
                await operation.ConfigureAwait(false);
                if (observing)
                {
                    m_outcome.TrySetResult(StatusCodes.Good);
                }
            }
            catch (Exception exception)
            {
                if (observing)
                {
                    m_outcome.TrySetResult(new ServiceResult(exception).StatusCode);
                }
                throw;
            }
        }

        /// <summary>
        /// Forwards the complete original byte-transport contract.
        /// </summary>
        private sealed class ObservedTransport(LaneSendObservation owner, IUaSCByteTransport inner) : IUaSCByteTransport
        {
            /// <inheritdoc/>
            public EndPoint? LocalEndpoint => inner.LocalEndpoint;

            /// <inheritdoc/>
            public EndPoint? RemoteEndpoint => inner.RemoteEndpoint;

            /// <inheritdoc/>
            public TransportChannelFeatures Features => inner.Features;

            /// <inheritdoc/>
            public string Implementation => inner.Implementation;

            /// <inheritdoc/>
            public ValueTask ConnectAsync(Uri url, CancellationToken ct) => inner.ConnectAsync(url, ct);

            /// <inheritdoc/>
            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
                => owner.ObserveAsync(inner.SendChunkAsync(chunk, ct));

            /// <inheritdoc/>
            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
                => owner.ObserveAsync(inner.SendChunkAsync(buffers, ct));

            /// <inheritdoc/>
            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct) => inner.ReceiveChunkAsync(ct);

            /// <inheritdoc/>
            public void Close() => inner.Close();
        }

        private readonly TaskCompletionSource<bool> m_attempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<StatusCode> m_outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_armed;
    }
}
