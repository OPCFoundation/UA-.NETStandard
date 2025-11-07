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
    /// View description
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ViewDescription : IEncodeable, IJsonEncodeable
    {
        /// <inheritdoc/>
        public ViewDescription()
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
            ViewId = null;
            Timestamp = DateTime.MinValue;
            ViewVersion = 0;
        }

        /// <summary>
        /// View id
        /// </summary>
        [DataMember(Name = "ViewId", IsRequired = false, Order = 1)]
        public NodeId ViewId { get; set; }

        /// <summary>
        /// Time stamp
        /// </summary>
        [DataMember(Name = "Timestamp", IsRequired = false, Order = 2)]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// View version
        /// </summary>
        [DataMember(Name = "ViewVersion", IsRequired = false, Order = 3)]
        public uint ViewVersion { get; set; }

        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        [IgnoreDataMember]
        public object Handle { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.ViewDescription;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.ViewDescription_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.ViewDescription_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.ViewDescription_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("ViewId", ViewId);
            encoder.WriteDateTime("Timestamp", Timestamp);
            encoder.WriteUInt32("ViewVersion", ViewVersion);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ViewId = decoder.ReadNodeId("ViewId");
            Timestamp = decoder.ReadDateTime("Timestamp");
            ViewVersion = decoder.ReadUInt32("ViewVersion");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ViewDescription value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ViewId, value.ViewId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Timestamp, value.Timestamp))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ViewVersion, value.ViewVersion))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (ViewDescription)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (ViewDescription)base.MemberwiseClone();

            clone.ViewId = CoreUtils.Clone(ViewId);
            clone.Timestamp = (DateTime)CoreUtils.Clone(Timestamp);
            clone.ViewVersion = (uint)CoreUtils.Clone(ViewVersion);

            return clone;
        }

        /// <summary>
        /// Returns true if the view description represents the default (null) view.
        /// </summary>
        public static bool IsDefault(ViewDescription view)
        {
            if (view == null)
            {
                return true;
            }

            if (NodeId.IsNull(view.ViewId) &&
                view.ViewVersion == 0 &&
                view.Timestamp == DateTime.MinValue)
            {
                return true;
            }

            return false;
        }
    }
}
