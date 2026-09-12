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
using System.Linq;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    internal static class WotRegistryTestAuthorities
    {
        public static WotRegistryIdentityBindings ForResources(params string[] aliases)
        {
            return new WotRegistryIdentityBindings
            {
                Groups = s_groups.ToArrayOf(),
                Resources = s_groups.SelectMany(group => aliases.Select(alias =>
                    new WotRegistryResourceIdentity(group.GroupId, alias, "urn:" + alias))).ToArrayOf()
            };
        }

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
