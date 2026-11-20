/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Export
{
    /// <summary>
    /// What a NodeSet2 comparison is bounded by, and how it reads a name a
    /// document writes where a NodeId is expected.
    /// </summary>
    /// <remarks>
    /// Comparing two documents reads untrusted XML, so the one limit a caller
    /// has to be able to state is how deeply nested a document may be before
    /// it is rejected. Callers that hold a richer set of limits project the
    /// relevant one onto this type rather than handing the comparison an
    /// options object it would ignore most of. Beside it sits the alias policy
    /// the comparison applies, which is the caller's to state for the same
    /// reason: what an undeclared name means is not something a comparison of
    /// two documents can decide for itself.
    /// </remarks>
    public sealed class NodeSetComparisonOptions
    {
        /// <summary>
        /// Gets or sets the maximum XML nesting depth accepted when reading a
        /// NodeSet2 document.
        /// </summary>
        public int MaxXmlDepth { get; set; } = 256;

        /// <summary>
        /// Gets or sets the policy consulted for a name a document uses but
        /// does not declare in its own <c>&lt;Aliases&gt;</c> table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only <see cref="NodeSetComparer.CompareEquivalent"/> reads names
        /// through a table at all, and it resolves each document through its
        /// own declarations first: what a document declares always wins. This
        /// is what answers for a name it does not declare.
        /// </para>
        /// <para>
        /// The default of <c>null</c> resolves nothing further, which is the
        /// only reading a comparison of two arbitrary NodeSet2 documents may
        /// take: a document that writes <c>HasComponent</c> without declaring
        /// it cannot be imported at all, so reading it as <c>i=47</c> would
        /// report an unloadable document as equivalent to a loadable one. A
        /// caller whose profile states which names may be written without
        /// being declared - the WoT Binding, for instance - supplies that
        /// policy here.
        /// </para>
        /// </remarks>
        public INodeSetAliasResolver? AliasResolver { get; set; }

        /// <summary>
        /// Validates the option values and throws when a limit is not positive.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when any configured limit is not strictly positive.
        /// </exception>
        public void Validate()
        {
            if (MaxXmlDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxXmlDepth),
                    MaxXmlDepth,
                    "The configured limit must be a positive value.");
            }
        }
    }
}
