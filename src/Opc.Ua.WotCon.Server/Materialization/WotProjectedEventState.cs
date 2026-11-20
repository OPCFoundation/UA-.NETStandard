// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// An occurrence snapshot whose filter values retain the compiled qualified
    /// paths. Generated base-event child lookup alone cannot represent fields
    /// with the same local name in another namespace.
    /// </summary>
    internal sealed class WotProjectedEventState : BaseEventState
    {
        /// <summary>
        /// Creates a projected event state without an address-space parent.
        /// </summary>
        public WotProjectedEventState()
            : base(null)
        {
        }

        /// <summary>
        /// Adds a captured event-field value under its qualified browse path.
        /// </summary>
        /// <param name="path">The qualified browse path identifying the captured event field.</param>
        /// <param name="value">The captured value to associate with the event field path.</param>
        public void AddField(ArrayOf<QualifiedName> path, in DataValue value)
        {
            m_fields.Add((path, value));
        }

        /// <summary>
        /// Gets an attribute value of the projected event, resolving it from the captured event fields.
        /// </summary>
        /// <param name="context">The filter context.</param>
        /// <param name="typeDefinitionId">The type definition identifier.</param>
        /// <param name="relativePath">The relative path.</param>
        /// <param name="attributeId">The attribute ID.</param>
        /// <param name="indexRange">The index range.</param>
        public override Variant GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            ArrayOf<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            if (!typeDefinitionId.IsNull && typeDefinitionId != Ua.ObjectTypeIds.BaseEventType &&
                typeDefinitionId != TypeDefinitionId && !context.TypeTree.IsTypeOf(TypeDefinitionId, typeDefinitionId))
            {
                return Variant.Null;
            }
            if (relativePath.Count == 0)
            {
                return attributeId == Attributes.NodeId ? new Variant(NodeId) : Variant.Null;
            }
            if (attributeId != Attributes.Value)
            {
                return Variant.Null;
            }
            Variant value = Variant.Null;
            bool found = false;
            if (relativePath.Count == 1 && relativePath[0].NamespaceIndex == 0)
            {
                value = relativePath[0].Name switch
                {
                    Ua.BrowseNames.EventId => new Variant(EventId is null ? default : EventId.Value),
                    Ua.BrowseNames.EventType => new Variant(TypeDefinitionId),
                    Ua.BrowseNames.SourceNode => new Variant(SourceNode is null ? NodeId.Null : SourceNode.Value),
                    Ua.BrowseNames.ReceiveTime => new Variant(ReceiveTime is null ? default : ReceiveTime.Value),
                    _ => Variant.Null
                };
                found = relativePath[0].Name is
                    Ua.BrowseNames.EventId or Ua.BrowseNames.EventType or
                    Ua.BrowseNames.SourceNode or Ua.BrowseNames.ReceiveTime;
            }
            if (!found)
            {
                foreach ((ArrayOf<QualifiedName> path, DataValue field) in m_fields)
                {
                    if (relativePath.Span.SequenceEqual(path.Span))
                    {
                        value = StatusCode.IsBad(field.StatusCode)
                            ? new Variant(field.StatusCode) : field.WrappedValue;
                        found = true;
                        break;
                    }
                }
            }
            if (!found && !relativePath.Contains(name => name.NamespaceIndex != 0))
            {
                return base.GetAttributeValue(context, typeDefinitionId, relativePath, attributeId, indexRange);
            }
            if (!value.IsNull && ServiceResult.IsBad(indexRange.ApplyRange(ref value)))
            {
                return Variant.Null;
            }
            return value;
        }

        private readonly List<(ArrayOf<QualifiedName> Path, DataValue Value)> m_fields = [];
    }
}
