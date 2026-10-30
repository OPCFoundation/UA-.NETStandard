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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    public sealed partial class WebTransportDataChannelConnection
    {
        /// <summary>
        /// Allocates an actual WT stream and commits its single-use association-token preface.
        /// </summary>
        internal async ValueTask<Stream> OpenDirectStreamAsync(
            Guid token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ReserveDirectTokenLocked(token);
            }
            using var deadline = new CancellationTokenSource(m_options.EstablishmentTimeout, m_clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, deadline.Token, m_lifetime);
            ulong id = await OpenWebTransportStreamAsync(false, linked.Token).ConfigureAwait(false);
            StreamState state;
            WebTransportByteStream stream;
            lock (m_gate)
            {
                state = GetActualStreamLocked(id);
                stream = new WebTransportByteStream(this, state, m_lifetime, direct: true);
                state.ByteStream = stream;
            }
            try
            {
                await SendBaselineAsync(state, new ByteString(token.ToByteArray()), linked.Token)
                    .ConfigureAwait(false);
                lock (m_gate)
                {
                    CompleteDirectBindingLocked(state);
                }
                return stream;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Reserves a single-use receive token before admitting any matching actual stream object.
        /// </summary>
        internal async ValueTask<Stream> AcceptDirectStreamAsync(
            Guid token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<Stream> admission;
            lock (m_gate)
            {
                if (m_directStreamAdmissions.Count >= m_options.MaxPendingStreams)
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                ReserveDirectTokenLocked(token);
                admission = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_directStreamAdmissions.Add(token, admission);
            }
            using var deadline = new CancellationTokenSource(m_options.EstablishmentTimeout, m_clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, deadline.Token, m_lifetime);
            try
            {
                return await admission.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Task<Stream>? abandoned = null;
                lock (m_gate)
                {
                    m_directStreamAdmissions.Remove(token);
                    if (admission.Task.IsCompletedSuccessfully)
                    {
                        abandoned = admission.Task;
                    }
                }
                if (abandoned != null)
                {
                    Stream stream = await abandoned.ConfigureAwait(false);
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                if (exception is OperationCanceledException &&
                    deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested &&
                    !m_lifetime.IsCancellationRequested)
                {
                    throw new DataChannelException(DataChannelResult.Timeout);
                }
                throw;
            }
        }

        /// <summary>
        /// Retains nonreuse history for the lifetime of the actual CONNECT session.
        /// </summary>
        private void ReserveDirectTokenLocked(Guid token)
        {
            if (!m_uascBaseline || m_uascManagement)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            if (m_closing)
            {
                throw new DataChannelException(DataChannelResult.Closed);
            }
            if (token == Guid.Empty)
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
            if (m_directStreamTokens.Contains(token))
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            if (m_directStreamTokens.Count >= m_options.MaxStreamHistory)
            {
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            m_directStreamTokens.Add(token);
        }

        /// <summary>
        /// Releases exactly one provisional-stream slot when the actual object is bound.
        /// </summary>
        private void CompleteDirectBindingLocked(StreamState state)
        {
            state.DirectTokenPrefix = null;
            state.DirectTokenPrefixLength = 0;
            if (state.PendingBinding)
            {
                state.PendingBinding = false;
                m_pendingStreams--;
            }
            state.Bound.TrySetResult();
            PulseLocked();
        }

        /// <summary>
        /// Consumes a bounded token preface before exposing payload bytes on its actual stream object.
        /// </summary>
        private async ValueTask DeliverDirectStreamAsync(
            StreamState state,
            Task previous,
            Http3Event metadata,
            ByteString payload)
        {
            await previous.ConfigureAwait(false);
            WebTransportByteStream? stream;
            int prefixBytes = 0;
            bool refuse;
            lock (m_gate)
            {
                stream = state.ByteStream;
                refuse = state.Closing || state.Retired || state.Stream.IsLocal;
                if (!refuse && stream == null && state.DirectTokenPrefix is { } prefix)
                {
                    prefixBytes = Math.Min(16 - state.DirectTokenPrefixLength, payload.Length);
                    payload.Span[..prefixBytes].CopyTo(prefix.AsSpan(state.DirectTokenPrefixLength));
                    state.DirectTokenPrefixLength += prefixBytes;
                    if (state.DirectTokenPrefixLength == 16)
                    {
                        Guid token = new(prefix);
                        if (m_directStreamAdmissions.Remove(token, out TaskCompletionSource<Stream>? admission))
                        {
                            stream = new WebTransportByteStream(this, state, m_lifetime, direct: true);
                            state.ByteStream = stream;
                            CompleteDirectBindingLocked(state);
                            admission.TrySetResult(stream);
                        }
                        else
                        {
                            refuse = true;
                        }
                    }
                }
                if (stream == null && (state.DirectTokenPrefix == null || metadata.Kind == 16))
                {
                    refuse = true;
                }
            }
            int consumed = refuse ? payload.Length : prefixBytes;
            if (consumed != 0)
            {
                await RetryCommandAsync(18, 0, (ulong)consumed, default, m_lifetime).ConfigureAwait(false);
            }
            if (refuse)
            {
                await CloseFlowAsync(state.Stream.StreamId, DataChannelResult.InvalidState).ConfigureAwait(false);
                return;
            }
            if (stream != null)
            {
                await stream.DeliverAsync(payload.Slice(prefixBytes), metadata.Kind == 16, m_lifetime)
                    .ConfigureAwait(false);
            }
        }
    }
}
