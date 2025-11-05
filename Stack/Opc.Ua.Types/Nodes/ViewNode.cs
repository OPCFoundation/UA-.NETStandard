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

namespace Opc.Ua
{
    /// <summary>
    /// View node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ViewNode : InstanceNode, IView
    {
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public ViewNode(ILocalNode source)
            : base(source)
        {
            NodeClass = NodeClass.View;

            if (source is IView node)
            {
                EventNotifier = node.EventNotifier;
                ContainsNoLoops = node.ContainsNoLoops;
            }
        }

        /// <inheritdoc/>
        public ViewNode()
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
            ContainsNoLoops = true;
            EventNotifier = 0;
        }

        /// <summary>
        /// Contains no loops
        /// </summary>
        [DataMember(Name = "ContainsNoLoops", IsRequired = false, Order = 1)]
        public bool ContainsNoLoops { get; set; }

        /// <summary>
        /// Event notifier
        /// </summary>
        [DataMember(Name = "EventNotifier", IsRequired = false, Order = 2)]
        public byte EventNotifier { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.ViewNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.ViewNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.ViewNode_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.ViewNode_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteBoolean("ContainsNoLoops", ContainsNoLoops);
            encoder.WriteByte("EventNotifier", EventNotifier);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ContainsNoLoops = decoder.ReadBoolean("ContainsNoLoops");
            EventNotifier = decoder.ReadByte("EventNotifier");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ViewNode value)
            {
                return false;
            }

            if (!Utils.IsEqual(ContainsNoLoops, value.ContainsNoLoops))
            {
                return false;
            }

            if (!Utils.IsEqual(EventNotifier, value.EventNotifier))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (ViewNode)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (ViewNode)base.MemberwiseClone();

            clone.ContainsNoLoops = (bool)Utils.Clone(ContainsNoLoops);
            clone.EventNotifier = (byte)Utils.Clone(EventNotifier);

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
                case Attributes.EventNotifier:
                case Attributes.ContainsNoLoops:
                    return true;
                default:
                    return base.SupportsAttribute(attributeId);
            }
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier:
                    return EventNotifier;
                case Attributes.ContainsNoLoops:
                    return ContainsNoLoops;
                default:
                    return base.Read(attributeId);
            }
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="value">The value.</param>
        /// <returns>The write operation result.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier:
                    EventNotifier = (byte)value;
                    return ServiceResult.Good;
                case Attributes.ContainsNoLoops:
                    ContainsNoLoops = (bool)value;
                    return ServiceResult.Good;
                default:
                    return base.Write(attributeId, value);
            }
        }
    }
}
