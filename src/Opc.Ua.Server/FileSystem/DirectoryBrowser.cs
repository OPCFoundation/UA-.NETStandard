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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Enumerates the immediate children (files + directories) of a
    /// <see cref="DirectoryObjectState"/> via the underlying
    /// <see cref="IFileSystemProvider"/>.
    /// </summary>
    internal sealed class DirectoryBrowser : NodeBrowser, IAsyncDisposable
    {
        public DirectoryBrowser(
            ISystemContext context, ViewDescription? view,
            NodeId referenceType, bool includeSubtypes, BrowseDirection browseDirection,
            QualifiedName browseName, IEnumerable<IReference>? additionalReferences,
            bool internalOnly,
            IFileSystemHost host,
            DirectoryObjectState source)
            : base(context, view, referenceType, includeSubtypes, browseDirection,
                browseName, additionalReferences, internalOnly)
        {
            m_host = host;
            m_source = source;
            m_stage = Stage.Begin;
            m_logger = context.Telemetry.CreateLogger<DirectoryBrowser>();
        }

        /// <summary>
        /// Asynchronous iteration is the primary path: the server browse and
        /// translate-path loops drive it, so the provider enumeration is awaited
        /// rather than blocked on.
        /// </summary>
        /// <exception cref="AggregateException">Enumeration and cursor cleanup both fail.</exception>
        /// <exception cref="InvalidOperationException">
        /// The provider returns a directory cursor already held by another cursor owner.
        /// </exception>
        public override async ValueTask<IReference?> NextAsync(
            CancellationToken cancellationToken = default)
        {
            Task admission;
            lock (m_lifetimeLock)
            {
                if (m_disposed)
                {
                    return null;
                }
                admission = m_cursorSemaphore.WaitAsync(CancellationToken.None);
            }
            await admission.ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReference? reference = base.Next();
                if (reference != null)
                {
                    return reference;
                }
                if (m_stage == Stage.Done)
                {
                    return null;
                }

                if (!NeedsProviderEntries())
                {
                    m_stage = Stage.Done;
                    return null;
                }
                if (m_stage == Stage.Begin)
                {
                    var ownership = new DirectoryCursorOwnership();
                    m_host.RegisterDirectoryCursor(ownership);
                    m_cursorOwnership = ownership;
                    IAsyncEnumerator<FileSystemEntry> cursor = m_host.Provider
                        .EnumerateAsync(m_source.ProviderPath, m_enumerationCancellation.Token)
                        .GetAsyncEnumerator(m_enumerationCancellation.Token);
                    if (!ownership.TrySetCursor(cursor))
                    {
                        throw new InvalidOperationException("The provider returned a directory cursor already in use.");
                    }
                    m_cursor = cursor;
                    m_stage = Stage.Children;
                }

                using CancellationTokenRegistration registration = cancellationToken.Register(
                    static state => ((CancellationTokenSource)state!).Cancel(), m_enumerationCancellation);
                while (await m_cursor!.MoveNextAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    m_enumerationCancellation.Token.ThrowIfCancellationRequested();
                    FileSystemEntry entry = m_cursor.Current;
                    if (!BrowseName.IsNull && entry.Name != BrowseName.Name)
                    {
                        continue;
                    }
                    reference = CreateReference(entry);
                    if (!BrowseName.IsNull)
                    {
                        await CompleteEnumerationAsync().ConfigureAwait(false);
                    }
                    return reference;
                }
                cancellationToken.ThrowIfCancellationRequested();
                await CompleteEnumerationAsync().ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                try
                {
                    await CompleteEnumerationAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(exception, cleanupException);
                }
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            finally
            {
                m_cursorSemaphore.Release();
            }
        }

        /// <summary>
        /// Synchronous bridge for the consumers that still iterate with
        /// <see cref="NodeBrowser.Next"/> (the nodeset exporter, the legacy
        /// <c>CustomNodeManager2</c>). It blocks on the provider enumeration.
        /// </summary>
        public override IReference? Next()
        {
            return NextAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Dispose();
            await m_disposalCompleted.Task.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_lifetimeLock)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                }
                var start = new TaskCompletionSource<bool>();
                Task cleanup = DisposeCursorAsync(start.Task);
                try
                {
                    m_host.TrackCursorCleanup(cleanup);
                }
                finally
                {
                    // Allow synchronous cleanup only after its task has been registered.
                    start.TrySetResult(true);
                }
            }
            base.Dispose(disposing);
        }

        private bool NeedsProviderEntries()
        {
            return !InternalOnly &&
                IsRequired(ReferenceTypeIds.HasComponent, false) &&
                (BrowseName.IsNull || BrowseName.NamespaceIndex == m_source.BrowseName.NamespaceIndex);
        }

        private async ValueTask<Exception?> CompleteEnumerationAsync(bool captureCursorFailure = false)
        {
            m_stage = Stage.Done;
            IAsyncEnumerator<FileSystemEntry>? cursor = m_cursor;
            DirectoryCursorOwnership? ownership = m_cursorOwnership;
            m_cursor = null;
            m_cursorOwnership = null;
            Exception? failure = null;
            try
            {
                if (cursor != null)
                {
                    await cursor.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                if (!captureCursorFailure || ownership is null)
                {
                    throw;
                }
            }
            finally
            {
                if (ownership is not null)
                {
                    ownership.Complete(failure);
                    m_host.CompleteDirectoryCursor(ownership);
                }
            }
            return failure;
        }

        private async Task DisposeCursorAsync(Task start)
        {
            await start.ConfigureAwait(false);
            Exception? failure = null;
            Exception? ancillaryFailure = null;
            try
            {
                await m_enumerationCancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
                ancillaryFailure = exception;
            }
            try
            {
                await m_cursorSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    // Report the cursor error to the disposer even if recovery has already
                    // confirmed the retained failure. Only ancillary errors fault the tracked task.
                    Exception? cursorFailure = await CompleteEnumerationAsync(captureCursorFailure: true)
                        .ConfigureAwait(false);
                    if (cursorFailure is not null)
                    {
                        failure = CombineFailures(failure, cursorFailure);
                    }
                }
                finally
                {
                    m_cursorSemaphore.Release();
                }
            }
            catch (Exception exception)
            {
                failure = CombineFailures(failure, exception);
                ancillaryFailure = CombineFailures(ancillaryFailure, exception);
            }
            finally
            {
                try
                {
                    m_enumerationCancellation.Dispose();
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception);
                    ancillaryFailure = CombineFailures(ancillaryFailure, exception);
                }
                try
                {
                    m_cursorSemaphore.Dispose();
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception);
                    ancillaryFailure = CombineFailures(ancillaryFailure, exception);
                }
            }
            if (failure != null)
            {
                try
                {
                    m_logger.DirectoryCursorCleanupFailed(failure);
                }
                catch (Exception exception)
                {
                    failure = CombineFailures(failure, exception);
                    ancillaryFailure = CombineFailures(ancillaryFailure, exception);
                }
                m_disposalCompleted.TrySetException(failure);
                _ = m_disposalCompleted.Task.Exception;
            }
            else
            {
                m_disposalCompleted.TrySetResult(true);
            }
            if (ancillaryFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ancillaryFailure).Throw();
            }
        }

        /// <summary>
        /// Preserves both cleanup failures when more than one operation fails.
        /// </summary>
        private static Exception CombineFailures(Exception? earlier, Exception later)
        {
            return earlier is null ? later : new AggregateException(earlier, later);
        }

        private NodeStateReference CreateReference(FileSystemEntry entry)
        {
            NodeId targetId = entry.IsDirectory
                ? m_host.BuildDirectoryNodeId(entry.Path)
                : m_host.BuildFileNodeId(entry.Path);
            return new NodeStateReference(ReferenceTypeIds.HasComponent, false, targetId);
        }

        private enum Stage
        {
            Begin,
            Children,
            Done
        }

        private readonly IFileSystemHost m_host;
        private readonly DirectoryObjectState m_source;
        private readonly ILogger m_logger;
        private readonly Lock m_lifetimeLock = new();
        private readonly SemaphoreSlim m_cursorSemaphore = new(1, 1);
        private readonly CancellationTokenSource m_enumerationCancellation = new();

        private readonly TaskCompletionSource<bool> m_disposalCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private IAsyncEnumerator<FileSystemEntry>? m_cursor;
        private DirectoryCursorOwnership? m_cursorOwnership;
        private bool m_disposed;
        private Stage m_stage;
    }

    /// <summary>
    /// Retains one provider cursor until cleanup succeeds or its failure is confirmed.
    /// </summary>
    internal sealed class DirectoryCursorOwnership
    {
        /// <summary>
        /// Gets the opaque identifier for a failure retained by this owner.
        /// </summary>
        internal Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// Gets the completion of the cursor's one cleanup attempt.
        /// </summary>
        internal Task Completion => m_completion.Task;

        /// <summary>
        /// Gets whether the cursor is finished without a retained failure.
        /// </summary>
        internal bool IsReleased
        {
            get
            {
                lock (m_lock)
                {
                    return m_finished && m_failure is null;
                }
            }
        }

        /// <summary>
        /// Claims the exact enumerator before it can be used.
        /// </summary>
        /// <param name="cursor">The provider-owned enumerator.</param>
        /// <returns>Whether another cursor owner does not already hold the same enumerator.</returns>
        /// <exception cref="InvalidOperationException">
        /// This owner has already completed or has already been assigned a cursor.
        /// </exception>
        internal bool TrySetCursor(IAsyncDisposable cursor)
        {
            lock (s_identityLock)
            {
                if (s_cursorOwners.TryGetValue(cursor, out _))
                {
                    return false;
                }
                lock (m_lock)
                {
                    if (m_finished || m_cursor is not null)
                    {
                        throw new InvalidOperationException("The directory cursor is already assigned.");
                    }
                    s_cursorOwners.Add(cursor, this);
                    m_cursor = cursor;
                    return true;
                }
            }
        }

        /// <summary>
        /// Records the outcome without retrying a failed cursor disposal.
        /// </summary>
        /// <param name="failure">The original cleanup failure, or null on success.</param>
        /// <exception cref="InvalidOperationException">This cursor owner has already completed.</exception>
        internal void Complete(Exception? failure)
        {
            lock (s_identityLock)
            {
                lock (m_lock)
                {
                    if (m_finished)
                    {
                        throw new InvalidOperationException("The directory cursor is already complete.");
                    }
                    m_failure = failure;
                    if (failure is null && m_cursor is { } cursor)
                    {
                        ReleaseIdentity(cursor);
                        m_cursor = null;
                    }
                    m_finished = true;
                }
            }
            m_completion.TrySetResult(true);
        }

        /// <summary>
        /// Releases the failed cursor after the host has verified and confirmed its cleanup.
        /// </summary>
        /// <returns>Whether this owner retained a failure to confirm.</returns>
        internal bool TryConfirm()
        {
            lock (s_identityLock)
            {
                lock (m_lock)
                {
                    if (!m_finished || m_cursor is not { } cursor || m_failure is null)
                    {
                        return false;
                    }
                    ReleaseIdentity(cursor);
                    m_cursor = null;
                    m_failure = null;
                    return true;
                }
            }
        }

        /// <summary>
        /// Retrieves the exact cursor and original error only after cleanup failed.
        /// </summary>
        /// <param name="cursor">The retained cursor when a failure exists.</param>
        /// <param name="failure">The original cleanup error when a failure exists.</param>
        /// <returns>Whether this owner retains a failed cursor.</returns>
        internal bool TryGetFailure(out IAsyncDisposable? cursor, out Exception? failure)
        {
            lock (m_lock)
            {
                cursor = m_cursor;
                failure = m_failure;
                return m_finished && cursor is not null && failure is not null;
            }
        }

        /// <summary>
        /// Borrows the exact failed cursor without transferring its cleanup ownership.
        /// </summary>
        /// <returns>The resource reference for trusted verification.</returns>
        /// <exception cref="InvalidOperationException">The cursor has no retained cleanup failure.</exception>
        internal NodeManagerCleanupResource GetCleanupResource()
        {
            lock (m_lock)
            {
                if (!m_finished || m_cursor is null || m_failure is null)
                {
                    throw new InvalidOperationException("The directory cursor has no retained cleanup failure.");
                }
                return NodeManagerCleanupResource.FromAsyncDisposable(m_cursor);
            }
        }

        private void ReleaseIdentity(IAsyncDisposable cursor)
        {
            if (!s_cursorOwners.TryGetValue(cursor, out DirectoryCursorOwnership? owner) ||
                !ReferenceEquals(owner, this) ||
                !s_cursorOwners.Remove(cursor))
            {
                throw new InvalidOperationException("The directory cursor ownership has changed.");
            }
        }

        private static readonly Lock s_identityLock = new();
        private static readonly ConditionalWeakTable<IAsyncDisposable, DirectoryCursorOwnership> s_cursorOwners = new();
        private readonly Lock m_lock = new();

        private readonly TaskCompletionSource<bool> m_completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private IAsyncDisposable? m_cursor;
        private Exception? m_failure;
        private bool m_finished;
    }

    internal static partial class DirectoryBrowserLog
    {
        [LoggerMessage(EventId = ServerEventIds.DirectoryBrowser, Level = LogLevel.Error,
            Message = "Asynchronous directory cursor cleanup failed.")]
        public static partial void DirectoryCursorCleanupFailed(this ILogger logger, Exception exception);
    }
}
