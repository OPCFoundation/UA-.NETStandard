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
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Configuration;

namespace Opc.Ua.AMB.Server.Structure
{
    /// <summary>
    /// A location Property an application asked for.
    /// </summary>
    /// <param name="Value">The initial value; unknown when null.</param>
    /// <param name="Writable">Whether clients can write and persist it.</param>
    internal sealed record LocationPropertyRequest(string? Value, bool Writable);

    /// <summary>
    /// The <c>LocalTime</c> an application asked for.
    /// </summary>
    /// <param name="Value">The time zone.</param>
    /// <param name="Writable">Whether clients can write and persist it.</param>
    internal sealed record LocalTimeRequest(TimeZoneDataType Value, bool Writable);

    /// <summary>
    /// The version information an application asked for.
    /// </summary>
    /// <param name="HardwareRevision">The hardware revision, or null.</param>
    /// <param name="SoftwareRevision">The software revision, or null.</param>
    /// <param name="RevisionCounter">The revision counter, or null to keep it.</param>
    internal sealed record VersionRequest(string? HardwareRevision, string? SoftwareRevision, int? RevisionCounter);

    /// <summary>
    /// What an application asked for about the version information,
    /// locations, classification and structure of an asset
    /// (OPC 10000-110 §10, §11, §13, §14).
    /// </summary>
    internal sealed class AssetStructureRequest
    {
        /// <summary>Gets or sets the version information.</summary>
        public VersionRequest? Version { get; set; }

        /// <summary>Gets the location Properties by kind.</summary>
        public Dictionary<AssetLocationKind, LocationPropertyRequest> LocationProperties { get; } = [];

        /// <summary>Gets or sets the local time.</summary>
        public LocalTimeRequest? LocalTime { get; set; }

        /// <summary>Gets the dictionary entries that classify the asset.</summary>
        public List<ExpandedNodeId> Classifications { get; } = [];

        /// <summary>Gets or sets the requirements.</summary>
        public AssetEntriesRequest? Requirements { get; set; }

        /// <summary>Gets or sets the capabilities.</summary>
        public AssetEntriesRequest? Capabilities { get; set; }

        /// <summary>Gets the relations to other nodes.</summary>
        public List<(NodeId ReferenceTypeId, NodeId Target)> Relations { get; } = [];

        /// <summary>Gets the locations the asset is in.</summary>
        public List<(AssetLocationKind Kind, string Path)> LocatedIn { get; } = [];
    }

    /// <summary>
    /// What registering added to an asset for its version information,
    /// locations, classification and structure.
    /// </summary>
    internal sealed class AssetStructure
    {
        private AssetStructure(AssetHandle handle)
        {
            m_handle = handle;
        }

        /// <summary>
        /// Gets the location objects that contain the asset.
        /// </summary>
        public ArrayOf<(AssetLocationKind Kind, NodeId Location)> Locations
        {
            get
            {
                lock (m_lock)
                {
                    return m_locations.ToArray().ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets the dictionary entries the asset is classified with that were
        /// <c>DictionaryEntryType</c> objects when it was registered.
        /// </summary>
        public ArrayOf<NodeId> CheckedClassifications
        {
            get
            {
                lock (m_lock)
                {
                    return m_classifications.ToArray().ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets the nodes registering added that reference a dictionary entry
        /// of this server: the asset and its requirement and capability
        /// variables.
        /// </summary>
        /// <param name="entry">The dictionary entry.</param>
        public ArrayOf<NodeId> SourcesOf(NodeId entry)
        {
            lock (m_lock)
            {
                var sources = new List<NodeId>();
                foreach ((NodeId source, NodeId target) in m_dictionaryReferences)
                {
                    if (target == entry)
                    {
                        sources.Add(source);
                    }
                }
                return sources.ToArray().ToArrayOf();
            }
        }

        /// <summary>
        /// Records a location that contains the asset.
        /// </summary>
        internal void AddLocation(AssetLocationKind kind, NodeId location)
        {
            lock (m_lock)
            {
                m_locations.Add((kind, location));
            }
        }

        /// <summary>
        /// Adds what an application asked for to an asset while it is
        /// registered.
        /// </summary>
        public static async ValueTask<AssetStructure> ApplyAsync(
            AmbNodeManager manager,
            AssetHandle handle,
            AssetStructureRequest request,
            IAssetConfigurationStore store,
            AmbServerOptions options,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var structure = new AssetStructure(handle);
            ISystemContext context = handle.Context;
            BaseObjectState asset = handle.Asset;
            string productInstanceUri = handle.ProductInstanceUri;

            if (request.Version is VersionRequest version)
            {
                ApplyVersion(handle, version);
            }

            foreach (KeyValuePair<AssetLocationKind, LocationPropertyRequest> location in request.LocationProperties)
            {
                string name = NameOf(location.Key);
                NodeId dataType = location.Key == AssetLocationKind.Digital
                    ? Ua.DataTypeIds.UriString
                    : Ua.DataTypeIds.String;
                BaseVariableState property = AssetProperties.Ensure(
                    handle,
                    new QualifiedName(name, manager.AmbNamespaceIndex),
                    dataType,
                    location.Value.Value == null ? Variant.Null : Variant.From(location.Value.Value));
                if (location.Value.Writable)
                {
                    RequireProductInstanceUri(handle, productInstanceUri, name);
                    await PersistentProperty.BindAsync(
                        property,
                        store,
                        productInstanceUri,
                        name,
                        StringCodec.Instance,
                        options.MaxLocationLength,
                        logger,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (request.LocalTime is LocalTimeRequest localTime)
            {
                BaseVariableState property = AssetProperties.Ensure(
                    handle,
                    new QualifiedName(Ua.BrowseNames.LocalTime),
                    Ua.DataTypeIds.TimeZoneDataType,
                    Variant.FromStructure(localTime.Value));
                if (localTime.Writable)
                {
                    RequireProductInstanceUri(handle, productInstanceUri, Ua.BrowseNames.LocalTime);
                    await PersistentProperty.BindAsync(
                        property,
                        store,
                        productInstanceUri,
                        AssetConfigurationNames.LocalTime,
                        TimeZoneCodec.Instance,
                        null,
                        logger,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var dictionaryReferences = new List<(NodeId Source, NodeId Entry)>();
            foreach (ExpandedNodeId dictionaryEntry in request.Classifications)
            {
                // Part 19: the target is a DictionaryEntryType object. An entry
                // of this server is referenced by its local NodeId; whether it
                // is one is checked now, and again for the entries the AMB
                // manager defines later.
                NodeId local = ExpandedNodeId.ToNodeId(dictionaryEntry, context.NamespaceUris);
                if (local.IsNull)
                {
                    asset.AddReference(Ua.ReferenceTypeIds.HasDictionaryEntry, false, dictionaryEntry);
                    continue;
                }
                asset.AddReference(Ua.ReferenceTypeIds.HasDictionaryEntry, false, local);
                dictionaryReferences.Add((asset.NodeId, local));
                NodeState? target = await manager.Server.NodeManager
                    .FindNodeInAddressSpaceAsync(local, cancellationToken)
                    .ConfigureAwait(false);
                if (target is BaseObjectState entryObject &&
                    manager.Server.TypeTree.IsTypeOf(
                        entryObject.TypeDefinitionId,
                        Ua.ObjectTypeIds.DictionaryEntryType))
                {
                    lock (structure.m_lock)
                    {
                        structure.m_classifications.Add(local);
                    }
                }
            }

            if (request.Requirements != null)
            {
                AssetProperties.AddFolder(
                    handle,
                    manager.AmbNamespaceIndex,
                    AmbBrowseNames.Requirements,
                    request.Requirements,
                    dictionaryReferences);
            }
            if (request.Capabilities != null)
            {
                AssetProperties.AddFolder(
                    handle,
                    manager.AmbNamespaceIndex,
                    AmbBrowseNames.Capabilities,
                    request.Capabilities,
                    dictionaryReferences);
            }

            // References are exposed in both directions: a dictionary entry of
            // this server lists what it classifies through DictionaryEntryOf.
            // An entry that does not exist yet gets the inverse references when
            // the AMB manager defines it.
            foreach ((NodeId source, NodeId entry) in dictionaryReferences)
            {
                await manager.Server.NodeManager
                    .AddReferencesAsync(
                        entry,
                        [
                            new ReferenceNode
                            {
                                ReferenceTypeId = Ua.ReferenceTypeIds.HasDictionaryEntry,
                                IsInverse = true,
                                TargetId = source
                            }
                        ],
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            lock (structure.m_lock)
            {
                structure.m_dictionaryReferences.AddRange(dictionaryReferences);
            }

            foreach ((NodeId referenceTypeId, NodeId target) in request.Relations)
            {
                asset.AddReference(referenceTypeId, false, target);
                // References are exposed in both directions; the target may live in
                // any node manager, or not yet exist.
                await manager.Server.NodeManager
                    .AddReferencesAsync(
                        target,
                        [
                            new ReferenceNode
                            {
                                ReferenceTypeId = referenceTypeId,
                                IsInverse = true,
                                TargetId = asset.NodeId
                            }
                        ],
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach ((AssetLocationKind kind, string path) in request.LocatedIn)
            {
                await manager.ContainAsync(kind, path, asset, structure, cancellationToken).ConfigureAwait(false);
            }
            asset.ClearChangeMasks(context, true);
            return structure;
        }

        /// <summary>
        /// Increments the <c>RevisionCounter</c> of the asset.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidState"/> when the asset has no
        /// <c>RevisionCounter</c>.
        /// </exception>
        public int IncrementRevisionCounter()
        {
            BaseVariableState counter = AssetIdentification.FindProperty(
                m_handle.Context,
                m_handle.Asset,
                m_handle.DiNamespaceIndex,
                Opc.Ua.Di.BrowseNames.RevisionCounter) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Asset '{0}' has no RevisionCounter.",
                    m_handle.BrowseName);
            lock (m_lock)
            {
                int next = (counter.WrappedValue.TryGetValue(out int current) ? current : 0) + 1;
                counter.WrappedValue = Variant.From(next);
                counter.Timestamp = DateTimeUtc.Now;
                counter.ClearChangeMasks(m_handle.Context, false);
                return next;
            }
        }

        /// <summary>
        /// Gets the browse name of the location Property of a kind.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not defined.</exception>
        internal static string NameOf(AssetLocationKind kind)
        {
            return kind switch
            {
                AssetLocationKind.Hierarchical => AmbBrowseNames.HierarchicalLocation,
                AssetLocationKind.Operational => AmbBrowseNames.OperationalLocation,
                AssetLocationKind.Digital => AmbBrowseNames.DigitalLocation,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        private static void ApplyVersion(AssetHandle handle, VersionRequest version)
        {
            if (version.HardwareRevision != null)
            {
                AssetProperties.EnsureDi(
                    handle,
                    Opc.Ua.Di.BrowseNames.HardwareRevision,
                    Ua.DataTypeIds.String,
                    Variant.From(version.HardwareRevision),
                    Variant.Null,
                    Opc.Ua.Di.ObjectTypeIds.IVendorNameplateType);
            }
            if (version.SoftwareRevision != null)
            {
                AssetProperties.EnsureDi(
                    handle,
                    Opc.Ua.Di.BrowseNames.SoftwareRevision,
                    Ua.DataTypeIds.String,
                    Variant.From(version.SoftwareRevision),
                    Variant.Null,
                    Opc.Ua.Di.ObjectTypeIds.IVendorNameplateType);
            }
            // Without a counter of its own, an asset that asks for version
            // information keeps a counter it has - unless that is the
            // "not supported" default -1 of OPC 10000-100 - and otherwise
            // starts at 0.
            Variant counterValue = Variant.Null;
            if (version.RevisionCounter is int counter)
            {
                counterValue = Variant.From(counter);
            }
            else if (AssetIdentification.FindProperty(
                    handle.Context,
                    handle.Asset,
                    handle.DiNamespaceIndex,
                    Opc.Ua.Di.BrowseNames.RevisionCounter) is BaseVariableState existing &&
                !(existing.WrappedValue.TryGetValue(out int current) && current >= 0))
            {
                counterValue = Variant.From(0);
            }
            AssetProperties.EnsureDi(
                handle,
                Opc.Ua.Di.BrowseNames.RevisionCounter,
                Ua.DataTypeIds.Int32,
                counterValue,
                Variant.From(0),
                interfaceId: ExpandedNodeId.Null);
        }

        private static void RequireProductInstanceUri(AssetHandle handle, string productInstanceUri, string property)
        {
            if (productInstanceUri.Length == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Asset '{0}' has no ProductInstanceUri, which a writable {1} is persisted under.",
                    handle.BrowseName,
                    property);
            }
        }

        private readonly AssetHandle m_handle;
        private readonly Lock m_lock = new();
        private readonly List<(AssetLocationKind Kind, NodeId Location)> m_locations = [];
        private readonly List<NodeId> m_classifications = [];
        private readonly List<(NodeId Source, NodeId Entry)> m_dictionaryReferences = [];
    }
}
