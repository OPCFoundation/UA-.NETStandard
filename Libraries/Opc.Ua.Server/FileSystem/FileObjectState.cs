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
            : base(null)
        {
            ProviderPath = providerPath;

            TypeDefinitionId = ObjectTypeIds.FileType;
            SymbolicName = providerPath;
            NodeId = nodeId;
            BrowseName = new QualifiedName(displayName);
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

            Open = new OpenMethodState(this)
            {
                OnCall = OnOpen,
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
            return handle.Open(mode, out fileHandle);
        }

        private ServiceResult OnClose(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            return handle.Close(fileHandle)
                ? ServiceResult.Good
                : ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle could not be closed.");
        }

        private ServiceResult OnSetPosition(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, ulong position)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            Stream? stream = handle.GetStream(fileHandle);
            if (stream == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }
            stream.Position = (long)position;
            return ServiceResult.Good;
        }

        private ServiceResult OnGetPosition(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, ref ulong position)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            Stream? stream = handle.GetStream(fileHandle);
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
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            Stream? stream = handle.GetStream(fileHandle);
            if (stream == null)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "File handle not open.");
            }

            if (length < 0)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                    "Negative length.");
            }
            var buffer = new byte[length];
            int read = stream.Read(buffer, 0, length);
            if (read == length)
            {
                data = ByteString.From(buffer);
            }
            else
            {
                var trimmed = new byte[read];
                Array.Copy(buffer, 0, trimmed, 0, read);
                data = ByteString.From(trimmed);
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnWrite(ISystemContext context, MethodState method,
            NodeId objectId, uint fileHandle, ByteString data)
        {
            if (!TryGetHandle(context, out FileHandle? handle, out ServiceResult result))
            {
                return result;
            }
            Stream? stream = handle.GetStream(fileHandle);
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
            FileSystemNodeManager? manager = ResolveManager(context);
            if (manager == null)
            {
                return;
            }
            NodeId parentId = manager.GetParentNodeId(ProviderPath);
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
            FileSystemNodeManager? manager = ResolveManager(context);
            if (manager == null)
            {
                handle = null;
                result = ServiceResult.Create(
                    StatusCodes.BadInvalidState,
                    "Node manager unavailable.");
                return false;
            }

            handle = manager.GetOrCreateHandle(NodeId, ProviderPath);
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

        private static FileSystemNodeManager? ResolveManager(ISystemContext context)
        {
            return context?.SystemHandle as FileSystemNodeManager;
        }
    }
}
