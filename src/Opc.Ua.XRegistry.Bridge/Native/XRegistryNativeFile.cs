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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    internal sealed record XRegistryFileSnapshot(ByteString Bytes, XRegistryRequest? Baseline = null);

    internal sealed class XRegistryFileBudget
    {
        public XRegistryFileBudget(XRegistryBridgeNativeOptions options)
        {
            m_options = options;
        }

        public void ReserveHandle()
        {
            if (Interlocked.Increment(ref m_handles) > m_options.MaxOpenFiles)
            {
                Interlocked.Decrement(ref m_handles);
                throw new ServiceResultException(StatusCodes.BadTooManyOperations, "The open file limit was reached.");
            }
        }

        public void ReleaseHandle()
        {
            Interlocked.Decrement(ref m_handles);
        }

        public uint AllocateHandle()
        {
            long handle = Interlocked.Increment(ref m_nextHandle);
            if (handle > uint.MaxValue)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                    "The native handle identity space is exhausted; restart the endpoint.");
            }
            return (uint)handle;
        }

        public void ReserveBytes(int bytes)
        {
            if (Interlocked.Add(ref m_bytes, bytes) > m_options.MaxBufferedBytes)
            {
                Interlocked.Add(ref m_bytes, -bytes);
                throw new ServiceResultException(
                    StatusCodes.BadOutOfMemory, "The native file byte budget was reached.");
            }
        }

        public void ReleaseBytes(int bytes)
        {
            Interlocked.Add(ref m_bytes, -bytes);
        }

        private readonly XRegistryBridgeNativeOptions m_options;
        private long m_bytes;
        private long m_nextHandle;
        private int m_handles;
    }

    /// <summary>
    /// Bounded asynchronous FileType binding. Opening obtains an immutable snapshot;
    /// closing a changed write awaits the authoritative operation, never a background PUT.
    /// </summary>
    internal sealed class XRegistryNativeFile : IXRegistryProjectedResourceFile
    {
        public XRegistryNativeFile(
            FileState node,
            XRegistryBridgeNativeOptions options,
            XRegistryFileBudget budget,
            int maximumBytes,
            Func<ISystemContext, byte, CancellationToken, ValueTask<XRegistryFileSnapshot>> open,
            Func<ISystemContext, XRegistryFileSnapshot, ByteString,
                CancellationToken, ValueTask<ServiceResult>>? close,
            bool commitClean = false,
            bool mutatesEndpoint = false)
        {
            m_node = node;
            m_options = options;
            m_budget = budget;
            m_maximumBytes = maximumBytes;
            m_open = open;
            m_close = close;
            m_commitClean = commitClean;
            m_mutatesEndpoint = mutatesEndpoint;
            Bind();
        }

        public NodeId NodeId => m_node.NodeId;

        public void Bind()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            if (m_node.Open is null)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    $"The native file binding outlived its node: {m_node.NodeId}.");
            }
            m_node.Open!.OnCall = null;
            m_node.Open.OnCallAsync = null;
            m_node.Open.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 1 || !i[0].TryGetValue(out byte mode))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                uint handle = await OpenAsync(c, mode, ct).ConfigureAwait(false);
                output[0] = Variant.From(handle);
                return ServiceResult.Good;
            };
            m_node.Read!.OnCall = null;
            m_node.Read.OnCallAsync = null;
            m_node.Read.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 2 || !i[0].TryGetValue(out uint handle) || !i[1].TryGetValue(out int length))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                output[0] = Variant.From(await ReadAsync(c, handle, length, ct).ConfigureAwait(false));
                return ServiceResult.Good;
            };
            m_node.Write!.OnCall = null;
            m_node.Write.OnCallAsync = null;
            m_node.Write.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 2 || !i[0].TryGetValue(out uint handle) || !i[1].TryGetValue(out ByteString bytes))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                await WriteAsync(c, handle, bytes, ct).ConfigureAwait(false);
                return ServiceResult.Good;
            };
            m_node.Close!.OnCall = null;
            m_node.Close.OnCallAsync = null;
            m_node.Close.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 1 || !i[0].TryGetValue(out uint handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                return await CloseAsync(c, handle, ct).ConfigureAwait(false);
            };
            m_node.GetPosition!.OnCall = null;
            m_node.GetPosition.OnCallAsync = null;
            m_node.GetPosition.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 1 || !i[0].TryGetValue(out uint handle))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                output[0] = Variant.From(await PositionAsync(c, handle, null, ct).ConfigureAwait(false));
                return ServiceResult.Good;
            };
            m_node.SetPosition!.OnCall = null;
            m_node.SetPosition.OnCallAsync = null;
            m_node.SetPosition.OnCallMethod2Async = async (c, m, o, i, output, ct) =>
            {
                if (i.Count != 2 || !i[0].TryGetValue(out uint handle) || !i[1].TryGetValue(out ulong position))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                _ = await PositionAsync(c, handle, position, ct).ConfigureAwait(false);
                return ServiceResult.Good;
            };
            m_node.Writable!.Value = m_close is not null;
            m_node.UserWritable!.Value = m_close is not null;
        }

        public async ValueTask<uint> OpenAsync(ISystemContext context, byte mode, CancellationToken ct)
        {
            using OperationLease operation = BeginOperation();
            NodeId sessionId = SessionId(context);
            bool write = (mode & 2) != 0;
            if ((mode & ~15) != 0 ||
                (mode & 3) == 0 ||
                (!write && (mode & 12) != 0) ||
                (write && m_close is null))
            {
                throw new ServiceResultException(
                    write && m_close is null ? StatusCodes.BadNotWritable : StatusCodes.BadInvalidArgument);
            }
            await XRegistryNativeAuthorization.EnsureAsync(
                m_options, context, write && m_mutatesEndpoint, ct).ConfigureAwait(false);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                ExpireHandles();
                if (write &&
                    (m_handles.Values.Any(handle => handle.Write) ||
                        Volatile.Read(ref m_closingWriters) != 0))
                {
                    throw new ServiceResultException(StatusCodes.BadNotWritable, "The file already has a writer.");
                }
                uint id = m_budget.AllocateHandle();
                m_budget.ReserveHandle();
                XRegistryFileSnapshot snapshot;
                try
                {
                    snapshot = await m_open(context, mode, ct).ConfigureAwait(false);
                    if (snapshot.Bytes.IsNull || snapshot.Bytes.Length > m_maximumBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                    }
                }
                catch
                {
                    m_budget.ReleaseHandle();
                    throw;
                }
                int capacity = (mode & 4) != 0 ? 0 : snapshot.Bytes.Length;
                int reserved = checked(snapshot.Bytes.Length + capacity);
                try
                {
                    m_budget.ReserveBytes(reserved);
                }
                catch
                {
                    m_budget.ReleaseHandle();
                    throw;
                }
                var buffer = new MemoryStream(capacity);
                if (capacity != 0)
                {
                    XRegistryNativeBuffers.Write(buffer, snapshot.Bytes);
                }
                buffer.Position = (mode & 8) != 0 ? buffer.Length : 0;
                m_handles.Add(id, new Handle(
                    sessionId, m_options.ContextFactory(context), snapshot, buffer, write, (mode & 1) != 0,
                    m_options.TimeProvider.GetUtcNow(), reserved));
                m_node.OpenCount!.Value = checked((ushort)m_handles.Count);
                return id;
            }
            finally
            {
                m_gate.Release();
            }
        }

        public async ValueTask<ByteString> ReadAsync(
            ISystemContext context, uint id, int length, CancellationToken ct)
        {
            using OperationLease operation = BeginOperation();
            await XRegistryNativeAuthorization.EnsureAsync(m_options, context, false, ct).ConfigureAwait(false);
            if (length < 0 || length > m_options.ChunkSize)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange, "Read length exceeds the chunk limit.");
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Handle handle = GetHandle(context, id);
                if (!handle.Read)
                {
                    throw new ServiceResultException(StatusCodes.BadNotReadable);
                }
                int count = (int)Math.Min(length, handle.Buffer.Length - handle.Buffer.Position);
                byte[] bytes = new byte[count];
                _ = handle.Buffer.Read(bytes, 0, count);
                return ByteString.From(bytes);
            }
            finally
            {
                m_gate.Release();
            }
        }

        public async ValueTask WriteAsync(
            ISystemContext context, uint id, ByteString bytes, CancellationToken ct)
        {
            using OperationLease operation = BeginOperation();
            await XRegistryNativeAuthorization.EnsureAsync(
                m_options, context, m_mutatesEndpoint, ct).ConfigureAwait(false);
            if (bytes.IsNull || bytes.Length > m_options.ChunkSize)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Handle handle = GetHandle(context, id);
                if (!handle.Write)
                {
                    throw new ServiceResultException(StatusCodes.BadNotWritable);
                }
                long end = handle.Buffer.Position + bytes.Length;
                if (end > m_maximumBytes)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                if (end > handle.Buffer.Capacity)
                {
                    int additional = (int)end - handle.Buffer.Capacity;
                    m_budget.ReserveBytes(additional);
                    handle.ReservedBytes += additional;
                    handle.Buffer.Capacity = (int)end;
                }
                XRegistryNativeBuffers.Write(handle.Buffer, bytes);
            }
            finally
            {
                m_gate.Release();
            }
        }

        public async ValueTask<ServiceResult> CloseAsync(
            ISystemContext context, uint id, CancellationToken ct)
        {
            using OperationLease operation = BeginOperation();
            await XRegistryNativeAuthorization.EnsureAsync(m_options, context, false, ct).ConfigureAwait(false);
            Handle handle;
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                handle = GetHandle(context, id);
                m_handles.Remove(id);
                if (handle.Write)
                {
                    Interlocked.Increment(ref m_closingWriters);
                }
                m_node.OpenCount!.Value = checked((ushort)m_handles.Count);
            }
            finally
            {
                m_gate.Release();
            }
            try
            {
                var bytes = ByteString.From(handle.Buffer.ToArray());
                if (handle.Write &&
                    m_close is not null &&
                    (m_commitClean || !bytes.Span.SequenceEqual(handle.Snapshot.Bytes.Span)))
                {
                    await XRegistryNativeAuthorization.EnsureAsync(
                        m_options, context, m_mutatesEndpoint, ct).ConfigureAwait(false);
                    return await m_close(context, handle.Snapshot, bytes, ct).ConfigureAwait(false);
                }
                return ServiceResult.Good;
            }
            finally
            {
                Release(handle);
                if (handle.Write)
                {
                    Interlocked.Decrement(ref m_closingWriters);
                }
            }
        }

        public async ValueTask CloseSessionAsync(NodeId sessionId, CancellationToken ct)
        {
            using OperationLease operation = BeginOperation();
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (uint id in m_handles.Where(pair => pair.Value.SessionId == sessionId)
                    .Select(pair => pair.Key).ToArray())
                {
                    Release(m_handles[id]);
                    m_handles.Remove(id);
                }
                m_node.OpenCount!.Value = checked((ushort)m_handles.Count);
            }
            finally
            {
                m_gate.Release();
            }
        }

        public ServiceResult TryOpenWriteHandle(ISystemContext context, out uint fileHandle)
        {
            fileHandle = 0;
            return ServiceResult.Create(
                StatusCodes.BadNotSupported, "Remote write handles require the asynchronous native operation path.");
        }

        public void ApplyResource(IXRegistryProjectionResource resource)
        {
            m_node.MimeType?.Value = resource.ContentType;
        }

        public void Dispose()
        {
            bool cleanup;
            lock (m_lifetime)
            {
                Volatile.Write(ref m_disposed, 1);
                cleanup = ReserveCleanup();
            }
            if (cleanup)
            {
                CompleteDisposal();
            }
        }

        internal static NodeId SessionId(ISystemContext context)
        {
            return context is ISessionSystemContext { SessionId: { IsNull: false } sessionId }
                ? sessionId
                : throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
        }

        internal static bool SameCaller(XRegistryCallContext first, XRegistryCallContext second)
        {
            return first.Subject == second.Subject &&
                first.Authority == second.Authority &&
                first.IsAuthenticated == second.IsAuthenticated;
        }

        private void CompleteDisposal()
        {
            foreach (Handle handle in m_handles.Values)
            {
                Release(handle);
            }
            m_handles.Clear();
            m_gate.Dispose();
        }

        private async ValueTask<ulong> PositionAsync(
            ISystemContext context, uint id, ulong? position, CancellationToken ct)
        {
            using OperationLease operation = BeginOperation();
            await XRegistryNativeAuthorization.EnsureAsync(m_options, context, false, ct).ConfigureAwait(false);
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Handle handle = GetHandle(context, id);
                if (position.HasValue)
                {
                    handle.Buffer.Position = (long)Math.Min(position.Value, (ulong)handle.Buffer.Length);
                }
                return (ulong)handle.Buffer.Position;
            }
            finally
            {
                m_gate.Release();
            }
        }

        private Handle GetHandle(ISystemContext context, uint id)
        {
            ThrowIfDisposed();
            ExpireHandles();
            if (!m_handles.TryGetValue(id, out Handle? handle) ||
                handle.SessionId != SessionId(context) ||
                !SameCaller(handle.Caller, m_options.ContextFactory(context)))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "The file handle is not owned.");
            }
            return handle;
        }

        private void ExpireHandles()
        {
            DateTimeOffset now = m_options.TimeProvider.GetUtcNow();
            foreach (uint id in m_handles
                .Where(pair => now - pair.Value.Opened >= m_options.FileLifetime).Select(pair => pair.Key).ToArray())
            {
                Release(m_handles[id]);
                m_handles.Remove(id);
            }
            m_node.OpenCount!.Value = checked((ushort)m_handles.Count);
        }

        private void Release(Handle handle)
        {
            handle.Buffer.Dispose();
            m_budget.ReleaseBytes(handle.ReservedBytes);
            m_budget.ReleaseHandle();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "The file incarnation was retired.");
            }
        }

        private OperationLease BeginOperation()
        {
            lock (m_lifetime)
            {
                ThrowIfDisposed();
                m_activeOperations++;
                return new OperationLease(this);
            }
        }

        private void EndOperation()
        {
            bool cleanup;
            lock (m_lifetime)
            {
                m_activeOperations--;
                cleanup = ReserveCleanup();
            }
            if (cleanup)
            {
                CompleteDisposal();
            }
        }

        private bool ReserveCleanup()
        {
            if (m_disposed == 0 || m_activeOperations != 0 || m_cleanupStarted)
            {
                return false;
            }
            m_cleanupStarted = true;
            return true;
        }

        private readonly struct OperationLease(XRegistryNativeFile owner) : IDisposable
        {
            public void Dispose()
            {
                owner.EndOperation();
            }
        }

        private sealed class Handle(
            NodeId sessionId,
            XRegistryCallContext caller,
            XRegistryFileSnapshot snapshot,
            MemoryStream buffer,
            bool write,
            bool read,
            DateTimeOffset opened,
            int reservedBytes)
        {
            public NodeId SessionId { get; } = sessionId;
            public XRegistryCallContext Caller { get; } = caller;
            public XRegistryFileSnapshot Snapshot { get; } = snapshot;
            public MemoryStream Buffer { get; } = buffer;
            public bool Write { get; } = write;
            public bool Read { get; } = read;
            public DateTimeOffset Opened { get; } = opened;
            public int ReservedBytes { get; set; } = reservedBytes;
        }

        private readonly FileState m_node;
        private readonly XRegistryBridgeNativeOptions m_options;
        private readonly XRegistryFileBudget m_budget;
        private readonly int m_maximumBytes;
        private readonly Func<ISystemContext, byte, CancellationToken, ValueTask<XRegistryFileSnapshot>> m_open;

        private readonly Func<ISystemContext, XRegistryFileSnapshot, ByteString,
            CancellationToken, ValueTask<ServiceResult>>? m_close;

        private readonly bool m_commitClean;
        private readonly bool m_mutatesEndpoint;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Lock m_lifetime = new();
        private readonly Dictionary<uint, Handle> m_handles = [];
        private int m_disposed;
        private int m_closingWriters;
        private int m_activeOperations;
        private bool m_cleanupStarted;
    }
}
