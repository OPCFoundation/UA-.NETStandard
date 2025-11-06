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
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Structure definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public partial class StructureDefinition : DataTypeDefinition
    {
        /// <inheritdoc/>
        public StructureDefinition()
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
            DefaultEncodingId = null;
            BaseDataType = null;
            StructureType = StructureType.Structure;
            m_fields = [];
        }

        /// <summary>
        /// Default encoding
        /// </summary>
        [DataMember(Name = "DefaultEncodingId", IsRequired = false, Order = 1)]
        public NodeId DefaultEncodingId { get; set; }

        /// <summary>
        /// Base data tyoe
        /// </summary>
        [DataMember(Name = "BaseDataType", IsRequired = false, Order = 2)]
        public NodeId BaseDataType { get; set; }

        /// <summary>
        /// Structure type
        /// </summary>
        [DataMember(Name = "StructureType", IsRequired = false, Order = 3)]
        public StructureType StructureType { get; set; }

        /// <summary>
        /// Fields
        /// </summary>
        [DataMember(Name = "Fields", IsRequired = false, Order = 4)]
        public StructureFieldCollection Fields
        {
            get
            {
                return m_fields;
            }

            set
            {
                m_fields = value;

                if (value == null)
                {
                    m_fields = [];
                }
            }
        }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.StructureDefinition;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.StructureDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.StructureDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.StructureDefinition_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("DefaultEncodingId", DefaultEncodingId);
            encoder.WriteNodeId("BaseDataType", BaseDataType);
            encoder.WriteEnumerated("StructureType", StructureType);
            encoder.WriteEncodeableArray("Fields", Fields.ToArray(), typeof(StructureField));

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            DefaultEncodingId = decoder.ReadNodeId("DefaultEncodingId");
            BaseDataType = decoder.ReadNodeId("BaseDataType");
            StructureType = (StructureType)decoder.ReadEnumerated("StructureType", typeof(StructureType));
            Fields = (StructureFieldCollection)decoder.ReadEncodeableArray("Fields", typeof(StructureField));

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            var value = encodeable as StructureDefinition;

            if (value == null)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(DefaultEncodingId, value.DefaultEncodingId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(BaseDataType, value.BaseDataType))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(StructureType, value.StructureType))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_fields, value.m_fields))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (StructureDefinition)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (StructureDefinition)base.MemberwiseClone();

            clone.DefaultEncodingId = CoreUtils.Clone(DefaultEncodingId);
            clone.BaseDataType = CoreUtils.Clone(BaseDataType);
            clone.StructureType = (StructureType)CoreUtils.Clone(StructureType);
            clone.m_fields = CoreUtils.Clone(m_fields);

            return clone;
        }

        /// <summary>
        /// Set the default encoding id for the requested data encoding.
        /// </summary>
        /// <param name="context">The system context with the encodeable factory.</param>
        /// <param name="typeId">The type id of the Data Type.</param>
        /// <param name="dataEncoding">The data encoding to apply to the default encoding id.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void SetDefaultEncodingId(
            ISystemContext context,
            NodeId typeId,
            QualifiedName dataEncoding)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (dataEncoding?.Name == BrowseNames.DefaultJson)
            {
                DefaultEncodingId = ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris);
                return;
            }

            // note: custom types must be added to the encodeable factory by the node manager to be found
            Type systemType = context.EncodeableFactory?.GetSystemType(
                NodeId.ToExpandedNodeId(typeId, context.NamespaceUris));
            if (systemType != null &&
                Activator.CreateInstance(systemType) is IEncodeable encodeable)
            {
                if (dataEncoding == null || dataEncoding.Name == BrowseNames.DefaultBinary)
                {
                    DefaultEncodingId = ExpandedNodeId.ToNodeId(
                        encodeable.BinaryEncodingId,
                        context.NamespaceUris);
                }
                else if (dataEncoding.Name == BrowseNames.DefaultXml)
                {
                    DefaultEncodingId = ExpandedNodeId.ToNodeId(
                        encodeable.XmlEncodingId,
                        context.NamespaceUris);
                }
            }
        }

        private StructureFieldCollection m_fields;
    }

    /// <summary>
    /// Structure definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStructureDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StructureDefinition")]
    public class StructureDefinitionCollection : List<StructureDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public StructureDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public StructureDefinitionCollection(int capacity) : base(capacity)
        {
        }

        /// <inheritdoc/>
        public StructureDefinitionCollection(IEnumerable<StructureDefinition> collection) : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator StructureDefinitionCollection(StructureDefinition[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator StructureDefinition[](StructureDefinitionCollection values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return null;
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (StructureDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new StructureDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
