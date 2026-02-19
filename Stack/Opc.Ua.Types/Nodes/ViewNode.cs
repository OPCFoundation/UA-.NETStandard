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

using System.Runtime.Serialization;
using Opc.Ua.Types;

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

            if (!CoreUtils.IsEqual(ContainsNoLoops, value.ContainsNoLoops))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(EventNotifier, value.EventNotifier))
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

            clone.ContainsNoLoops = CoreUtils.Clone(ContainsNoLoops);
            clone.EventNotifier = CoreUtils.Clone(EventNotifier);

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
        protected override Variant Read(uint attributeId)
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
        protected override ServiceResult Write(uint attributeId, Variant value)
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
