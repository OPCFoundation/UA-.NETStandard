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
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Structure;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// The location hierarchies below <c>HierarchicalLocations</c> and
    /// <c>OperationalLocations</c> (OPC 10000-110 §13.3.3, §13.4.3).
    /// </summary>
    public sealed partial class AmbNodeManager
    {
        /// <summary>
        /// The separator of the levels of a location path.
        /// </summary>
        internal const char LocationPathSeparator = '/';

        /// <summary>
        /// Finds or creates the location a path names, level by level.
        /// </summary>
        /// <remarks>
        /// The first level is organized by the entry point of its kind, each
        /// deeper level is a component of the one above, as §13.3.3
        /// recommends. The location objects live in the instance namespace.
        /// </remarks>
        /// <param name="kind">The kind of location.</param>
        /// <param name="path">The levels, separated by a slash.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The deepest level.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="kind"/> has no location objects, or
        /// <paramref name="path"/> is empty or has an empty level.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the address
        /// space lacks the entry point of the kind.
        /// </exception>
        internal async ValueTask<BaseObjectState> EnsureLocationAsync(
            AssetLocationKind kind,
            string path,
            CancellationToken cancellationToken)
        {
            ExpandedNodeId entryPoint = kind switch
            {
                AssetLocationKind.Hierarchical => ObjectIds.HierarchicalLocations,
                AssetLocationKind.Operational => ObjectIds.OperationalLocations,
                _ => throw new ArgumentException(
                    $"{kind} locations have no location objects (OPC 10000-110 §13.5).",
                    nameof(kind))
            };
            string[] levels = SplitLocationPath(path);

            await m_locationsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                NodeState parent = FindPredefinedNode<FolderState>(
                    ExpandedNodeId.ToNodeId(entryPoint, Server.NamespaceUris)) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The AMB model does not provide the location entry point {0}.",
                        entryPoint);
                bool root = true;
                foreach (string level in levels)
                {
                    var browseName = new QualifiedName(level, InstanceNamespaceIndex);
                    if (parent.FindChildWithQualifiedName(SystemContext, browseName) is not BaseObjectState location)
                    {
                        location = new BaseObjectState(parent)
                        {
                            SymbolicName = level,
                            BrowseName = browseName,
                            DisplayName = new LocalizedText(level),
                            TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType,
                            ReferenceTypeId = root
                                ? Ua.ReferenceTypeIds.Organizes
                                : Ua.ReferenceTypeIds.HasComponent
                        };
                        location.NodeId = New(SystemContext, location);
                        parent.AddChild(location);
                        await AddPredefinedNodeAsync(SystemContext, location, cancellationToken).ConfigureAwait(false);
                    }
                    parent = location;
                    root = false;
                }
                return (BaseObjectState)parent;
            }
            finally
            {
                m_locationsLock.Release();
            }
        }

        /// <summary>
        /// Puts an asset into a location: the location contains it and the
        /// asset is located in it, in both directions as §13.1 requires.
        /// </summary>
        /// <remarks>
        /// An asset registered before this manager loaded the location entry
        /// points - by a Device Integration manager created earlier - is put
        /// into its locations when the address space is created.
        /// </remarks>
        internal async ValueTask ContainAsync(
            AssetLocationKind kind,
            string path,
            BaseObjectState asset,
            AssetStructure structure,
            CancellationToken cancellationToken)
        {
            lock (m_pendingLocationsLock)
            {
                if (!m_locationsReady)
                {
                    m_pendingLocations.Add((kind, path, asset, structure));
                    return;
                }
            }
            NodeId location = await ContainNowAsync(kind, path, asset, cancellationToken).ConfigureAwait(false);
            structure.AddLocation(kind, location);
        }

        /// <summary>
        /// Takes an unregistered asset out of its locations: the
        /// <c>*Contains</c> references in both directions go, so no location
        /// points at an object that is no asset any more.
        /// </summary>
        internal async ValueTask ReleaseLocationsAsync(AssetHandle handle, CancellationToken cancellationToken)
        {
            lock (m_pendingLocationsLock)
            {
                m_pendingLocations.RemoveAll(pending => ReferenceEquals(pending.Asset, handle.Asset));
            }
            if (handle.Structure == null)
            {
                return;
            }

            ArrayOf<(AssetLocationKind Kind, NodeId Location)> locations = handle.Structure.Locations;
            if (locations.Count == 0)
            {
                return;
            }
            await m_locationsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach ((AssetLocationKind kind, NodeId location) in locations)
                {
                    NodeId referenceTypeId = ContainsReferenceOf(kind);
                    FindPredefinedNode<BaseObjectState>(location)?.RemoveReference(
                        referenceTypeId,
                        false,
                        handle.NodeId);
                    handle.Asset.RemoveReference(referenceTypeId, true, location);
                    ModelChangeAggregator.RecordReferenceDeleted(location);
                }
                EmitModelChange(SystemContext);
            }
            finally
            {
                m_locationsLock.Release();
            }
        }

        /// <summary>
        /// Puts the assets registered before the address space existed into
        /// their locations.
        /// </summary>
        private async ValueTask InitializeLocationsAsync(CancellationToken cancellationToken)
        {
            List<(AssetLocationKind Kind, string Path, BaseObjectState Asset, AssetStructure Structure)> pending;
            lock (m_pendingLocationsLock)
            {
                m_locationsReady = true;
                pending = [.. m_pendingLocations];
                m_pendingLocations.Clear();
            }
            foreach ((AssetLocationKind kind, string path, BaseObjectState asset, AssetStructure structure) in pending)
            {
                NodeId location = await ContainNowAsync(kind, path, asset, cancellationToken).ConfigureAwait(false);
                structure.AddLocation(kind, location);
            }
        }

        private async ValueTask<NodeId> ContainNowAsync(
            AssetLocationKind kind,
            string path,
            BaseObjectState asset,
            CancellationToken cancellationToken)
        {
            BaseObjectState location = await EnsureLocationAsync(kind, path, cancellationToken).ConfigureAwait(false);
            NodeId referenceTypeId = ContainsReferenceOf(kind);
            if (!location.ReferenceExists(referenceTypeId, false, asset.NodeId))
            {
                location.AddReference(referenceTypeId, false, asset.NodeId);
            }
            if (!asset.ReferenceExists(referenceTypeId, true, location.NodeId))
            {
                asset.AddReference(referenceTypeId, true, location.NodeId);
            }
            return location.NodeId;
        }

        /// <summary>
        /// Finds or creates the dictionary entry of an IRDI below
        /// <c>Server/Dictionaries</c> (OPC 10000-19): an
        /// <c>IrdiDictionaryEntryType</c> object whose NodeId is the IRDI in
        /// the IRDI namespace.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="irdi"/> is empty.</exception>
        internal async ValueTask<NodeId> EnsureDictionaryEntryAsync(
            string irdi,
            LocalizedText displayName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(irdi))
            {
                throw new ArgumentException("A dictionary entry needs an IRDI.", nameof(irdi));
            }
            var nodeId = new NodeId(irdi, IrdiNamespaceIndex);

            await m_locationsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (FindPredefinedNode<BaseObjectState>(nodeId) == null)
                {
                    var entry = new BaseObjectState(null)
                    {
                        NodeId = nodeId,
                        SymbolicName = irdi,
                        BrowseName = new QualifiedName(irdi, IrdiNamespaceIndex),
                        DisplayName = displayName.IsNullOrEmpty ? new LocalizedText(irdi) : displayName,
                        TypeDefinitionId = Ua.ObjectTypeIds.IrdiDictionaryEntryType,
                        ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent
                    };
                    entry.AddReference(Ua.ReferenceTypeIds.HasComponent, true, Ua.ObjectIds.Dictionaries);
                    await AddPredefinedNodeAsync(SystemContext, entry, cancellationToken).ConfigureAwait(false);
                    await Server.NodeManager
                        .AddReferencesAsync(
                            Ua.ObjectIds.Dictionaries,
                            [
                                new ReferenceNode
                                {
                                    ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                                    IsInverse = false,
                                    TargetId = nodeId
                                }
                            ],
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                lock (m_pendingLocationsLock)
                {
                    m_dictionaryEntries.Add(nodeId);
                }
                return nodeId;
            }
            finally
            {
                m_locationsLock.Release();
            }
        }

        /// <summary>
        /// Gets whether a node is a dictionary entry this manager created.
        /// </summary>
        internal bool IsDictionaryEntry(NodeId nodeId)
        {
            lock (m_pendingLocationsLock)
            {
                return m_dictionaryEntries.Contains(nodeId);
            }
        }

        /// <summary>
        /// Gets the index of the IRDI dictionary namespace.
        /// </summary>
        internal ushort IrdiNamespaceIndex => (ushort)Server.NamespaceUris.GetIndex(AmbServerOptions.IrdiNamespaceUri);

        private NodeId ContainsReferenceOf(AssetLocationKind kind)
        {
            return ExpandedNodeId.ToNodeId(
                kind == AssetLocationKind.Hierarchical
                    ? ReferenceTypeIds.HierarchicalContains
                    : ReferenceTypeIds.OperationalContains,
                Server.NamespaceUris);
        }

        private static string[] SplitLocationPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A location path needs at least one level.", nameof(path));
            }
            string[] levels = path.Split(LocationPathSeparator);
            for (int ii = 0; ii < levels.Length; ii++)
            {
                levels[ii] = levels[ii].Trim();
                if (levels[ii].Length == 0)
                {
                    throw new ArgumentException($"The location path '{path}' has an empty level.", nameof(path));
                }
            }
            return levels;
        }

        private readonly SemaphoreSlim m_locationsLock = new(1, 1);
        private readonly Lock m_pendingLocationsLock = new();

        private readonly List<(AssetLocationKind Kind, string Path, BaseObjectState Asset, AssetStructure Structure)>
            m_pendingLocations = [];

        private readonly HashSet<NodeId> m_dictionaryEntries = [];
        private bool m_locationsReady;
    }
}
