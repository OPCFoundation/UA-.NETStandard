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
    /// Browse description
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class BrowseDescription : IEncodeable, IJsonEncodeable
    {
        /// <inheritdoc/>
        public BrowseDescription()
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
            NodeId = null;
            BrowseDirection = BrowseDirection.Forward;
            ReferenceTypeId = null;
            IncludeSubtypes = true;
            NodeClassMask = 0;
            ResultMask = 0;
        }

        /// <summary>
        /// Node id
        /// </summary>
        [DataMember(Name = "NodeId", IsRequired = false, Order = 1)]
        public NodeId NodeId { get; set; }

        /// <summary>
        /// Browse direction
        /// </summary>
        [DataMember(Name = "BrowseDirection", IsRequired = false, Order = 2)]
        public BrowseDirection BrowseDirection { get; set; }

        /// <summary>
        /// Reference type id
        /// </summary>
        [DataMember(Name = "ReferenceTypeId", IsRequired = false, Order = 3)]
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Include sub types
        /// </summary>
        [DataMember(Name = "IncludeSubtypes", IsRequired = false, Order = 4)]
        public bool IncludeSubtypes { get; set; }

        /// <summary>
        /// Node class mask
        /// </summary>
        [DataMember(Name = "NodeClassMask", IsRequired = false, Order = 5)]
        public uint NodeClassMask { get; set; }

        /// <summary>
        /// Result mask
        /// </summary>
        [DataMember(Name = "ResultMask", IsRequired = false, Order = 6)]
        public uint ResultMask { get; set; }

        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        [IgnoreDataMember]
        public object Handle { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.BrowseDescription;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.BrowseDescription_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.BrowseDescription_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.BrowseDescription_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("NodeId", NodeId);
            encoder.WriteEnumerated("BrowseDirection", BrowseDirection);
            encoder.WriteNodeId("ReferenceTypeId", ReferenceTypeId);
            encoder.WriteBoolean("IncludeSubtypes", IncludeSubtypes);
            encoder.WriteUInt32("NodeClassMask", NodeClassMask);
            encoder.WriteUInt32("ResultMask", ResultMask);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            NodeId = decoder.ReadNodeId("NodeId");
            BrowseDirection = (BrowseDirection)decoder.ReadEnumerated("BrowseDirection", typeof(BrowseDirection));
            ReferenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            IncludeSubtypes = decoder.ReadBoolean("IncludeSubtypes");
            NodeClassMask = decoder.ReadUInt32("NodeClassMask");
            ResultMask = decoder.ReadUInt32("ResultMask");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not BrowseDescription value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(NodeId, value.NodeId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(BrowseDirection, value.BrowseDirection))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ReferenceTypeId, value.ReferenceTypeId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(IncludeSubtypes, value.IncludeSubtypes))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(NodeClassMask, value.NodeClassMask))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ResultMask, value.ResultMask))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (BrowseDescription)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (BrowseDescription)base.MemberwiseClone();

            clone.NodeId = CoreUtils.Clone(NodeId);
            clone.BrowseDirection = (BrowseDirection)CoreUtils.Clone(BrowseDirection);
            clone.ReferenceTypeId = CoreUtils.Clone(ReferenceTypeId);
            clone.IncludeSubtypes = (bool)CoreUtils.Clone(IncludeSubtypes);
            clone.NodeClassMask = (uint)CoreUtils.Clone(NodeClassMask);
            clone.ResultMask = (uint)CoreUtils.Clone(ResultMask);

            return clone;
        }
    }

    /// <summary>
    /// Browse description collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfBrowseDescription",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "BrowseDescription")]
    public class BrowseDescriptionCollection : List<BrowseDescription>, ICloneable
    {
        /// <inheritdoc/>
        public BrowseDescriptionCollection()
        {
        }

        /// <inheritdoc/>
        public BrowseDescriptionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public BrowseDescriptionCollection(IEnumerable<BrowseDescription> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator BrowseDescriptionCollection(BrowseDescription[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator BrowseDescription[](BrowseDescriptionCollection values)
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
            return (BrowseDescriptionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new BrowseDescriptionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
