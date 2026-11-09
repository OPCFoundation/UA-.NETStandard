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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Retains only verified generated files, under opaque caller- and generation-bound handles.
    /// </summary>
    internal sealed class VisionFileHandles : IAsyncDisposable
    {
        /// <summary>
        /// Reserves bounded capacity before generating a remote file.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The caller or host transfer quota is full.
        /// </exception>
        internal void Reserve(McpCallerIdentity caller)
        {
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                m_counts.TryGetValue(caller, out int count);
                if (m_pending >= 128 || count >= 16)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The caller or host Vision transfer quota is full.");
                }
                m_pending++;
                m_counts[caller] = count + 1;
            }
        }

        /// <summary>
        /// Releases one reservation after failure or removal, without retaining the captured caller scope.
        /// </summary>
        internal void Release(McpCallerIdentity caller)
        {
            lock (m_state)
            {
                if (m_counts.TryGetValue(caller, out int count))
                {
                    m_pending--;
                    if (count == 1)
                    {
                        m_counts.Remove(caller);
                    }
                    else
                    {
                        m_counts[caller] = count - 1;
                    }
                }
            }
        }

        /// <summary>
        /// Takes ownership of a verified file scope and publishes an opaque caller-bound reference.
        /// Failed publication disposes the transferred scope without leaking its server handle.
        /// </summary>
        internal async ValueTask<(uint Handle, Entry File)> AddAsync(
            OpcUaSessionManager manager, McpCallerIdentity caller, OpcUaSessionManager.SessionInfo registration,
            McpMutationSession scope, NodeId transfer, NodeId file, uint serverHandle, bool writable, NodeId completion)
        {
            Entry? entry = null;
            try
            {
                entry = new Entry(manager, caller, registration, scope, transfer, file, serverHandle,
                    writable, completion);
                lock (m_state)
                {
                    ObjectDisposedException.ThrowIf(m_disposed, this);
                    uint handle;
                    do
                    {
                        handle = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
                    }
                    while (handle == entry.ServerHandle || m_entries.ContainsKey(handle));
                    m_entries.Add(handle, entry);
                    return (handle, entry);
                }
            }
            catch
            {
                if (entry is not null)
                {
                    await entry.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        /// <summary>
        /// Resolves only the creating caller's exact still-current registration.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The handle does not belong to this caller and registration.
        /// </exception>
        internal Entry Get(uint handle, McpCallerIdentity caller, OpcUaSessionManager.SessionInfo registration)
        {
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (!m_entries.TryGetValue(handle, out Entry? entry) ||
                    entry.Owner != caller ||
                    !ReferenceEquals(entry.Registration, registration))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                        "The handle does not identify this caller's Vision transfer on this registration.");
                }
                return entry;
            }
        }

        /// <summary>
        /// Removes a completed or aborted transfer without making its handle reusable.
        /// </summary>
        internal void Remove(uint handle, Entry entry)
        {
            lock (m_state)
            {
                if (!m_entries.TryGetValue(handle, out Entry? current) || !ReferenceEquals(current, entry))
                {
                    return;
                }
                m_entries.Remove(handle);
                Release(entry.Owner);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Entry[] entries;
            lock (m_state)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                entries = [.. m_entries.Values];
                m_entries.Clear();
                m_counts.Clear();
                m_pending = 0;
            }
            List<Exception>? failures = null;
            foreach (Entry entry in entries)
            {
                try
                {
                    await entry.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
            if (failures is not null)
            {
                throw new AggregateException("One or more Vision transfer cleanups failed.", failures);
            }
        }

        /// <summary>
        /// One verified transfer and its generation-bound, non-owning service scope.
        /// </summary>
        internal sealed class Entry(
            OpcUaSessionManager manager,
            McpCallerIdentity owner,
            OpcUaSessionManager.SessionInfo registration,
            McpMutationSession scope,
            NodeId transfer,
            NodeId file,
            uint serverHandle,
            bool writable,
            NodeId completion) : IAsyncDisposable
        {
            /// <summary>
            /// Gets the exact creating caller.
            /// </summary>
            internal McpCallerIdentity Owner { get; } = owner;

            /// <summary>
            /// Gets the original immutable registration.
            /// </summary>
            internal OpcUaSessionManager.SessionInfo Registration { get; } = registration;

            /// <summary>
            /// Gets the retained generation-bound dispatch scope.
            /// </summary>
            internal McpMutationSession Scope { get; } = scope;

            /// <summary>
            /// Gets the verified transfer object.
            /// </summary>
            internal NodeId Transfer { get; } = transfer;

            /// <summary>
            /// Gets the verified file returned by that transfer.
            /// </summary>
            internal NodeId File { get; } = file;

            /// <summary>
            /// Gets the private UA handle, never returned to an MCP caller.
            /// </summary>
            internal uint ServerHandle { get; } = serverHandle;

            /// <summary>
            /// Gets whether GenerateWrite established this transfer.
            /// </summary>
            internal bool Writable { get; } = writable;

            /// <summary>
            /// Gets or sets the verified completion object returned by generation or commit.
            /// </summary>
            internal NodeId Completion { get; set; } = completion;

            /// <summary>
            /// Gets or sets whether the write has already been committed.
            /// </summary>
            internal bool Committed { get; set; }

            /// <summary>
            /// Gets whether the handle has been closed.
            /// </summary>
            internal bool Closed => Volatile.Read(ref m_closed) != 0;

            /// <summary>
            /// Runs an operation serially without exposing its synchronization primitive.
            /// </summary>
            /// <typeparam name="T">The result of the serialized file operation.</typeparam>
            /// <exception cref="ServiceResultException">
            /// The transfer is already closed.
            /// </exception>
            internal async ValueTask<T> UseAsync<T>(Func<ValueTask<T>> operation, CancellationToken ct)
            {
                Interlocked.Increment(ref m_users);
                bool acquired = false;
                try
                {
                    ThrowIfClosed();
                    await m_operations.WaitAsync(ct).ConfigureAwait(false);
                    acquired = true;
                    if (Closed)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The Vision transfer is already closed.");
                    }
                    Scope.VerifyBeforeDispatch(ct);
                    return await operation().ConfigureAwait(false);
                }
                finally
                {
                    ReleaseOperation(acquired);
                }
            }

            /// <summary>
            /// Aborts only this verified upload; an invalidated generation is never used for cleanup dispatch.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await CloseCoreAsync(cleanup: true, CancellationToken.None).ConfigureAwait(false);
            }

            /// <summary>
            /// Closes an explicit caller operation without hiding invalidation or a refused server Close.
            /// </summary>
            internal ValueTask CloseAsync(CancellationToken ct)
            {
                return CloseCoreAsync(cleanup: false, ct);
            }

            /// <summary>
            /// Releases this entry exactly once and permits stale-generation suppression only during disposal.
            /// </summary>
            private async ValueTask CloseCoreAsync(bool cleanup, CancellationToken ct)
            {
                Interlocked.Increment(ref m_users);
                bool acquired = false;
                try
                {
                    if (Closed)
                    {
                        return;
                    }
                    await m_operations.WaitAsync(ct).ConfigureAwait(false);
                    acquired = true;
                    if (Closed)
                    {
                        return;
                    }
                    Interlocked.Exchange(ref m_closed, 1);
                    Scope.VerifyBeforeDispatch(ct);
                    if (!Committed)
                    {
                        await manager.UaOperationGuard.AuthorizeUnprotectedFileAsync(Scope.Access, File, ct)
                            .ConfigureAwait(false);
                        Scope.VerifyBeforeDispatch(ct);
                        var fileClient = new FileTypeClient(
                            Scope.Access.Session, File, Scope.Access.Session.MessageContext.Telemetry);
                        await fileClient.CloseAsync(ServerHandle, ct).ConfigureAwait(false);
                    }
                }
                catch (ServiceResultException exception) when (cleanup &&
                    (exception.StatusCode == StatusCodes.BadSessionIdInvalid ||
                        exception.StatusCode == StatusCodes.BadSessionClosed ||
                        exception.StatusCode == StatusCodes.BadUserAccessDenied))
                {
                    // Only disposal can silently retire an already unusable service generation.
                }
                finally
                {
                    try
                    {
                        if (acquired)
                        {
                            await Scope.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        ReleaseOperation(acquired);
                    }
                }
            }

            /// <summary>
            /// Refuses late operations before they can wait on a retired operation queue.
            /// </summary>
            /// <exception cref="ServiceResultException">
            /// The transfer is already closed.
            /// </exception>
            private void ThrowIfClosed()
            {
                if (Closed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The Vision transfer is already closed.");
                }
            }

            /// <summary>
            /// Disposes the private operation queue only after admitted and waiting operations have drained.
            /// </summary>
            private void ReleaseOperation(bool acquired)
            {
                if (acquired)
                {
                    m_operations.Release();
                }
                if (Interlocked.Decrement(ref m_users) == 0 &&
                    Closed &&
                    Interlocked.Exchange(ref m_operationsDisposed, 1) == 0)
                {
                    m_operations.Dispose();
                }
            }

            /// <summary>Serializes operations on this server-side file stream.</summary>
            private readonly SemaphoreSlim m_operations = new(1, 1);

            /// <summary>Admitted and waiting operations retaining the private queue.</summary>
            private int m_users;

            /// <summary>Whether this file handle has been closed.</summary>
            private int m_closed;

            /// <summary>Whether the drained operation queue was disposed.</summary>
            private int m_operationsDisposed;
        }

        /// <summary>Protects only registry metadata, never UA dispatch.</summary>
        private readonly Lock m_state = new();

        /// <summary>Verified files keyed by opaque MCP handles.</summary>
        private readonly Dictionary<uint, Entry> m_entries = [];

        /// <summary>Reserved and published transfer counts per caller.</summary>
        private readonly Dictionary<McpCallerIdentity, int> m_counts = [];

        /// <summary>Total reserved and published transfers.</summary>
        private int m_pending;

        /// <summary>Whether this accessor's transfer registry is closed.</summary>
        private bool m_disposed;
    }
}
