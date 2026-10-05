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
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Structure;
using ConformanceUnitNames = Opc.Ua.AMB.Server.ConformanceUnits;
using ServerProfileUris = Opc.Ua.AMB.Server.ServerProfiles;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Decides which OPC 10000-110 conformance units and facets a server
    /// meets, from the assets it has registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A unit whose rule reads "all manageable assets" is met only when at
    /// least one asset is registered and every one of them satisfies it; with
    /// no asset the rule would be vacuously true, which would let an empty
    /// server claim the facet.
    /// </para>
    /// <para>
    /// A facet is advertised only when every conformance unit it mandates is
    /// advertised, the rule <c>DiNodeManager</c> applies to its facets. The
    /// unit names are qualified in namespace 0, as Device Integration and
    /// Machinery publish theirs.
    /// </para>
    /// </remarks>
    internal static class AmbConformance
    {
        /// <summary>
        /// The OPC 10000-110 server facets with their mandatory units
        /// (Table 56).
        /// </summary>
        private static readonly ArrayOf<AmbServerFacet> s_facets =
        [
            new(ServerProfileUris.BaseServer, [ConformanceUnitNames.AssetIdentification])
        ];

        /// <summary>
        /// Evaluates the conformance units the registered assets meet.
        /// </summary>
        /// <param name="assets">The registered assets.</param>
        /// <param name="persistentStore">
        /// Whether what clients configure survives a restart.
        /// </param>
        /// <param name="interfacesOnTypes">
        /// Whether the alarm and condition types of the assets implement the
        /// AMB interfaces (<see cref="AmbServerOptions.UseServerDefinedAlarmTypes"/>).
        /// </param>
        /// <param name="typeTree">The type tree of the server.</param>
        /// <param name="isDictionaryEntry">Whether a node is a dictionary entry the AMB manager defined.</param>
        public static ArrayOf<QualifiedName> EvaluateUnits(
            ArrayOf<AssetHandle> assets,
            bool persistentStore,
            bool interfacesOnTypes,
            ITypeTable typeTree,
            Func<NodeId, bool> isDictionaryEntry)
        {
            var units = new List<QualifiedName>();
            if (assets.Count == 0)
            {
                return units.ToArrayOf();
            }

            bool identified = true;
            bool configurable = persistentStore;
            bool byProductInstanceUri = true;
            bool byAssetId = true;
            bool anyAssetId = false;
            bool deviceHealth = true;
            bool healthAlarms = true;
            bool maintenance = true;
            bool anyLinks = false;
            bool editableLinks = persistentStore;
            bool userLinks = persistentStore;
            foreach (AssetHandle asset in assets)
            {
                identified &= asset.ProductInstanceUri.Length > 0;
                configurable &= asset.IsAssetIdConfigurable;
                byProductInstanceUri &= asset.ProductInstanceUriAlias != null;
                if (asset.HasAssetIdProperty)
                {
                    anyAssetId = true;
                    byAssetId &= asset.AssetIdAlias != null;
                }
                deviceHealth &= asset.HasDiChild(Opc.Ua.Di.BrowseNames.DeviceHealth);
                healthAlarms &= asset.Health?.AlarmCount > 0;
                maintenance &= asset.Maintenance?.ActivityCount > 0;
                anyLinks |= asset.DocumentationLinks?.PublishedLinkCount > 0;
                editableLinks &= asset.DocumentationLinks?.HasEditableLink == true;
                userLinks &= asset.DocumentationLinks?.AllowsUserLinks == true;
            }

            if (identified)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.AssetIdentification));
            }
            if (configurable)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.ConfigurableAssetIdentification));
            }

            // Every asset listed, in categories whose NodeVersion and model
            // change events follow the aliases (§8.2); AssetsByAssetId lists
            // the assets that have an AssetId, at least one (§8.2.3).
            if (byProductInstanceUri)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.AssetDiscoveryByProductInstanceUri));
            }
            if (byAssetId && anyAssetId)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.AssetDiscoveryByAssetId));
            }

            if (deviceHealth)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.AssetHealthStatusBase));
            }

            // Every health alarm the manager creates reports an AMB condition
            // class (§9.5). Root causes ask the alarm types to implement
            // IRootCauseIndicationType (§9.4.1), which only the server-defined
            // types do; without them the interface is on the instances.
            if (healthAlarms)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.AssetHealthStatusAlarms));
                if (interfacesOnTypes)
                {
                    units.Add(new QualifiedName(ConformanceUnitNames.AssetHealthStatusRootCauses));
                }
                units.Add(new QualifiedName(ConformanceUnitNames.AssetHealthStatusAlarmCategories));
            }

            // The maintenance condition types have to implement
            // IMaintenanceEventType (§12.1), which only the server-defined
            // type does.
            if (maintenance && interfacesOnTypes)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.CurrentAndFutureMaintenanceActivities));
            }

            // Links users change or add have to survive a restart (§10.5.3),
            // so the editing units need a persistent store.
            if (anyLinks)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.DocumentationLinksBase));
            }
            if (editableLinks)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.DocumentationLinksEditBase));
            }
            if (userLinks)
            {
                units.Add(new QualifiedName(ConformanceUnitNames.DocumentationLinksEditAdvanced));
            }

            AssetInspection.EvaluateStructure(assets, typeTree, isDictionaryEntry, units);
            return units.ToArrayOf();
        }

        /// <summary>
        /// Selects the facets whose mandatory units are all met.
        /// </summary>
        /// <param name="units">The conformance units the server meets.</param>
        public static ArrayOf<string> EvaluateProfiles(ArrayOf<QualifiedName> units)
        {
            var met = new HashSet<QualifiedName>();
            foreach (QualifiedName unit in units)
            {
                met.Add(unit);
            }

            var profiles = new List<string>();
            foreach (AmbServerFacet facet in s_facets)
            {
                if (facet.IsMetBy(met))
                {
                    profiles.Add(facet.Profile);
                }
            }
            return profiles.ToArrayOf();
        }

        /// <summary>
        /// A server facet and the conformance units it mandates.
        /// </summary>
        /// <param name="Profile">The facet URI.</param>
        /// <param name="MandatoryUnits">The names of the mandatory units.</param>
        private readonly record struct AmbServerFacet(string Profile, ArrayOf<string> MandatoryUnits)
        {
            /// <summary>
            /// Gets whether every mandatory unit is in <paramref name="units"/>.
            /// </summary>
            public bool IsMetBy(HashSet<QualifiedName> units)
            {
                foreach (string unit in MandatoryUnits)
                {
                    if (!units.Contains(new QualifiedName(unit)))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
