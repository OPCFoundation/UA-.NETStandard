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
    /// Enum definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class EnumDefinition : DataTypeDefinition
    {
        /// <inheritdoc/>
        public EnumDefinition()
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
            m_fields = [];
        }

        /// <summary>
        /// Fields
        /// </summary>
        [DataMember(Name = "Fields", IsRequired = false, Order = 1)]
        public EnumFieldCollection Fields
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
        /// If TRUE the values are bit positions rather than values.
        /// </summary>
        public bool IsOptionSet { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.EnumDefinition;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.EnumDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.EnumDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.EnumDefinition_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteEncodeableArray("Fields", [.. Fields], typeof(EnumField));

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Fields = (EnumFieldCollection)decoder.ReadEncodeableArray("Fields", typeof(EnumField));

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not EnumDefinition value)
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
            return (EnumDefinition)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (EnumDefinition)base.MemberwiseClone();

            clone.m_fields = CoreUtils.Clone(m_fields);

            return clone;
        }

        private EnumFieldCollection m_fields;
    }

    /// <summary>
    /// Enum definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumDefinition")]
    public class EnumDefinitionCollection : List<EnumDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public EnumDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(IEnumerable<EnumDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator EnumDefinitionCollection(EnumDefinition[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator EnumDefinition[](EnumDefinitionCollection values)
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
            return (EnumDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
