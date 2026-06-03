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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Implements the OPC 10000-5 §11.4 <c>TemporaryFileTransferType</c>
    /// server protocol for the DI software-update facet's
    /// <c>PackageLoadingType.FileTransfer</c> slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flow per spec:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     Client calls <c>GenerateFileForWrite(generateOptions)</c>.
    ///     The server allocates a fresh handle, creates a transient
    ///     <see cref="FileState"/> child of the FileTransfer object
    ///     backed by an in-memory buffer, registers it with the node
    ///     manager, and returns its NodeId + handle.
    ///   </description></item>
    ///   <item><description>
    ///     Client opens the returned FileState in
    ///     <c>Write|EraseExisting</c> mode, streams chunks via the
    ///     standard <c>Write</c> method, and closes it.
    ///   </description></item>
    ///   <item><description>
    ///     Client calls <c>CloseAndCommit(handle)</c>. The server
    ///     resolves the buffered bytes, constructs a
    ///     <see cref="SoftwarePackage"/>, hands the payload to
    ///     <see cref="ISoftwarePackageStore.AddAsync"/>, and removes
    ///     the transient FileState from the address space.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Each handle is owned by the session that allocated it; cross-
    /// session access is rejected with
    /// <see cref="StatusCodes.BadUserAccessDenied"/>. The per-instance
    /// handle count is capped at
    /// <see cref="MaxConcurrentHandles"/> to bound memory usage.
    /// </para>
    /// </remarks>
    internal sealed class SoftwareUpdateFileTransferManager : IDisposable
    {
        /// <summary>
        /// Maximum concurrent upload handles per FileTransfer instance.
        /// </summary>
        internal const int MaxConcurrentHandles = 8;

        /// <summary>
        /// Maximum buffered upload size in bytes (64 MiB).
        /// </summary>
        internal const long MaxUploadSizeBytes = 64L * 1024 * 1024;

        /// <summary>OPC 10000-5 §11.3.3 — Open mode <c>Write|EraseExisting</c>.</summary>
        private const byte OpenModeWriteEraseExisting = 6;

        private readonly TemporaryFileTransferState m_fileTransfer;
        private readonly DiNodeManager m_manager;
        private readonly ISoftwarePackageStore m_packageStore;
        private readonly ILogger m_logger;
        private readonly object m_gate = new();
        private readonly Dictionary<uint, UploadSlot> m_slots = [];
        private uint m_nextHandle;
        private bool m_disposed;

        public SoftwareUpdateFileTransferManager(
            TemporaryFileTransferState fileTransfer,
            DiNodeManager manager,
            ISoftwarePackageStore packageStore,
            ILogger logger)
        {
            m_fileTransfer = fileTransfer ?? throw new ArgumentNullException(nameof(fileTransfer));
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_packageStore = packageStore ?? throw new ArgumentNullException(nameof(packageStore));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (m_fileTransfer.GenerateFileForWrite != null)

            {

                m_fileTransfer.GenerateFileForWrite.OnCall = OnGenerateFileForWrite;

            }
            if (m_fileTransfer.CloseAndCommit != null)
            {
                m_fileTransfer.CloseAndCommit.OnCall = OnCloseAndCommit;
            }
        }

        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                foreach (UploadSlot slot in m_slots.Values)
                {
                    slot.Dispose();
                }
                m_slots.Clear();
            }
        }

        private ServiceResult OnGenerateFileForWrite(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            Variant generateOptions,
            ref NodeId fileNodeId,
            ref uint fileHandle)
        {
            _ = method;
            _ = objectId;

            NodeId? sessionId = SessionIdOf(context);
            UploadSlot slot;

            lock (m_gate)
            {
                if (m_disposed)
                {
                    fileNodeId = NodeId.Null;
                    fileHandle = 0;
                    return StatusCodes.BadInvalidState;
                }
                if (m_slots.Count >= MaxConcurrentHandles)
                {
                    fileNodeId = NodeId.Null;
                    fileHandle = 0;
                    return ServiceResult.Create(
                        StatusCodes.BadTooManyOperations,
                        "Concurrent upload handle limit reached.");
                }

                fileHandle = ++m_nextHandle;
                slot = new UploadSlot(
                    fileHandle, sessionId, generateOptions, m_logger);

                try
                {
                    slot.AttachFileState(
                        m_manager,
                        m_fileTransfer,
                        OpenSlot,
                        WriteSlot,
                        ReadSlot,
                        CloseSlot,
                        GetPositionSlot,
                        SetPositionSlot);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        ex,
                        "Failed to create transient FileState for upload handle {Handle}.",
                        fileHandle);
                    slot.Dispose();
                    fileNodeId = NodeId.Null;
                    fileHandle = 0;
                    return ServiceResult.Create(
                        ex, StatusCodes.BadInternalError,
                        "Failed to create transient upload FileState.");
                }

                m_slots[fileHandle] = slot;
                fileNodeId = slot.FileNodeId;
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnCloseAndCommit(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref NodeId completionStateMachine)
        {
            _ = method;
            _ = objectId;

            UploadSlot slot;
            byte[] payload;

            lock (m_gate)
            {
                if (m_disposed)
                {
                    completionStateMachine = NodeId.Null;
                    return StatusCodes.BadInvalidState;
                }
                if (!TryGetOwnedSlotLocked(context, fileHandle, out slot, out ServiceResult err))
                {
                    completionStateMachine = NodeId.Null;
                    return err;
                }
                payload = slot.SnapshotPayload();
                m_slots.Remove(fileHandle);
            }

            try
            {
                SoftwarePackage metadata = BuildPackageMetadata(slot, payload);
                using var payloadStream = new MemoryStream(payload, writable: false);
                _ = m_packageStore.AddAsync(metadata, payloadStream, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                m_logger.LogInformation(
                    "Committed upload handle {Handle} ({Bytes} bytes) as package {PackageId}.",
                    fileHandle, payload.Length, metadata.Id);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Failed to commit upload handle {Handle} to the package store.",
                    fileHandle);
                slot.Dispose();
                completionStateMachine = NodeId.Null;
                return ServiceResult.Create(
                    ex, StatusCodes.BadUserAccessDenied,
                    "Failed to persist the uploaded package.");
            }
            finally
            {
                slot.DetachFromAddressSpace(m_manager);
                slot.Dispose();
            }

            completionStateMachine = NodeId.Null;
            return ServiceResult.Good;
        }

        private ServiceResult OpenSlot(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            _ = method;
            _ = objectId;

            if (mode != OpenModeWriteEraseExisting)
            {
                fileHandle = 0;
                return ServiceResult.Create(
                    StatusCodes.BadNotSupported,
                    "Transient upload files only support Write|EraseExisting (mode 6).");
            }
            lock (m_gate)
            {
                UploadSlot? slot = FindSlotByFileObject(method.Parent);
                if (slot is null)
                {
                    fileHandle = 0;
                    return StatusCodes.BadNotFound;
                }
                NodeId? sessionId = SessionIdOf(context);
                if (slot.OwnerSessionId != null && sessionId != null
                    && slot.OwnerSessionId != sessionId)
                {
                    fileHandle = 0;
                    return ServiceResult.Create(
                        StatusCodes.BadUserAccessDenied,
                        "File handle is owned by another session.");
                }
                if (slot.IsOpen)
                {
                    fileHandle = 0;
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "Transient upload file is already open.");
                }
                slot.OpenForWrite();
                fileHandle = slot.Handle;
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
            _ = method;
            _ = objectId;
            lock (m_gate)
            {
                if (!TryGetOwnedSlotLocked(context, fileHandle, out UploadSlot slot, out ServiceResult err))
                {
                    return err;
                }
                if (!slot.IsOpen)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "Transient upload file is not open.");
                }
                if (data.IsNull || data.Span.Length == 0)
                {
                    return ServiceResult.Good;
                }
                if (slot.BufferedBytes + data.Span.Length > MaxUploadSizeBytes)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadOutOfMemory,
                        "Upload payload exceeds the configured maximum size.");
                }
                slot.AppendWrite(data.Span);
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
            _ = context;
            _ = method;
            _ = objectId;
            _ = fileHandle;
            _ = length;
            // Write-only transient files don't support read.
            data = default;
            return ServiceResult.Create(
                StatusCodes.BadNotSupported,
                "Transient upload files are write-only.");
        }

        private ServiceResult CloseSlot(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            _ = method;
            _ = objectId;
            lock (m_gate)
            {
                if (!TryGetOwnedSlotLocked(context, fileHandle, out UploadSlot slot, out ServiceResult err))
                {
                    return err;
                }
                if (!slot.IsOpen)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "Transient upload file is already closed.");
                }
                slot.Close();
            }
            return ServiceResult.Good;
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
                if (!TryGetOwnedSlotLocked(context, fileHandle, out UploadSlot slot, out ServiceResult err))
                {
                    position = 0;
                    return err;
                }
                position = (ulong)slot.BufferedBytes;
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
                if (!TryGetOwnedSlotLocked(context, fileHandle, out UploadSlot slot, out ServiceResult err))
                {
                    return err;
                }
                if (position > (ulong)slot.BufferedBytes)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "Requested position exceeds file length.");
                }
                slot.Seek((long)position);
            }
            return ServiceResult.Good;
        }

        private static NodeId? SessionIdOf(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId;
        }

        private bool TryGetOwnedSlotLocked(
            ISystemContext context,
            uint fileHandle,
            out UploadSlot slot,
            out ServiceResult error)
        {
            if (!m_slots.TryGetValue(fileHandle, out UploadSlot? located))
            {
                slot = null!;
                error = ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "Unknown file handle.");
                return false;
            }
            NodeId? expected = SessionIdOf(context);
            if (located.OwnerSessionId != null && expected != null
                && located.OwnerSessionId != expected)
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

        private UploadSlot? FindSlotByFileObject(NodeState? parent)
        {
            if (parent is null)
            {
                return null;
            }
            foreach (UploadSlot slot in m_slots.Values)
            {
                if (object.ReferenceEquals(slot.FileObject, parent))
                {
                    return slot;
                }
            }
            return null;
        }

        private static SoftwarePackage BuildPackageMetadata(
            UploadSlot slot, byte[] payload)
        {
            string suggestedId = slot.ExtractSuggestedId();
            return new SoftwarePackage(
                Id: string.IsNullOrEmpty(suggestedId)
                    ? $"upload-{slot.Handle}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"
                    : suggestedId,
                Version: string.Empty,
                Vendor: string.Empty,
                Description: "Uploaded via FileTransfer",
                SizeBytes: payload.LongLength,
                CreatedAt: DateTimeOffset.UtcNow,
                Hash: string.Empty);
        }

        private sealed class UploadSlot : IDisposable
        {
            private readonly Variant m_generateOptions;
            private readonly ILogger m_logger;
            private readonly MemoryStream m_buffer = new();
            private bool m_disposed;

            public UploadSlot(
                uint handle,
                NodeId? ownerSessionId,
                Variant generateOptions,
                ILogger logger)
            {
                Handle = handle;
                OwnerSessionId = ownerSessionId;
                m_generateOptions = generateOptions;
                m_logger = logger;
            }

            public uint Handle { get; }

            public NodeId? OwnerSessionId { get; }

            public FileState? FileObject { get; private set; }

            public NodeId FileNodeId =>
                FileObject?.NodeId ?? NodeId.Null;

            public bool IsOpen { get; private set; }

            public long BufferedBytes => m_buffer.Length;

            public void AttachFileState(
                DiNodeManager manager,
                TemporaryFileTransferState parent,
                OpenMethodStateMethodCallHandler openHandler,
                WriteMethodStateMethodCallHandler writeHandler,
                ReadMethodStateMethodCallHandler readHandler,
                CloseMethodStateMethodCallHandler closeHandler,
                GetPositionMethodStateMethodCallHandler getPositionHandler,
                SetPositionMethodStateMethodCallHandler setPositionHandler)
            {
                ServerSystemContext ctx = manager.SystemContext;
                var browseName = new QualifiedName(
                    $"UploadFile_{Handle}", manager.DiNamespaceIndex);

                FileState file = ctx.CreateInstanceOfFileType(parent, browseName);
                file.SymbolicName = browseName.Name ?? string.Empty;
                file.BrowseName = browseName;
                file.DisplayName = new LocalizedText(browseName.Name);
                file.NodeId = ctx.NodeIdFactory.New(ctx, file);
                file.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
                file.ModellingRuleId = NodeId.Null;

                if (file.Writable != null)

                {

                    file.Writable.Value = true;

                }
                if (file.UserWritable != null)
                {
                    file.UserWritable.Value = true;
                }
                if (file.Size != null)
                {
                    file.Size.Value = 0;
                }
                if (file.OpenCount != null)
                {
                    file.OpenCount.Value = 0;
                }
                if (file.MimeType != null)
                {
                    file.MimeType.Value = "application/octet-stream";
                }
                if (file.Open != null)

                {

                    file.Open.OnCall = openHandler;

                }
                if (file.Write != null)
                {
                    file.Write.OnCall = writeHandler;
                }
                if (file.Read != null)
                {
                    file.Read.OnCall = readHandler;
                }
                if (file.Close != null)
                {
                    file.Close.OnCall = closeHandler;
                }
                if (file.GetPosition != null)
                {
                    file.GetPosition.OnCall = getPositionHandler;
                }
                if (file.SetPosition != null)
                {
                    file.SetPosition.OnCall = setPositionHandler;
                }
                parent.AddChild(file);
                manager.AddPredefinedNodeAsync(file, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                FileObject = file;
            }

            public void DetachFromAddressSpace(DiNodeManager manager)
            {
                FileState? file = FileObject;
                if (file is null)
                {
                    return;
                }
                try
                {
                    manager.DeleteNodeAsync(manager.SystemContext, file.NodeId)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(
                        ex,
                        "Failed to delete transient upload FileState {NodeId}; will rely on dispose to release the buffer.",
                        file.NodeId);
                }
                FileObject = null;
            }

            public void OpenForWrite()
            {
                m_buffer.Position = 0;
                m_buffer.SetLength(0);
                IsOpen = true;
                if (FileObject?.OpenCount != null)
                {
                    FileObject.OpenCount.Value = 1;
                }
            }

            public void AppendWrite(ReadOnlySpan<byte> chunk)
            {
                byte[] copy = chunk.ToArray();
                m_buffer.Write(copy, 0, copy.Length);
                if (FileObject?.Size != null)
                {
                    FileObject.Size.Value = (ulong)m_buffer.Length;
                }
            }

            public void Seek(long position)
            {
                m_buffer.Position = position;
            }

            public void Close()
            {
                IsOpen = false;
                if (FileObject?.OpenCount != null)
                {
                    FileObject.OpenCount.Value = 0;
                }
            }

            public byte[] SnapshotPayload()
            {
                return m_buffer.ToArray();
            }

            public string ExtractSuggestedId()
            {
                // The OPC 10000-100 spec lets vendors pass a vendor-defined
                // generateOptions structure; we don't parse it for v1, but
                // expose a string passthrough so a calling application can
                // bake their package id directly into the request.
                if (m_generateOptions.TryGetValue(out string s) && !string.IsNullOrEmpty(s))
                {
                    return s;
                }
                return string.Empty;
            }

            public void Dispose()
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                IsOpen = false;
                m_buffer.Dispose();
            }
        }
    }
}
