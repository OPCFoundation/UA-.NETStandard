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

namespace Quickstarts.FileSystem
{
    using Opc.Ua;
    using System;
    using System.IO;

    /// <summary>
    /// A object which maps a segment to a UA object.
    /// </summary>
    public class FileObjectState : FileState
    {
        /// <summary>
        /// Gets the path to the file
        /// </summary>
        public string FullPath { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileObjectState"/> class.
        /// </summary>
        public FileObjectState(ISystemContext context, NodeId nodeId, string path)
            : base(null)
        {
            System.Diagnostics.Contracts.Contract.Assume(context != null);
            FullPath = path;

            string name = Path.GetFileName(path);
            TypeDefinitionId = ObjectTypeIds.FileType;
            SymbolicName = path;
            NodeId = nodeId;
            BrowseName = new QualifiedName(name, nodeId.NamespaceIndex);
            DisplayName = new LocalizedText(name);
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

            Write = new WriteMethodState(this)
            {
                OnCall = OnWrite,
                Executable = true,
                UserExecutable = true
            };
            Write.Create(context, MethodIds.FileType_Write,
                new QualifiedName(BrowseNames.Write),
                new LocalizedText(BrowseNames.Write), false);

            Read = new ReadMethodState(this)
            {
                OnCall = OnRead,
                Executable = true,
                UserExecutable = true
            };
            Read.Create(context, MethodIds.FileType_Read,
                new QualifiedName(BrowseNames.Read),
                new LocalizedText(BrowseNames.Read), false);

            Close = new CloseMethodState(this)
            {
                OnCall = OnClose,
                Executable = true,
                UserExecutable = true
            };
            Close.Create(context, MethodIds.FileType_Close,
                new QualifiedName(BrowseNames.Close),
                new LocalizedText(BrowseNames.Close), false);

            GetPosition = new GetPositionMethodState(this)
            {
                OnCall = OnGetPosition,
                Executable = true,
                UserExecutable = true
            };
            GetPosition.Create(context, MethodIds.FileType_GetPosition,
                new QualifiedName(BrowseNames.GetPosition),
                new LocalizedText(BrowseNames.GetPosition), false);

            SetPosition = new SetPositionMethodState(this)
            {
                OnCall = OnSetPosition,
                Executable = true,
                UserExecutable = true
            };
            SetPosition.Create(context, MethodIds.FileType_SetPosition,
                new QualifiedName(BrowseNames.SetPosition),
                new LocalizedText(BrowseNames.SetPosition), false);
        }

        private ServiceResult OnMimeType(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (GetFileHandle(context, NodeId, out FileHandle handle, out ServiceResult result))
            {
                value = new Variant(handle.MimeType);
                timestamp = DateTimeUtc.Now;
                statusCode = StatusCodes.Uncertain;
            }
            return result;
        }

        private ServiceResult OnLastModifiedTime(ISystemContext context,
            NodeState node, NumericRange indexRange, QualifiedName dataEncoding,
            ref Variant value, ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (GetFileHandle(context, NodeId, out FileHandle handle, out ServiceResult result))
            {
                value = new Variant((DateTimeUtc)handle.LastModifiedTime);
                timestamp = DateTimeUtc.Now;
                statusCode = StatusCodes.Good;
            }
            return result;
        }

        private ServiceResult OnWritable(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (GetFileHandle(context, NodeId, out FileHandle handle, out ServiceResult result))
            {
                value = new Variant(handle.IsWriteable);
                timestamp = DateTimeUtc.Now;
                statusCode = StatusCodes.Good;
            }
            return result;
        }

        private ServiceResult OnSize(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (GetFileHandle(context, NodeId, out FileHandle handle, out ServiceResult result))
            {
                value = new Variant((ulong)handle.Length);
                timestamp = DateTimeUtc.Now;
                statusCode = StatusCodes.Good;
            }
            return result;
        }

        private ServiceResult OnOpenCount(ISystemContext context, NodeState node,
            NumericRange indexRange, QualifiedName dataEncoding, ref Variant value,
            ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            if (GetFileHandle(context, NodeId, out FileHandle handle, out ServiceResult result))
            {
                value = new Variant(handle.OpenCount);
                timestamp = DateTimeUtc.Now;
                statusCode = StatusCodes.Good;
            }
            return result;
        }

        private ServiceResult OnOpen(ISystemContext _context, MethodState _method,
            NodeId _objectId, byte mode, ref uint fileHandle)
        {
            if (GetFileHandle(_context, _objectId, out FileHandle handle, out ServiceResult result))
            {
                result = handle.Open(mode, out fileHandle);
            }
            return result;
        }

        private ServiceResult OnClose(ISystemContext _context, MethodState _method,
            NodeId _objectId, uint fileHandle)
        {
            if (GetFileHandle(_context, _objectId, out FileHandle handle, out ServiceResult result)
                && !handle.Close(fileHandle))
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                   "File handle could not be closed.");
            }
            return result;
        }

        private ServiceResult OnSetPosition(ISystemContext _context, MethodState _method,
            NodeId _objectId, uint fileHandle, ulong position)
        {
            if (GetFileHandle(_context, _objectId, out FileHandle handle, out ServiceResult result))
            {
                Stream stream = handle.GetStream(fileHandle);
                if (stream == null)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                       "File handle not open.");
                }
                stream.Position = (long)position;
            }
            return result;
        }

        private ServiceResult OnGetPosition(ISystemContext _context,
            MethodState _method, NodeId _objectId, uint fileHandle, ref ulong position)
        {
            if (GetFileHandle(_context, _objectId, out FileHandle handle, out ServiceResult result))
            {
                Stream stream = handle.GetStream(fileHandle);
                if (stream == null)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                       "File handle not open.");
                }
                position = (ulong)stream.Position;
            }
            return result;
        }

        private ServiceResult OnRead(ISystemContext _context, MethodState _method,
            NodeId _objectId, uint fileHandle, int length, ref ByteString data)
        {
            if (GetFileHandle(_context, _objectId, out FileHandle handle, out ServiceResult result))
            {
                Stream stream = handle.GetStream(fileHandle);
                if (stream == null)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                       "File handle not open.");
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
            }
            return result;
        }

        private ServiceResult OnWrite(ISystemContext _context, MethodState _method,
            NodeId _objectId, uint fileHandle, ByteString data)
        {
            if (GetFileHandle(_context, _objectId, out FileHandle handle, out ServiceResult result))
            {
                Stream stream = handle.GetStream(fileHandle);
                if (stream == null)
                {
                    return StatusCodes.BadInvalidState;
                }
                byte[] bytes = data.ToArray();
                stream.Write(bytes, 0, bytes.Length);
            }
            return result;
        }

        /// <summary>
        /// Populates the browser with references that meet the criteria.
        /// </summary>
        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            // check if the parent segments need to be returned.
            if (browser.IsRequired(ReferenceTypeIds.HasComponent, true))
            {
                string directory = Path.GetDirectoryName(FullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    if (Path.GetPathRoot(FullPath) == directory)
                    {
                        browser.Add(ReferenceTypeIds.HasComponent, true,
                            ModelUtils.ConstructIdForVolume(directory, NodeId.NamespaceIndex));
                    }
                    else
                    {
                        browser.Add(ReferenceTypeIds.HasComponent, true,
                            ModelUtils.ConstructIdForDirectory(directory, NodeId.NamespaceIndex));
                    }
                }
            }
        }

        private static bool GetFileHandle(ISystemContext context, NodeId nodeId,
            out FileHandle handle, out ServiceResult result)
        {
            if (!(context.SystemHandle is FileSystem system) ||
               !(system.GetHandle(nodeId) is FileHandle h))
            {
                result = ServiceResult.Create(StatusCodes.BadInvalidState,
                    "Object is not a file.");
                handle = null;
                return false;
            }
            handle = h;
            result = ServiceResult.Good;
            return true;
        }
    }
}
