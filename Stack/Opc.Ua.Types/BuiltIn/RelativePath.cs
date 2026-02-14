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
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Relative path
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class RelativePath : IEncodeable, IJsonEncodeable
    {
        /// <summary>
        /// Creates a relative path to follow any hierarchial references to find the specified browse name.
        /// </summary>
        public RelativePath(QualifiedName browseName)
            : this(ReferenceTypeIds.HierarchicalReferences, false, true, browseName)
        {
        }

        /// <summary>
        /// Creates a relative path to follow the forward reference type to find the specified browse name.
        /// </summary>
        public RelativePath(NodeId referenceTypeId, QualifiedName browseName)
            : this(referenceTypeId, false, true, browseName)
        {
        }

        /// <summary>
        /// Creates a relative path to follow the forward reference type to find the specified browse name.
        /// </summary>
        public RelativePath(
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            Initialize();

            var element = new RelativePathElement
            {
                ReferenceTypeId = referenceTypeId,
                IsInverse = isInverse,
                IncludeSubtypes = includeSubtypes,
                TargetName = browseName
            };

            m_elements.Add(element);
        }

        /// <inheritdoc/>
        public RelativePath()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            m_elements = [];
        }

        /// <summary>
        /// Elements of the relative path
        /// </summary>
        [DataMember(Name = "Elements", IsRequired = false, Order = 1)]
        public RelativePathElementCollection Elements
        {
            get => m_elements;

            set
            {
                m_elements = value;

                if (value == null)
                {
                    m_elements = [];
                }
            }
        }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.RelativePath;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.RelativePath_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.RelativePath_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.RelativePath_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteEncodeableArray("Elements", [.. Elements], typeof(RelativePathElement));

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Elements = (RelativePathElementCollection)decoder.ReadEncodeableArray("Elements", typeof(RelativePathElement));

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not RelativePath value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_elements, value.m_elements))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (RelativePath)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (RelativePath)base.MemberwiseClone();

            clone.m_elements = CoreUtils.Clone(m_elements);

            return clone;
        }

        /// <summary>
        /// Formats the relative path as a string.
        /// </summary>
        public string Format(ITypeTable typeTree)
        {
            var formatter = new RelativePathFormatter(this, typeTree);
            return formatter.ToString();
        }

        /// <summary>
        /// Returns true if the relative path does not specify any elements.
        /// </summary>
        public static bool IsEmpty(RelativePath relativePath)
        {
            if (relativePath != null)
            {
                return relativePath.Elements.Count == 0;
            }

            return true;
        }

        /// <summary>
        /// Parses a relative path formatted as a string.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="typeTree"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static RelativePath Parse(string browsePath, ITypeTable typeTree)
        {
            if (typeTree == null)
            {
                throw new ArgumentNullException(nameof(typeTree));
            }

            // parse the string.
            var formatter = RelativePathFormatter.Parse(browsePath);

            // convert the browse names to node ids.
            var relativePath = new RelativePath();

            foreach (RelativePathFormatter.Element element in formatter.Elements)
            {
                var parsedElement = new RelativePathElement
                {
                    ReferenceTypeId = default,
                    IsInverse = false,
                    IncludeSubtypes = element.IncludeSubtypes,
                    TargetName = element.TargetName
                };

                switch (element.ElementType)
                {
                    case RelativePathFormatter.ElementType.AnyHierarchical:
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                        break;
                    case RelativePathFormatter.ElementType.AnyComponent:
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.Aggregates;
                        break;
                    case RelativePathFormatter.ElementType.ForwardReference:
                        parsedElement.ReferenceTypeId = typeTree.FindReferenceType(
                            element.ReferenceTypeName);
                        break;
                    case RelativePathFormatter.ElementType.InverseReference:
                        parsedElement.ReferenceTypeId = typeTree.FindReferenceType(
                            element.ReferenceTypeName);
                        parsedElement.IsInverse = true;
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            "Unexpected ElementType value: {0}", element.ElementType);
                }

                if (parsedElement.ReferenceTypeId.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSyntaxError,
                        "Could not convert BrowseName to a ReferenceTypeId: {0}",
                        element.ReferenceTypeName);
                }

                relativePath.Elements.Add(parsedElement);
            }

            return relativePath;
        }

        /// <summary>
        /// Parses a relative path formatted as a string.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static RelativePath Parse(
            string browsePath,
            ITypeTable typeTree,
            NamespaceTable currentTable,
            NamespaceTable targetTable)
        {
            // parse the string.
            var formatter = RelativePathFormatter.Parse(browsePath, currentTable, targetTable);

            // convert the browse names to node ids.
            var relativePath = new RelativePath();

            foreach (RelativePathFormatter.Element element in formatter.Elements)
            {
                var parsedElement = new RelativePathElement
                {
                    ReferenceTypeId = default,
                    IsInverse = false,
                    IncludeSubtypes = element.IncludeSubtypes,
                    TargetName = element.TargetName
                };

                switch (element.ElementType)
                {
                    case RelativePathFormatter.ElementType.AnyHierarchical:
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                        break;
                    case RelativePathFormatter.ElementType.AnyComponent:
                        parsedElement.ReferenceTypeId = ReferenceTypeIds.Aggregates;
                        break;
                    case RelativePathFormatter.ElementType.ForwardReference:
                    case RelativePathFormatter.ElementType.InverseReference:
                        if (typeTree == null)
                        {
                            throw new InvalidOperationException(
                                "Cannot parse path with reference names without a type table.");
                        }

                        parsedElement.ReferenceTypeId = typeTree.FindReferenceType(
                            element.ReferenceTypeName);
                        parsedElement.IsInverse =
                            element.ElementType == RelativePathFormatter
                                .ElementType
                                .InverseReference;
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            "Unexpected ElementType value: {0}", element.ElementType);
                }

                if (parsedElement.ReferenceTypeId.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSyntaxError,
                        "Could not convert BrowseName to a ReferenceTypeId: {0}",
                        element.ReferenceTypeName);
                }

                relativePath.Elements.Add(parsedElement);
            }

            return relativePath;
        }

        private RelativePathElementCollection m_elements;
    }
}
