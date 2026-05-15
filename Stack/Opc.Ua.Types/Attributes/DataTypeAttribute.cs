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

#nullable enable

using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Declares a type as an IEncodeable data type with the specified
    /// data type and encoding identifiers.
    /// </para>
    /// <para>
    /// If the type is a partial class the source generator will
    /// implement the <see cref="IEncodeable"/> interface with the
    /// given information. The resulting encodeable is always of type
    /// <see cref="StructureType.StructureWithSubtypedValues"/>.
    /// Optional fields and Union
    /// </para>
    /// <para>
    /// If the type is an enum the source generator will generate
    /// <see cref="EnumeratedType{T}"/> whith an extension method
    /// Add[NamespaceWithoutDots] in the current .net namespace to
    /// add the enumerated type to an encodeable factory builder.
    /// In addition the source generator will also generate a factory
    /// for the corresponding <see cref="EnumDefinition"/>
    /// (see "model" generated data types).
    /// </para>
    /// <para>
    /// If the type is a partial class the source generator will
    /// generate a <see cref="EncodeableType{T}"/> instead which is
    /// added to a encodeable factory via the extension method
    /// Add[NamespaceWithoutDots] in the current .net namespace.
    /// In this case the source generator will generate a factory for
    /// <see cref="StructureDefinition"/> (same as "model" generated
    /// data types).
    /// </para>
    /// <para>
    /// If the public properties of the partial class are annoteded
    /// with a <see cref="DataTypeFieldAttribute"/> then only the
    /// annotated fields are part of the <see cref="IEncodeable"/>
    /// implementation. Otherwise all public properties are part of
    /// it.
    /// </para>
    /// <para>
    /// If the class is a **record type**, the Clone, IsEqual and
    /// GetHashCode implementation of the <see cref="IEncodeable"/>
    /// are delegated to the record implementation.
    /// Otherwise they are generated just like for the data types
    /// generated from the models.
    /// </para>
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class |
        AttributeTargets.Struct |
        AttributeTargets.Enum,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class DataTypeAttribute : Attribute
    {
        /// <summary>
        /// The namespace uri to use for the data type. If a
        /// namespace uri is not supplied via a DataContractAttribute
        /// then the .net namespace name will be used prefixed
        /// with urn:.
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Data type identifier to use - if null the identifier
        /// will be a string identifier with the name being the
        /// name of the annotated type (e.g "s=MyTypeName").
        /// The identifier must be prefixed with i=, s=, g=, or b=
        /// everything else is an error. Namespace indexes are
        /// discarded. The identifier must be unique across all
        /// data types in the namespace. If the namespace is open
        /// it is best to use a random Guid string prefixed with g=.
        /// </summary>
        public string? DataTypeId { get; set; }

        /// <summary>
        /// The optional binary encoding id. The identifier must
        /// be prefixed with i=, s=, g=, or b= everything else is
        /// an error. Namespace indexes are discarded.
        /// </summary>
        public string? BinaryEncodingId { get; set; }

        /// <summary>
        /// The optional xml encoding id. The identifier must
        /// be prefixed with i=, s=, g=, or b= everything else is
        /// an error. Namespace indexes are discarded.
        /// </summary>
        public string? XmlEncodingId { get; set; }

        /// <summary>
        /// Try get the type ids from the attribute of the type.
        /// Parses the information in the data type attribute
        /// according to the documented rules and returns the
        /// identifiers.
        /// </summary>
        /// <returns>
        /// False if parsing of any identifier failed (e.g.
        /// because it contained a namespace uri or no prefix.
        /// </returns>
        public static bool TryGetTypeIdsFromType(
            Type type,
            out ExpandedNodeId typeId,
            out ExpandedNodeId binaryEncodingId,
            out ExpandedNodeId xmlEncodingId)
        {
            DataTypeAttribute? attribute =
                type.GetCustomAttribute<DataTypeAttribute>(false);
            if (attribute == null)
            {
                // Not a decorated type
                typeId = default;
                binaryEncodingId = default;
                xmlEncodingId = default;
                return false;
            }

            string typename = type.Name;
            string typeNamespace =
                attribute.Namespace ??
                type.GetCustomAttribute<DataContractAttribute>(
                    false)?.Namespace ??
                ("urn:" + (type.Namespace?.ToLowerInvariant() ?? string.Empty));

            return TryGetTypeIdsFromType(
                typeNamespace,
                attribute.DataTypeId,
                attribute.BinaryEncodingId,
                attribute.XmlEncodingId,
                typename,
                out typeId,
                out binaryEncodingId,
                out xmlEncodingId);
        }

        /// <summary>
        /// Get type ids from strings according to the documented
        /// rules and returns the identifiers.
        /// </summary>
        /// <returns>
        /// False if parsing of any identifier failed (e.g.
        /// because it contained a namespace uri or no prefix.
        /// </returns>
        public static bool TryGetTypeIdsFromType(
            string namespaceUri,
            string? dataTypeIdString,
            string? binaryEncodingIdString,
            string? xmlEncodingIdString,
            string typename,
            out ExpandedNodeId typeId,
            out ExpandedNodeId binaryEncodingId,
            out ExpandedNodeId xmlEncodingId)
        {
            typeId = default;
            binaryEncodingId = default;
            xmlEncodingId = default;

            string? nodeIdString = dataTypeIdString;
            // Parsing null string succeeds with true and returns NodeId.Null
            if (!NodeId.TryParse(nodeIdString, out NodeId nodeId))
            {
                return false;
            }
            typeId = nodeId.IsNull ?
                new ExpandedNodeId(typename, namespaceUri) :
                new ExpandedNodeId(nodeId, namespaceUri);

            // Parse the rest but fail if parsing the identifier fails.
            if (!NodeId.TryParse(binaryEncodingIdString, out nodeId))
            {
                return false;
            }
            binaryEncodingId = nodeId.IsNull ?
                default :
                new ExpandedNodeId(nodeId, namespaceUri);
            if (!NodeId.TryParse(xmlEncodingIdString, out nodeId))
            {
                return false;
            }
            xmlEncodingId = nodeId.IsNull ?
                default :
                new ExpandedNodeId(nodeId, namespaceUri);
            return true;
        }
    }
}
