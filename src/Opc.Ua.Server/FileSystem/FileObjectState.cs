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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Address-space representation of a single file in an
    /// <see cref="IFileSystemProvider"/>. Hangs the FileType
    /// metadata properties (Size, Writable, LastModifiedTime, …)
    /// and the FileType methods (Open / Read / Write / Close /
    /// SetPosition / GetPosition) on top of an underlying
    /// <see cref="FileHandle"/> obtained from the owning
    /// <see cref="FileSystemNodeManager"/>.
    /// </summary>
    internal sealed class FileObjectState : FileState
    {
        public FileObjectState(
            ISystemContext context,
            NodeId nodeId,
            string providerPath,
            string displayName)
            : this(context, nodeId, providerPath, displayName, host: null)
        {
        }

        /// <summary>
        /// Creates a provider-backed file node with metadata, read limits and FileType method handlers.
        /// </summary>
        public FileObjectState(
            ISystemContext context,
            NodeId nodeId,
            string providerPath,
            string displayName,
            IFileSystemHost? host)
            : base(null)
        {
            m_host = host;
            ProviderPath = providerPath;

            TypeDefinitionId = ObjectTypeIds.FileType;
            SymbolicName = providerPath;
            NodeId = nodeId;
            BrowseName = new QualifiedName(displayName, nodeId.NamespaceIndex);
            DisplayName = new LocalizedText(displayName);
            Description = LocalizedText.Null;
            WriteMask = 0;
            UserWriteMask = 0;
            EventNotifier = EventNotifiers.None;

            OpenCount = PropertyState<ushort>.With<VariantBuilder>(this);
            OpenCount.OnReadValue += OnOpenCount;
            OpenCount.AccessLevel = AccessLevels.CurrentRead;
            OpenCount.UserAccessLevel = AccessLevels.CurrentRead;
            OpenCount.Create(context, VariableIds.FileType_OpenCount,
                new QualifiedName(BrowseNames.OpenCount),
                new LocalizedText(BrowseNames.OpenCount), true);

            Writable = PropertyState<bool>.With<VariantBuilder>(this);
            Writable.OnReadValueAsync += OnReadMetadataAsync;
            Writable.AccessLevel = AccessLevels.CurrentRead;
            Writable.UserAccessLevel = AccessLevels.CurrentRead;
            Writable.Create(context, VariableIds.FileType_Writable,
                new QualifiedName(BrowseNames.Writable),
                new LocalizedText(BrowseNames.Writable), true);

            UserWritable = PropertyState<bool>.With<VariantBuilder>(this);
            UserWritable.OnReadValueAsync += OnReadMetadataAsync;
            UserWritable.AccessLevel = AccessLevels.CurrentRead;
            UserWritable.UserAccessLevel = AccessLevels.CurrentRead;
            UserWritable.Create(context, VariableIds.FileType_UserWritable,
                new QualifiedName(BrowseNames.UserWritable),
                new LocalizedText(BrowseNames.UserWritable), true);

            Size = PropertyState<ulong>.With<VariantBuilder>(this);
            Size.OnReadValueAsync += OnReadMetadataAsync;
            Size.AccessLevel = AccessLevels.CurrentRead;
            Size.UserAccessLevel = AccessLevels.CurrentRead;
            Size.Create(context, VariableIds.FileType_Size,
                new QualifiedName(BrowseNames.Size),
                new LocalizedText(BrowseNames.Size), true);

            MimeType = PropertyState<string>.With<VariantBuilder>(this);
            MimeType.OnReadValueAsync += OnReadMetadataAsync;
            MimeType.AccessLevel = AccessLevels.CurrentRead;
            MimeType.UserAccessLevel = AccessLevels.CurrentRead;
            MimeType.Create(context, VariableIds.FileType_MimeType,
                new QualifiedName(BrowseNames.MimeType),
                new LocalizedText(BrowseNames.MimeType), true);

            LastModifiedTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(this);
            LastModifiedTime.OnReadValueAsync += OnReadMetadataAsync;
            LastModifiedTime.AccessLevel = AccessLevels.CurrentRead;
            LastModifiedTime.UserAccessLevel = AccessLevels.CurrentRead;
            LastModifiedTime.Create(context, VariableIds.FileType_LastModifiedTime,
                new QualifiedName(BrowseNames.LastModifiedTime),
                new LocalizedText(BrowseNames.LastModifiedTime), true);

            MaxByteStringLength = PropertyState<uint>.With<VariantBuilder>(this);
            MaxByteStringLength.AccessLevel = AccessLevels.CurrentRead;
            MaxByteStringLength.UserAccessLevel = AccessLevels.CurrentRead;
            MaxByteStringLength.Create(context, VariableIds.FileType_MaxByteStringLength,
                new QualifiedName(BrowseNames.MaxByteStringLength),
                new LocalizedText(BrowseNames.MaxByteStringLength), true);
            MaxByteStringLength.Value = (uint)GetServerReadLimit(context);

            Open = new OpenMethodState(this)
            {
                OnCall = OnOpen,
                OnCallAsync = OnOpenAsync,
                Executable = true,
                UserExecutable = true
            };
            Open.Create(context, MethodIds.FileType_Open,
                new QualifiedName(BrowseNames.Open),
                new LocalizedText(BrowseNames.Open), false);
            Open.MethodDeclarationId = MethodIds.FileType_Open;

            Write = new WriteMethodState(this)
            {
                OnCall = OnWrite,
                OnCallAsync = OnWriteAsync,
                OnReadUserExecutable = OnReadWriteUserExecutable,
                Executable = true,
                UserExecutable = true
            };
            Write.Create(context, MethodIds.FileType_Write,
                new QualifiedName(BrowseNames.Write),
                new LocalizedText(BrowseNames.Write), false);
            Write.MethodDeclarationId = MethodIds.FileType_Write;

            Read = new ReadMethodState(this)
            {
                OnCall = OnRead,
                OnCallAsync = OnReadAsync,
                Executable = true,
                UserExecutable = true
            };
            Read.Create(context, MethodIds.FileType_Read,
                new QualifiedName(BrowseNames.Read),
                new LocalizedText(BrowseNames.Read), false);
            Read.MethodDeclarationId = MethodIds.FileType_Read;

            Close = new CloseMethodState(this)
            {
                OnCall = OnClose,
                OnCallAsync = OnCloseAsync,
                Executable = true,
                UserExecutable = true
            };
            Close.Create(context, MethodIds.FileType_Close,
                new QualifiedName(BrowseNames.Close),
                new LocalizedText(BrowseNames.Close), false);
            Close.MethodDeclarationId = MethodIds.FileType_Close;

            GetPosition = new GetPositionMethodState(this)
            {
                OnCall = OnGetPosition,
                OnCallAsync = OnGetPositionAsync,
                Executable = true,
                UserExecutable = true
            };
            GetPosition.Create(context, MethodIds.FileType_GetPosition,
                new QualifiedName(BrowseNames.GetPosition),
                new LocalizedText(BrowseNames.GetPosition), false);
            GetPosition.MethodDeclarationId = MethodIds.FileType_GetPosition;

            SetPosition = new SetPositionMethodState(this)
            {
                OnCall = OnSetPosition,
                OnCallAsync = OnSetPositionAsync,
                Executable = true,
                UserExecutable = true
            };
            SetPosition.Create(context, MethodIds.FileType_SetPosition,
                new QualifiedName(BrowseNames.SetPosition),
                new LocalizedText(BrowseNames.SetPosition), false);
            SetPosition.MethodDeclarationId = MethodIds.FileType_SetPosition;
        }

        public string ProviderPath { get; }

        private async ValueTask<AttributeReadResult> OnReadMetadataAsync(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            CancellationToken cancellationToken)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return new AttributeReadResult(
                    StatusCodes.BadInvalidState, Variant.Null, StatusCodes.BadInvalidState, DateTimeUtc.Now);
            }
            FileSystemEntry? entry = await host.Provider.GetEntryAsync(ProviderPath, cancellationToken)
                .ConfigureAwait(false);
            if (!entry.HasValue || entry.Value.IsDirectory)
            {
                return new AttributeReadResult(
                    StatusCodes.BadNodeIdUnknown, Variant.Null, StatusCodes.BadNodeIdUnknown, DateTimeUtc.Now);
            }

            Variant value = node.BrowseName.Name switch
            {
                BrowseNames.Size => new Variant((ulong)entry.Value.Length),
                BrowseNames.LastModifiedTime => new Variant((DateTimeUtc)entry.Value.LastModifiedUtc),
                BrowseNames.MimeType => new Variant(entry.Value.MimeType),
                BrowseNames.Writable =>
                    new Variant(host.Provider.IsWritable && entry.Value.IsWritable),
                BrowseNames.UserWritable =>
                    new Variant(host.Provider.IsWritable && entry.Value.IsWritable && host.CanUserWrite(context)),
                _ => throw new ServiceResultException(StatusCodes.BadAttributeIdInvalid)
            };
            return new AttributeReadResult(ServiceResult.Good, value,
                node == MimeType ? StatusCodes.Uncertain : StatusCodes.Good, DateTimeUtc.Now);
        }

        private ServiceResult OnOpenCount(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "Node manager unavailable.");
            }
            value = new Variant(host.FindHandle(ProviderPath)?.OpenCount ?? 0);
            timestamp = DateTimeUtc.Now;
            statusCode = StatusCodes.Good;
            return ServiceResult.Good;
        }

        private ServiceResult OnOpen(ISystemContext context, MethodState method,
            NodeId objectId, byte mode, ref uint fileHandle)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState, "Node manager unavailable.");
            }
            if (!FileSystemNodeManager.TryGetSessionId(
                    context,
                    out NodeId sessionId,
                    out ServiceResult result))
            {
                return result;
            }
            if (!CanOpen(host, context, mode, out result))
            {
                return result;
            }
            while (TryGetHandle(context, out FileHandle? handle, out result, create: true))
            {
                try
                {
                    result = handle.Open(sessionId, mode, out fileHandle);
                    if (result.StatusCode != StatusCodes.BadShutdown)
                    {
                        return result;
                    }
                }
                finally
                {
                    host.ReleaseHandle(handle);
                }
            }
            return result;
        }

        private ServiceResult OnClose(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle)
        {
            if (!FileSystemNodeManager.TryGetSessionId(
                    context,
                    out NodeId sessionId,
                    out ServiceResult result) ||
                !TryGetHandle(context, out FileHandle? handle, out result))
            {
                return result;
            }
            try
            {
                return handle.Close(sessionId, fileHandle)
                    ? ServiceResult.Good
                    : ServiceResult.Create(StatusCodes.BadInvalidState,
                        "File handle is invalid, belongs to another Session, or is already closed.");
            }
            finally
            {
                ResolveHost(context)!.ReleaseHandle(handle);
            }
        }

        /// <summary>
        /// Opens the file asynchronously for the calling session and returns its assigned handle.
        /// </summary>
        private async ValueTask<OpenMethodStateResult> OnOpenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            CancellationToken cancellationToken)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return new OpenMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
            }
            if (!FileSystemNodeManager.TryGetSessionId(
                context, out NodeId sessionId, out ServiceResult result) ||
                !CanOpen(host, context, mode, out result))
            {
                return new OpenMethodStateResult { ServiceResult = result };
            }
            (ServiceResult error, uint fileHandle) = await FileSystemDirectoryOperations.OpenFileAsync(
                host, NodeId, ProviderPath, sessionId, mode, cancellationToken).ConfigureAwait(false);
            return new OpenMethodStateResult { ServiceResult = error, FileHandle = fileHandle };
        }

        /// <summary>
        /// Sets the position of a session-owned stream without seeking past the current end of the file.
        /// </summary>
        private ServiceResult OnSetPosition(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, ulong position)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            if (!FileSystemNodeManager.TryGetSessionId(
                    context,
                    out NodeId sessionId,
                    out result))
            {
                return result;
            }
            using FileHandle.Operation? operation = handle.Acquire(sessionId, fileHandle, 0);
            if (operation == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }
            operation.Stream.Position = (long)Math.Min(position, (ulong)operation.Stream.Length);
            return ServiceResult.Good;
        }

        private ServiceResult OnGetPosition(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, ref ulong position)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            if (!FileSystemNodeManager.TryGetSessionId(
                    context,
                    out NodeId sessionId,
                    out result))
            {
                return result;
            }
            using FileHandle.Operation? operation = handle.Acquire(sessionId, fileHandle, 0);
            if (operation == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }
            position = (ulong)operation.Stream.Position;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads a bounded chunk from a session-owned readable stream for the synchronous FileType callback.
        /// </summary>
        private ServiceResult OnRead(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, int length, ref ByteString data)
        {
            ServiceResult result = PrepareRead(
                context, fileHandle, length, out FileHandle? handle, out NodeId session, out int count);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            using FileHandle.Operation? operation = handle!.Acquire(session, fileHandle, 1);
            if (operation == null)
            {
                return StatusCodes.BadInvalidState;
            }
            count = ClampToRemaining(operation.Stream, count);
            byte[] buffer = new byte[count];
            int read = operation.Stream.Read(buffer, 0, count);
            data = ByteString.From(read == count ? buffer : buffer.AsSpan(0, read).ToArray());
            return ServiceResult.Good;
        }

        /// <summary>
        /// Reads a bounded chunk asynchronously from a session-owned readable stream.
        /// </summary>
        private async ValueTask<ReadMethodStateResult> OnReadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            CancellationToken cancellationToken)
        {
            ServiceResult result = PrepareRead(
                context, fileHandle, length, out FileHandle? handle, out NodeId session, out int count);
            if (ServiceResult.IsBad(result))
            {
                return new ReadMethodStateResult { ServiceResult = result };
            }
            using FileHandle.Operation? operation = await handle!.AcquireAsync(
                session, fileHandle, 1, cancellationToken).ConfigureAwait(false);
            if (operation == null)
            {
                return new ReadMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
            }
            count = ClampToRemaining(operation.Stream, count);
            byte[] buffer = new byte[count];
#if NET5_0_OR_GREATER
            int read = await operation.Stream.ReadAsync(buffer.AsMemory(0, count), operation.CancellationToken)
                .ConfigureAwait(false);
#else
            int read = await operation.Stream.ReadAsync(buffer, 0, count, operation.CancellationToken)
                .ConfigureAwait(false);
#endif
            return new ReadMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Data = ByteString.From(read == count ? buffer : buffer.AsSpan(0, read).ToArray())
            };
        }

        /// <summary>
        /// Validates read access and clamps the requested count to file and server encoding limits.
        /// </summary>
        private ServiceResult PrepareRead(
            ISystemContext context,
            uint fileHandle,
            int length,
            out FileHandle? handle,
            out NodeId sessionId,
            out int count)
        {
            sessionId = default;
            count = 0;
            if (!TryGetHandle(context, out handle, out ServiceResult result))
            {
                return result;
            }
            if (!FileSystemNodeManager.TryGetSessionId(
                    context,
                    out sessionId,
                    out result))
            {
                return result;
            }
            if (handle.GetStream(sessionId, fileHandle, 1) == null)
            {
                return StatusCodes.BadInvalidState;
            }
            if (length <= 0)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                    "File Read length must be positive.");
            }
            int limit = GetServerReadLimit(context);
            uint fileLimit = MaxByteStringLength?.Value ?? 0;
            if (fileLimit > 0)
            {
                limit = (int)Math.Min(limit, fileLimit);
            }
            if (limit <= 0)
            {
                return ServiceResult.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize cannot hold a File Read response.");
            }
            count = Math.Min(length, limit);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Limits a read buffer to the bytes left after the position of a seekable stream,
        /// so a read near the end of a file does not allocate the whole requested length.
        /// </summary>
        internal static int ClampToRemaining(Stream stream, int count)
        {
            if (!stream.CanSeek)
            {
                return count;
            }
            long remaining = stream.Length - stream.Position;
            return remaining <= 0 ? 0 : (int)Math.Min(count, remaining);
        }

        /// <summary>
        /// Calculates the read payload limit after accounting for ByteString limits and response overhead.
        /// </summary>
        internal static int GetServerReadLimit(ISystemContext context)
        {
            IServiceMessageContext? messageContext = (context as ServerSystemContext)?.Server.MessageContext;
            int limit = messageContext?.MaxByteStringLength ?? DefaultEncodingLimits.MaxByteStringLength;
            if (limit <= 0)
            {
                limit = DefaultEncodingLimits.MaxByteStringLength;
            }
            int messageLimit = messageContext?.MaxMessageSize ?? DefaultEncodingLimits.MaxMessageSize;
            if (messageLimit > 0)
            {
                // Binary CallResponse with one ByteString result and empty diagnostics.
                const int responseOverhead = 57;
                limit = Math.Min(limit, Math.Max(0, messageLimit - responseOverhead));
            }
            return limit;
        }

        /// <summary>
        /// Writes the supplied bytes only through a handle opened for writing by the calling session.
        /// </summary>
        private ServiceResult OnWrite(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, ByteString data)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            if (!FileSystemNodeManager.TryGetSessionId(
                    context,
                    out NodeId sessionId,
                    out result))
            {
                return result;
            }
            using FileHandle.Operation? operation = handle.Acquire(sessionId, fileHandle, 2);
            if (operation == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }
            byte[] bytes = data.ToArray();
            operation.Stream.Write(bytes, 0, bytes.Length);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Closes admission and drains all previously admitted stream operations.
        /// </summary>
        private async ValueTask<CloseMethodStateResult> OnCloseAsync(
            ISystemContext context, MethodState method, NodeId objectId, uint fileHandle, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result) ||
                !FileSystemNodeManager.TryGetSessionId(context, out NodeId session, out result))
            {
                return new CloseMethodStateResult { ServiceResult = result };
            }
            try
            {
                bool closed = await handle.CloseAsync(session, fileHandle).ConfigureAwait(false);
                return new CloseMethodStateResult
                {
                    ServiceResult = closed ? ServiceResult.Good : StatusCodes.BadInvalidState
                };
            }
            finally
            {
                ResolveHost(context)!.ReleaseHandle(handle);
            }
        }

        /// <summary>
        /// Writes using the handle's serialized cursor and linked lifetime cancellation.
        /// </summary>
        private async ValueTask<WriteMethodStateResult> OnWriteAsync(
            ISystemContext context, MethodState method, NodeId objectId, uint fileHandle,
            ByteString data, CancellationToken ct)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result) ||
                !FileSystemNodeManager.TryGetSessionId(context, out NodeId session, out result))
            {
                return new WriteMethodStateResult { ServiceResult = result };
            }
            using FileHandle.Operation? operation =
                await handle.AcquireAsync(session, fileHandle, 2, ct).ConfigureAwait(false);
            if (operation == null)
            {
                return new WriteMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
            }
            byte[] buffer = data.ToArray();
#if NET5_0_OR_GREATER
            await operation.Stream.WriteAsync(buffer.AsMemory(), operation.CancellationToken).ConfigureAwait(false);
#else
            await operation.Stream.WriteAsync(buffer, 0, buffer.Length, operation.CancellationToken)
                .ConfigureAwait(false);
#endif
            return new WriteMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <summary>
        /// Reads the position in the same serialized operation order as reads and writes.
        /// </summary>
        private async ValueTask<GetPositionMethodStateResult> OnGetPositionAsync(
            ISystemContext context, MethodState method, NodeId objectId, uint fileHandle, CancellationToken ct)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result) ||
                !FileSystemNodeManager.TryGetSessionId(context, out NodeId session, out result))
            {
                return new GetPositionMethodStateResult { ServiceResult = result };
            }
            using FileHandle.Operation? operation =
                await handle.AcquireAsync(session, fileHandle, 0, ct).ConfigureAwait(false);
            return operation == null
                ? new GetPositionMethodStateResult { ServiceResult = StatusCodes.BadInvalidState }
                : new GetPositionMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Position = (ulong)operation.Stream.Position
                };
        }

        /// <summary>
        /// Seeks in the same serialized operation order as reads and writes.
        /// </summary>
        private async ValueTask<SetPositionMethodStateResult> OnSetPositionAsync(
            ISystemContext context, MethodState method, NodeId objectId, uint fileHandle,
            ulong position, CancellationToken ct)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result) ||
                !FileSystemNodeManager.TryGetSessionId(context, out NodeId session, out result))
            {
                return new SetPositionMethodStateResult { ServiceResult = result };
            }
            using FileHandle.Operation? operation =
                await handle.AcquireAsync(session, fileHandle, 0, ct).ConfigureAwait(false);
            if (operation == null)
            {
                return new SetPositionMethodStateResult { ServiceResult = StatusCodes.BadInvalidState };
            }
            Stream stream = operation.Stream;
            stream.Position = (long)Math.Min(position, (ulong)stream.Length);
            return new SetPositionMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            if (!browser.IsRequired(ReferenceTypeIds.HasComponent, true))
            {
                return;
            }

            // Reverse reference to the parent directory.
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return;
            }
            NodeId parentId = host.GetParentNodeId(ProviderPath);
            if (!parentId.IsNull)
            {
                browser.Add(ReferenceTypeIds.HasComponent, true, parentId);
            }
        }

        private bool TryGetHandle(
            ISystemContext context,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FileHandle? handle,
            out ServiceResult result,
            bool create = false)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                handle = null;
                result = ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "Node manager unavailable.");
                return false;
            }

            handle = create ? host.GetOrCreateHandle(NodeId, ProviderPath) : host.FindHandle(ProviderPath);
            if (handle == null)
            {
                result = ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "File handle unavailable.");
                return false;
            }
            result = ServiceResult.Good;
            return true;
        }

        /// <summary>
        /// Disconnects metadata and method callbacks when the materialized file node is retired.
        /// </summary>
        internal void DetachCallbacks()
        {
            OpenCount?.OnReadValue -= OnOpenCount;
            Writable?.OnReadValueAsync -= OnReadMetadataAsync;
            UserWritable?.OnReadValueAsync -= OnReadMetadataAsync;
            Size?.OnReadValueAsync -= OnReadMetadataAsync;
            MimeType?.OnReadValueAsync -= OnReadMetadataAsync;
            LastModifiedTime?.OnReadValueAsync -= OnReadMetadataAsync;
            if (Open != null)
            {
                Open.OnCall = null;
                Open.OnCallAsync = null;
            }
            if (Write != null)
            {
                Write.OnCall = null;
                Write.OnCallAsync = null;
            }
            if (Read != null)
            {
                Read.OnCall = null;
                Read.OnCallAsync = null;
            }
            if (Close != null)
            {
                Close.OnCall = null;
                Close.OnCallAsync = null;
            }
            if (GetPosition != null)
            {
                GetPosition.OnCall = null;
                GetPosition.OnCallAsync = null;
            }
            if (SetPosition != null)
            {
                SetPosition.OnCall = null;
                SetPosition.OnCallAsync = null;
            }
        }

        /// <summary>
        /// Rejects an open for writing by a user who may not modify the hosted file system.
        /// </summary>
        private static bool CanOpen(IFileSystemHost host, ISystemContext context, byte mode, out ServiceResult result)
        {
            if ((mode & 0x2) != 0 && !host.CanUserWrite(context))
            {
                result = ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                    "The user is not allowed to open the file for writing.");
                return false;
            }
            result = ServiceResult.Good;
            return true;
        }

        /// <summary>
        /// Reports the Write method as executable only for users who may modify the hosted file system.
        /// </summary>
        private ServiceResult OnReadWriteUserExecutable(ISystemContext context, NodeState node, ref bool value)
        {
            IFileSystemHost? host = ResolveHost(context);
            value = host != null && host.Provider.IsWritable && host.CanUserWrite(context);
            return ServiceResult.Good;
        }

        private IFileSystemHost? ResolveHost(ISystemContext context)
        {
            return m_host ?? context?.SystemHandle as IFileSystemHost;
        }

        private readonly IFileSystemHost? m_host;
    }
}
