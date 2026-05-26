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

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Builds an <see cref="EventFilter"/> targeting Part 9 alarm and
    /// condition types. The select clauses use the same field order as
    /// <see cref="AlarmEventDecoder"/> so the resulting records can be
    /// decoded directly.
    /// </summary>
    /// <remarks>
    /// Where clause construction is intentionally limited to OfType
    /// because most Part 9 filtering use cases are type-based.
    /// Additional filtering (severity, active-only, retained-only) can
    /// be applied client-side on the decoded <see cref="ConditionRecord"/>.
    /// </remarks>
    public class AlarmEventFilterBuilder
    {
        private NodeId? m_eventType;

        /// <summary>
        /// Restricts notifications to events of the specified type or its subtypes.
        /// </summary>
        public AlarmEventFilterBuilder OfType(NodeId eventType)
        {
            m_eventType = eventType;
            return this;
        }

        /// <summary>
        /// Restricts notifications to condition events.
        /// </summary>
        public AlarmEventFilterBuilder ForConditions()
            => OfType(ObjectTypeIds.ConditionType);

        /// <summary>
        /// Restricts notifications to alarm condition events.
        /// </summary>
        public AlarmEventFilterBuilder ForAlarms()
            => OfType(ObjectTypeIds.AlarmConditionType);

        /// <summary>
        /// Restricts notifications to dialog condition events.
        /// </summary>
        public AlarmEventFilterBuilder ForDialogs()
            => OfType(ObjectTypeIds.DialogConditionType);

        /// <summary>
        /// Builds the configured <see cref="EventFilter"/>.
        /// </summary>
        public EventFilter Build()
        {
            var filter = new EventFilter();

            // Select clauses match the AlarmEventDecoder field order.
            foreach (QualifiedName[] path in AlarmEventDecoder.StandardFields)
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

            // Build optional where clause (OfType).
            if (m_eventType != null && m_eventType != ObjectTypeIds.BaseEventType)
            {
                filter.WhereClause.Push(FilterOperator.OfType, Variant.From((NodeId)m_eventType));
            }

            return filter;
        }
    }
}
