/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Options for the WoT Connectivity 1.1 registry NodeManager. These are
    /// bindable from configuration (only the simple-typed members) and augmented
    /// at runtime with the persistence store, binder registry and management
    /// access policy.
    /// </summary>
    public sealed class WotRegistryServerOptions
    {
        /// <summary>
        /// Gets or sets the folder used by the file-backed registry store. When
        /// <c>null</c> the registry is kept in memory only.
        /// </summary>
        public string? StorageFolder { get; set; }

        /// <summary>
        /// Gets or sets the store that holds the document bytes. When <c>null</c> the documents
        /// live wherever the registry store puts them — in the server process for an in-memory
        /// registry, or in the registry folder for a file-backed one.
        /// <para>
        /// Substituting a store is what lets a registry run in a high-availability or distributed
        /// deployment, because the documents then live somewhere every node can reach. The store is
        /// keyed by the SHA-256 content digest of the document, so writes are idempotent and two
        /// resource versions with identical bytes share one entry. A supplied store owns the
        /// durability of the bytes it holds; the registry still writes and switches its own
        /// manifest atomically, which is safe because content-addressed documents are immutable and
        /// are always written before the manifest that references them.
        /// </para>
        /// </summary>
        public IXRegistryResourceStore? ResourceStore { get; set; }

        /// <summary>
        /// Gets or sets whether the registry automatically re-projects after every
        /// content mutation. Defaults to <c>true</c>.
        /// </summary>
        public bool AutoRefresh { get; set; } = true;

        /// <summary>
        /// Gets or sets whether unsupported binding forms fail a strict closure
        /// (rather than materializing degraded nodes).
        /// </summary>
        public bool StrictBindings { get; set; }

        /// <summary>
        /// Gets or sets the policy applied to hosted logical Resource unload and delete operations.
        /// </summary>
        public WoTDeletePolicyEnum DeletePolicy { get; set; } = WoTDeletePolicyEnum.Reject;

        /// <summary>
        /// Gets or sets the algorithm for importing document-set partitions.
        /// Independent readable-model normalization is opt-in and never
        /// relaxes native or archive authority.
        /// </summary>
        public WotDocumentSetMode DocumentSetMode { get; set; } = WotDocumentSetMode.PartitionReconstruction;

        /// <summary>
        /// Gets or sets the provider of actual projection-host forms.
        /// A provider registered in DI takes precedence. No provider means
        /// missing host forms fail resolution rather than borrowing source forms.
        /// </summary>
        public IWotProjectionFormProvider? ProjectionFormProvider { get; set; }

        /// <summary>
        /// Gets or sets how a superseded projection generation is retired after
        /// a successful version switch.
        /// </summary>
        public WotProjectionRetirementPolicy RetirementPolicy { get; set; } =
            WotProjectionRetirementPolicy.Migrate;

        /// <summary>
        /// Gets or sets the id of the group into which legacy 1.02 assets are
        /// registered as Thing Description resources.
        /// </summary>
        public string LegacyGroupId { get; set; } = WotRegistryGroups.ThingDescriptions;

        /// <summary>
        /// Gets the resource bounds enforced by the registry service.
        /// </summary>
        public WotRegistryPersistenceBounds Bounds { get; } = new WotRegistryPersistenceBounds();

        /// <summary>
        /// Gets or sets explicit source-authority bindings for inherited generic creation Methods.
        /// Typed provisioning accepts arbitrary exact authorities without configured aliases.
        /// </summary>
        public WotRegistryIdentityBindings IdentityBindings { get; set; } = new();

        /// <summary>
        /// Gets or sets the management access policy used to secure the registry
        /// management Methods.
        /// </summary>
        public WotManagementAccessPolicy ManagementAccess { get; set; }
            = new WotManagementAccessPolicy();

        /// <summary>
        /// Gets or sets optional generic xRegistry event configuration. WoT materialization
        /// events remain available independently and may coexist with these events.
        /// </summary>
        public XRegistryServerOptions XRegistryEvents { get; set; } = new XRegistryServerOptions();

        /// <summary>
        /// Gets the WoT binding capabilities advertised by the registry
        /// <c>SupportedBindings</c> object. Empty in this phase (no concrete
        /// protocol binders are registered).
        /// </summary>
        public IList<WoTBindingCapabilityDataType> SupportedBindings { get; }
            = [];
    }
}
