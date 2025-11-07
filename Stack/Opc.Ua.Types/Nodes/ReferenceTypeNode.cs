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

using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Reference type node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ReferenceTypeNode : TypeNode, IReferenceType
    {
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public ReferenceTypeNode(ILocalNode source)
            : base(source)
        {
            NodeClass = NodeClass.ReferenceType;

            if (source is IReferenceType node)
            {
                IsAbstract = node.IsAbstract;
                InverseName = node.InverseName;
                Symmetric = node.Symmetric;
            }
        }

        /// <inheritdoc/>
        public ReferenceTypeNode()
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
            IsAbstract = true;
            Symmetric = true;
            InverseName = null;
        }

        /// <summary>
        /// Is abstract node
        /// </summary>
        [DataMember(Name = "IsAbstract", IsRequired = false, Order = 1)]
        public bool IsAbstract { get; set; }

        /// <summary>
        /// Is symmetric
        /// </summary>
        [DataMember(Name = "Symmetric", IsRequired = false, Order = 2)]
        public bool Symmetric { get; set; }

        /// <summary>
        /// Inverse name
        /// </summary>
        [DataMember(Name = "InverseName", IsRequired = false, Order = 3)]
        public LocalizedText InverseName { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.ReferenceTypeNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.ReferenceTypeNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.ReferenceTypeNode_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.ReferenceTypeNode_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteBoolean("IsAbstract", IsAbstract);
            encoder.WriteBoolean("Symmetric", Symmetric);
            encoder.WriteLocalizedText("InverseName", InverseName);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            IsAbstract = decoder.ReadBoolean("IsAbstract");
            Symmetric = decoder.ReadBoolean("Symmetric");
            InverseName = decoder.ReadLocalizedText("InverseName");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ReferenceTypeNode value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(IsAbstract, value.IsAbstract))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Symmetric, value.Symmetric))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(InverseName, value.InverseName))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (ReferenceTypeNode)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (ReferenceTypeNode)base.MemberwiseClone();

            clone.IsAbstract = (bool)CoreUtils.Clone(IsAbstract);
            clone.Symmetric = (bool)CoreUtils.Clone(Symmetric);
            clone.InverseName = CoreUtils.Clone(InverseName);

            return clone;
        }

        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                case Attributes.InverseName:
                case Attributes.Symmetric:
                    return true;
                default:
                    return base.SupportsAttribute(attributeId);
            }
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                    return IsAbstract;
                case Attributes.InverseName:
                    return InverseName;
                case Attributes.Symmetric:
                    return Symmetric;
                default:
                    return base.Read(attributeId);
            }
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                    IsAbstract = (bool)value;
                    return ServiceResult.Good;
                case Attributes.InverseName:
                    InverseName = (LocalizedText)value;
                    return ServiceResult.Good;
                case Attributes.Symmetric:
                    Symmetric = (bool)value;
                    return ServiceResult.Good;
                default:
                    return base.Write(attributeId, value);
            }
        }
    }
}
