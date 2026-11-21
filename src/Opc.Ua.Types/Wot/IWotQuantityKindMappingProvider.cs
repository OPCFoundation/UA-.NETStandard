// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Resolves quantity kinds from a declaration's portable identity and engineering-unit identity.
    /// </summary>
    public interface IWotQuantityKindMappingProvider
    {
        /// <summary>
        /// Tries to resolve verified declaration semantics without interpreting names or unit dimensions.
        /// </summary>
        /// <param name="declarationId">The namespace-URI-qualified portable declaration NodeId.</param>
        /// <param name="unitNamespaceUri">The engineering-unit authority.</param>
        /// <param name="unitId">The engineering-unit identifier within that authority.</param>
        /// <param name="mapping">The quantity kind and provenance, or a known source inconsistency.</param>
        /// <returns>Whether a verified mapping or source inconsistency is known.</returns>
        bool TryGetMapping(
            string declarationId,
            string unitNamespaceUri,
            int unitId,
            [NotNullWhen(true)] out WotQuantityKindMapping? mapping);
    }
}
