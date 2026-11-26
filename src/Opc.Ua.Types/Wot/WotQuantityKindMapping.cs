// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// A declaration-specific quantity-kind annotation with its evidence and optional source warning.
    /// </summary>
    public sealed class WotQuantityKindMapping
    {
        /// <summary>
        /// Creates a verified annotation or a warning that prevents quantity-kind enrichment.
        /// </summary>
        /// <param name="sourceUri">The source establishing the declaration's semantics.</param>
        /// <param name="quantityKindIri">The quantity-kind IRI, or null when no kind is established.</param>
        /// <param name="warning">An optional explanation of inconsistent source metadata.</param>
        public WotQuantityKindMapping(string sourceUri, string? quantityKindIri = null, string? warning = null)
        {
            SourceUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
            QuantityKindIri = quantityKindIri;
            Warning = warning;
        }

        /// <summary>
        /// Gets the source establishing the declaration's semantics.
        /// </summary>
        public string SourceUri { get; }

        /// <summary>
        /// Gets the quantity-kind IRI, or null when no kind is established.
        /// </summary>
        public string? QuantityKindIri { get; }

        /// <summary>
        /// Gets the optional explanation of inconsistent source metadata.
        /// </summary>
        public string? Warning { get; }
    }
}
