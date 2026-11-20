/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Presents a condition to one client's select clauses with <c>Retain</c> forced to
    /// <c>false</c>.
    /// </summary>
    /// <remarks>
    /// OPC UA Part 9, 5.5.2 requires the trailing event a condition produces on its way out
    /// of a client's where clause to carry a client specific <c>Retain = false</c>, whatever
    /// the server itself retains. The filter target behind that event is shared by every
    /// monitored item the condition is reported to, so the override cannot be written into
    /// it; this wrapper applies it while the fields for a single item are read and leaves
    /// the shared target untouched.
    /// </remarks>
    internal sealed class FilteredRetainTarget : IFilterTarget
    {
        private readonly IFilterTarget m_target;

        /// <summary>
        /// Wraps <paramref name="target"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is <c>null</c>.</exception>
        public FilteredRetainTarget(IFilterTarget target)
        {
            m_target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>
        /// The wrapped filter target.
        /// </summary>
        public IFilterTarget Target => m_target;

        /// <inheritdoc/>
        public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
        {
            return m_target.IsTypeOf(context, typeDefinitionId);
        }

        /// <inheritdoc/>
        public Variant GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            ArrayOf<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            Variant value = m_target.GetAttributeValue(
                context,
                typeDefinitionId,
                relativePath,
                attributeId,
                indexRange);

            // only a Retain the target actually resolves is overridden; a clause the type
            // check or the browse path rejects stays null so the field list keeps its shape.
            if (attributeId == Attributes.Value &&
                relativePath.Count == 1 &&
                relativePath[0] == BrowseNames.Retain &&
                value.TryGetValue(out bool _))
            {
                return new Variant(false);
            }

            return value;
        }
    }
}
