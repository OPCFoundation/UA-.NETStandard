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
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The loaded-AddressSpace part of the WoT Binding Section 5.1.5 local
    /// context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 5.1.5 makes the AddressSpace the fallback half of the local
    /// context, consulted after the sibling documents of the conversion. It is
    /// what lets a document bind to a type a companion model defines and the
    /// Server has loaded - the primary use of Section 5.2.1 - so without it
    /// every companion-model type binding is unresolvable and, because Section
    /// 5.2.1 forbids falling back to <c>BaseObjectType</c>, fails the
    /// projection.
    /// </para>
    /// <para>
    /// Compose it behind <see cref="SnapshotWotNodeResolver"/> with
    /// <see cref="WotCompositeNodeResolver"/> to get the specified order.
    /// </para>
    /// <para>
    /// The BrowseName index is built once, on first use. Types are loaded when
    /// a node manager starts, so the type hierarchy is settled by the time any
    /// document is converted. The ReferenceType index of Section 5.3, which
    /// carries both of the names OPC 10000-3 gives a ReferenceType, is built
    /// the same way and independently, so a conversion that names no relation
    /// never pays for it.
    /// </para>
    /// </remarks>
    public sealed class AddressSpaceWotNodeResolver : IWotNodeResolver, IWotReferenceTypeResolver
    {
        /// <summary>
        /// Initializes a resolver over a Server's AddressSpace.
        /// </summary>
        /// <param name="server">The Server whose AddressSpace is consulted.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="server"/> is <c>null</c>.
        /// </exception>
        public AddressSpaceWotNodeResolver(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <inheritdoc/>
        public ValueTask<bool> HoldsNamespaceAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A namespace in the Server's table is one it has loaded as an
            // information model, which is exactly what Section 5.2.1 means by
            // a namespace the local context holds.
            return new ValueTask<bool>(
                !string.IsNullOrEmpty(namespaceUri) &&
                m_server.NamespaceUris.GetIndex(namespaceUri) >= 0);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri,
            string browseName,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(namespaceUri) || string.IsNullOrEmpty(browseName))
            {
                return ArrayOf<WotResolvedNode>.Empty;
            }

            IReadOnlyDictionary<string, List<WotResolvedNode>> index =
                await IndexAsync(cancellationToken).ConfigureAwait(false);
            if (!index.TryGetValue(
                Key(namespaceUri, browseName), out List<WotResolvedNode>? found))
            {
                return ArrayOf<WotResolvedNode>.Empty;
            }

            var matches = new List<WotResolvedNode>(found.Count);
            foreach (WotResolvedNode node in found)
            {
                if (expected == WotExpectedNodeClass.Any || node.NodeClass == expected)
                {
                    matches.Add(node);
                }
            }
            return new ArrayOf<WotResolvedNode>(matches.ToArray());
        }

        /// <inheritdoc/>
        public async ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(expandedNodeId))
            {
                return null;
            }

            // NodeId implements INullable, so it is never wrapped in
            // System.Nullable; NodeId.Null signals "not translatable".
            NodeId nodeId = TryToLocalNodeId(expandedNodeId);
            if (nodeId.IsNull)
            {
                return null;
            }

            NodeClass? nodeClass = await TryGetNodeClassAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            return nodeClass is null
                ? null
                : new WotResolvedNode(expandedNodeId, ToExpectedNodeClass(nodeClass.Value));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// The Server's own ReferenceTypes are the definitive answer for a
        /// relation a companion model states: a Server that has loaded the
        /// model holds every ReferenceType it defines, with the BrowseName and
        /// the InverseName OPC 10000-3 gives each. Nothing here is restricted
        /// to a fixed table - any ReferenceType the AddressSpace holds resolves
        /// by the same rules the base-namespace ones do.
        /// </para>
        /// <para>
        /// A name that is one ReferenceType's BrowseName and another's
        /// InverseName matches both; the caller settles that with
        /// <c>uav:refId</c> or reports it, because choosing here would assert a
        /// relation the document never chose.
        /// </para>
        /// </remarks>
        public async ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
            string namespaceUri,
            string name,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(namespaceUri) || string.IsNullOrEmpty(name))
            {
                return ArrayOf<WotResolvedReferenceType>.Empty;
            }

            IReadOnlyDictionary<string, List<WotResolvedReferenceType>> index =
                await ReferenceTypeIndexAsync(cancellationToken).ConfigureAwait(false);
            return index.TryGetValue(
                Key(namespaceUri, name), out List<WotResolvedReferenceType>? found)
                ? new ArrayOf<WotResolvedReferenceType>(found.ToArray())
                : ArrayOf<WotResolvedReferenceType>.Empty;
        }

        /// <summary>
        /// Builds the ReferenceType index on first use, keyed by each of the
        /// two names a ReferenceType answers to.
        /// </summary>
        /// <remarks>
        /// Two concurrent first callers may both build it, which is harmless
        /// for the same reason the type index is built this way: the hierarchy
        /// is settled before any document is converted, so both produce the
        /// same content, and no lock is held across the awaits the walk needs.
        /// </remarks>
        private async ValueTask<IReadOnlyDictionary<string, List<WotResolvedReferenceType>>>
            ReferenceTypeIndexAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, List<WotResolvedReferenceType>>? index = m_referenceTypes;
            if (index is not null)
            {
                return index;
            }

            var built = new Dictionary<string, List<WotResolvedReferenceType>>(
                StringComparer.Ordinal);
            var pending = new Queue<NodeId>();
            var seen = new HashSet<NodeId>();
            pending.Enqueue(Opc.Ua.ReferenceTypeIds.References);
            seen.Add(Opc.Ua.ReferenceTypeIds.References);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId current = pending.Dequeue();

                // Materialised before the await: ArrayOf<T> enumerates as a
                // span, which cannot be preserved across an await boundary.
                var subTypes = new List<NodeId>();
                foreach (NodeId subType in m_server.TypeTree.FindSubTypes(current))
                {
                    subTypes.Add(subType);
                }
                foreach (NodeId subType in subTypes)
                {
                    if (subType.IsNull || !seen.Add(subType))
                    {
                        continue;
                    }
                    pending.Enqueue(subType);
                    await IndexReferenceTypeAsync(subType, built, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            m_referenceTypes = built;
            return built;
        }

        /// <summary>
        /// Adds one ReferenceType to the index under its BrowseName and, unless
        /// it is symmetric, under its InverseName.
        /// </summary>
        private async ValueTask IndexReferenceTypeAsync(
            NodeId referenceTypeId,
            Dictionary<string, List<WotResolvedReferenceType>> index,
            CancellationToken cancellationToken)
        {
            ArrayOf<DataValue> values = await ReadAttributesAsync(
                referenceTypeId,
                [
                    Opc.Ua.Attributes.NodeClass,
                    Opc.Ua.Attributes.BrowseName,
                    Opc.Ua.Attributes.InverseName,
                    Opc.Ua.Attributes.Symmetric
                ],
                cancellationToken).ConfigureAwait(false);
            if (values.Count != 4 ||
                !values[0].WrappedValue.TryGetValue(out int nodeClass) ||
                (NodeClass)nodeClass != NodeClass.ReferenceType ||
                !values[1].WrappedValue.TryGetValue(out QualifiedName browseName) ||
                browseName.IsNull ||
                string.IsNullOrEmpty(browseName.Name))
            {
                return;
            }
            string? namespaceUri = m_server.NamespaceUris.GetString(browseName.NamespaceIndex);
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return;
            }

            string portable = ToPortable(referenceTypeId);
            AddReferenceTypeName(
                index, namespaceUri!, browseName.Name!, portable, browseName.Name!, true);

            bool isSymmetric =
                values[3].WrappedValue.TryGetValue(out bool symmetric) && symmetric;
            string? inverseName =
                values[2].WrappedValue.TryGetValue(out LocalizedText inverse)
                    ? inverse.Text
                    : null;
            if (!isSymmetric &&
                !string.IsNullOrEmpty(inverseName) &&
                !string.Equals(inverseName, browseName.Name, StringComparison.Ordinal))
            {
                AddReferenceTypeName(
                    index, namespaceUri!, inverseName!, portable, inverseName!, false);
            }
        }

        private static void AddReferenceTypeName(
            Dictionary<string, List<WotResolvedReferenceType>> index,
            string namespaceUri,
            string name,
            string nodeId,
            string matchedName,
            bool isForward)
        {
            string key = Key(namespaceUri, name);
            if (!index.TryGetValue(key, out List<WotResolvedReferenceType>? matches))
            {
                matches = [];
                index[key] = matches;
            }
            matches.Add(new WotResolvedReferenceType(nodeId, matchedName, isForward));
        }

        /// <summary>
        /// Reads a Node's attributes through the Server's own read path.
        /// </summary>
        /// <remarks>
        /// The InverseName and Symmetric Attributes are not part of the browse
        /// metadata, so they are read as Attributes. The Server's asynchronous
        /// read is used rather than a node manager's state objects, so the
        /// resolver holds no lock, never blocks on an asynchronous call, and
        /// works for any node manager implementation.
        /// </remarks>
        private async ValueTask<ArrayOf<DataValue>> ReadAttributesAsync(
            NodeId nodeId,
            uint[] attributes,
            CancellationToken cancellationToken)
        {
            var nodesToRead = new ReadValueId[attributes.Length];
            for (int ii = 0; ii < attributes.Length; ii++)
            {
                nodesToRead[ii] = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attributes[ii]
                };
            }
            try
            {
                using OperationContext context = CreateContext();
                (ArrayOf<DataValue> values, _) = await m_server.NodeManager.ReadAsync(
                    context,
                    0,
                    TimestampsToReturn.Neither,
                    new ArrayOf<ReadValueId>(nodesToRead),
                    cancellationToken).ConfigureAwait(false);
                return values;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A node manager that refuses the read contributes no name
                // rather than failing every other resolution.
                return ArrayOf<DataValue>.Empty;
            }
        }

        /// <summary>
        /// Builds the BrowseName index on first use.
        /// </summary>
        private async ValueTask<IReadOnlyDictionary<string, List<WotResolvedNode>>> IndexAsync(
            CancellationToken cancellationToken)
        {
            Dictionary<string, List<WotResolvedNode>>? index = m_index;
            if (index is not null)
            {
                return index;
            }

            // Two concurrent first callers may both build the index. That is
            // harmless - the type hierarchy is settled before any document is
            // converted, so both produce the same content - and it avoids
            // holding a lock across the awaits the walk needs, which in turn
            // keeps the resolver free of a disposable field whose ownership
            // the IWotNodeResolver contract does not model.
            var built = new Dictionary<string, List<WotResolvedNode>>(StringComparer.Ordinal);
            await AddSubTypesAsync(
                Opc.Ua.ObjectTypeIds.BaseObjectType, built, cancellationToken)
                .ConfigureAwait(false);
            await AddSubTypesAsync(
                Opc.Ua.VariableTypeIds.BaseVariableType, built, cancellationToken)
                .ConfigureAwait(false);
            m_index = built;
            return built;
        }

        /// <summary>
        /// Walks the subtypes of a root type, indexing each by its
        /// NamespaceUri-qualified BrowseName.
        /// </summary>
        private async ValueTask AddSubTypesAsync(
            NodeId rootTypeId,
            Dictionary<string, List<WotResolvedNode>> index,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<NodeId>();
            var seen = new HashSet<NodeId>();
            pending.Enqueue(rootTypeId);
            seen.Add(rootTypeId);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId current = pending.Dequeue();

                // Materialised before the await: ArrayOf<T> enumerates as a
                // span, which cannot be preserved across an await boundary.
                var subTypes = new List<NodeId>();
                foreach (NodeId subType in m_server.TypeTree.FindSubTypes(current))
                {
                    subTypes.Add(subType);
                }

                foreach (NodeId subType in subTypes)
                {
                    if (subType.IsNull || !seen.Add(subType))
                    {
                        continue;
                    }
                    pending.Enqueue(subType);
                    await IndexTypeAsync(subType, index, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Adds one type to the index, keyed by its qualified BrowseName.
        /// </summary>
        private async ValueTask IndexTypeAsync(
            NodeId typeId,
            Dictionary<string, List<WotResolvedNode>> index,
            CancellationToken cancellationToken)
        {
            NodeMetadata? metadata = await TryGetMetadataAsync(typeId, cancellationToken)
                .ConfigureAwait(false);
            if (metadata is null || metadata.BrowseName.IsNull)
            {
                return;
            }

            string? namespaceUri = m_server.NamespaceUris
                .GetString(metadata.BrowseName.NamespaceIndex);
            if (string.IsNullOrEmpty(namespaceUri) ||
                string.IsNullOrEmpty(metadata.BrowseName.Name))
            {
                return;
            }

            string key = Key(namespaceUri!, metadata.BrowseName.Name!);
            if (!index.TryGetValue(key, out List<WotResolvedNode>? bucket))
            {
                bucket = [];
                index[key] = bucket;
            }
            bucket.Add(new WotResolvedNode(
                ToPortable(typeId), ToExpectedNodeClass(metadata.NodeClass)));
        }

        /// <summary>
        /// Reads a node's metadata, or <c>null</c> when the Server does not
        /// hold it.
        /// </summary>
        private async ValueTask<NodeMetadata?> TryGetMetadataAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            try
            {
                (object? handle, IAsyncNodeManager? manager) = await m_server.NodeManager
                    .GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false);
                if (handle is null || manager is null)
                {
                    return null;
                }
                using OperationContext context = CreateContext();
                return await manager.GetNodeMetadataAsync(
                    context,
                    handle,
                    BrowseResultMask.NodeClass | BrowseResultMask.BrowseName,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A node manager that refuses the lookup contributes no name
                // rather than failing every other resolution.
                return null;
            }
        }

        private async ValueTask<NodeClass?> TryGetNodeClassAsync(
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            NodeMetadata? metadata = await TryGetMetadataAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            return metadata?.NodeClass;
        }

        /// <summary>
        /// Translates a portable ExpandedNodeId to this Server's local NodeId.
        /// </summary>
        private NodeId TryToLocalNodeId(string expandedNodeId)
        {
            try
            {
                var parsed = ExpandedNodeId.Parse(expandedNodeId, m_server.NamespaceUris);
                if (parsed.IsNull)
                {
                    return NodeId.Null;
                }
                return ExpandedNodeId.ToNodeId(parsed, m_server.NamespaceUris);
            }
            catch (Exception ex) when (ex is ServiceResultException or FormatException)
            {
                return NodeId.Null;
            }
        }

        /// <summary>
        /// Renders a local NodeId as the portable form of Section 5.1.1, which
        /// is what a type binding carries and what the converter parses back.
        /// Namespace 0 keeps its canonical form and needs no
        /// <c>nsu=</c> prefix.
        /// </summary>
        private string ToPortable(NodeId nodeId)
        {
            var buffer = new System.Text.StringBuilder();
            if (nodeId.NamespaceIndex != 0)
            {
                string? namespaceUri = m_server.NamespaceUris.GetString(nodeId.NamespaceIndex);
                if (!string.IsNullOrEmpty(namespaceUri))
                {
                    buffer.Append("nsu=")
                        .Append(CoreUtils.EscapeUri(namespaceUri!))
                        .Append(';');
                }
            }
            NodeId.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                buffer,
                nodeId.IdentifierAsString,
                nodeId.IdType,
                0);
            return buffer.ToString();
        }

        private static WotExpectedNodeClass ToExpectedNodeClass(NodeClass nodeClass)
        {
            return nodeClass switch
            {
                NodeClass.ObjectType => WotExpectedNodeClass.ObjectType,
                NodeClass.VariableType => WotExpectedNodeClass.VariableType,
                NodeClass.ReferenceType => WotExpectedNodeClass.ReferenceType,
                _ => WotExpectedNodeClass.Any
            };
        }

        private static OperationContext CreateContext()
        {
            return new OperationContext(
                new RequestHeader(), null, RequestType.Browse, RequestLifetime.None);
        }

        private static string Key(string namespaceUri, string browseName)
        {
            return namespaceUri + "\u0000" + browseName;
        }

        private readonly IServerInternal m_server;
        private volatile Dictionary<string, List<WotResolvedNode>>? m_index;
        private volatile Dictionary<string, List<WotResolvedReferenceType>>? m_referenceTypes;
    }
}
