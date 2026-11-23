// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Enriches explicitly verified companion declarations, guarded by their engineering-unit identities.
    /// </summary>
    /// <remarks>
    /// Namespace indexes, display names and dimensions never select a mapping. Inherited declarations
    /// retain their source identity; unrelated instances and overrides receive no inferred annotation.
    /// </remarks>
    public sealed class WotCompanionQuantityKindMappingProvider : IWotQuantityKindMappingProvider
    {
        /// <summary>
        /// Creates the verified companion-declaration provider.
        /// </summary>
        public WotCompanionQuantityKindMappingProvider()
        {
        }

        /// <inheritdoc/>
        public bool TryGetMapping(
            string declarationId,
            string unitNamespaceUri,
            int unitId,
            [NotNullWhen(true)] out WotQuantityKindMapping? mapping)
        {
            if (declarationId is null)
            {
                throw new ArgumentNullException(nameof(declarationId));
            }
            if (unitNamespaceUri is null)
            {
                throw new ArgumentNullException(nameof(unitNamespaceUri));
            }
            mapping = null;
            if (!string.Equals(unitNamespaceUri.TrimEnd('/'),
                    "http://www.opcfoundation.org/UA/units/un/cefact", StringComparison.OrdinalIgnoreCase) ||
                !s_mappings.TryGetValue(declarationId, out (int UnitId, WotQuantityKindMapping Mapping) entry) ||
                entry.UnitId != unitId)
            {
                return false;
            }
            mapping = entry.Mapping;
            return true;
        }

        /// <summary>
        /// Builds the allowlist from the pinned official NodeSets cited by each mapping.
        /// </summary>
        private static Dictionary<string, (int UnitId, WotQuantityKindMapping Mapping)> CreateMappings()
        {
            var mappings = new Dictionary<string, (int UnitId, WotQuantityKindMapping Mapping)>(StringComparer.Ordinal);
            const string pumps = "Pumps";
            foreach (int id in new[] { 13133, 13135, 13137, 13153, 13155, 13161, 13163 })
            {
                Add(pumps, id, 4403510, "LinearVelocity");
            }
            foreach (int id in new[] { 9347, 8127, 7703, 7706, 7713, 7725, 7753, 7758, 13145, 12719 })
            {
                Add(pumps, id, 4405559, "Frequency");
            }
            Add(pumps, 8104, 4404789, "DynamicViscosity");
            Add(pumps, 8110, 21300, "KinematicViscosity");
            Add(pumps, 7745, 12878, "SoundPowerLevel");
            Add(pumps, 12713, 12878, "SoundPowerLevel");
            Add(pumps, 7747, 12878, "SoundPressureLevel");
            Add(pumps, 12717, 12878, "SoundPressureLevel");
            Add(pumps, 7757, 4933459, "MassFlowRate");
            Add(pumps, 12673, 4933459, "MassFlowRate");
            Add(pumps, 9315, 4600375, "MassFlowRate");
            Add("Machinery/Energy", 6061, 4933459, "MassFlowRate");
            Add("PNEM", 6081, 4469812, "ReactivePower", fileName: "Opc.Ua.PnEm.NodeSet2.xml");
            Add("ECM", 6081, 4469812, "ReactivePower");
            Add("PlasticsRubber/TCD", 6245, 4405556, "Frequency",
                folder: "PlasticsRubber/TCD/1.01");
            Add(pumps, 12649, 4665393, null,
                "DynamicViscosity describes kinematic viscosity but declares a pressure-volume flow unit.");
            Add(pumps, 12663, 21300, null,
                "KinematicViscosity declares a kinematic-viscosity unit but describes dynamic viscosity.");
            return mappings;

            void Add(
                string model,
                int id,
                int unitId,
                string? kind,
                string? warning = null,
                string? folder = null,
                string? fileName = null)
            {
                string declarationId = $"nsu=http://opcfoundation.org/UA/{model}/;i={id}";
                string source = k_sourceRoot +
                    (folder ?? model) +
                    "/" +
                    (fileName ?? ("Opc.Ua." + model.Replace('/', '.') + ".NodeSet2.xml"));
                mappings.Add(declarationId, (unitId, new WotQuantityKindMapping(
                    source,
                    kind is null ? null : WotUnitMigration.QuantityKindNamespace + kind,
                    warning)));
            }
        }

        /// <summary>The pinned official NodeSet revision supplying the declaration evidence.</summary>
        private const string k_sourceRoot =
            "https://github.com/OPCFoundation/UA-Nodeset/blob/928a9f4b7378dd2f6cd1a3c88690d5c03857a7b4/";

        /// <summary>Verified declaration identities and expected engineering-unit identifiers.</summary>
        private static readonly Dictionary<string, (int UnitId, WotQuantityKindMapping Mapping)> s_mappings =
            CreateMappings();
    }
}
