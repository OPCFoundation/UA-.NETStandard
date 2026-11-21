// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Explicit authority bindings for the unchanged generic xRegistry Methods.
    /// Typed provisioning needs no configured aliases.
    /// </summary>
    public sealed class WotRegistryIdentityBindings
    {
        /// <summary>
        /// Gets or sets the exact supplied group identifier bindings.
        /// </summary>
        public ArrayOf<WotRegistryGroupIdentity> Groups { get; set; } = [];

        /// <summary>
        /// Gets or sets the exact supplied resource identifier bindings, scoped by owning group.
        /// </summary>
        public ArrayOf<WotRegistryResourceIdentity> Resources { get; set; } = [];
    }

    /// <summary>
    /// Binds a generic group identifier to one exact catalogue authority and document kind.
    /// An existing unambiguous allocation is preserved; a new allocation uses the typed domain.
    /// </summary>
    /// <param name="GroupId">The configured generic identifier or an established group identifier.</param>
    /// <param name="Kind">ThingDescription or ThingModel, never All.</param>
    /// <param name="CatalogUri">The exact absolute catalogue URI.</param>
    public sealed record WotRegistryGroupIdentity(string GroupId, WoTDocumentKindEnum Kind, string CatalogUri);

    /// <summary>
    /// Binds a generic resource identifier to its exact source identity within one owning group.
    /// </summary>
    /// <param name="GroupId">The owning assigned group identifier or its configured alias.</param>
    /// <param name="ResourceId">The supplied resource identifier.</param>
    /// <param name="SourceId">The exact absolute ThingId or ModelId.</param>
    public sealed record WotRegistryResourceIdentity(string GroupId, string ResourceId, string SourceId);
}
