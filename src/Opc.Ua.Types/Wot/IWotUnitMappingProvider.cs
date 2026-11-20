// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Resolves an engineering unit's authority and identifier into explicit semantic annotations.
    /// </summary>
    public interface IWotUnitMappingProvider
    {
        /// <summary>
        /// Tries to resolve a unit IRI and an optional quantity-kind IRI without interpreting display names.
        /// </summary>
        /// <param name="namespaceUri">The EUInformation unit authority.</param>
        /// <param name="unitId">The identifier in that authority.</param>
        /// <param name="unitIri">The unit IRI when a mapping exists.</param>
        /// <param name="quantityKindIri">The explicitly mapped quantity kind, or null when ambiguous.</param>
        /// <returns>Whether an explicit unit mapping exists.</returns>
        bool TryGetMapping(string namespaceUri, int unitId, out string unitIri, out string? quantityKindIri);
    }
}
