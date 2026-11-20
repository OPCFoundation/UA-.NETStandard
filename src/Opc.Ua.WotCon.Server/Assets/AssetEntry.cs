// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Per-asset state held by the <see cref="AssetRegistry"/>.
    /// </summary>
    internal sealed class AssetEntry
    {
        /// <summary>
        /// Initializes a new <c>AssetEntry</c> instance.
        /// </summary>
        /// <param name="name">The name used to initialize <c>AssetEntry</c>.</param>
        /// <param name="asset">The asset used to initialize the instance.</param>
        public AssetEntry(string name, IWoTAssetState asset)
        {
            Name = name;
            Asset = asset;
            UnboundTypeDefinitionId = asset.TypeDefinitionId;
        }

        /// <summary>
        /// Gets the registry name of the asset.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the asset state exposed in the server address space.
        /// </summary>
        public IWoTAssetState Asset { get; }

        /// <summary>
        /// Gets the unbound type definition ID for <c>AssetEntry</c>.
        /// </summary>
        public NodeId UnboundTypeDefinitionId { get; }

        /// <summary>
        /// Gets or sets the native graph for <c>AssetEntry</c>.
        /// </summary>
        public WotLegacyPreparedGraph? NativeGraph { get; set; }

        /// <summary>
        /// Gets or sets the FileType manager serving the asset's content.
        /// </summary>
        public WotAssetFileManager? FileManager { get; set; }

        /// <summary>
        /// Gets or sets the provider responsible for the asset's source content.
        /// </summary>
        public IWotAssetProvider? Provider { get; set; }

        /// <summary>
        /// Gets or sets the registry mirror for <c>AssetEntry</c>.
        /// </summary>
        public AssetRegistryMirror? RegistryMirror { get; set; }

        /// <summary>
        /// Gets or sets the deletion status for <c>AssetEntry</c>.
        /// </summary>
        public ServiceResult? DeletionStatus { get; set; }

        /// <summary>
        /// Gets or sets the registry delete requires recovery for <c>AssetEntry</c>.
        /// </summary>
        public bool RegistryDeleteRequiresRecovery { get; set; }

        /// <summary>
        /// Variables created from TD properties keyed by NodeId.
        /// </summary>
        public Dictionary<NodeId, (BaseDataVariableState Variable, WotPropertyTag Tag)> Properties { get; } = [];

        /// <summary>
        /// Methods created from TD actions keyed by NodeId.
        /// </summary>
        public Dictionary<NodeId, (MethodState Method, WotActionTag Tag)> Actions { get; } = [];

        /// <summary>
        /// EventTypes created from TD events keyed by EventType NodeId.
        /// </summary>
        public Dictionary<NodeId, (BaseObjectTypeState EventType, WotEventTag Tag)> Events { get; } = [];

        /// <summary>
        /// Identifies the current Thing Description generation for event
        /// callbacks.
        /// </summary>
        /// <remarks>
        /// A provider callback runs on the provider's own thread and can
        /// arrive after the TD it belongs to has been replaced. The callback
        /// captures the token that was current when it subscribed, so a
        /// stale occurrence can be dropped instead of being reported against
        /// an event type that no longer exists.
        /// </remarks>
        public object EventGeneration { get; set; } = new();

        /// <summary>
        /// Active observation callbacks keyed by monitored-item id, used to
        /// route value changes from the provider back to the right variable.
        /// </summary>
        public Dictionary<uint, OnWotValueChange> SubscriberCallbacks { get; } = [];
    }

    /// <summary>
    /// The registry and assigned resource identity backing a mirrored asset.
    /// </summary>
    /// <param name="Registry">The WoT registry service.</param>
    /// <param name="GroupId">The group ID.</param>
    /// <param name="ResourceId">The resource ID.</param>
    /// <param name="Generation">The registry generation.</param>
    internal sealed record AssetRegistryMirror(
        IWotRegistryService Registry, string GroupId, string ResourceId, long Generation);
}
