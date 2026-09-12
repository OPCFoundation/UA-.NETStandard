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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Workspace;

/// <summary>
/// Owns document membership, selection, cancellation and asynchronous disposal.
/// Every accepted operation is tracked; closing removes a document immediately,
/// cancels its work, then awaits that work before disposing it exactly once.
/// </summary>
internal sealed class DocumentWorkspace<TDocument> : ObservableObject, IAsyncDisposable
    where TDocument : class, IWorkspaceDocument
{
    public DocumentWorkspace(
        ILogger log,
        IWorkspaceDispatcher? dispatcher = null,
        Func<TDocument, CancellationToken, Task>? synchronizeConnection = null)
    {
        m_log = log ?? throw new ArgumentNullException(nameof(log));
        m_dispatcher = dispatcher ?? InlineWorkspaceDispatcher.Instance;
        m_synchronizeConnection = synchronizeConnection
            ?? ((document, cancellationToken) => document.OnConnectionStateChangedAsync(cancellationToken));
        m_connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
        Documents = new DocumentCollection<TDocument>(m_documents);
    }

    public DocumentCollection<TDocument> Documents { get; }

    public TDocument? ActiveDocument
    {
        get
        {
            lock (m_sync)
            {
                return m_activeDocument;
            }
        }
    }

    public bool IsClosing
    {
        get
        {
            lock (m_sync)
            {
                return m_isClosing;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (m_sync)
            {
                return m_lastError;
            }
        }
    }

    /// <summary>
    /// Creates a document even when disconnected. The optional configuration runs
    /// before connection delivery. Failed or cancelled opens are removed and disposed.
    /// </summary>
    public async Task<TDocument> OpenAsync(
        Func<TDocument> create,
        Func<TDocument, CancellationToken, Task>? configureAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        TDocument? result = null;
        await m_dispatcher.InvokeAsync(
            async () =>
            {
                Entry entry = CreateEntry(create, cancellationToken);
                try
                {
                    lock (m_sync)
                    {
                        entry.IsVisible = true;
                        m_documents.Add(entry.Document);
                        SetActiveDocument(entry.Document);
                    }
                    await QueueWork(
                        entry,
                        async token =>
                        {
                            if (configureAsync is not null)
                            {
                                await configureAsync(entry.Document, token).ConfigureAwait(true);
                            }
                        },
                        cancellationToken).ConfigureAwait(true);
                    await InitializeConnectionAsync(entry, cancellationToken).ConfigureAwait(true);
                    result = entry.Document;
                }
                catch
                {
                    await CloseEntryAsync(entry).ConfigureAwait(true);
                    throw;
                }
            },
            cancellationToken).ConfigureAwait(false);
        return result!;
    }

    /// <summary>
    /// Applies explicit user configuration, such as adding another node to a bench,
    /// in the same cancellable queue as connection delivery and document initialization.
    /// </summary>
    public Task ConfigureAsync(
        TDocument document,
        Func<TDocument, CancellationToken, Task> configureAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configureAsync);
        return m_dispatcher.InvokeAsync(
            () =>
            {
                lock (m_sync)
                {
                    if (!m_entries.TryGetValue(document, out Entry? entry) || !entry.IsVisible || entry.IsClosing)
                    {
                        throw new ArgumentException("The document is not open.", nameof(document));
                    }
                    return QueueWork(entry, token => configureAsync(document, token), cancellationToken);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Changes selection synchronously on the owning context. Selecting the same
    /// document is a no-op; a document not owned by this workspace cannot be selected.
    /// </summary>
    public void Activate(TDocument? document)
    {
        m_dispatcher.VerifyAccess();
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_isClosing, this);
            if (document is not null
                && (!m_entries.TryGetValue(document, out Entry? entry) || entry.IsClosing || !entry.IsVisible))
            {
                throw new ArgumentException("The document is not open in this workspace.", nameof(document));
            }
            SetActiveDocument(document);
        }
    }

    /// <summary>
    /// Cancels and drains all accepted work before disposal. Cancellation can prevent
    /// accepting a close, but never abandons cleanup after the document has been removed.
    /// </summary>
    public Task CloseAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return m_dispatcher.InvokeAsync(
            () =>
            {
                lock (m_sync)
                {
                    if (m_entries.TryGetValue(document, out Entry? entry))
                    {
                        return CloseEntryAsync(entry);
                    }
                    return m_ownership.TryGetValue(document, out Ownership? ownership)
                        ? ownership.Closing ?? Task.CompletedTask
                        : Task.CompletedTask;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Prepares replacement documents without changing the current workspace.
    /// A cancelled or invalid restore leaves the old documents intact. A successful
    /// commit replaces order and selection together, then awaits all old disposals.
    /// The optional synchronous commit notification updates companion metadata before
    /// old-document cleanup; it must not initiate another workspace operation.
    /// </summary>
    public Task RestoreAsync(
        ArrayOf<DocumentRestore<TDocument>> documents,
        int selectedIndex = 0,
        Action? onCommitted = null,
        CancellationToken cancellationToken = default)
    {
        if (selectedIndex < 0 || selectedIndex > Math.Max(0, documents.Count - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        }
        return m_dispatcher.InvokeAsync(
            () => RestoreCoreAsync(documents, selectedIndex, onCommitted, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Delivers the latest live connection serially. A newer delivery cancels its
    /// predecessor; no late operation can overlap a document's detach or disposal.
    /// Callers can await this to observe completed document delivery.
    /// </summary>
    public Task SynchronizeConnectionAsync(CancellationToken cancellationToken = default)
        => QueueConnectionDeliveryAsync(m_synchronizeConnection, includeClosing: false, cancellationToken);

    /// <summary>
    /// Cancels pending binding work and releases session-dependent resources before
    /// the connection owner releases its session. Documents and local intent remain.
    /// Closing documents are also drained so their resource cleanup cannot escape this barrier.
    /// </summary>
    public Task ReleaseConnectionAsync(
        Func<TDocument, CancellationToken, Task> releaseAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseAsync);
        return QueueConnectionDeliveryAsync(releaseAsync, includeClosing: true, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task disposal;
        lock (m_sync)
        {
            if (m_disposal is null)
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = completion.Task;
            }
            disposal = m_disposal;
        }
        if (completion is not null)
        {
            try
            {
                try
                {
                    await m_dispatcher.InvokeAsync(DisposeCoreAsync).ConfigureAwait(false);
                }
                finally
                {
                    m_connectionCancellation.Dispose();
                    m_lifetime.Dispose();
                }
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
                await disposal.ConfigureAwait(false);
                throw;
            }
        }
        await disposal.ConfigureAwait(false);
    }

    private Task QueueConnectionDeliveryAsync(
        Func<TDocument, CancellationToken, Task> synchronize,
        bool includeClosing,
        CancellationToken cancellationToken)
    {
        return m_dispatcher.InvokeAsync(
            () =>
            {
                lock (m_sync)
                {
                    ObjectDisposedException.ThrowIf(m_isClosing, this);
                    CancellationTokenSource? previousCancellation = m_connectionCancellation;
                    bool releasePending = m_connectionDeliveryIsRelease && !m_connectionDelivery.IsCompleted;
                    Task cancellation = releasePending
                        ? Task.CompletedTask
                        : previousCancellation?.CancelAsync() ?? Task.CompletedTask;
                    m_connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        m_lifetime.Token, cancellationToken);
                    m_connectionDelivery = DeliverConnectionAsync(
                        m_connectionDelivery,
                        cancellation,
                        previousCancellation,
                        m_connectionCancellation,
                        ++m_connectionVersion,
                        synchronize,
                        includeClosing);
                    m_connectionDeliveryIsRelease = includeClosing;
                    return m_connectionDelivery;
                }
            },
            cancellationToken);
    }

    private Entry CreateEntry(Func<TDocument> create, CancellationToken cancellationToken)
    {
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_isClosing, this);
            cancellationToken.ThrowIfCancellationRequested();
            TDocument document = create();
            ArgumentNullException.ThrowIfNull(document);
            if (m_ownership.TryGetValue(document, out _))
            {
                throw new InvalidOperationException("A document factory must return a new, unowned document.");
            }

            var ownership = new Ownership();
            var entry = new Entry(document, ownership);
            m_ownership.Add(document, ownership);
            m_entries.Add(document, entry);
            return entry;
        }
    }

    private async Task InitializeConnectionAsync(Entry entry, CancellationToken cancellationToken)
    {
        CancellationToken stateToken;
        long version;
        lock (m_sync)
        {
            if (entry.IsClosing || m_isClosing)
            {
                throw new OperationCanceledException(new CancellationToken(canceled: true));
            }
            stateToken = m_connectionCancellation.Token;
            version = m_connectionVersion;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stateToken, cancellationToken);
        try
        {
            await QueueWork(
                entry,
                token => ApplyConnectionAsync(entry, version, m_synchronizeConnection, token),
                linked.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (stateToken.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested && !entry.IsClosing && !m_isClosing)
        {
            // A connection transition interrupts binding, not the user's saved
            // configuration. The newer delivery is queued behind the cancelled work.
        }
    }

    private static async Task ApplyConnectionAsync(
        Entry entry,
        long version,
        Func<TDocument, CancellationToken, Task> synchronize,
        CancellationToken cancellationToken)
    {
        if (entry.ConnectionVersion == version)
        {
            return;
        }
        await synchronize(entry.Document, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        entry.ConnectionVersion = version;
    }

    private Task QueueWork(Entry entry, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        lock (m_sync)
        {
            if (entry.IsClosing || m_isClosing)
            {
                return Task.FromCanceled(new CancellationToken(canceled: true));
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                entry.Lifetime.Token, m_lifetime.Token, cancellationToken);
            entry.Pending = RunWorkAsync(entry, entry.Pending, operation, cancellation);
            return entry.Pending;
        }
    }

    private async Task RunWorkAsync(
        Entry entry,
        Task previous,
        Func<CancellationToken, Task> operation,
        CancellationTokenSource cancellation)
    {
        // Publish the pending task before a callback can re-enter the workspace.
        await Task.Yield();
        using (cancellation)
        {
            // Earlier work reports errors to its caller and LastError. An earlier
            // failure must not prevent a subsequent detach or retry from running.
            await previous.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            CancellationToken token = cancellation.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                await operation(token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                ReportFailure(entry.Document.Title, error);
                throw;
            }
        }
    }

    private async Task RestoreCoreAsync(
        ArrayOf<DocumentRestore<TDocument>> documents,
        int selectedIndex,
        Action? onCommitted,
        CancellationToken cancellationToken)
    {
        var prepared = new List<Entry>(documents.Count);
        bool committed = false;
        try
        {
            for (int index = 0; index < documents.Count; index++)
            {
                DocumentRestore<TDocument> restore = documents[index];
                Entry entry = CreateEntry(restore.Create, cancellationToken);
                prepared.Add(entry);
                if (restore.ConfigureAsync is not null)
                {
                    await QueueWork(
                        entry,
                        token => restore.ConfigureAsync(entry.Document, token),
                        cancellationToken).ConfigureAwait(true);
                }
            }

            Task[] closing;
            lock (m_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(m_isClosing, this);
                SetActiveDocument(null);
                closing = m_entries.Values
                    .Where(entry => entry.IsVisible && !entry.IsClosing)
                    .ToArray()
                    .Select(CloseEntryAsync)
                    .ToArray();
                foreach (Entry entry in prepared)
                {
                    entry.IsVisible = true;
                    m_documents.Add(entry.Document);
                }
                SetActiveDocument(prepared.Count == 0 ? null : prepared[selectedIndex].Document);
                committed = true;
                onCommitted?.Invoke();
            }
            try
            {
                await Task.WhenAll(closing).ConfigureAwait(true);
            }
            finally
            {
                await SynchronizeConnectionAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            if (!committed)
            {
                await Task.WhenAll(prepared.Select(CloseEntryAsync)).ConfigureAwait(true);
            }
        }
    }

    private async Task DeliverConnectionAsync(
        Task previous,
        Task previousCancellation,
        CancellationTokenSource? previousSource,
        CancellationTokenSource cancellation,
        long version,
        Func<TDocument, CancellationToken, Task> synchronize,
        bool includeClosing)
    {
        await Task.Yield();
        try
        {
            try
            {
                await previousCancellation.ConfigureAwait(true);
            }
            finally
            {
                await previous.ConfigureAwait(
                    ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        finally
        {
            previousSource?.Dispose();
        }

        CancellationToken token = cancellation.Token;
        Entry[] entries;
        lock (m_sync)
        {
            token.ThrowIfCancellationRequested();
            entries = m_entries.Values
                .Where(entry => includeClosing || (entry.IsVisible && !entry.IsClosing))
                .ToArray();
        }

        var failures = new List<Exception>();
        foreach (Entry entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                try
                {
                    if (!entry.IsClosing)
                    {
                        await QueueWork(
                            entry,
                            workToken => ApplyConnectionAsync(entry, version, synchronize, workToken),
                            token).ConfigureAwait(true);
                    }
                }
                catch (OperationCanceledException) when (entry.Lifetime.IsCancellationRequested
                    && !token.IsCancellationRequested)
                {
                    // The close below owns cleanup after cancellation of this document's work.
                }
                finally
                {
                    if (includeClosing && entry.Closing is { } closing)
                    {
                        await closing.ConfigureAwait(true);
                    }
                }
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                failures.Add(error);
            }
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Document connection delivery failed.", failures);
        }
    }

    private Task CloseEntryAsync(Entry entry)
    {
        lock (m_sync)
        {
            if (entry.Closing is not null)
            {
                return entry.Closing;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            entry.Closing = completion.Task;
            entry.IsClosing = true;
            Task cancellation = entry.Lifetime.CancelAsync();
            int index = FindDocumentIndex(entry.Document);
            Exception? selectionFailure = null;
            Task cleanup;
            try
            {
                if (index >= 0 && ReferenceEquals(m_activeDocument, entry.Document))
                {
                    TDocument? next = m_documents.Count == 1
                        ? null
                        : m_documents[index + 1 < m_documents.Count ? index + 1 : index - 1];
                    SetActiveDocument(next);
                }
            }
            catch (Exception error)
            {
                selectionFailure = error;
            }
            finally
            {
                try
                {
                    index = FindDocumentIndex(entry.Document);
                    if (index >= 0)
                    {
                        m_documents.RemoveAt(index);
                    }
                }
                catch (Exception error)
                {
                    selectionFailure = selectionFailure is null
                        ? error
                        : new AggregateException(selectionFailure, error);
                }
                finally
                {
                    entry.IsVisible = false;
                    cleanup = CompleteCloseAsync(entry, cancellation, selectionFailure, completion);
                }
            }
            return cleanup;
        }
    }

    private async Task CompleteCloseAsync(
        Entry entry,
        Task cancellation,
        Exception? selectionFailure,
        TaskCompletionSource completion)
    {
        try
        {
            await DrainAndDisposeAsync(entry, cancellation, selectionFailure).ConfigureAwait(true);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
            await completion.Task.ConfigureAwait(true);
            throw;
        }
    }

    private async Task DrainAndDisposeAsync(Entry entry, Task cancellation, Exception? selectionFailure)
    {
        await Task.Yield();
        try
        {
            try
            {
                try
                {
                    await cancellation.ConfigureAwait(true);
                }
                finally
                {
                    await entry.Pending.ConfigureAwait(
                        ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
                }
            }
            finally
            {
                await entry.Document.DisposeAsync().ConfigureAwait(true);
            }
            if (selectionFailure is not null)
            {
                throw new AggregateException("Document presentation failed during close.", selectionFailure);
            }
        }
        catch (Exception error)
        {
            ReportFailure(entry.Document.Title, error);
            throw;
        }
        finally
        {
            lock (m_sync)
            {
                m_entries.Remove(entry.Document);
            }
            entry.Lifetime.Dispose();
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task cancellation;
        Task[] closing;
        Task delivery;
        lock (m_sync)
        {
            m_isClosing = true;
            OnPropertyChanged(nameof(IsClosing));
            cancellation = m_lifetime.CancelAsync();
            closing = m_entries.Values.ToArray().Select(CloseEntryAsync).ToArray();
            delivery = m_connectionDelivery;
        }
        try
        {
            try
            {
                await cancellation.ConfigureAwait(true);
            }
            finally
            {
                await delivery.ConfigureAwait(
                    ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            }
        }
        finally
        {
            await Task.WhenAll(closing).ConfigureAwait(true);
        }
    }

    private void SetActiveDocument(TDocument? document)
    {
        if (ReferenceEquals(m_activeDocument, document))
        {
            return;
        }
        TDocument? previous = m_activeDocument;
        m_activeDocument = document;
        try
        {
            previous?.OnDeactivated();
        }
        finally
        {
            OnPropertyChanged(nameof(ActiveDocument));
            if (ReferenceEquals(m_activeDocument, document))
            {
                document?.OnActivated();
            }
        }
    }

    private int FindDocumentIndex(TDocument document)
    {
        for (int i = 0; i < m_documents.Count; i++)
        {
            if (ReferenceEquals(m_documents[i], document))
            {
                return i;
            }
        }
        return -1;
    }

    private void ReportFailure(string title, Exception error)
    {
        lock (m_sync)
        {
            m_lastError = $"{title}: {error.Message}";
            OnPropertyChanged(nameof(LastError));
        }
        DocumentWorkspaceLog.WorkFailed(m_log, title, error);
    }

    private sealed class Ownership
    {
        public Task? Closing { get; set; }
    }

    private sealed class Entry
    {
        public Entry(TDocument document, Ownership ownership)
        {
            Document = document;
            m_ownership = ownership;
        }

        public TDocument Document { get; }
        public CancellationTokenSource Lifetime { get; } = new();
        public bool IsVisible { get; set; }
        public bool IsClosing { get; set; }
        public long ConnectionVersion { get; set; } = -1;
        public Task Pending { get; set; } = Task.CompletedTask;
        public Task? Closing
        {
            get => m_ownership.Closing;
            set => m_ownership.Closing = value;
        }

        private readonly Ownership m_ownership;
    }

    private readonly ILogger m_log;
    private readonly IWorkspaceDispatcher m_dispatcher;
    private readonly Func<TDocument, CancellationToken, Task> m_synchronizeConnection;
    private readonly ObservableCollection<TDocument> m_documents = new();
    private readonly Dictionary<TDocument, Entry> m_entries =
        new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<TDocument, Ownership> m_ownership = new();
    private readonly System.Threading.Lock m_sync = new();
    private readonly CancellationTokenSource m_lifetime = new();
    private TDocument? m_activeDocument;
    private string? m_lastError;
    private bool m_isClosing;
    private CancellationTokenSource m_connectionCancellation;
    private long m_connectionVersion;
    private Task m_connectionDelivery = Task.CompletedTask;
    private bool m_connectionDeliveryIsRelease;
    private Task? m_disposal;
}

/// <summary>
/// Read-only live document collection with public change notifications for existing
/// desktop bindings. Only the workspace can add or remove documents.
/// </summary>
internal sealed class DocumentCollection<TDocument> : ReadOnlyObservableCollection<TDocument>
{
    public DocumentCollection(ObservableCollection<TDocument> documents)
        : base(documents)
    {
    }

    public new event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => base.CollectionChanged += value;
        remove => base.CollectionChanged -= value;
    }
}

internal static partial class DocumentWorkspaceLog
{
    [LoggerMessage(
        EventId = UaLensEventIds.DocumentWorkFailed,
        Level = LogLevel.Error,
        Message = "Document {Title} operation failed.")]
    public static partial void WorkFailed(ILogger logger, string title, Exception exception);
}
