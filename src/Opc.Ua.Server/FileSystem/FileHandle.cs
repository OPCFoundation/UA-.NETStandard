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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Per-file handle bag. Tracks the streams opened against a single
    /// file via repeated <c>FileType.Open</c> calls (one writer +
    /// many readers per the spec) and dispenses the integer
    /// file-handle values returned to OPC UA clients.
    /// </summary>
    internal sealed class FileHandle : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Creates a handle bag with an optional admission check for the hosting session lifecycle.
        /// </summary>
        public FileHandle(
            IFileSystemProvider provider,
            string providerPath,
            Func<NodeId, bool>? isSessionTerminated = null,
            Action? openCountChanged = null)
        {
            m_provider = provider;
            ProviderPath = providerPath;
            m_isSessionTerminated = isSessionTerminated;
            m_openCountChanged = openCountChanged;
        }

        public string ProviderPath { get; }

        /// <summary>
        /// Gets the number of completed file opens, excluding reservations still waiting for provider streams.
        /// </summary>
        public ushort OpenCount
        {
            get
            {
                lock (m_lock)
                {
                    int count = m_write is { Stream: not null, Closing: false } ? 1 : 0;
                    foreach (OpenFile file in m_reads.Values)
                    {
                        if (file.Stream != null && !file.Closing)
                        {
                            count++;
                        }
                    }
                    return (ushort)count;
                }
            }
        }

        /// <summary>
        /// Gets whether both the provider and the current file entry permit writing.
        /// </summary>
        public bool IsWriteable
        {
            get
            {
                lock (m_lock)
                {
                    if (!m_provider.IsWritable)
                    {
                        return false;
                    }
                }
                // Outside the lock: ask the provider for the latest
                // writable bit without serialising readers.
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.IsWritable ?? false;
            }
        }

        public long Length
        {
            get
            {
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.Length ?? 0L;
            }
        }

        public DateTime LastModifiedTime
        {
            get
            {
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.LastModifiedUtc ?? DateTime.MinValue;
            }
        }

        public string MimeType
        {
            get
            {
                FileSystemEntry? entry = m_provider.GetEntryAsync(
                    ProviderPath, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return entry?.MimeType ?? string.Empty;
            }
        }

        /// <summary>
        /// Gets or sets the maximum lifecycle wait. Expiry never releases an unfinished reservation.
        /// </summary>
        public TimeSpan CloseTimeout
        {
            get => m_closeTimeout;
            set => m_closeTimeout = ValidateCloseTimeout(value);
        }

        /// <summary>
        /// Returns all cleanup outcomes, including work that outlived an earlier deadline.
        /// </summary>
        internal Task CleanupCompletion
        {
            get
            {
                lock (m_lock)
                {
                    return Task.WhenAll(m_cleanup);
                }
            }
        }

        /// <summary>
        /// Returns a session-owned stream only when its open mode includes all requested access bits.
        /// </summary>
        public Stream? GetStream(NodeId sessionId, uint fileHandle, byte requiredMode = 0)
        {
            lock (m_lock)
            {
                if (m_write != null && !m_write.Closing &&
                    fileHandle == m_write.Handle &&
                    m_write.SessionId.Equals(sessionId) &&
                    (m_write.Mode & requiredMode) == requiredMode)
                {
                    return m_write.Stream;
                }
                if (m_reads.TryGetValue(fileHandle, out OpenFile? openFile) &&
                    !openFile.Closing && openFile.SessionId.Equals(sessionId) &&
                    (openFile.Mode & requiredMode) == requiredMode)
                {
                    return openFile.Stream;
                }
                return null;
            }
        }

        /// <summary>
        /// Implements <c>FileType.Open</c>. The mode bits are per
        /// Part 5 §C: 0x1 = Read, 0x2 = Write, 0x4 = EraseExisting,
        /// 0x8 = Append.
        /// </summary>
        public ServiceResult Open(NodeId sessionId, byte mode, out uint fileHandle)
        {
            fileHandle = 0u;
            if (!TryReserveOpen(sessionId, mode, out OpenFile? pending, out ServiceResult error))
            {
                return error;
            }
            Stream? stream = null;
            bool accepted = false;
            try
            {
                stream = OpenProviderAsync(mode, pending!.Lifetime.Token).AsTask().GetAwaiter().GetResult();
                error = CompleteOpen(pending!, stream);
                accepted = ServiceResult.IsGood(error);
                if (accepted)
                {
                    fileHandle = pending!.Handle;
                }
                return error;
            }
            catch (FileNotFoundException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadNotFound, "File not found");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied, "Failed to open file");
            }
            catch (IOException ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadInvalidState, "Failed to open file");
            }
            finally
            {
                if (!accepted)
                {
                    try
                    {
                        DisposeRejectedStream(pending!, stream);
                    }
                    finally
                    {
                        lock (m_lock)
                        {
                            pending!.Closing = true;
                        }
                        pending.Opened.TrySetResult(true);
                        TrackCleanup(pending);
                    }
                }
                pending!.Opened.TrySetResult(true);
            }
        }

        /// <summary>
        /// Reserves compatible access, opens the provider stream and publishes the handle if the reservation survives.
        /// </summary>
        public async ValueTask<(ServiceResult Result, uint Handle)> OpenAsync(
            NodeId sessionId,
            byte mode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReserveOpen(sessionId, mode, out OpenFile? pending, out ServiceResult error,
                linkedOpenCancellation: true))
            {
                return (error, 0);
            }
            Stream? stream = null;
            bool accepted = false;
            var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, pending!.Lifetime.Token);
            try
            {
                stream = await OpenProviderAsync(mode, linked.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                error = CompleteOpen(pending!, stream);
                accepted = ServiceResult.IsGood(error);
                return (error, accepted ? pending!.Handle : 0);
            }
            catch (FileNotFoundException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadNotFound, "File not found"), 0);
            }
            catch (UnauthorizedAccessException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied, "Failed to open file"), 0);
            }
            catch (IOException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadInvalidState, "Failed to open file"), 0);
            }
            catch (OperationCanceledException) when (
                pending.Lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return (StatusCodes.BadSessionClosed, 0);
            }
            finally
            {
                try
                {
                    if (!accepted)
                    {
                        try
                        {
                            await DisposeRejectedStreamAsync(pending, stream).ConfigureAwait(false);
                        }
                        finally
                        {
                            lock (m_lock)
                            {
                                pending.Closing = true;
                            }
                        }
                    }
                }
                finally
                {
                    // Inline shutdown may treat an opened handle as idle once this signal is set.
                    linked.Dispose();
                    pending!.Opened.TrySetResult(true);
                    if (!accepted)
                    {
                        await TrackCleanup(pending, defer: true).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Records unsuccessful stream disposal without releasing the exclusive reservation.
        /// </summary>
        private static void DisposeRejectedStream(OpenFile pending, Stream? stream)
        {
            try
            {
                stream?.Dispose();
            }
            catch (Exception exception)
            {
                pending.OpenCleanupError = exception;
                throw;
            }
        }

        /// <summary>
        /// Records unsuccessful asynchronous stream disposal without releasing the exclusive reservation.
        /// </summary>
        private static async ValueTask DisposeRejectedStreamAsync(OpenFile pending, Stream? stream)
        {
            try
            {
                if (stream != null)
                {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                    await stream.DisposeAsync().ConfigureAwait(false);
#else
                    stream.Dispose();
#endif
                }
            }
            catch (Exception exception)
            {
                pending.OpenCleanupError = exception;
                throw;
            }
        }

        /// <summary>
        /// Selects one provider stream for the accepted access bits.
        /// </summary>
        private ValueTask<Stream> OpenProviderAsync(byte mode, CancellationToken ct)
        {
            if ((mode & 3) == 3)
            {
                return ((IFileSystemReadWriteProvider)m_provider).OpenReadWriteAsync(
                    ProviderPath, GetWriteMode(mode), ct);
            }
            return (mode & 1) != 0 ? m_provider.OpenReadAsync(ProviderPath, ct) :
                m_provider.OpenWriteAsync(ProviderPath, GetWriteMode(mode), ct);
        }

        /// <summary>
        /// Validates the session and mode and reserves compatible access before opening the provider stream.
        /// </summary>
        private bool TryReserveOpen(
            NodeId sessionId,
            byte mode,
            out OpenFile? pending,
            out ServiceResult error,
            bool linkedOpenCancellation = false)
        {
            pending = null;
            if (sessionId.IsNull)
            {
                error = ServiceResult.Create(
                    StatusCodes.BadSessionIdInvalid,
                    "A valid Session is required to open a file.");
                return false;
            }

            bool wantsRead = (mode & 0x1) != 0;
            bool wantsWrite = (mode & 0x2) != 0;

            if (!wantsRead && !wantsWrite)
            {
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "FileType.Open mode must include read or write.");
                return false;
            }
            // Part 20 4.2.2: bits 4:7 shall be zero and EraseExisting can only
            // be set when the file is opened for writing.
            if ((mode & 0xF0) != 0 || ((mode & 0x4) != 0 && !wantsWrite))
            {
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "FileType.Open mode setting is invalid.");
                return false;
            }
            if (wantsRead && wantsWrite && m_provider is not IFileSystemReadWriteProvider)
            {
                // Read and Write are independent mode bits, so the mode is valid (Part 20
                // 4.2.2); IFileSystemProvider only hands out read-only or write-only streams.
                error = ServiceResult.Create(
                    StatusCodes.BadNotSupported,
                    "Simultaneous read + write open not supported.");
                return false;
            }
            if (wantsWrite && !m_provider.IsWritable)
            {
                error = ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Provider is read-only.");
                return false;
            }

            while (true)
            {
                int epoch = Volatile.Read(ref m_closeEpoch);
                bool terminated = m_isSessionTerminated?.Invoke(sessionId) == true;
                lock (m_lock)
                {
                    // Session registry callbacks can acquire the server's event gate. Call them outside
                    // this bag's lock and retry if a session-close snapshot passed us in the meantime.
                    if (epoch != m_closeEpoch)
                    {
                        continue;
                    }
                    if (m_disposed)
                    {
                        error = StatusCodes.BadShutdown;
                        return false;
                    }
                    if (terminated)
                    {
                        error = StatusCodes.BadSessionClosed;
                        return false;
                    }
                    if (m_openBlocks != 0)
                    {
                        error = wantsWrite
                            ? ServiceResult.Create(StatusCodes.BadInvalidState,
                                "The file is being deleted or moved and thus not writable.")
                            : ServiceResult.Create(StatusCodes.BadNotReadable,
                                "The file is being deleted or moved and thus not readable.");
                        return false;
                    }
                    if (m_write != null && !wantsWrite)
                    {
                        error = ServiceResult.Create(StatusCodes.BadNotReadable,
                            "The file is locked for writing and thus not readable.");
                        return false;
                    }
                    if (m_write != null || (wantsWrite && m_reads.Count != 0))
                    {
                        error = ServiceResult.Create(StatusCodes.BadInvalidState,
                            "File already open with incompatible access.");
                        return false;
                    }
                    pending = new OpenFile(CreateFileHandle(), sessionId, mode, linkedOpenCancellation);
                    if (wantsWrite)
                    {
                        m_write = pending;
                    }
                    else
                    {
                        m_reads.Add(pending.Handle, pending);
                    }
                    error = ServiceResult.Good;
                    return true;
                }
            }
        }

        /// <summary>
        /// Attaches an opened stream only if its reservation has not been closed or disposed.
        /// </summary>
        private ServiceResult CompleteOpen(OpenFile pending, Stream stream)
        {
            lock (m_lock)
            {
                if (pending.Closing || (!ReferenceEquals(m_write, pending) &&
                    (!m_reads.TryGetValue(pending.Handle, out OpenFile? current) ||
                    !ReferenceEquals(current, pending))))
                {
                    return ServiceResult.Create(StatusCodes.BadSessionClosed,
                        "The file open was closed before the provider completed.");
                }
                if ((pending.Mode & 3) == 3 && (!stream.CanRead || !stream.CanWrite || !stream.CanSeek))
                {
                    return ServiceResult.Create(StatusCodes.BadNotSupported,
                        "Combined access requires one seekable, readable and writable stream.");
                }
                pending.Stream = stream;
                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Removes an unsuccessful open reservation without disturbing a replacement.
        /// </summary>
        private void CancelOpen(OpenFile pending)
        {
            lock (m_lock)
            {
                if (ReferenceEquals(m_write, pending))
                {
                    m_write = null;
                }
                else if (m_reads.TryGetValue(pending.Handle, out OpenFile? current) &&
                    ReferenceEquals(current, pending))
                {
                    m_reads.Remove(pending.Handle);
                }
            }
        }

        /// <summary>
        /// Maps FileType erase and append flags to the provider's write mode.
        /// </summary>
        private static FileWriteMode GetWriteMode(byte mode)
        {
            if ((mode & 4) != 0)
            {
                return FileWriteMode.Truncate;
            }
            return (mode & 8) != 0 ? FileWriteMode.Append : FileWriteMode.OpenOrCreate;
        }

        /// <summary>
        /// Validates lifecycle timeout values consistently on .NET Framework and modern .NET.
        /// </summary>
        internal static TimeSpan ValidateCloseTimeout(TimeSpan timeout)
        {
            if ((timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan) ||
                timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            return timeout;
        }

        /// <summary>
        /// Admits an operation before waiting for its turn at the handle's single cursor.
        /// </summary>
        public async ValueTask<Operation?> AcquireAsync(
            NodeId sessionId, uint fileHandle, byte mode, CancellationToken ct)
        {
            OpenFile? file = Admit(sessionId, fileHandle, mode);
            if (file == null)
            {
                return null;
            }
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, file.Lifetime.Token);
            try
            {
                await file.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
                return new Operation(file.Stream!, linked, () =>
                {
                    file.Gate.Release();
                    CompleteOperation(file);
                });
            }
            catch
            {
                linked.Dispose();
                CompleteOperation(file);
                throw;
            }
        }

        /// <summary>
        /// Admits a synchronous compatibility callback without exposing the stream outside its lease.
        /// </summary>
        public Operation? Acquire(NodeId sessionId, uint fileHandle, byte mode)
        {
            OpenFile? file = Admit(sessionId, fileHandle, mode);
            if (file == null)
            {
                return null;
            }
            var linked = CancellationTokenSource.CreateLinkedTokenSource(file.Lifetime.Token);
            try
            {
                file.Gate.Wait(linked.Token);
                return new Operation(file.Stream!, linked, () =>
                {
                    file.Gate.Release();
                    CompleteOperation(file);
                });
            }
            catch
            {
                linked.Dispose();
                CompleteOperation(file);
                throw;
            }
        }

        /// <summary>
        /// Closes admission and starts draining a synchronous compatibility close.
        /// </summary>
        public bool Close(NodeId sessionId, uint fileHandle)
        {
            OpenFile? file;
            lock (m_lock)
            {
                file = Find(sessionId, fileHandle, 0);
                if (file == null || file.Closing)
                {
                    return false;
                }
                file.Closing = true;
            }
            TrackCleanup(file);
            return true;
        }

        /// <summary>
        /// Stops admission and drains admitted operations before closing the stream.
        /// </summary>
        public async ValueTask<bool> CloseAsync(NodeId sessionId, uint fileHandle)
        {
            OpenFile? file;
            bool accepted;
            lock (m_lock)
            {
                file = Find(sessionId, fileHandle, 0);
                if (file == null)
                {
                    return false;
                }
                accepted = !file.Closing;
                file.Closing = true;
            }
            await TrackCleanup(file, defer: true).ConfigureAwait(false);
            return accepted;
        }

        /// <summary>
        /// Joins lifecycle cleanup already requested for a handle, without starting a new close.
        /// </summary>
        internal async ValueTask WaitForCloseAsync(NodeId sessionId, uint fileHandle)
        {
            OpenFile? file;
            lock (m_lock)
            {
                file = Find(sessionId, fileHandle, 0);
                if (file?.Closing != true)
                {
                    return;
                }
            }
            await TrackCleanup(file, defer: true).ConfigureAwait(false);
        }

        /// <summary>
        /// Requests cancellation and retains cleanup ownership after the lifecycle deadline.
        /// </summary>
        /// <param name="sessionId">The session whose handles must close.</param>
        /// <param name="defer">Whether to defer provider disposal rather than starting it on the calling thread.</param>
        public void CloseSession(NodeId sessionId, bool defer = true)
        {
            _ = BeginShutdown(sessionId, defer: defer);
        }

        /// <summary>
        /// Requests cancellation and waits up to the configured lifecycle deadline.
        /// </summary>
        public ValueTask CloseSessionAsync(NodeId sessionId) => WaitForCleanupAsync(BeginShutdown(sessionId));

        /// <inheritdoc/>
        public void Dispose()
        {
            _ = BeginShutdown(default, all: true, defer: false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => WaitForCleanupAsync(BeginShutdown(default, all: true));

        /// <summary>
        /// Requests shutdown without invoking arbitrary provider disposal on the lifecycle caller's thread.
        /// </summary>
        internal Task RequestShutdown() => BeginShutdown(default, all: true);

        private OpenFile? Find(NodeId session, uint id, byte mode)
        {
            OpenFile? file = m_write?.Handle == id ? m_write :
                m_reads.TryGetValue(id, out OpenFile? read) ? read : null;
            return file != null && file.SessionId == session && (file.Mode & mode) == mode ? file : null;
        }

        private OpenFile? Admit(NodeId session, uint id, byte mode)
        {
            lock (m_lock)
            {
                OpenFile? file = Find(session, id, mode);
                if (file == null || file.Closing || file.Stream == null)
                {
                    return null;
                }
                file.Active++;
                return file;
            }
        }

        private void CompleteOperation(OpenFile file)
        {
            lock (m_lock)
            {
                file.Active--;
                if (file.Closing && file.Active == 0)
                {
                    file.Drained.TrySetResult(true);
                }
            }
        }

        private Task BeginShutdown(NodeId session, bool all = false, bool defer = true)
        {
            List<OpenFile> files = [];
            lock (m_lock)
            {
                m_closeEpoch++;
                if (all)
                {
                    m_disposed = true;
                }
                if (m_write != null && (all || m_write.SessionId == session))
                {
                    files.Add(m_write);
                }
                foreach (OpenFile file in m_reads.Values)
                {
                    if (all || file.SessionId == session)
                    {
                        files.Add(file);
                    }
                }
                foreach (OpenFile file in files)
                {
                    file.Closing = true;
                }
            }
            foreach (OpenFile file in files)
            {
                // Cancellation callbacks are provider code; never run them under the bag lock.
                TrackCleanup(file, cancel: true, defer);
            }
            return CleanupCompletion;
        }

        private Task TrackCleanup(OpenFile file, bool cancel = false, bool defer = false)
        {
            TaskCompletionSource<bool>? completion = null;
            TaskCompletionSource<bool>? cancellation = null;
            lock (m_lock)
            {
                // Inline idle cleanup has no provider request left to cancel. In particular, do not
                // queue the legacy CancelAsync polyfill before releasing an already-drained reservation.
                if (cancel && (defer || m_disposed || !file.LinkedOpenCancellation ||
                    file.Active != 0 || !file.Opened.Task.IsCompleted) &&
                    !file.Finished && file.Cancellation == null)
                {
                    cancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    file.Cancellation = cancellation.Task;
                }
                if (file.Active == 0)
                {
                    file.Drained.TrySetResult(true);
                }
                if (file.Cleanup == null)
                {
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    file.Cleanup = completion.Task;
                    m_cleanup.Add(file.Cleanup);
                }
            }
            if (cancellation != null)
            {
                _ = CancelLifetimeAsync(file, cancellation);
            }
            if (completion != null)
            {
                _ = CleanupAsync(file, completion, defer);
            }
            return file.Cleanup;
        }

        private static async Task CancelLifetimeAsync(OpenFile file, TaskCompletionSource<bool> completion)
        {
            try
            {
                await file.Lifetime.CancelAsync().ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task CleanupAsync(OpenFile file, TaskCompletionSource<bool> completion, bool defer)
        {
            if (defer)
            {
                await Task.Yield();
            }
            Exception? error = null;
            try
            {
                await file.Opened.Task.ConfigureAwait(false);
                await file.Drained.Task.ConfigureAwait(false);
                if (file.OpenCleanupError != null)
                {
                    throw file.OpenCleanupError;
                }
                if (file.Stream != null)
                {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                    await file.Stream.DisposeAsync().ConfigureAwait(false);
#else
                    file.Stream.Dispose();
#endif
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                lock (m_lock)
                {
                    file.Finished = true;
                }
            }
            try
            {
                if (file.Cancellation != null)
                {
                    await file.Cancellation.ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                error = error == null ? exception : new AggregateException(error, exception);
            }
            finally
            {
                file.Lifetime.Dispose();
                file.Gate.Dispose();
            }
            if (error == null)
            {
                lock (m_lock)
                {
                    CancelOpen(file);
                }
                try
                {
                    m_openCountChanged?.Invoke();
                    lock (m_lock)
                    {
                        m_cleanup.Remove(completion.Task);
                    }
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }
            else
            {
                completion.TrySetException(error);
            }
        }

        private async ValueTask WaitForCleanupAsync(Task cleanup)
        {
            if (await Task.WhenAny(cleanup, Task.Delay(CloseTimeout)).ConfigureAwait(false) == cleanup)
            {
                try
                {
                    await cleanup.ConfigureAwait(false);
                }
                catch
                {
                    throw cleanup.Exception ?? new AggregateException("File cleanup failed.");
                }
            }
        }

        /// <summary>
        /// Retires an idle bag atomically with open reservation admission.
        /// A caller that obtained this bag just before retirement must reacquire it from its host.
        /// </summary>
        internal bool TryRetire()
        {
            lock (m_lock)
            {
                if (m_write != null || m_reads.Count != 0 || m_cleanup.Count != 0)
                {
                    return false;
                }
                m_disposed = true;
                return true;
            }
        }

        /// <summary>
        /// Blocks new opens for a Delete or MoveOrCopy of this file or an ancestor
        /// directory. Fails, without blocking, while the file is open, including an
        /// open whose provider stream is still pending, so that the lock check and
        /// the mutation cannot interleave with an open.
        /// </summary>
        internal bool TryBlockOpens()
        {
            lock (m_lock)
            {
                if (m_write != null || m_reads.Count != 0)
                {
                    return false;
                }
                m_openBlocks++;
                return true;
            }
        }

        /// <summary>
        /// Blocks new opens of a handle created while a mutation of its path or an
        /// ancestor directory is running.
        /// </summary>
        internal void BlockOpens()
        {
            lock (m_lock)
            {
                m_openBlocks++;
            }
        }

        /// <summary>
        /// Releases one block taken by <see cref="TryBlockOpens"/> or <see cref="BlockOpens"/>.
        /// </summary>
        internal void UnblockOpens()
        {
            lock (m_lock)
            {
                if (m_openBlocks > 0)
                {
                    m_openBlocks--;
                }
            }
        }

        private uint CreateFileHandle()
        {
            uint fileHandle;
            do
            {
                fileHandle = BitConverter.ToUInt32(
                    Nonce.CreateRandomNonceData(sizeof(uint)),
                    0);
            }
            while (fileHandle == 0 ||
                m_write?.Handle == fileHandle ||
                m_reads.ContainsKey(fileHandle));

            return fileHandle;
        }

        private readonly Lock m_lock = new();
        private readonly List<Task> m_cleanup = [];

        /// <summary>Bounds lifecycle waiting without shortening stream ownership.</summary>
        private TimeSpan m_closeTimeout = TimeSpan.FromSeconds(10);
        private readonly Dictionary<uint, OpenFile> m_reads = [];
        private readonly IFileSystemProvider m_provider;

        /// <summary>Rejects a late reservation after its session's close notification has already run.</summary>
        private readonly Func<NodeId, bool>? m_isSessionTerminated;

        /// <summary>Publishes the count after asynchronous stream cleanup.</summary>
        private readonly Action? m_openCountChanged;

        private OpenFile? m_write;

        /// <summary>
        /// Prevents new open reservations after the handle bag has been disposed.
        /// </summary>
        private bool m_disposed;

        /// <summary>
        /// Number of running Delete or MoveOrCopy mutations that currently refuse new opens.
        /// </summary>
        private int m_openBlocks;

        /// <summary>Detects session closure while admission checks the external session registry.</summary>
        private int m_closeEpoch;

        /// <summary>
        /// Retains a session-owned open reservation and its eventual provider stream.
        /// </summary>
        private sealed class OpenFile
        {
            /// <summary>
            /// Creates an open reservation for a session with the requested FileType mode bits.
            /// </summary>
            public OpenFile(uint handle, NodeId sessionId, byte mode, bool linkedOpenCancellation)
            {
                Handle = handle;
                SessionId = sessionId;
                Mode = mode;
                LinkedOpenCancellation = linkedOpenCancellation;
            }

            /// <summary>
            /// Gets the nonzero FileType handle allocated for this reservation.
            /// </summary>
            public uint Handle { get; }

            /// <summary>
            /// Gets the session authorized to access the reserved handle.
            /// </summary>
            public NodeId SessionId { get; }

            /// <summary>
            /// Gets the access and positioning flags accepted when reserving the handle.
            /// </summary>
            public byte Mode { get; }

            /// <summary>
            /// Gets whether completed provider-open cancellation is detached from the handle lifetime.
            /// </summary>
            public bool LinkedOpenCancellation { get; }

            /// <summary>
            /// Gets or sets the opened stream, or null while the provider open is still pending.
            /// </summary>
            public Stream? Stream { get; set; }

            /// <summary>
            /// Gets or sets the failure of rejected provider-stream cleanup.
            /// </summary>
            public Exception? OpenCleanupError { get; set; }

            /// <summary>
            /// Serializes access to the stream cursor.
            /// </summary>
            public SemaphoreSlim Gate { get; } = new(1, 1);

            /// <summary>
            /// Cancels provider requests when session or server lifetime ends.
            /// </summary>
            public CancellationTokenSource Lifetime { get; } = new();

            /// <summary>
            /// Signals completion of provider open and any rejected-stream cleanup.
            /// </summary>
            public TaskCompletionSource<bool> Opened { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Signals that all admitted operations have left the stream.
            /// </summary>
            public TaskCompletionSource<bool> Drained { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets or sets the number of admitted operations.
            /// </summary>
            public int Active { get; set; }

            /// <summary>
            /// Gets or sets whether operation admission has stopped.
            /// </summary>
            public bool Closing { get; set; }

            /// <summary>
            /// Gets or sets the retained stream cleanup task.
            /// </summary>
            public Task? Cleanup { get; set; }

            /// <summary>
            /// Gets or sets the asynchronous cancellation notification.
            /// </summary>
            public Task? Cancellation { get; set; }

            /// <summary>
            /// Gets or sets whether stream cleanup has finished.
            /// </summary>
            public bool Finished { get; set; }
        }

        /// <summary>
        /// Owns one serialized stream operation, including its request and lifetime cancellation.
        /// </summary>
        internal sealed class Operation : IDisposable
        {
            /// <summary>
            /// Creates an admitted operation after acquiring its cursor semaphore.
            /// </summary>
            internal Operation(Stream stream, CancellationTokenSource cancellation, Action complete)
            {
                Stream = stream;
                m_complete = complete;
                m_cancellation = cancellation;
            }

            /// <summary>
            /// Gets the stream while this operation owns its cursor.
            /// </summary>
            public Stream Stream { get; }

            /// <summary>
            /// Gets the combined request and handle-lifetime cancellation token.
            /// </summary>
            public CancellationToken CancellationToken => m_cancellation.Token;

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    m_cancellation.Dispose();
                    m_complete();
                }
            }

            private readonly Action m_complete;
            private readonly CancellationTokenSource m_cancellation;
            private int m_disposed;
        }
    }
}
