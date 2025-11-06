/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Runtime.Serialization;

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
            get
            {
                return m_elements;
            }

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
            encoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);

            encoder.WriteEncodeableArray("Elements", Elements.ToArray(), typeof(RelativePathElement));

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);

            Elements = (RelativePathElementCollection)decoder.ReadEncodeableArray("Elements", typeof(RelativePathElement));

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            RelativePath value = encodeable as RelativePath;

            if (value == null)
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
            RelativePath clone = (RelativePath)base.MemberwiseClone();

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
                    ReferenceTypeId = null,
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

                if (NodeId.IsNull(parsedElement.ReferenceTypeId))
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
                    ReferenceTypeId = null,
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

                if (NodeId.IsNull(parsedElement.ReferenceTypeId))
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
