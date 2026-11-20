// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Creates deterministic identity bindings for registry groups and test resource aliases.
    /// </summary>
    internal static class WotRegistryTestAuthorities
    {
        /// <summary>
        /// Creates group and resource identity bindings for each supplied alias.
        /// </summary>
        /// <param name="aliases">The resource aliases to bind to deterministic test authorities.</param>
        public static WotRegistryIdentityBindings ForResources(params string[] aliases)
        {
            return new WotRegistryIdentityBindings
            {
                Groups = s_groups.ToArrayOf(),
                Resources = s_groups.SelectMany(group => aliases.Select(alias =>
                    new WotRegistryResourceIdentity(group.GroupId, alias, "urn:" + alias))).ToArrayOf()
            };
        }

        /// <summary>
        /// Finds a resource in the snapshot by group identifier and resource alias.
        /// </summary>
        /// <param name="snapshot">The WoT registry snapshot.</param>
        /// <param name="groupId">The group ID.</param>
        /// <param name="resourceAlias">The resource alias.</param>
        public static WotResource? FindResource(WotRegistrySnapshot snapshot, string groupId, string resourceAlias)
        {
            WotResourceGroup? group = snapshot.FindGroup(groupId);
            if (group is null)
            {
                WotRegistryGroupIdentity identity = s_groups.Single(candidate => candidate.GroupId == groupId);
                group = snapshot.Groups.Values.SingleOrDefault(candidate => candidate.Kind == identity.Kind &&
                    string.Equals(candidate.CatalogUri, identity.CatalogUri, StringComparison.Ordinal));
            }
            return group?.Resources.Values.SingleOrDefault(resource =>
                string.Equals(resource.SourceId, "urn:" + resourceAlias, StringComparison.Ordinal));
        }

        private static readonly WotRegistryGroupIdentity[] s_groups =
        [
            new(WotRegistryGroups.ThingDescriptions, WoTDocumentKindEnum.ThingDescription, "urn:test:catalogue:td"),
            new(WotRegistryGroups.ThingModels, WoTDocumentKindEnum.ThingModel, "urn:test:catalogue:tm"),
            new("MyGroup", WoTDocumentKindEnum.ThingDescription, "urn:test:catalogue:custom"),
            new("sensors", WoTDocumentKindEnum.ThingDescription, "urn:test:catalogue:sensors"),
            new("secure-files", WoTDocumentKindEnum.ThingDescription, "urn:test:catalogue:secure-files"),
            new("labelgroup", WoTDocumentKindEnum.ThingDescription, "urn:test:catalogue:labels")
        ];
    }
}
