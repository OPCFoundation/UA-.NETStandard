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

using System;
using System.Collections.Generic;

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

        /// <inheritdoc/>
        public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
        {
            return m_target.IsTypeOf(context, typeDefinitionId);
        }

        /// <inheritdoc/>
        public object GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            object value = m_target.GetAttributeValue(
                context,
                typeDefinitionId,
                relativePath,
                attributeId,
                indexRange);

            // only a Retain the target actually resolves is overridden; a clause the type
            // check or the browse path rejects stays null so the field list keeps its shape.
            if (value is bool &&
                attributeId == Attributes.Value &&
                relativePath != null &&
                relativePath.Count == 1 &&
                relativePath[0] != null &&
                relativePath[0].NamespaceIndex == 0 &&
                relativePath[0].Name == BrowseNames.Retain)
            {
                return false;
            }

            return value;
        }
    }
}
