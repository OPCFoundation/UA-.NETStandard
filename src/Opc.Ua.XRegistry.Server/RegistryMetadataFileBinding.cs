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
using System.Threading;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Serves read-only metadata FileType views of committed registry entities.
    /// </summary>
    /// <remarks>
    /// Open captures the committed JSON bytes, so every Read of a handle returns the revision that
    /// was current when it was opened. A handle belongs to the Session and authorization view that
    /// opened it; another Session or a changed view cannot use it. Writes are rejected: the JSON
    /// view is changed only through the registry's native or compatibility mutations.
    /// </remarks>
    public sealed class RegistryMetadataFileBinding : IDisposable
    {
        /// <summary>
        /// Creates a binding with bounded concurrent handles.
        /// </summary>
        public RegistryMetadataFileBinding(
            int maxHandlesPerSession = 8,
            int maxHandles = 256,
            ulong maxRetainedBytes = 64 * 1024 * 1024)
        {
            if (maxHandlesPerSession < 1 || maxHandles < maxHandlesPerSession ||
                maxHandles > ushort.MaxValue || maxRetainedBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHandlesPerSession));
            }
            m_maxHandlesPerSession = maxHandlesPerSession;
            m_maxHandles = maxHandles;
            m_maxRetainedBytes = maxRetainedBytes;
        }

        /// <summary>
        /// Wires the FileType Methods of <paramref name="file"/> to the committed document that
        /// <paramref name="source"/> returns. Call before the node is published.
        /// </summary>
        public void Bind(
            FileState file,
            Func<ByteString> source,
            Func<ISystemContext, RegistryAccessKind, ServiceResult> authorize)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (authorize is null)
            {
                throw new ArgumentNullException(nameof(authorize));
            }
            XRegistryProjectionEngine.SetValue(file.Writable, false);
            XRegistryProjectionEngine.SetValue(file.UserWritable, false);
            XRegistryProjectionEngine.SetValue(file.OpenCount, (ushort)0);
            file.Open!.OnCall = (ISystemContext context, MethodState _, NodeId _, byte mode, ref uint handle) =>
            {
                handle = 0;
                ServiceResult allowed = authorize(context, RegistryAccessKind.Read);
                if (ServiceResult.IsBad(allowed))
                {
                    return allowed;
                }
                if ((mode & kRead) == 0 || (mode & ~kRead) != 0)
                {
                    return StatusCodes.BadNotWritable;
                }
                ByteString bytes;
                try
                {
                    bytes = source();
                }
                catch (ServiceResultException error)
                {
                    return error.Result;
                }
                if (bytes.IsNull)
                {
                    return StatusCodes.BadInvalidState;
                }
                NodeId session = SessionOf(context);
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return StatusCodes.BadShutdown;
                    }
                    int owned = 0;
                    foreach (Handle open in m_handles.Values)
                    {
                        owned += open.Session == session ? 1 : 0;
                    }
                    if (owned >= m_maxHandlesPerSession || m_handles.Count >= m_maxHandles)
                    {
                        return StatusCodes.BadTooManyOperations;
                    }
                    if ((ulong)bytes.Length > m_maxRetainedBytes - m_retainedBytes)
                    {
                        return StatusCodes.BadResourceUnavailable;
                    }
                    do
                    {
                        handle = unchecked(++m_next);
                    }
                    while (handle == 0 || m_handles.ContainsKey(handle));
                    m_handles.Add(handle, new Handle(session, RegistryAccessPolicy.AuthorizationView(context),
                        ByteString.From(bytes.ToArray()), file));
                    m_retainedBytes += (ulong)bytes.Length;
                    UpdateOpenCount(file);
                }
                return ServiceResult.Good;
            };
            file.Read!.OnCall = (ISystemContext context, MethodState _, NodeId _, uint handle, int length,
                ref ByteString data) =>
            {
                data = default;
                ServiceResult allowed = authorize(context, RegistryAccessKind.Read);
                if (ServiceResult.IsBad(allowed))
                {
                    return allowed;
                }
                if (length < 0)
                {
                    return StatusCodes.BadInvalidArgument;
                }
                lock (m_gate)
                {
                    if (!TryFind(context, file, handle, out Handle? open))
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    int count = (int)Math.Min(Math.Min(length, kMaxReadBytes), open.Bytes.Length - open.Position);
                    data = ByteString.From(open.Bytes.Span.Slice((int)open.Position, count).ToArray());
                    open.Position += count;
                }
                return ServiceResult.Good;
            };
            file.Close!.OnCall = (ISystemContext context, MethodState _, NodeId _, uint handle) =>
            {
                lock (m_gate)
                {
                    if (!TryFind(context, file, handle, out _))
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    Remove(handle);
                }
                return ServiceResult.Good;
            };
            file.GetPosition!.OnCall = (ISystemContext context, MethodState _, NodeId _, uint handle,
                ref ulong position) =>
            {
                ServiceResult allowed = authorize(context, RegistryAccessKind.Read);
                if (ServiceResult.IsBad(allowed))
                {
                    return allowed;
                }
                lock (m_gate)
                {
                    if (!TryFind(context, file, handle, out Handle? open))
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    position = (ulong)open.Position;
                }
                return ServiceResult.Good;
            };
            file.SetPosition!.OnCall = (ISystemContext context, MethodState _, NodeId _, uint handle,
                ulong position) =>
            {
                ServiceResult allowed = authorize(context, RegistryAccessKind.Read);
                if (ServiceResult.IsBad(allowed))
                {
                    return allowed;
                }
                lock (m_gate)
                {
                    if (!TryFind(context, file, handle, out Handle? open))
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    open.Position = (long)Math.Min(position, (ulong)open.Bytes.Length);
                }
                return ServiceResult.Good;
            };
        }

        /// <summary>
        /// Releases the handles of a closed Session.
        /// </summary>
        public void ReleaseSession(NodeId session)
        {
            lock (m_gate)
            {
                var released = new List<uint>();
                foreach (KeyValuePair<uint, Handle> handle in m_handles)
                {
                    if (handle.Value.Session == session)
                    {
                        released.Add(handle.Key);
                    }
                }
                foreach (uint handle in released)
                {
                    Remove(handle);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_gate)
            {
                m_disposed = true;
                foreach (Handle handle in m_handles.Values)
                {
                    XRegistryProjectionEngine.SetValue(handle.File.OpenCount, (ushort)0);
                }
                m_handles.Clear();
                m_retainedBytes = 0;
            }
        }

        private void Remove(uint handle)
        {
            Handle open = m_handles[handle];
            m_handles.Remove(handle);
            m_retainedBytes -= (ulong)open.Bytes.Length;
            UpdateOpenCount(open.File);
        }

        private void UpdateOpenCount(FileState file)
        {
            ushort count = 0;
            foreach (Handle handle in m_handles.Values)
            {
                count += handle.File.NodeId == file.NodeId ? (ushort)1 : (ushort)0;
            }
            XRegistryProjectionEngine.SetValue(file.OpenCount, count);
        }

        private bool TryFind(
            ISystemContext context,
            FileState file,
            uint handle,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Handle? open)
        {
            if (m_handles.TryGetValue(handle, out open) && open.Session == SessionOf(context) &&
                open.File.NodeId == file.NodeId)
            {
                if (string.Equals(open.View, RegistryAccessPolicy.AuthorizationView(context), StringComparison.Ordinal))
                {
                    return true;
                }
                Remove(handle);
            }
            open = null;
            return false;
        }

        private static NodeId SessionOf(ISystemContext context)
        {
            return context is ISessionSystemContext { SessionId: { IsNull: false } id } ? id : NodeId.Null;
        }

        private sealed class Handle(NodeId session, string view, ByteString bytes, FileState file)
        {
            public NodeId Session { get; } = session;

            public string View { get; } = view;

            public ByteString Bytes { get; } = bytes;

            public FileState File { get; } = file;

            public long Position { get; set; }
        }

        private const byte kRead = 1;
        private const int kMaxReadBytes = 65536;
        private readonly Lock m_gate = new();
        private readonly Dictionary<uint, Handle> m_handles = [];
        private readonly int m_maxHandlesPerSession;
        private readonly int m_maxHandles;
        private readonly ulong m_maxRetainedBytes;
        private ulong m_retainedBytes;
        private bool m_disposed;
        private uint m_next;
    }
}
