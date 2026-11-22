// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Runtime helper used by the source-generated
    /// <c>{Type}Record.EventFilters.Build</c> factories. Composes an
    /// <see cref="EventFilter"/> whose <c>SelectClauses</c> come from
    /// a supplied <see cref="EventRecordDecoderRegistry"/> (or
    /// <see cref="EventRecordDecoderRegistry.Default"/>) and whose
    /// <c>WhereClause</c> restricts events to the supplied event
    /// type id.
    /// </summary>
    /// <remarks>
    /// The composed <c>SelectClauses</c> are a superset across every
    /// decoder registered in the supplied registry, so the same
    /// filter shape can decode every record type the registry knows
    /// about. The registry's
    /// <see cref="EventRecordDecoderRegistry.Decode"/> path remaps
    /// the server-returned positional fields to each decoder's own
    /// layout before invocation, so no positional alignment is
    /// required between filter and decoder.
    /// </remarks>
    public static class EventFilterFactory
    {
        /// <summary>
        /// Builds an event filter selecting the registry's composed
        /// standard fields and restricting events to
        /// <paramref name="eventTypeId"/> via an <c>OfType</c> where
        /// clause. When <paramref name="eventTypeId"/> equals
        /// <see cref="ObjectTypeIds.BaseEventType"/> the where
        /// clause is omitted.
        /// </summary>
        /// <param name="eventTypeId">The event type to restrict
        /// to.</param>
        /// <param name="registry">The decoder registry whose
        /// composed <c>StandardFields</c> form the select clauses.
        /// When <c>null</c>,
        /// <see cref="EventRecordDecoderRegistry.Default"/> is
        /// used.</param>
        public static EventFilter Create(
            NodeId eventTypeId,
            EventRecordDecoderRegistry? registry = null)
        {
            EventRecordDecoderRegistry r = registry ?? EventRecordDecoderRegistry.Default;
            var filter = new EventFilter();
            bool hasConditionIdClause = false;

            foreach (QualifiedName[] path in r.StandardFields)
            {
                if (path.Length == 0)
                {
                    hasConditionIdClause = true;
                }
                var clause = new SimpleAttributeOperand
                {
                    TypeDefinitionId = path.Length == 0
                        ? ObjectTypeIds.ConditionType
                        : ObjectTypeIds.BaseEventType,
                    AttributeId = path.Length == 0
                        ? Attributes.NodeId
                        : Attributes.Value
                };
                foreach (QualifiedName segment in path)
                {
                    clause.BrowsePath = clause.BrowsePath.AddItem(segment);
                }
                filter.SelectClauses = filter.SelectClauses.AddItem(clause);
            }

            if (eventTypeId == ObjectTypeIds.ConditionType && !hasConditionIdClause)
            {
                filter.SelectClauses = filter.SelectClauses.AddItem(
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.ConditionType,
                        AttributeId = Attributes.NodeId
                    });
            }

            if (!eventTypeId.IsNull && eventTypeId != ObjectTypeIds.BaseEventType)
            {
                filter.WhereClause.Push(FilterOperator.OfType, Variant.From(eventTypeId));
            }

            return filter;
        }
    }
}
