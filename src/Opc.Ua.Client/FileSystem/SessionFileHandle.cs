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

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Owns a private FileType handle opened through one captured session generation.
    /// </summary>
    /// <remarks>
    /// Use sequentially, like a Stream, and dispose before its request context.
    /// Disposal attempts Close only through the original context and never disposes that context or its owner.
    /// A failed transfer or Close does not imply rollback; temporary CloseAndCommit is a distinct operation.
    /// </remarks>
    public sealed class SessionFileHandle : IAsyncDisposable
    {
        /// <summary>
        /// Retains a handle just allocated through the supplied captured context.
        /// </summary>
        internal SessionFileHandle(
            SessionRequestContext context, FileTypeClient file, uint handle,
            TemporaryFileTransferTypeClient? transfer = null)
        {
            SessionId = context.SessionId;
            m_file = file;
            m_handle = handle;
            m_transfer = transfer;
        }

        /// <summary>
        /// Gets the file node on the original server.
        /// </summary>
        public NodeId FileId => m_file.ObjectId;

        /// <summary>
        /// Gets the original server-session identifier.
        /// </summary>
        public NodeId SessionId { get; }

        /// <summary>
        /// Gets whether the server accepted CloseAndCommit, including when its output was malformed.
        /// </summary>
        public bool IsCommitted { get; private set; }

        /// <summary>
        /// Streams bounded data to the original open handle without closing or committing it.
        /// </summary>
        public ValueTask<long> WriteFromAsync(
            Stream source, long maxBytes, int chunkSize = 4096, CancellationToken ct = default)
        {
            ThrowIfClosed();
            return m_file.WriteStreamAsync(m_handle, source, maxBytes, chunkSize, ct);
        }

        /// <summary>
        /// Streams bounded data from the original handle until an empty read, without closing it.
        /// </summary>
        public ValueTask<long> ReadToAsync(
            Stream destination, long maxBytes, int chunkSize = 4096, CancellationToken ct = default)
        {
            ThrowIfClosed();
            return m_file.ReadStreamAsync(m_handle, destination, maxBytes, chunkSize, ct);
        }

        /// <summary>
        /// Closes the original handle and returns the server's unmodified method result.
        /// A refused Close leaves the handle eligible for best-effort disposal.
        /// </summary>
        public async ValueTask<CallMethodResult> CloseAsync(CancellationToken ct = default)
        {
            ThrowIfClosed();
            CallMethodResult result = await m_file.CallWithResultAsync(
                MethodIds.FileType_Close, [Variant.From(m_handle)], ct).ConfigureAwait(false);
            if (StatusCode.IsGood(result.StatusCode))
            {
                m_closed = true;
            }
            return result;
        }

        /// <summary>
        /// Commits a temporary write handle and retains the completed status and completion-state-machine identity.
        /// There is no post-commit cancellation check or automatic retry.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<(NodeId CompletionStateMachine, CallMethodResult Result)> CommitAsync(
            CancellationToken ct = default)
        {
            ThrowIfClosed();
            if (m_transfer is null)
            {
                throw new InvalidOperationException("Only a temporary write file can be committed.");
            }
            CallMethodResult result = await m_transfer.CallWithResultAsync(
                MethodIds.TemporaryFileTransferType_CloseAndCommit, [Variant.From(m_handle)], ct)
                .ConfigureAwait(false);
            NodeId stateMachine = NodeId.Null;
            if (StatusCode.IsGood(result.StatusCode))
            {
                IsCommitted = true;
                m_closed = true;
                if (result.OutputArguments.Count != 1 ||
                    !result.OutputArguments[0].TryGetValue(out stateMachine))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "CloseAndCommit succeeded but did not return a completion state machine NodeId.");
                }
            }
            return (stateMachine, result);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (!m_closed)
            {
                m_closed = true;
                await m_file.CloseAfterFailureAsync(m_handle).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rejects attempts to reuse a terminated server handle locally.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfClosed()
        {
            if (m_closed)
            {
                throw new ObjectDisposedException(nameof(SessionFileHandle));
            }
        }

        private readonly FileTypeClient m_file;
        private readonly TemporaryFileTransferTypeClient? m_transfer;
        private readonly uint m_handle;
        private bool m_closed;
    }
}
