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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server.Results
{
    /// <summary>
    /// Serves the OPC 40001-101 download path: <c>GenerateFileForRead</c> on a
    /// <c>ResultTransferType</c> object hands the caller a transient
    /// <c>FileType</c> object and a handle, and the caller reads the result
    /// payload through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repository already had the write half of temporary file transfer —
    /// <c>SoftwareUpdateFileTransferManager</c> serves
    /// <c>GenerateFileForWrite</c> / <c>CloseAndCommit</c> for Device
    /// Integration software packages. This is the read half, built to the same
    /// shape: one transient <c>FileState</c> per handle, the handle bound to
    /// the session that opened it, a hard cap including pending generation, and a
    /// timeout after which an abandoned handle is reclaimed. Session closure
    /// awaits removal of that session's transient files, independently of
    /// whether its subscriptions are retained.
    /// </para>
    /// <para>
    /// The two write-side methods <c>TemporaryFileTransferType</c> makes
    /// mandatory are bound as well, and refuse: <c>GenerateFileForWrite</c>
    /// with <c>Bad_NotWritable</c> and <c>CloseAndCommit</c> with
    /// <c>Bad_InvalidArgument</c>, the codes OPC 10000-20 §4.4.4 and §4.4.5
    /// define for a read-only file and a handle that was never a write handle.
    /// </para>
    /// <para>
    /// Inactivity expiry is swept rather than timed: it runs at the start of every
    /// <c>GenerateFileForRead</c>, which is the only moment the cap can
    /// actually bite. That keeps the manager free of a background timer per
    /// result object, at the cost of an inactive handle in a live session surviving
    /// until the next download starts. Session closure and shutdown independently
    /// await their owned generation, rollback and node deletion.
    /// </para>
    /// <para>
    /// <c>GenerateFileForRead</c> returns a <c>completionStateMachine</c>
    /// NodeId for servers that generate the payload after the call returns.
    /// This manager materialises the payload from the store within the call,
    /// so it returns <see cref="NodeId.Null"/> — the value OPC 10000-5 defines
    /// for "no completion state machine".
    /// </para>
    /// </remarks>
    internal sealed partial class MachineryResultTransferManager : IAsyncDisposable
    {
        private const byte OpenModeRead = 1;

        /// <summary>
        /// Binds read-only transfer methods and awaited session-resource cleanup.
        /// </summary>
        public MachineryResultTransferManager(
            ResultTransferState transfer,
            AsyncCustomNodeManager manager,
            IMachineryResultStore store,
            MachineryServerOptions options,
            ILogger logger,
            TimeProvider? timeProvider = null)
        {
            m_transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_sessionRegistration = m_manager.RegisterSessionClosingHandler(OnSessionClosingAsync);

            m_transfer.ClientProcessingTimeout?.Value =
                m_options.ResultTransferTimeout.TotalMilliseconds;

            // OPC 40001-101 redeclares GenerateFileForRead with its own
            // arguments instead of a method type, so the generated
            // ResultTransferState exposes it as a plain MethodState. The
            // outputs keep the order of OPC 10000-5 GenerateFileForRead.
            m_transfer.GenerateFileForRead?.OnCallMethod2Async = OnGenerateFileForReadAsync;

            // TemporaryFileTransferType makes both write-side methods
            // mandatory (OPC 10000-20 §4.4.1), so a ResultTransfer object
            // always carries them and they have to answer. OPC 40001-101
            // §7.2.1 leaves them unspecialised "as results are supposed to be
            // only generated by the Server, not received", which is the
            // read-only case OPC 10000-20 already names a status code for.
            m_transfer.GenerateFileForWrite?.OnCall = OnGenerateFileForWrite;
            m_transfer.CloseAndCommit?.OnCall = OnCloseAndCommit;
        }

        /// <summary>
        /// Admits generation before calling the store, retaining ownership until registration or rollback completes.
        /// </summary>
        private async ValueTask<ServiceResult> OnGenerateFileForReadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            _ = method;
            _ = objectId;
            cancellationToken.ThrowIfCancellationRequested();

            string? resultId = ExtractResultId(inputArguments[0]);
            if (string.IsNullOrEmpty(resultId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "GenerateFileForRead requires a ResultTransferOptionsDataType " +
                    "carrying the ResultId to download.");
            }

            await ReclaimExpiredHandlesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            NodeId sessionId = SessionIdOf(context);
            ISession? owner = FindLiveSession(sessionId);
            DownloadSlot slot;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return StatusCodes.BadInvalidState;
                }
                if (owner is { IsClosing: true } or { HasExpired: true })
                {
                    return StatusCodes.BadSessionClosed;
                }
                if (m_slots.Count >= m_options.MaxConcurrentResultTransfers)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadTooManyOperations,
                        "Concurrent result-transfer handle limit reached.");
                }

                uint handle;
                do
                {
                    handle = ++m_nextHandle;
                }
                while (handle == 0 || m_slots.ContainsKey(handle));

                slot = new DownloadSlot(
                    handle,
                    sessionId,
                    m_timeProvider.GetUtcNow());

                // Pending store reads and registrations belong to the owner too.
                m_slots[handle] = slot;
            }

            ServiceResult status = StatusCodes.BadInvalidState;
            Exception? failure = null;
            try
            {
                using var generation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, slot.Shutdown.Token);
                status = await GenerateFileAsync(slot, owner, resultId!, outputArguments, generation.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                slot.GenerationCompleted.TrySetResult(true);
            }

            if (!slot.Published)
            {
                try
                {
                    await CleanupSlotAsync(slot).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failure = failure == null
                        ? ex
                        : new AggregateException("Result generation and rollback failed.", failure, ex);
                }
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return status;
        }

        /// <summary>
        /// Reads and registers a file while its admitted generation remains cancellable and drainable.
        /// </summary>
        private async ValueTask<ServiceResult> GenerateFileAsync(
            DownloadSlot slot,
            ISession? owner,
            string resultId,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MachineryResult? result = await m_store.GetResultByIdAsync(resultId, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result == null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadNotFound,
                    "No result with id '{0}' is available.",
                    resultId);
            }
            if (result.FileContent.IsNull || result.FileContent.Span.Length == 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadNothingToDo,
                    "Result '{0}' carries no transferable data on file.",
                    resultId);
            }

            lock (m_gate)
            {
                if (m_disposed || slot.Closing)
                {
                    return StatusCodes.BadInvalidState;
                }
                if (owner is { IsClosing: true } or { HasExpired: true })
                {
                    return StatusCodes.BadSessionClosed;
                }
                slot.SetResult(result);
            }
            try
            {
                await AttachFileStateAsync(slot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.FailedToCreateTransientResultFile(ex, slot.Handle);
                return ServiceResult.Create(
                    ex, StatusCodes.BadInternalError, "Failed to create the transient result FileState.");
            }

            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (owner is { IsClosing: true } or { HasExpired: true })
                {
                    return StatusCodes.BadSessionClosed;
                }
                if (!m_disposed && !slot.Closing)
                {
                    // OPC 10000-5: the handle GenerateFileForRead returns is
                    // already open. A client reads straight from it and closes
                    // it when it is done - it never calls Open.
                    slot.Open(m_timeProvider.GetUtcNow());
                    slot.Published = true;
                    outputArguments[0] = Variant.From(slot.FileNodeId);
                    outputArguments[1] = Variant.From(slot.Handle);
                    outputArguments[2] = Variant.From(NodeId.Null);
                    return ServiceResult.Good;
                }
            }

            return StatusCodes.BadInvalidState;
        }

        /// <summary>
        /// Answers <c>GenerateFileForWrite</c>. OPC 10000-20 §4.4.4 defines
        /// <c>Bad_NotWritable</c> for a temporary file that "is readonly and
        /// can not be generated for writing" — exactly what a result file is,
        /// because OPC 40001-101 §7.2.1 has results generated only by the
        /// server.
        /// </summary>
        private static ServiceResult OnGenerateFileForWrite(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            Variant generateOptions,
            ref NodeId fileNodeId,
            ref uint fileHandle)
        {
            _ = context;
            _ = method;
            _ = objectId;
            _ = generateOptions;
            fileNodeId = NodeId.Null;
            fileHandle = 0;
            return ServiceResult.Create(
                StatusCodes.BadNotWritable,
                "Result files are read-only: OPC 40001-101 results are generated by the " +
                "server, not received. Use GenerateFileForRead to download one.");
        }

        /// <summary>
        /// Answers <c>CloseAndCommit</c>. Its <c>fileHandle</c> is "the
        /// fileHandle used to write the file" (OPC 10000-20 §4.4.5), and
        /// <see cref="OnGenerateFileForWrite"/> never hands one out, so every
        /// handle is invalid — including a read handle, which is closed with
        /// the file's own <c>Close</c> and is left untouched here.
        /// </summary>
        private ServiceResult OnCloseAndCommit(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref NodeId completionStateMachine)
        {
            _ = method;
            _ = objectId;
            completionStateMachine = NodeId.Null;
            bool isReadHandle;
            lock (m_gate)
            {
                isReadHandle = m_slots.TryGetValue(fileHandle, out DownloadSlot? slot) &&
                    OwnsSlot(context, slot);
            }
            return ServiceResult.Create(
                StatusCodes.BadInvalidArgument,
                isReadHandle
                    ? "File handle {0} belongs to a result download; close it with the " +
                        "file's Close method. ResultTransfer hands out no write handles."
                    : "File handle {0} is not a write handle of this ResultTransfer; " +
                        "result files cannot be written.",
                fileHandle);
        }

        private static string? ExtractResultId(Variant generateOptions)
        {
            if (generateOptions.TryGetStructure<BaseResultTransferOptionsDataType>(
                    out BaseResultTransferOptionsDataType? options) &&
                options is not null)
            {
                return options.ResultId;
            }
            return generateOptions.TryGetValue(out string resultId) ? resultId : null;
        }

        /// <summary>
        /// Retains the staged file before registration so cancellation can roll back even a partially indexed tree.
        /// </summary>
        private async ValueTask AttachFileStateAsync(DownloadSlot slot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ServerSystemContext context = m_manager.SystemContext;
            var browseName = new QualifiedName(
                $"ResultFile_{slot.Handle}",
                m_transfer.BrowseName.NamespaceIndex);

            FileState file = context.CreateInstanceOfFileType(m_transfer, browseName);
            file.SymbolicName = browseName.Name ?? string.Empty;
            file.BrowseName = browseName;
            file.DisplayName = new LocalizedText(browseName.Name);
            file.NodeId = context.NodeIdFactory!.New(context, file);
            file.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            file.ModellingRuleId = NodeId.Null;

            file.Writable?.Value = false;
            file.UserWritable?.Value = false;
            file.Size?.Value = (ulong)slot.Payload.Span.Length;
            file.OpenCount?.Value = 0;
            file.MimeType?.Value = slot.MimeType;
            file.Open?.OnCall = OpenSlot;
            file.Read?.OnCall = ReadSlot;
            file.Close?.OnCallAsync = CloseSlotAsync;
            file.GetPosition?.OnCall = GetPositionSlot;
            file.SetPosition?.OnCall = SetPositionSlot;
            file.Write?.OnCall = WriteSlot;

            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_disposed || slot.Closing)
                {
                    throw new OperationCanceledException(slot.Shutdown.Token);
                }
                slot.AttachFile(file);
                m_transfer.AddChild(file);
            }
            await m_manager.AddPredefinedNodeAsync(file, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Answers <c>Open</c> on a transient result file. The handle that
        /// <c>GenerateFileForRead</c> returned is already open, so this only
        /// hands the same handle back to a client that opens defensively, and
        /// refuses any mode but read.
        /// </summary>
        private ServiceResult OpenSlot(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            _ = objectId;
            if (mode != OpenModeRead)
            {
                fileHandle = 0;
                return ServiceResult.Create(
                    StatusCodes.BadNotSupported,
                    "Transient result files are read-only (mode 1).");
            }
            lock (m_gate)
            {
                DownloadSlot? slot = FindSlotByFileObject(method.Parent);
                if (slot is null)
                {
                    fileHandle = 0;
                    return StatusCodes.BadNotFound;
                }
                if (!OwnsSlot(context, slot))
                {
                    fileHandle = 0;
                    return ServiceResult.Create(
                        StatusCodes.BadUserAccessDenied,
                        "File handle is owned by another session.");
                }
                slot.Seek(0, m_timeProvider.GetUtcNow());
                fileHandle = slot.Handle;
            }
            return ServiceResult.Good;
        }

        private ServiceResult ReadSlot(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref ByteString data)
        {
            _ = method;
            _ = objectId;
            data = default;
            if (length < 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "The requested read length must not be negative.");
            }
            lock (m_gate)
            {
                if (!TryGetOwnedSlotLocked(context, fileHandle, out DownloadSlot slot, out ServiceResult err))
                {
                    return err;
                }
                if (!slot.IsOpen)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "Transient result file is not open.");
                }
                data = slot.Read(length, m_timeProvider.GetUtcNow());
            }
            return ServiceResult.Good;
        }

        private ServiceResult WriteSlot(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ByteString data)
        {
            _ = context;
            _ = method;
            _ = objectId;
            _ = fileHandle;
            _ = data;
            return ServiceResult.Create(
                StatusCodes.BadNotWritable,
                "Transient result files are read-only.");
        }

        private async ValueTask<CloseMethodStateResult> CloseSlotAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            CancellationToken cancellationToken)
        {
            _ = method;
            _ = objectId;
            cancellationToken.ThrowIfCancellationRequested();
            DownloadSlot slot;
            lock (m_gate)
            {
                if (!TryGetOwnedSlotLocked(context, fileHandle, out slot, out ServiceResult err))
                {
                    return new CloseMethodStateResult { ServiceResult = err };
                }
                slot.Closing = true;
            }
            await CleanupSlotAsync(slot).ConfigureAwait(false);
            return new CloseMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        private ServiceResult GetPositionSlot(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref ulong position)
        {
            _ = method;
            _ = objectId;
            lock (m_gate)
            {
                if (!TryGetOwnedSlotLocked(context, fileHandle, out DownloadSlot slot, out ServiceResult err))
                {
                    position = 0;
                    return err;
                }
                position = (ulong)slot.Position;
            }
            return ServiceResult.Good;
        }

        private ServiceResult SetPositionSlot(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ulong position)
        {
            _ = method;
            _ = objectId;
            lock (m_gate)
            {
                if (!TryGetOwnedSlotLocked(context, fileHandle, out DownloadSlot slot, out ServiceResult err))
                {
                    return err;
                }
                if (position > (ulong)slot.Payload.Span.Length)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "Requested position exceeds the result length.");
                }
                slot.Seek((long)position, m_timeProvider.GetUtcNow());
            }
            return ServiceResult.Good;
        }

        private bool TryGetOwnedSlotLocked(
            ISystemContext context,
            uint fileHandle,
            out DownloadSlot slot,
            out ServiceResult error)
        {
            if (!m_slots.TryGetValue(fileHandle, out DownloadSlot? located) || located.Closing)
            {
                slot = null!;
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Unknown result-transfer handle.");
                return false;
            }
            if (!OwnsSlot(context, located))
            {
                slot = null!;
                error = ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "File handle is owned by another session.");
                return false;
            }
            slot = located;
            error = ServiceResult.Good;
            return true;
        }

        private static bool OwnsSlot(ISystemContext context, DownloadSlot slot)
        {
            NodeId sessionId = SessionIdOf(context);
            return slot.OwnerSessionId.IsNull ||
                sessionId.IsNull ||
                slot.OwnerSessionId == sessionId;
        }

        private DownloadSlot? FindSlotByFileObject(NodeState? parent)
        {
            if (parent is null)
            {
                return null;
            }
            foreach (DownloadSlot slot in m_slots.Values)
            {
                if (!slot.Closing && ReferenceEquals(slot.FileObject, parent))
                {
                    return slot;
                }
            }
            return null;
        }

        /// <summary>
        /// The session of the caller, or <see cref="NodeId.Null"/> for a call
        /// that does not come through a session.
        /// </summary>
        private static NodeId SessionIdOf(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        private readonly ResultTransferState m_transfer;
        private readonly AsyncCustomNodeManager m_manager;
        private readonly IMachineryResultStore m_store;
        private readonly MachineryServerOptions m_options;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_gate = new();
        private readonly Dictionary<uint, DownloadSlot> m_slots = [];
        private uint m_nextHandle;
        private bool m_disposed;

        /// <summary>
        /// An admitted generation, its published file, and the shared completion of its cleanup.
        /// </summary>
        private sealed class DownloadSlot
        {
            /// <summary>
            /// Reserves a handle before any provider or registration callback can yield.
            /// </summary>
            public DownloadSlot(
                uint handle,
                NodeId ownerSessionId,
                DateTimeOffset created)
            {
                Handle = handle;
                OwnerSessionId = ownerSessionId;
                LastActivity = created;
            }

            /// <summary>
            /// Gets the unique handle while its reservation exists.
            /// </summary>
            public uint Handle { get; }

            /// <summary>
            /// Gets the owning session, or null for an internal caller.
            /// </summary>
            public NodeId OwnerSessionId { get; }

            /// <summary>
            /// Gets the copied payload after the store returns.
            /// </summary>
            public ByteString Payload { get; private set; }

            /// <summary>
            /// Gets the file's media type.
            /// </summary>
            public string MimeType { get; private set; } = string.Empty;

            /// <summary>
            /// Gets the staged or published file until deletion completes.
            /// </summary>
            public FileState? FileObject => Volatile.Read(ref m_file);

            /// <summary>
            /// Gets the transient file identifier.
            /// </summary>
            public NodeId FileNodeId => FileObject?.NodeId ?? NodeId.Null;

            /// <summary>
            /// Gets whether the read handle has been opened.
            /// </summary>
            public bool IsOpen { get; private set; }

            /// <summary>
            /// Gets the current read position.
            /// </summary>
            public long Position { get; private set; }

            /// <summary>
            /// Gets the last activity used by the expiry sweep.
            /// </summary>
            public DateTimeOffset LastActivity { get; private set; }

            /// <summary>
            /// Gets or sets whether the file was returned to a client.
            /// </summary>
            public bool Published { get; set; }

            /// <summary>
            /// Gets or sets whether admission and file access are closed.
            /// </summary>
            public bool Closing { get; set; }

            /// <summary>
            /// Gets or sets whether cleanup released the reservation and cancellation source.
            /// </summary>
            public bool Removed { get; set; }

            /// <summary>
            /// Gets cancellation for a pending generation.
            /// </summary>
            public CancellationTokenSource Shutdown { get; } = new();

            /// <summary>
            /// Gets completion of all store and registration work, before rollback is joined.
            /// </summary>
            public TaskCompletionSource<bool> GenerationCompleted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets or sets the shared removal attempt, protected by the transfer gate.
            /// </summary>
            public TaskCompletionSource<bool>? CleanupCompleted { get; set; }

            /// <summary>
            /// Gets or sets the deletion snapshot retained across a failed removal attempt.
            /// </summary>
            public List<NodeId>? FileNodes { get; set; }

            /// <summary>
            /// Copies store data so later provider mutations cannot change an open file.
            /// </summary>
            public void SetResult(MachineryResult result)
            {
                Payload = new ByteString(result.FileContent.Span.ToArray());
                MimeType = result.MimeType;
            }

            /// <summary>
            /// Retains the staged node before registration starts.
            /// </summary>
            public void AttachFile(FileState file)
            {
                Volatile.Write(ref m_file, file);
            }

            /// <summary>
            /// Drops the file only after all node removal has completed.
            /// </summary>
            public FileState? DetachFile()
            {
                IsOpen = false;
                return Interlocked.Exchange(ref m_file, null);
            }

            /// <summary>
            /// Opens the returned read handle.
            /// </summary>
            public void Open(DateTimeOffset now)
            {
                IsOpen = true;
                Position = 0;
                LastActivity = now;
                if (FileObject?.OpenCount != null)
                {
                    FileObject.OpenCount.Value = 1;
                }
            }

            /// <summary>
            /// Reads a bounded slice of the immutable payload.
            /// </summary>
            public ByteString Read(int length, DateTimeOffset now)
            {
                LastActivity = now;
                long remaining = Payload.Span.Length - Position;
                if (remaining <= 0 || length == 0)
                {
                    return ByteString.Empty;
                }
                int take = (int)Math.Min(remaining, length);
                byte[] chunk = Payload.Span.Slice((int)Position, take).ToArray();
                Position += take;
                return new ByteString(chunk);
            }

            /// <summary>
            /// Updates the position and activity timestamp.
            /// </summary>
            public void Seek(long position, DateTimeOffset now)
            {
                Position = position;
                LastActivity = now;
            }

            /// <summary>The staged or published file retained until removal completes.</summary>
            private FileState? m_file;
        }
    }

    internal static partial class MachineryResultTransferManagerLog
    {
        [LoggerMessage(
            EventId = MachineryServerEventIds.ResultTransferFileFailed,
            Level = LogLevel.Error,
            Message = "Failed to create the transient result file for handle {Handle}.")]
        public static partial void FailedToCreateTransientResultFile(
            this ILogger logger,
            Exception exception,
            uint handle);

        [LoggerMessage(
            EventId = MachineryServerEventIds.ResultTransferFileNotRemoved,
            Level = LogLevel.Warning,
            Message = "Failed to remove the transient result file {NodeId}.")]
        public static partial void FailedToRemoveTransientResultFile(
            this ILogger logger,
            Exception exception,
            NodeId nodeId);

        [LoggerMessage(
            EventId = MachineryServerEventIds.ResultTransferHandleExpired,
            Level = LogLevel.Information,
            Message = "Reclaimed abandoned result-transfer handle {Handle}.")]
        public static partial void ResultTransferHandleExpired(
            this ILogger logger,
            uint handle);
    }
}
