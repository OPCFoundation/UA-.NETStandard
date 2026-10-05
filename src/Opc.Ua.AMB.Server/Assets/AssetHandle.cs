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
using Opc.Ua.AMB.Server.DocumentationLinks;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.AMB.Server.Maintenance;
using Opc.Ua.AMB.Server.Structure;
using Opc.Ua.Di;
using Opc.Ua.Server;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// What an application asked for when it registered an asset.
    /// </summary>
    internal sealed class AssetBuilder : IAssetBuilder
    {
        /// <summary>
        /// Gets whether the <c>AssetId</c> is configurable.
        /// </summary>
        public bool ConfigurableAssetId { get; private set; }

        /// <summary>
        /// Gets the default of a configurable <c>AssetId</c>.
        /// </summary>
        public string? DefaultAssetId { get; private set; }

        /// <summary>
        /// Gets whether the asset gets <c>DeviceHealth</c>.
        /// </summary>
        public bool DeviceHealthRequested { get; private set; }

        /// <summary>
        /// Gets the health the asset starts with.
        /// </summary>
        public DeviceHealthEnumeration InitialDeviceHealth { get; private set; }

        /// <summary>
        /// Gets whether <c>DeviceHealth</c> follows the health alarms.
        /// </summary>
        public bool DeriveDeviceHealth { get; private set; }

        /// <summary>
        /// Gets the health alarms of the asset.
        /// </summary>
        public List<HealthAlarmRequest> HealthAlarms { get; } = [];

        /// <summary>
        /// Gets the maintenance activities of the asset.
        /// </summary>
        public List<MaintenanceRequest> Maintenance { get; } = [];

        /// <summary>
        /// Gets the documentation links of the asset, when asked for.
        /// </summary>
        public DocumentationLinksRequest? DocumentationLinks { get; private set; }

        /// <summary>
        /// Gets the version information, locations, classification and
        /// structure of the asset.
        /// </summary>
        public AssetStructureRequest Structure { get; } = new();

        /// <inheritdoc/>
        public IAssetBuilder WithConfigurableAssetId(string? defaultAssetId = null)
        {
            ConfigurableAssetId = true;
            DefaultAssetId = defaultAssetId;
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithDeviceHealth(
            DeviceHealthEnumeration initial = DeviceHealthEnumeration.NORMAL,
            bool deriveFromAlarms = false)
        {
            DeviceHealthRequested = true;
            InitialDeviceHealth = initial;
            DeriveDeviceHealth = deriveFromAlarms;
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithHealthAlarm(string name, AssetHealthAlarmKind kind, AmbConditionClass conditionClass)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A health alarm needs a name.", nameof(name));
            }
            if (conditionClass == null)
            {
                throw new ArgumentNullException(nameof(conditionClass));
            }
#if NET5_0_OR_GREATER
            if (!Enum.IsDefined(kind))
#else
            if (!Enum.IsDefined(typeof(AssetHealthAlarmKind), kind))
#endif
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
            EnsureUnique(name);
            HealthAlarms.Add(new HealthAlarmRequest(name, kind, conditionClass));
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithMaintenance(
            string name,
            AmbConditionClass conditionClass,
            Action<MaintenanceActivityDetails>? configure = null,
            Func<MaintenanceStateKind, CancellationToken, ValueTask>? onStateChangedAsync = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A maintenance activity needs a name.", nameof(name));
            }
            if (conditionClass == null)
            {
                throw new ArgumentNullException(nameof(conditionClass));
            }
            if (conditionClass.BaseClass != AmbConditionClassBase.Maintenance)
            {
                throw new ArgumentException(
                    $"'{conditionClass.Name}' is not a maintenance condition class; OPC 10000-110 §12.1 asks " +
                    "maintenance activities for MaintenanceConditionClassType or a subtype.",
                    nameof(conditionClass));
            }
            EnsureUnique(name);
            var details = new MaintenanceActivityDetails();
            configure?.Invoke(details);
            Maintenance.Add(new MaintenanceRequest(name, conditionClass, details, onStateChangedAsync));
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithDocumentationLinks(Action<IDocumentationLinksBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            DocumentationLinks ??= new DocumentationLinksRequest();
            configure(DocumentationLinks);
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithVersionInformation(
            string? hardwareRevision = null,
            string? softwareRevision = null,
            int? revisionCounter = null)
        {
            Structure.Version = new VersionRequest(hardwareRevision, softwareRevision, revisionCounter);
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithLocation(AssetLocationKind kind, string? value = null, bool writable = false)
        {
#if NET5_0_OR_GREATER
            if (!Enum.IsDefined(kind))
#else
            if (!Enum.IsDefined(typeof(AssetLocationKind), kind))
#endif
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
            Structure.LocationProperties[kind] = new LocationPropertyRequest(value, writable);
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithLocalTime(short offset, bool daylightSavingInOffset = false, bool writable = false)
        {
            Structure.LocalTime = new LocalTimeRequest(
                new TimeZoneDataType { Offset = offset, DaylightSavingInOffset = daylightSavingInOffset },
                writable);
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder LocatedIn(AssetLocationKind kind, string path)
        {
            if (kind is not AssetLocationKind.Hierarchical and not AssetLocationKind.Operational)
            {
                throw new ArgumentException(
                    $"{kind} locations have no location objects (OPC 10000-110 §13.5).",
                    nameof(kind));
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A location path needs at least one level.", nameof(path));
            }
            Structure.LocatedIn.Add((kind, path));
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder ClassifiedAs(ExpandedNodeId dictionaryEntry)
        {
            if (dictionaryEntry.IsNull)
            {
                throw new ArgumentException("The dictionary entry must not be null.", nameof(dictionaryEntry));
            }
            Structure.Classifications.Add(dictionaryEntry);
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithRequirements(Action<IAssetEntriesBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Structure.Requirements ??= new AssetEntriesRequest();
            configure(Structure.Requirements);
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder WithCapabilities(Action<IAssetEntriesBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            Structure.Capabilities ??= new AssetEntriesRequest();
            configure(Structure.Capabilities);
            return this;
        }

        /// <inheritdoc/>
        public IAssetBuilder RelatesTo(NodeId referenceTypeId, NodeId target)
        {
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentException("The reference type must not be null.", nameof(referenceTypeId));
            }
            if (target.IsNull)
            {
                throw new ArgumentException("The target must not be null.", nameof(target));
            }
            Structure.Relations.Add((referenceTypeId, target));
            return this;
        }

        /// <summary>
        /// Refuses a name an alarm or activity of the asset uses already: both
        /// become children of the asset with that browse name.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// An alarm or a maintenance activity of the asset has the name already.
        /// </exception>
        private void EnsureUnique(string name)
        {
            foreach (HealthAlarmRequest existing in HealthAlarms)
            {
                if (string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"The asset already has an alarm named '{name}'.", nameof(name));
                }
            }
            foreach (MaintenanceRequest existing in Maintenance)
            {
                if (string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"The asset already has a maintenance activity named '{name}'.",
                        nameof(name));
                }
            }
        }
    }

    /// <summary>
    /// A registered asset and what registering bound to it.
    /// </summary>
    internal sealed class AssetHandle : IAssetHandle
    {
        public AssetHandle(
            AssetManagement registry,
            BaseObjectState asset,
            IAsyncNodeManager owner,
            ISystemContext context,
            ushort diNamespaceIndex)
        {
            m_registry = registry;
            Asset = asset;
            Owner = owner;
            Context = context;
            DiNamespaceIndex = diNamespaceIndex;
            NodeId = asset.NodeId;
            BrowseName = asset.BrowseName;
        }

        /// <summary>
        /// Gets the asset object.
        /// </summary>
        public BaseObjectState Asset { get; }

        /// <summary>
        /// Gets the node manager that owns the asset.
        /// </summary>
        public IAsyncNodeManager Owner { get; }

        /// <summary>
        /// Gets the context of <see cref="Owner"/>.
        /// </summary>
        public ISystemContext Context { get; }

        /// <summary>
        /// Gets the index of the DI namespace.
        /// </summary>
        public ushort DiNamespaceIndex { get; }

        /// <summary>
        /// Gets or sets the configurable <c>AssetId</c>, when bound.
        /// </summary>
        public ConfigurableAssetId? ConfigurableAssetIdBinding { get; set; }

        /// <summary>
        /// Gets or sets the alias of the asset in
        /// <c>AssetsByProductInstanceUri</c>, once listed.
        /// </summary>
        public AssetAlias? ProductInstanceUriAlias { get; set; }

        /// <summary>
        /// Gets or sets the alias of the asset in <c>AssetsByAssetId</c>,
        /// once listed.
        /// </summary>
        public AssetAlias? AssetIdAlias { get; set; }

        /// <inheritdoc/>
        public NodeId NodeId { get; }

        /// <inheritdoc/>
        public QualifiedName BrowseName { get; }

        /// <inheritdoc/>
        public string ProductInstanceUri => AssetIdentification.ReadString(
            AssetIdentification.FindProperty(
                Context,
                Asset,
                DiNamespaceIndex,
                AssetIdentification.ProductInstanceUri)) ??
            string.Empty;

        /// <inheritdoc/>
        public string? AssetId
        {
            get
            {
                string? value = AssetIdentification.ReadString(
                    ConfigurableAssetIdBinding?.Variable ??
                    AssetIdentification.FindProperty(Context, Asset, DiNamespaceIndex, AssetIdentification.AssetId));
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }

        /// <inheritdoc/>
        public bool IsAssetIdConfigurable => ConfigurableAssetIdBinding != null;

        /// <summary>
        /// Gets whether the asset has a <c>2:AssetId</c>, bound or its own, so
        /// it is listed in <c>AssetsByAssetId</c> (§8.2.3).
        /// </summary>
        public bool HasAssetIdProperty =>
            ConfigurableAssetIdBinding != null ||
            AssetIdentification.FindProperty(Context, Asset, DiNamespaceIndex, AssetIdentification.AssetId) != null;

        /// <summary>
        /// Follows the values of the <c>ProductInstanceUri</c> and the
        /// <c>AssetId</c> of the asset: a change reported through
        /// <c>ClearChangeMasks</c> calls <paramref name="changed"/>.
        /// </summary>
        internal void WatchIdentification(Action<AssetHandle> changed)
        {
            NodeStateChangedHandler handler = (_, _, changes) =>
            {
                if ((changes & NodeStateChangeMasks.Value) != 0)
                {
                    changed(this);
                }
            };
            lock (m_watchLock)
            {
                m_identificationChanged = handler;
                foreach (string name in s_watchedProperties)
                {
                    if (AssetIdentification.FindProperty(Context, Asset, DiNamespaceIndex, name) is
                        BaseVariableState property)
                    {
                        property.StateChanged += handler;
                        m_watched.Add(property);
                    }
                }
            }
        }

        /// <summary>
        /// Stops following the identification of the asset.
        /// </summary>
        internal void UnwatchIdentification()
        {
            lock (m_watchLock)
            {
                foreach (BaseVariableState property in m_watched)
                {
                    property.StateChanged -= m_identificationChanged;
                }
                m_watched.Clear();
                m_identificationChanged = null;
            }
        }

        /// <inheritdoc/>
        IAssetHealth? IAssetHandle.Health => Health;

        /// <summary>
        /// Gets or sets the health status, when registering asked for it.
        /// </summary>
        public AssetHealth? Health { get; set; }

        /// <inheritdoc/>
        IAssetMaintenance? IAssetHandle.Maintenance => Maintenance;

        /// <summary>
        /// Gets or sets the maintenance activities, when registering asked
        /// for them.
        /// </summary>
        public AssetMaintenance? Maintenance { get; set; }

        /// <inheritdoc/>
        IDocumentationLinks? IAssetHandle.DocumentationLinks => DocumentationLinks;

        /// <summary>
        /// Gets or sets the <c>DocumentationLinks</c> AddIn, when registering
        /// asked for it.
        /// </summary>
        public AssetDocumentationLinks? DocumentationLinks { get; set; }

        /// <summary>
        /// Gets or sets what registering added for the version information,
        /// locations, classification and structure.
        /// </summary>
        public AssetStructure? Structure { get; set; }

        /// <inheritdoc/>
        public ValueTask<int> IncrementRevisionCounterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetStructure structure = Structure ??
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Asset '{0}' has no RevisionCounter.",
                    BrowseName);
            return new ValueTask<int>(structure.IncrementRevisionCounter());
        }

        /// <summary>
        /// Gets whether the asset has an OPC 10000-100 child of the name,
        /// directly.
        /// </summary>
        public bool HasDiChild(string name)
        {
            return Asset.FindChildWithQualifiedName(Context, new QualifiedName(name, DiNamespaceIndex)) != null;
        }

        /// <inheritdoc/>
        public bool IsRegistered => Volatile.Read(ref m_registered) != 0;

        /// <inheritdoc/>
        public ValueTask UnregisterAsync(CancellationToken cancellationToken = default)
        {
            return m_registry.UnregisterAsync(this, cancellationToken);
        }

        /// <summary>
        /// Marks the handle as registered.
        /// </summary>
        internal void MarkRegistered()
        {
            Volatile.Write(ref m_registered, 1);
        }

        /// <summary>
        /// Marks the handle as unregistered.
        /// </summary>
        /// <returns><see langword="true"/> for the call that unregistered it.</returns>
        internal bool MarkUnregistered()
        {
            return Interlocked.Exchange(ref m_registered, 0) != 0;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return BrowseName.ToString();
        }

        private static readonly string[] s_watchedProperties =
            [AssetIdentification.ProductInstanceUri, AssetIdentification.AssetId];

        private readonly AssetManagement m_registry;
        private readonly Lock m_watchLock = new();
        private readonly List<BaseVariableState> m_watched = [];
        private NodeStateChangedHandler? m_identificationChanged;
        private int m_registered;
    }
}
