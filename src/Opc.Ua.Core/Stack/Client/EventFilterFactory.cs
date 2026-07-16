/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

            foreach (QualifiedName[] path in r.StandardFields)
            {
                var clause = new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    AttributeId = Attributes.Value
                };
                foreach (QualifiedName segment in path)
                {
                    clause.BrowsePath = clause.BrowsePath.AddItem(segment);
                }
                filter.SelectClauses = filter.SelectClauses.AddItem(clause);
            }

            if (!eventTypeId.IsNull && eventTypeId != ObjectTypeIds.BaseEventType)
            {
                filter.WhereClause.Push(FilterOperator.OfType, Variant.From(eventTypeId));
            }

            return filter;
        }
    }
}
