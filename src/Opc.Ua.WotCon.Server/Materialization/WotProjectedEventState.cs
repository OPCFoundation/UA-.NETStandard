/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
        public WotProjectedEventState()
            : base(null)
        {
        }

        public void AddField(ArrayOf<QualifiedName> path, in DataValue value)
        {
            m_fields.Add((path, value));
        }

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
                    _ => Variant.Null
                };
                found = relativePath[0].Name is
                    Ua.BrowseNames.EventId or Ua.BrowseNames.EventType or Ua.BrowseNames.SourceNode;
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
