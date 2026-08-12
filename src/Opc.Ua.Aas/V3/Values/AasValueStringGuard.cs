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
using System.Globalization;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Guards the clause 6.3.2 restriction on <c>AASValueString</c>.
    /// </summary>
    /// <remarks>
    /// <c>AASValueString</c> exists for Structure fields whose static DataType
    /// cannot vary with a sibling declared type: qualifiers and extensions
    /// pair <c>Value</c> with <c>ValueType</c>, and IEC 61360 data
    /// specifications pair <c>Value</c> with <c>DataType</c>. A Variable can
    /// carry the DataType clause 6.3.1 assigns to its xsd type directly, so a
    /// Server shall not use <c>AASValueString</c> as a Variable DataType.
    /// </remarks>
    public static class AasValueStringGuard
    {
        /// <summary>
        /// Reports whether a Structure DataType is a legitimate carrier of an
        /// <c>AASValueString</c> field.
        /// </summary>
        /// <param name="structureDataTypeId">The Structure DataType to test.</param>
        /// <returns><c>true</c> when the Structure is one of the three carriers.</returns>
        public static bool IsLegitimateStructureCarrier(ExpandedNodeId structureDataTypeId)
        {
            return SameDataType(structureDataTypeId, DataTypeIds.AASQualifierDataType) ||
                SameDataType(structureDataTypeId, DataTypeIds.AASExtensionDataType) ||
                SameDataType(structureDataTypeId, DataTypeIds.AASDataSpecificationIec61360DataType);
        }

        /// <summary>
        /// Reports whether a Session-local Structure DataType is a legitimate
        /// carrier of an <c>AASValueString</c> field.
        /// </summary>
        /// <param name="structureDataTypeId">The Structure DataType to test.</param>
        /// <param name="namespaceUris">The Server's namespace table.</param>
        /// <returns><c>true</c> when the Structure is one of the three carriers.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="namespaceUris"/> is <c>null</c>.</exception>
        public static bool IsLegitimateStructureCarrier(
            NodeId structureDataTypeId,
            NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            return IsLegitimateStructureCarrier(ToExpandedNodeId(structureDataTypeId, namespaceUris));
        }

        /// <summary>
        /// Throws when a Variable DataType is the forbidden
        /// <c>AASValueString</c>.
        /// </summary>
        /// <param name="variableDataTypeId">The Variable's DataType.</param>
        /// <param name="nodeName">The offending node's diagnostic name.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="variableDataTypeId"/> is <c>AASValueString</c>.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="nodeName"/> is <c>null</c>.</exception>
        public static void AssertVariableDataTypeAllowed(
            ExpandedNodeId variableDataTypeId,
            string nodeName)
        {
            if (nodeName is null)
            {
                throw new ArgumentNullException(nameof(nodeName));
            }

            if (IsAasValueString(variableDataTypeId))
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Variable '{0}' uses AASValueString as its DataType. Clause 6.3.2 allows " +
                            "AASValueString only inside AASQualifierDataType, AASExtensionDataType " +
                            "and AASDataSpecificationIec61360DataType Structure fields; Variables " +
                            "must use the DataType assigned to their declared xsd type.",
                        nodeName),
                    nameof(variableDataTypeId));
            }
        }

        /// <summary>
        /// Throws when a Session-local Variable DataType is the forbidden
        /// <c>AASValueString</c>.
        /// </summary>
        /// <param name="variableDataTypeId">The Variable's DataType.</param>
        /// <param name="namespaceUris">The Server's namespace table.</param>
        /// <param name="nodeName">The offending node's diagnostic name.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="namespaceUris"/> or <paramref name="nodeName"/> is <c>null</c>.
        /// </exception>
        public static void AssertVariableDataTypeAllowed(
            NodeId variableDataTypeId,
            NamespaceTable namespaceUris,
            string nodeName)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            AssertVariableDataTypeAllowed(
                ToExpandedNodeId(variableDataTypeId, namespaceUris),
                nodeName);
        }

        private static bool IsAasValueString(ExpandedNodeId dataTypeId)
        {
            return SameDataType(dataTypeId, DataTypeIds.AASValueString);
        }

        private static bool SameDataType(ExpandedNodeId actual, ExpandedNodeId expected)
        {
            return !actual.IsNull &&
                string.Equals(actual.NamespaceUri, expected.NamespaceUri, StringComparison.Ordinal) &&
                actual.TryGetValue(out uint actualId) &&
                expected.TryGetValue(out uint expectedId) &&
                actualId == expectedId;
        }

        private static ExpandedNodeId ToExpandedNodeId(NodeId nodeId, NamespaceTable namespaceUris)
        {
            if (nodeId.IsNull)
            {
                return ExpandedNodeId.Null;
            }

            string? namespaceUri = namespaceUris.GetString(nodeId.NamespaceIndex);
            return namespaceUri is null
                ? ExpandedNodeId.Null
                : new ExpandedNodeId(nodeId, namespaceUri);
        }
    }
}
