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
using System.Security.Cryptography;
using System.Threading;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Bounded native subtree reads over immutable, Session-bound document snapshots.
    /// The host supplies an already authorized document and a server-owned authorization-view identity.
    /// </summary>
    public sealed class RegistryNativeSnapshots : IDisposable
    {
        /// <summary>
        /// Creates a snapshot store for one registry instance.
        /// The retained-byte budget is measured by complete Binary-encoded document size.
        /// </summary>
        public RegistryNativeSnapshots(
            IServiceMessageContext context,
            RegistrySnapshotLimitsDataType? limits = null,
            TimeProvider? timeProvider = null,
            ulong maxRetainedBytes = 64 * 1024 * 1024)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            m_limits = limits is null ? new RegistrySnapshotLimitsDataType
            {
                MaxSnapshotsPerSession = 16,
                MaxContinuationPointsPerSession = 64,
                MaxSnapshotBytes = 16 * 1024 * 1024,
                MaxDepth = 128,
                MaxReadItems = 256,
                MaxReadBytes = 65536,
                SnapshotTimeout = 60000
            } : (RegistrySnapshotLimitsDataType)limits.Clone();
            if (m_limits.MaxSnapshotsPerSession == 0 || m_limits.MaxContinuationPointsPerSession == 0 ||
                m_limits.MaxSnapshotBytes == 0 || m_limits.MaxSnapshotBytes > int.MaxValue ||
                m_limits.MaxDepth == 0 || m_limits.MaxDepth > int.MaxValue ||
                m_limits.MaxReadItems == 0 || m_limits.MaxReadBytes == 0 ||
                m_limits.SnapshotTimeout <= 0 || double.IsNaN(m_limits.SnapshotTimeout) ||
                double.IsInfinity(m_limits.SnapshotTimeout) || maxRetainedBytes < m_limits.MaxSnapshotBytes)
            {
                throw new ArgumentException("Snapshot bounds must be finite, positive and internally consistent.",
                    nameof(limits));
            }
            m_context = new ServiceMessageContext(context, context.Telemetry)
            {
                MaxStringLength = (int)m_limits.MaxSnapshotBytes,
                MaxByteStringLength = (int)m_limits.MaxSnapshotBytes,
                MaxArrayLength = 100000,
                MaxMessageSize = (int)m_limits.MaxSnapshotBytes,
                MaxEncodingNestingLevels = (int)m_limits.MaxDepth
            };
            m_clock = timeProvider ?? TimeProvider.System;
            m_maxRetainedBytes = maxRetainedBytes;
        }

        /// <summary>
        /// Gets a copy of the advertised bounds.
        /// </summary>
        public RegistrySnapshotLimitsDataType Limits => (RegistrySnapshotLimitsDataType)m_limits.Clone();

        /// <summary>
        /// Pins an authorized native document and both revision spaces.
        /// Identity selection and authorization must be completed by the host before this call.
        /// </summary>
        public RegistrySnapshotOpenResultDataType Open(
            NodeId session,
            string authorizationView,
            RegistrySnapshotOpenRequestDataType request,
            IEncodeable document,
            uint targetEpoch,
            uint registryEpoch)
        {
            return Open(session, authorizationView, request, document, targetEpoch, registryEpoch, null);
        }

        /// <summary>
        /// Pins a document with a host-owned policy check for the retained selection.
        /// The callback must not mutate snapshots or perform asynchronous work.
        /// </summary>
        public RegistrySnapshotOpenResultDataType Open(
            NodeId session,
            string authorizationView,
            RegistrySnapshotOpenRequestDataType request,
            IEncodeable document,
            uint targetEpoch,
            uint registryEpoch,
            Func<ServiceResult>? reauthorize)
        {
            lock (m_gate)
            {
                ThrowIfDisposed();
                Cleanup();
                try
                {
                    if (session.IsNull || authorizationView is null || request is null || document is null ||
                        request.View > 1 || string.IsNullOrEmpty(request.DocumentKind) || request.TargetXid is null ||
                        targetEpoch == 0 || registryEpoch == 0)
                    {
                        throw new ArgumentException("An authorized Session, document selection and revisions are required.");
                    }
                    if (request.ExpectedEpoch != 0 && request.ExpectedEpoch != targetEpoch)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                    if (SessionCount(session) >= m_limits.MaxSnapshotsPerSession)
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                    ulong bytes = checked((ulong)Size(document, m_limits.MaxSnapshotBytes));
                    if (m_retainedBytes + bytes > m_maxRetainedBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                    }
                    ByteString id = Token();
                    var snapshot = new Snapshot(session, authorizationView, (IEncodeable)document.Clone(),
                        bytes, m_clock.GetTimestamp(), reauthorize);
                    m_snapshots.Add(id, snapshot);
                    m_retainedBytes += bytes;
                    return new RegistrySnapshotOpenResultDataType
                    {
                        StatusCode = StatusCodes.Good,
                        SnapshotId = id,
                        TargetEpoch = targetEpoch,
                        RegistryEpoch = registryEpoch,
                        Issues = []
                    };
                }
                catch (ArgumentException error)
                {
                    return OpenFailure(StatusCodes.BadInvalidArgument, error.Message);
                }
                catch (ServiceResultException error)
                {
                    return OpenFailure(error.StatusCode, error.Message);
                }
            }
        }

        /// <summary>
        /// Reads one native subtree or leaf range, preserving the pinned generation.
        /// A invalid continuation does not consume any other valid token.
        /// </summary>
        public RegistrySnapshotReadResultDataType Read(
            NodeId session,
            string authorizationView,
            RegistrySnapshotReadRequestDataType request)
        {
            lock (m_gate)
            {
                ThrowIfDisposed();
                Cleanup();
                try
                {
                    if (request is null || request.MaxItems == 0 || request.MaxItems > m_limits.MaxReadItems ||
                        request.MaxBytes == 0 || request.MaxBytes > m_limits.MaxReadBytes ||
                        request.Path.Count > m_limits.MaxDepth)
                    {
                        throw new ArgumentException("Invalid native snapshot read bounds.");
                    }
                    Snapshot snapshot = Find(request.SnapshotId, session, authorizationView);
                    ServiceResult allowed = snapshot.Reauthorize?.Invoke() ?? ServiceResult.Good;
                    if (ServiceResult.IsBad(allowed))
                    {
                        Remove(request.SnapshotId);
                        throw new ServiceResultException(allowed);
                    }
                    bool continuing = request.ContinuationPoint.Length != 0;
                    if (continuing &&
                        (!m_continuations.TryGetValue(request.ContinuationPoint, out Continuation? continuation) ||
                            continuation.SnapshotId != request.SnapshotId ||
                            continuation.Offset != request.Offset || continuation.MaxItems != request.MaxItems ||
                            continuation.MaxBytes != request.MaxBytes || !SamePath(continuation.Path, request.Path)))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                    Node value = Resolve(snapshot.Root, request.Path);
                    ulong total = Length(value);
                    if (request.Offset > total || (value.Kind == 0 && request.Offset != 0))
                    {
                        throw new ArgumentException("Native snapshot offset is outside the selected value.");
                    }
                    int count = (int)Math.Min(request.MaxItems, total - request.Offset);
                    RegistrySnapshotReadResultDataType result;
                    while (true)
                    {
                        result = Part(value, request.Offset, count, total);
                        try
                        {
                            Size(result, request.MaxBytes);
                            break;
                        }
                        catch (ServiceResultException error) when (
                            error.StatusCode == StatusCodes.BadEncodingLimitsExceeded && count > 1)
                        {
                            count /= 2;
                        }
                    }
                    if (!result.Complete)
                    {
                        int tokenCount = ContinuationCount(session) - (continuing ? 1 : 0);
                        if (tokenCount >= m_limits.MaxContinuationPointsPerSession)
                        {
                            throw new ServiceResultException(StatusCodes.BadNoContinuationPoints);
                        }
                        ByteString token = Token();
                        m_continuations.Add(token, new Continuation(request.SnapshotId,
                            ClonePath(request.Path), request.Offset + (ulong)count, request.MaxItems, request.MaxBytes));
                        result.ContinuationPoint = token;
                    }
                    if (continuing)
                    {
                        m_continuations.Remove(request.ContinuationPoint);
                    }
                    snapshot.Touched = m_clock.GetTimestamp();
                    return result;
                }
                catch (ArgumentException error)
                {
                    return ReadFailure(StatusCodes.BadInvalidArgument, error.Message);
                }
                catch (ServiceResultException error)
                {
                    return ReadFailure(error.StatusCode, error.Message);
                }
            }
        }

        /// <summary>
        /// Releases one snapshot and its continuation points.
        /// </summary>
        public RegistrySnapshotCloseResultDataType Close(NodeId session, string authorizationView, ByteString id)
        {
            lock (m_gate)
            {
                ThrowIfDisposed();
                Cleanup();
                try
                {
                    Find(id, session, authorizationView);
                    Remove(id);
                    return new RegistrySnapshotCloseResultDataType { StatusCode = StatusCodes.Good, Issues = [] };
                }
                catch (ServiceResultException error)
                {
                    return new RegistrySnapshotCloseResultDataType
                    {
                        StatusCode = error.StatusCode,
                        Issues = [Diagnostic(error.StatusCode, error.Message)]
                    };
                }
            }
        }

        /// <summary>
        /// Releases every handle for a closed or reactivated Session.
        /// </summary>
        public void ReleaseSession(NodeId session)
        {
            lock (m_gate)
            {
                var ids = new List<ByteString>();
                foreach (KeyValuePair<ByteString, Snapshot> entry in m_snapshots)
                {
                    if (entry.Value.Session == session)
                    {
                        ids.Add(entry.Key);
                    }
                }
                foreach (ByteString id in ids)
                {
                    Remove(id);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_gate)
            {
                m_disposed = true;
                m_snapshots.Clear();
                m_continuations.Clear();
                m_retainedBytes = 0;
            }
        }

        private Node Resolve(IEncodeable document, ArrayOf<RegistryPathElementDataType> path)
        {
            Node node = Describe(Variant.FromStructure(document), document.TypeId);
            foreach (RegistryPathElementDataType step in path)
            {
                if (step is null || step.Kind > 1 || (step.Kind == 0 && (step.Name is null || step.Index != 0)) ||
                    (step.Kind == 1 && !string.IsNullOrEmpty(step.Name)))
                {
                    throw new ArgumentException("Invalid native member/index path selector.");
                }
                if (step.Kind == 0 && node.Structure is not null)
                {
                    bool found = false;
                    foreach (RegistryNativeField field in RegistryNativeFields.Read(node.Structure, m_context))
                    {
                        if (field.Name == step.Name)
                        {
                            node = Describe(field.Value, DeclaredType(node.Structure, field.Name));
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        throw new ArgumentException("The native Structure field was not found.");
                    }
                }
                else if (step.Kind == 1 && node.Array is not null && step.Index < node.Array.Count)
                {
                    node = Describe(node.Array.Get((int)step.Index), node.DataType);
                }
                else
                {
                    throw new ArgumentException("The path selector does not apply to this native value.");
                }
            }
            return node;
        }

        private Node Describe(in Variant value, ExpandedNodeId declared)
        {
            if (value.TypeInfo.ValueRank >= 0)
            {
                return new Node(value, declared, 3, null, Array(value));
            }
            if (value.TryGetValue(out ExtensionObject extension))
            {
                if (extension.IsNull)
                {
                    return new Node(value, declared, 5);
                }
                if (!extension.TryGetValue(out IEncodeable? structure))
                {
                    throw new ServiceResultException(StatusCodes.BadDataTypeIdUnknown,
                        "The host must decode a native Structure before exposing its fields.");
                }
                return new Node(value, structure.TypeId, 4, structure);
            }
            ExpandedNodeId type = declared.IsNull
                ? new ExpandedNodeId((uint)value.TypeInfo.BuiltInType)
                : declared;
            if (value.IsNull)
            {
                return new Node(value, type, 5);
            }
            if (value.TryGetValue(out string _))
            {
                return new Node(value, type, 1);
            }
            if (value.TryGetValue(out ByteString bytes))
            {
                return new Node(value, type, bytes.IsNull ? 5u : 2u);
            }
            return new Node(value, type, 0);
        }

        private ExpandedNodeId DeclaredType(IEncodeable owner, string field)
        {
            if (m_context.Factory.TryGetEncodeableType(owner.TypeId, out IEncodeableType? type) &&
                type is IDataTypeDefinitionSource source &&
                source.GetDataTypeDefinition(m_context.NamespaceUris) is StructureDefinition definition)
            {
                foreach (StructureField member in definition.Fields)
                {
                    if (member.Name == field)
                    {
                        return NodeId.ToExpandedNodeId(member.DataType, m_context.NamespaceUris);
                    }
                }
            }
            return ExpandedNodeId.Null;
        }

        private ulong Length(Node node)
        {
            if (node.Array is not null)
            {
                return (ulong)node.Array.Count;
            }
            if (node.Structure is not null)
            {
                return (ulong)RegistryNativeFields.Read(node.Structure, m_context).Count;
            }
            if (node.Kind == 1 && node.Value.TryGetValue(out string text))
            {
                ScalarIndex(text, int.MaxValue, out int count, countOnly: true);
                return (ulong)count;
            }
            if (node.Kind == 2 && node.Value.TryGetValue(out ByteString bytes))
            {
                return (ulong)bytes.Length;
            }
            return node.Kind == 5 ? 0u : 1u;
        }

        private RegistrySnapshotReadResultDataType Part(Node node, ulong offset, int count, ulong total)
        {
            bool complete = offset + (ulong)count == total;
            var result = new RegistrySnapshotReadResultDataType
            {
                StatusCode = StatusCodes.Good,
                DataTypeId = node.DataType,
                ValueRank = node.Array is null ? -1 : node.Value.TypeInfo.ValueRank,
                ArrayDimensions = node.Array?.Dimensions ?? [],
                Kind = node.Kind,
                Offset = offset,
                TotalLength = total,
                Complete = complete,
                Value = Variant.Null,
                Entries = [],
                ContinuationPoint = complete ? ByteString.Empty : ByteString.From(new byte[24]),
                Issues = []
            };
            if (node.Structure is not null)
            {
                ArrayOf<RegistryNativeField> fields = RegistryNativeFields.Read(node.Structure, m_context);
                var entries = new RegistrySnapshotEntryDataType[count];
                for (int index = 0; index < count; index++)
                {
                    RegistryNativeField field = fields[checked((int)offset) + index];
                    Node child = Describe(field.Value, DeclaredType(node.Structure, field.Name));
                    entries[index] = Entry(child, new RegistryPathElementDataType { Kind = 0, Name = field.Name });
                }
                result.Entries = entries;
            }
            else if (node.Array is not null)
            {
                var entries = new RegistrySnapshotEntryDataType[count];
                for (int index = 0; index < count; index++)
                {
                    uint position = checked((uint)(offset + (ulong)index));
                    Node child = Describe(node.Array.Get((int)position), node.DataType);
                    entries[index] = Entry(child,
                        new RegistryPathElementDataType { Kind = 1, Name = string.Empty, Index = position });
                }
                result.Entries = entries;
            }
            else if (node.Kind == 1 && node.Value.TryGetValue(out string text))
            {
                int start = ScalarIndex(text, checked((int)offset), out _);
                int end = ScalarIndex(text, checked((int)offset + count), out _);
                result.Value = Variant.From(text.Substring(start, end - start));
            }
            else if (node.Kind == 2 && node.Value.TryGetValue(out ByteString bytes))
            {
                result.Value = Variant.From(ByteString.From(bytes.Span.Slice((int)offset, count).ToArray()));
            }
            else if (node.Kind == 0)
            {
                result.Value = node.Value;
            }
            return result;
        }

        private RegistrySnapshotEntryDataType Entry(Node node, RegistryPathElementDataType selector)
        {
            return new RegistrySnapshotEntryDataType
            {
                Selector = selector,
                DataTypeId = node.DataType,
                ValueRank = node.Array is null ? -1 : node.Value.TypeInfo.ValueRank,
                Kind = node.Kind,
                Length = Length(node)
            };
        }

        private static int ScalarIndex(string value, int desired, out int scalars, bool countOnly = false)
        {
            int offset = 0;
            scalars = 0;
            while (offset < value.Length && scalars < desired)
            {
                char first = value[offset++];
                if (char.IsHighSurrogate(first))
                {
                    if (offset == value.Length || !char.IsLowSurrogate(value[offset++]))
                    {
                        throw new ArgumentException("A native String contains invalid Unicode.");
                    }
                }
                else if (char.IsLowSurrogate(first))
                {
                    throw new ArgumentException("A native String contains invalid Unicode.");
                }
                scalars++;
            }
            if (!countOnly && scalars != desired)
            {
                throw new ArgumentException("A String scalar offset is out of range.");
            }
            return countOnly ? 0 : offset;
        }

        private long Size(IEncodeable value, ulong maximum)
        {
            return RegistryEncodedSize.Measure(value, m_context, checked((long)maximum));
        }

        private Snapshot Find(ByteString id, NodeId session, string authorizationView)
        {
            if (!m_snapshots.TryGetValue(id, out Snapshot? snapshot) || snapshot.Session != session)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            if (!string.Equals(snapshot.AuthorizationView, authorizationView, StringComparison.Ordinal))
            {
                Remove(id);
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            }
            return snapshot;
        }

        private int SessionCount(NodeId session)
        {
            int count = 0;
            foreach (Snapshot value in m_snapshots.Values)
            {
                if (value.Session == session)
                {
                    count++;
                }
            }
            return count;
        }

        private int ContinuationCount(NodeId session)
        {
            int count = 0;
            foreach (Continuation value in m_continuations.Values)
            {
                if (m_snapshots.TryGetValue(value.SnapshotId, out Snapshot? snapshot) && snapshot.Session == session)
                {
                    count++;
                }
            }
            return count;
        }

        private void Remove(ByteString id)
        {
            if (!m_snapshots.TryGetValue(id, out Snapshot? snapshot))
            {
                return;
            }
            m_snapshots.Remove(id);
            m_retainedBytes -= snapshot.Bytes;
            var tokens = new List<ByteString>();
            foreach (KeyValuePair<ByteString, Continuation> token in m_continuations)
            {
                if (token.Value.SnapshotId == id)
                {
                    tokens.Add(token.Key);
                }
            }
            foreach (ByteString token in tokens)
            {
                m_continuations.Remove(token);
            }
        }

        private void Cleanup()
        {
            var expired = new List<ByteString>();
            foreach (KeyValuePair<ByteString, Snapshot> pair in m_snapshots)
            {
                if (m_clock.GetElapsedTime(pair.Value.Touched).TotalMilliseconds >= m_limits.SnapshotTimeout)
                {
                    expired.Add(pair.Key);
                }
            }
            foreach (ByteString id in expired)
            {
                Remove(id);
            }
        }

        private static bool SamePath(ArrayOf<RegistryPathElementDataType> first, ArrayOf<RegistryPathElementDataType> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }
            for (int index = 0; index < first.Count; index++)
            {
                if (first[index] is null || second[index] is null || !first[index].IsEqual(second[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static ArrayOf<RegistryPathElementDataType> ClonePath(ArrayOf<RegistryPathElementDataType> path)
        {
            var result = new RegistryPathElementDataType[path.Count];
            for (int index = 0; index < path.Count; index++)
            {
                result[index] = (RegistryPathElementDataType)path[index].Clone();
            }
            return result;
        }

        private static ByteString Token()
        {
            using RandomNumberGenerator random = RandomNumberGenerator.Create();
            var bytes = new byte[24];
            random.GetBytes(bytes);
            return ByteString.From(bytes);
        }

        private static RegistryDiagnosticDataType Diagnostic(StatusCode status, string detail) => new()
        {
            StatusCode = status,
            Code = "E_NATIVE_SNAPSHOT",
            Path = [],
            Detail = detail
        };
        private static RegistrySnapshotOpenResultDataType OpenFailure(StatusCode status, string detail) => new()
        {
            StatusCode = status,
            SnapshotId = ByteString.Empty,
            Issues = [Diagnostic(status, detail)]
        };
        private static RegistrySnapshotReadResultDataType ReadFailure(StatusCode status, string detail) => new()
        {
            StatusCode = status,
            Complete = false,
            Value = Variant.Null,
            Entries = [],
            ContinuationPoint = ByteString.Empty,
            Issues = [Diagnostic(status, detail)]
        };

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(RegistryNativeSnapshots));
            }
        }

        private sealed class Snapshot(
            NodeId session, string authorizationView, IEncodeable root, ulong bytes, long touched,
            Func<ServiceResult>? reauthorize)
        {
            public NodeId Session { get; } = session;
            public string AuthorizationView { get; } = authorizationView;
            public IEncodeable Root { get; } = root;
            public ulong Bytes { get; } = bytes;
            public long Touched { get; set; } = touched;
            public Func<ServiceResult>? Reauthorize { get; } = reauthorize;
        }

        private sealed record Continuation(ByteString SnapshotId, ArrayOf<RegistryPathElementDataType> Path,
            ulong Offset, uint MaxItems, uint MaxBytes);
        private sealed record Node(Variant Value, ExpandedNodeId DataType, uint Kind,
            IEncodeable? Structure = null, ArrayView? Array = null);

        private static ArrayView Array(in Variant value)
        {
            return value.TypeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => Array<bool>(value, BuiltInType.Boolean, item => Variant.From(item)),
                BuiltInType.SByte => Array<sbyte>(value, BuiltInType.SByte, item => Variant.From(item)),
                BuiltInType.Byte => Array<byte>(value, BuiltInType.Byte, item => Variant.From(item)),
                BuiltInType.Int16 => Array<short>(value, BuiltInType.Int16, item => Variant.From(item)),
                BuiltInType.UInt16 => Array<ushort>(value, BuiltInType.UInt16, item => Variant.From(item)),
                BuiltInType.Int32 => Array<int>(value, BuiltInType.Int32, item => Variant.From(item)),
                BuiltInType.UInt32 => Array<uint>(value, BuiltInType.UInt32, item => Variant.From(item)),
                BuiltInType.Int64 => Array<long>(value, BuiltInType.Int64, item => Variant.From(item)),
                BuiltInType.UInt64 => Array<ulong>(value, BuiltInType.UInt64, item => Variant.From(item)),
                BuiltInType.Float => Array<float>(value, BuiltInType.Float, item => Variant.From(item)),
                BuiltInType.Double => Array<double>(value, BuiltInType.Double, item => Variant.From(item)),
                BuiltInType.String => Array<string>(value, BuiltInType.String,
                    item => item is null ? Variant.Null : Variant.From(item)),
                BuiltInType.DateTime => Array<DateTimeUtc>(value, BuiltInType.DateTime, item => Variant.From(item)),
                BuiltInType.Guid => Array<Uuid>(value, BuiltInType.Guid, item => Variant.From(item)),
                BuiltInType.ByteString => Array<ByteString>(value, BuiltInType.ByteString, item => Variant.From(item)),
                BuiltInType.XmlElement => Array<XmlElement>(value, BuiltInType.XmlElement, item => Variant.From(item)),
                BuiltInType.NodeId => Array<NodeId>(value, BuiltInType.NodeId, item => Variant.From(item)),
                BuiltInType.ExpandedNodeId => Array<ExpandedNodeId>(value, BuiltInType.ExpandedNodeId,
                    item => Variant.From(item)),
                BuiltInType.StatusCode => Array<StatusCode>(value, BuiltInType.StatusCode, item => Variant.From(item)),
                BuiltInType.QualifiedName => Array<QualifiedName>(value, BuiltInType.QualifiedName,
                    item => Variant.From(item)),
                BuiltInType.LocalizedText => Array<LocalizedText>(value, BuiltInType.LocalizedText,
                    item => Variant.From(item)),
                BuiltInType.ExtensionObject => Array<ExtensionObject>(value, BuiltInType.ExtensionObject,
                    item => Variant.From(item)),
                BuiltInType.DataValue => Array<DataValue>(value, BuiltInType.DataValue, item => Variant.From(item)),
                BuiltInType.Variant => Array<Variant>(value, BuiltInType.Variant, item => item),
                BuiltInType.Enumeration => Array<EnumValue>(value, BuiltInType.Enumeration, item => Variant.From(item)),
                _ => throw new ServiceResultException(StatusCodes.BadNotSupported, "Unsupported native array type.")
            };
        }

        private static ArrayView Array<T>(Variant value, BuiltInType kind, Func<T, Variant> convert)
        {
            if (!value.TryGetArray(out ArrayOf<T> items, kind))
            {
                throw new ArgumentException("Native array storage does not match its declared type.");
            }
            uint[] dimensions;
            if (value.TypeInfo.ValueRank > 1 && value.TryGetMatrix(out MatrixOf<T> matrix, kind))
            {
                int[] source = matrix.Dimensions;
                dimensions = new uint[source.Length];
                for (int index = 0; index < source.Length; index++)
                {
                    dimensions[index] = checked((uint)source[index]);
                }
            }
            else
            {
                dimensions = [(uint)items.Count];
            }
            return new ArrayView(items.Count, index => convert(items[index]), dimensions);
        }

        private sealed record ArrayView(int Count, Func<int, Variant> Get, ArrayOf<uint> Dimensions);

        private readonly Lock m_gate = new();
        private readonly ServiceMessageContext m_context;
        private readonly RegistrySnapshotLimitsDataType m_limits;
        private readonly TimeProvider m_clock;
        private readonly ulong m_maxRetainedBytes;
        private readonly Dictionary<ByteString, Snapshot> m_snapshots = [];
        private readonly Dictionary<ByteString, Continuation> m_continuations = [];
        private ulong m_retainedBytes;
        private bool m_disposed;
    }
}
