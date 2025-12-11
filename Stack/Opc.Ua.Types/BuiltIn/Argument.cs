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
    /// Argument
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class Argument : IEncodeable, IJsonEncodeable
    {
        /// <summary>
        /// Initializes an instance of the argument.
        /// </summary>
        public Argument(string name, NodeId dataType, int valueRank, string description)
        {
            Name = name;
            DataType = dataType;
            ValueRank = valueRank;
            Description = description;
        }

        /// <inheritdoc/>
        public Argument()
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
            Name = null;
            DataType = null;
            ValueRank = 0;
            m_arrayDimensions = [];
            Description = null;
        }

        /// <summary>
        /// Name
        /// </summary>
        [DataMember(Name = "Name", IsRequired = false, Order = 1)]
        public string Name { get; set; }

        /// <summary>
        /// Data Type
        /// </summary>
        [DataMember(Name = "DataType", IsRequired = false, Order = 2)]
        public NodeId DataType { get; set; }

        /// <summary>
        /// Value Rank
        /// </summary>
        [DataMember(Name = "ValueRank", IsRequired = false, Order = 3)]
        public int ValueRank { get; set; }

        /// <summary>
        /// Array Dimensions
        /// </summary>
        [DataMember(Name = "ArrayDimensions", IsRequired = false, Order = 4)]
        public UInt32Collection ArrayDimensions
        {
            get => m_arrayDimensions;

            set
            {
                m_arrayDimensions = value;

                if (value == null)
                {
                    m_arrayDimensions = [];
                }
            }
        }

        /// <summary>
        /// Description
        /// </summary>
        [DataMember(Name = "Description", IsRequired = false, Order = 5)]
        public LocalizedText Description { get; set; }

        /// <summary>
        /// The value for the argument.
        /// </summary>
        [IgnoreDataMember]
        public object Value { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.Argument;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.Argument_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.Argument_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.Argument_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteString("Name", Name);
            encoder.WriteNodeId("DataType", DataType);
            encoder.WriteInt32("ValueRank", ValueRank);
            encoder.WriteUInt32Array("ArrayDimensions", ArrayDimensions);
            encoder.WriteLocalizedText("Description", Description);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Name = decoder.ReadString("Name");
            DataType = decoder.ReadNodeId("DataType");
            ValueRank = decoder.ReadInt32("ValueRank");
            ArrayDimensions = decoder.ReadUInt32Array("ArrayDimensions");
            Description = decoder.ReadLocalizedText("Description");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not Argument value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Name, value.Name))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(DataType, value.DataType))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ValueRank, value.ValueRank))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_arrayDimensions, value.m_arrayDimensions))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Description, value.Description))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (Argument)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (Argument)base.MemberwiseClone();

            clone.Name = CoreUtils.Clone(Name);
            clone.DataType = CoreUtils.Clone(DataType);
            clone.ValueRank = (int)CoreUtils.Clone(ValueRank);
            clone.m_arrayDimensions = CoreUtils.Clone(m_arrayDimensions);
            clone.Description = CoreUtils.Clone(Description);

            return clone;
        }

        private UInt32Collection m_arrayDimensions;
    }

    /// <summary>
    /// Argument collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfArgument",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "Argument")]
    public class ArgumentCollection : List<Argument>, ICloneable
    {
        /// <inheritdoc/>
        public ArgumentCollection()
        {
        }

        /// <inheritdoc/>
        public ArgumentCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ArgumentCollection(IEnumerable<Argument> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator ArgumentCollection(Argument[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator Argument[](ArgumentCollection values)
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
            return (ArgumentCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ArgumentCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
