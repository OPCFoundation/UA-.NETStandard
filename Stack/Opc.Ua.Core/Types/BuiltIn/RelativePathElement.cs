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
    /// An element of a relative path
    /// </summary>
    [DataContract(Namespace = Opc.Ua.Namespaces.OpcUaXsd)]
    public class RelativePathElement : IEncodeable, IJsonEncodeable
    {
        /// <inheritdoc/>
        public RelativePathElement()
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
            ReferenceTypeId = null;
            IsInverse = true;
            IncludeSubtypes = true;
            TargetName = null;
        }

        /// <summary>
        /// Reference type id
        /// </summary>
        [DataMember(Name = "ReferenceTypeId", IsRequired = false, Order = 1)]
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Is inverse
        /// </summary>
        [DataMember(Name = "IsInverse", IsRequired = false, Order = 2)]
        public bool IsInverse { get; set; }

        /// <summary>
        /// Include sub types
        /// </summary>
        [DataMember(Name = "IncludeSubtypes", IsRequired = false, Order = 3)]
        public bool IncludeSubtypes { get; set; }

        /// <summary>
        /// Target name
        /// </summary>
        [DataMember(Name = "TargetName", IsRequired = false, Order = 4)]
        public QualifiedName TargetName { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.RelativePathElement;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.RelativePathElement_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.RelativePathElement_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.RelativePathElement_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);

            encoder.WriteNodeId("ReferenceTypeId", ReferenceTypeId);
            encoder.WriteBoolean("IsInverse", IsInverse);
            encoder.WriteBoolean("IncludeSubtypes", IncludeSubtypes);
            encoder.WriteQualifiedName("TargetName", TargetName);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Opc.Ua.Namespaces.OpcUaXsd);

            ReferenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            IsInverse = decoder.ReadBoolean("IsInverse");
            IncludeSubtypes = decoder.ReadBoolean("IncludeSubtypes");
            TargetName = decoder.ReadQualifiedName("TargetName");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            RelativePathElement value = encodeable as RelativePathElement;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(ReferenceTypeId, value.ReferenceTypeId))
            {
                return false;
            }

            if (!Utils.IsEqual(IsInverse, value.IsInverse))
            {
                return false;
            }

            if (!Utils.IsEqual(IncludeSubtypes, value.IncludeSubtypes))
            {
                return false;
            }

            if (!Utils.IsEqual(TargetName, value.TargetName))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (RelativePathElement)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            RelativePathElement clone = (RelativePathElement)base.MemberwiseClone();

            clone.ReferenceTypeId = Utils.Clone(ReferenceTypeId);
            clone.IsInverse = (bool)Utils.Clone(IsInverse);
            clone.IncludeSubtypes = (bool)Utils.Clone(IncludeSubtypes);
            clone.TargetName = Utils.Clone(TargetName);

            return clone;
        }
    }

    /// <summary>
    /// List of RelativePathElement objects
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfRelativePathElement",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "RelativePathElement")]
    public class RelativePathElementCollection : List<RelativePathElement>, ICloneable
    {
        /// <inheritdoc/>
        public RelativePathElementCollection()
        {
        }

        /// <inheritdoc/>
        public RelativePathElementCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public RelativePathElementCollection(IEnumerable<RelativePathElement> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator RelativePathElementCollection(RelativePathElement[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator RelativePathElement[](RelativePathElementCollection values)
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
            return (RelativePathElementCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            RelativePathElementCollection clone = new RelativePathElementCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
