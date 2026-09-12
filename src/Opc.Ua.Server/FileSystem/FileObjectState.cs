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
        public string ProviderPath { get; }

        public FileObjectState(
            ISystemContext context,
            NodeId nodeId,
            string providerPath,
            string displayName)
            : this(context, nodeId, providerPath, displayName, host: null)
        {
        }

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
            Writable.OnReadValue += OnWritable;
            Writable.AccessLevel = AccessLevels.CurrentRead;
            Writable.UserAccessLevel = AccessLevels.CurrentRead;
            Writable.Create(context, VariableIds.FileType_Writable,
                new QualifiedName(BrowseNames.Writable),
                new LocalizedText(BrowseNames.Writable), true);

            UserWritable = PropertyState<bool>.With<VariantBuilder>(this);
            UserWritable.OnReadValue += OnWritable;
            UserWritable.AccessLevel = AccessLevels.CurrentRead;
            UserWritable.UserAccessLevel = AccessLevels.CurrentRead;
            UserWritable.Create(context, VariableIds.FileType_UserWritable,
                new QualifiedName(BrowseNames.UserWritable),
                new LocalizedText(BrowseNames.UserWritable), true);

            Size = PropertyState<ulong>.With<VariantBuilder>(this);
            Size.OnReadValue += OnSize;
            Size.AccessLevel = AccessLevels.CurrentRead;
            Size.UserAccessLevel = AccessLevels.CurrentRead;
            Size.Create(context, VariableIds.FileType_Size,
                new QualifiedName(BrowseNames.Size),
                new LocalizedText(BrowseNames.Size), true);

            MimeType = PropertyState<string>.With<VariantBuilder>(this);
            MimeType.OnReadValue += OnMimeType;
            MimeType.AccessLevel = AccessLevels.CurrentRead;
            MimeType.UserAccessLevel = AccessLevels.CurrentRead;
            MimeType.Create(context, VariableIds.FileType_MimeType,
                new QualifiedName(BrowseNames.MimeType),
                new LocalizedText(BrowseNames.MimeType), true);

            LastModifiedTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(this);
            LastModifiedTime.OnReadValue += OnLastModifiedTime;
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
                Executable = true,
                UserExecutable = true
            };
            SetPosition.Create(context, MethodIds.FileType_SetPosition,
                new QualifiedName(BrowseNames.SetPosition),
                new LocalizedText(BrowseNames.SetPosition), false);
            SetPosition.MethodDeclarationId = MethodIds.FileType_SetPosition;
        }

        private ServiceResult OnMimeType(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            value = new Variant(handle.MimeType);
            timestamp = DateTimeUtc.Now;
            statusCode = StatusCodes.Uncertain;
            return ServiceResult.Good;
        }

        private ServiceResult OnLastModifiedTime(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            value = new Variant((DateTimeUtc)handle.LastModifiedTime);
            timestamp = DateTimeUtc.Now;
            statusCode = StatusCodes.Good;
            return ServiceResult.Good;
        }

        private ServiceResult OnWritable(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            value = new Variant(handle.IsWriteable);
            timestamp = DateTimeUtc.Now;
            statusCode = StatusCodes.Good;
            return ServiceResult.Good;
        }

        private ServiceResult OnSize(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            value = new Variant((ulong)handle.Length);
            timestamp = DateTimeUtc.Now;
            statusCode = StatusCodes.Good;
            return ServiceResult.Good;
        }

        private ServiceResult OnOpenCount(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            value = new Variant(handle.OpenCount);
            timestamp = DateTimeUtc.Now;
            statusCode = StatusCodes.Good;
            return ServiceResult.Good;
        }

        private ServiceResult OnOpen(ISystemContext context, MethodState method,
            NodeId objectId, byte mode, ref uint fileHandle)
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
            return handle.Open(sessionId, mode, out fileHandle);
        }

        private ServiceResult OnClose(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle)
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
            return handle.Close(sessionId, fileHandle)
                ? ServiceResult.Good
                : ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle is invalid, belongs to another Session, or is already closed.");
        }

        private async ValueTask<OpenMethodStateResult> OnOpenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            CancellationToken cancellationToken)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result) ||
                !FileSystemNodeManager.TryGetSessionId(context, out NodeId sessionId, out result))
            {
                return new OpenMethodStateResult { ServiceResult = result };
            }
            (ServiceResult error, uint fileHandle) = await handle.OpenAsync(sessionId, mode, cancellationToken)
                .ConfigureAwait(false);
            return new OpenMethodStateResult { ServiceResult = error, FileHandle = fileHandle };
        }

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
            Stream? stream = handle.GetStream(sessionId, fileHandle);
            if (stream == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }
            stream.Position = (long)Math.Min(position, (ulong)stream.Length);
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
            Stream? stream = handle.GetStream(sessionId, fileHandle);
            if (stream == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }
            position = (ulong)stream.Position;
            return ServiceResult.Good;
        }

        private ServiceResult OnRead(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, int length, ref ByteString data)
        {
            ServiceResult result = PrepareRead(context, fileHandle, length, out Stream? stream, out int count);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }
            byte[] buffer = new byte[count];
            int read = stream!.Read(buffer, 0, count);
            data = ByteString.From(read == count ? buffer : buffer.AsSpan(0, read).ToArray());
            return ServiceResult.Good;
        }

        private async ValueTask<ReadMethodStateResult> OnReadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            CancellationToken cancellationToken)
        {
            ServiceResult result = PrepareRead(context, fileHandle, length, out Stream? stream, out int count);
            if (ServiceResult.IsBad(result))
            {
                return new ReadMethodStateResult { ServiceResult = result };
            }
            byte[] buffer = new byte[count];
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            int read = await stream!.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
#else
            int read = await stream!.ReadAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
#endif
            return new ReadMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Data = ByteString.From(read == count ? buffer : buffer.AsSpan(0, read).ToArray())
            };
        }

        private ServiceResult PrepareRead(
            ISystemContext context,
            uint fileHandle,
            int length,
            out Stream? stream,
            out int count)
        {
            stream = null;
            count = 0;
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
            stream = handle.GetStream(sessionId, fileHandle, requiredMode: 1);
            if (stream == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
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
                limit = (int)Math.Min(limit, (long)fileLimit);
            }
            if (limit <= 0)
            {
                return ServiceResult.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "MaxMessageSize cannot hold a File Read response.");
            }
            count = Math.Min(length, limit);
            return ServiceResult.Good;
        }

        private static int GetServerReadLimit(ISystemContext context)
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
            Stream? stream = handle.GetStream(sessionId, fileHandle, requiredMode: 2);
            if (stream == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }
            byte[] bytes = data.ToArray();
            stream.Write(bytes, 0, bytes.Length);
            return ServiceResult.Good;
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
            out ServiceResult result)
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

            handle = host.GetOrCreateHandle(NodeId, ProviderPath);
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

        internal void DetachCallbacks()
        {
            if (OpenCount != null)
            {
                OpenCount.OnReadValue -= OnOpenCount;
            }
            if (Writable != null)
            {
                Writable.OnReadValue -= OnWritable;
            }
            if (UserWritable != null)
            {
                UserWritable.OnReadValue -= OnWritable;
            }
            if (Size != null)
            {
                Size.OnReadValue -= OnSize;
            }
            if (MimeType != null)
            {
                MimeType.OnReadValue -= OnMimeType;
            }
            if (LastModifiedTime != null)
            {
                LastModifiedTime.OnReadValue -= OnLastModifiedTime;
            }
            if (Open != null)
            {
                Open.OnCall = null;
                Open.OnCallAsync = null;
            }
            if (Write != null)
            {
                Write.OnCall = null;
            }
            if (Read != null)
            {
                Read.OnCall = null;
                Read.OnCallAsync = null;
            }
            if (Close != null)
            {
                Close.OnCall = null;
            }
            if (GetPosition != null)
            {
                GetPosition.OnCall = null;
            }
            if (SetPosition != null)
            {
                SetPosition.OnCall = null;
            }
        }

        private IFileSystemHost? ResolveHost(ISystemContext context)
        {
            return m_host ?? context?.SystemHandle as IFileSystemHost;
        }

        private readonly IFileSystemHost? m_host;
    }
}
