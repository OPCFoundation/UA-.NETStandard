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
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Structure definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class StructureDefinition : DataTypeDefinition
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
            DefaultEncodingId = default;
            BaseDataType = default;
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
            get => m_fields;

            set
            {
                m_fields = value;

                if (value == null)
                {
                    m_fields = [];
                }
            }
        }

        /// <summary>
        /// The first non-inherited field in the structure definition.
        /// </summary>
        public int FirstExplicitFieldIndex { get; set; }

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
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("DefaultEncodingId", DefaultEncodingId);
            encoder.WriteNodeId("BaseDataType", BaseDataType);
            encoder.WriteEnumerated("StructureType", StructureType);
            encoder.WriteEncodeableArray("Fields", [.. Fields], typeof(StructureField));

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
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

            if (encodeable is not StructureDefinition value)
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

            return true;
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

            clone.DefaultEncodingId = DefaultEncodingId;
            clone.BaseDataType = BaseDataType;
            clone.StructureType = CoreUtils.Clone(StructureType);
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

            if (dataEncoding.Name == BrowseNames.DefaultJson)
            {
                DefaultEncodingId = ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris);
                return;
            }

            // note: custom types must be added to the encodeable factory by the node manager to be found
            var expandedTypeId = NodeId.ToExpandedNodeId(typeId, context.NamespaceUris);
            if (context.EncodeableFactory.TryGetEncodeableType(expandedTypeId, out IEncodeableType type) &&
                type.CreateInstance() is IEncodeable encodeable)
            {
                if (dataEncoding.IsNull || dataEncoding.Name == BrowseNames.DefaultBinary)
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
}
