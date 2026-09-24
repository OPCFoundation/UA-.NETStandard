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
                    m_cursor = m_host.Provider
                        .EnumerateAsync(m_source.ProviderPath, m_enumerationCancellation.Token)
                        .GetAsyncEnumerator(m_enumerationCancellation.Token);
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
                _ = DisposeCursorAsync();
            }
            base.Dispose(disposing);
        }

        private bool NeedsProviderEntries()
        {
            return !InternalOnly &&
                IsRequired(ReferenceTypeIds.HasComponent, false) &&
                (BrowseName.IsNull || BrowseName.NamespaceIndex == m_source.BrowseName.NamespaceIndex);
        }

        private async ValueTask CompleteEnumerationAsync()
        {
            m_stage = Stage.Done;
            IAsyncEnumerator<FileSystemEntry>? cursor = m_cursor;
            m_cursor = null;
            if (cursor != null)
            {
                await cursor.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task DisposeCursorAsync()
        {
            Exception? failure = null;
            try
            {
                await m_enumerationCancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                await m_cursorSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    await CompleteEnumerationAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_cursorSemaphore.Release();
                }
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }
            finally
            {
                m_enumerationCancellation.Dispose();
                m_cursorSemaphore.Dispose();
            }
            if (failure != null)
            {
                m_logger.DirectoryCursorCleanupFailed(failure);
                m_disposalCompleted.TrySetException(failure);
                _ = m_disposalCompleted.Task.Exception;
            }
            else
            {
                m_disposalCompleted.TrySetResult(true);
            }
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
        private bool m_disposed;
        private Stage m_stage;
    }

    internal static partial class DirectoryBrowserLog
    {
        [LoggerMessage(EventId = ServerEventIds.DirectoryBrowser, Level = LogLevel.Error,
            Message = "Asynchronous directory cursor cleanup failed.")]
        public static partial void DirectoryCursorCleanupFailed(this ILogger logger, Exception exception);
    }
}
