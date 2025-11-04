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
    /// Method node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class MethodNode : InstanceNode, IMethod
    {
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public MethodNode(ILocalNode source)
            : base(source)
        {
            NodeClass = NodeClass.Method;

            if (source is IMethod node)
            {
                Executable = node.Executable;
                UserExecutable = node.UserExecutable;
            }
        }

        /// <inheritdoc/>
        public MethodNode()
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
            Executable = true;
            UserExecutable = true;
        }

        /// <summary>
        /// Is executable
        /// </summary>
        [DataMember(Name = "Executable", IsRequired = false, Order = 1)]
        public bool Executable { get; set; }

        /// <summary>
        /// Is user executable
        /// </summary>
        [DataMember(Name = "UserExecutable", IsRequired = false, Order = 2)]
        public bool UserExecutable { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.MethodNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.MethodNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.MethodNode_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.MethodNode_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteBoolean("Executable", Executable);
            encoder.WriteBoolean("UserExecutable", UserExecutable);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Executable = decoder.ReadBoolean("Executable");
            UserExecutable = decoder.ReadBoolean("UserExecutable");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not MethodNode value)
            {
                return false;
            }

            if (!Utils.IsEqual(Executable, value.Executable))
            {
                return false;
            }

            if (!Utils.IsEqual(UserExecutable, value.UserExecutable))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (MethodNode)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (MethodNode)base.MemberwiseClone();

            clone.Executable = (bool)Utils.Clone(Executable);
            clone.UserExecutable = (bool)Utils.Clone(UserExecutable);

            return clone;
        }

        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Executable:
                case Attributes.UserExecutable:
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
                case Attributes.Executable:
                    return Executable;
                case Attributes.UserExecutable:
                    return UserExecutable;
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
                case Attributes.Executable:
                    Executable = (bool)value;
                    return ServiceResult.Good;
                case Attributes.UserExecutable:
                    UserExecutable = (bool)value;
                    return ServiceResult.Good;
                default:
                    return base.Write(attributeId, value);
            }
        }
    }
}
