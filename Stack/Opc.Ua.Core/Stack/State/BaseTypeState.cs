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

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all type nodes.
    /// </summary>
    public class BaseTypeState : NodeState
    {
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        protected BaseTypeState(NodeClass nodeClass) : base(nodeClass)
        {
            m_isAbstract = false;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is BaseTypeState type)
            {
                m_superTypeId = type.m_superTypeId;
                m_isAbstract = type.m_isAbstract;
            }

            base.Initialize(context, source);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a copy of the node and all children.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            var clone = new BaseTypeState(NodeClass);
            return CloneChildren(clone);
        }

        /// <summary>
        /// The identifier for the supertype node.
        /// </summary>
        public NodeId SuperTypeId
        {
            get => m_superTypeId;

            set
            {
                if (!ReferenceEquals(m_superTypeId, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.References;
                }

                m_superTypeId = value;
            }
        }

        /// <summary>
        /// Whether the type is an abstract type.
        /// </summary>
        public bool IsAbstract
        {
            get => m_isAbstract;

            set
            {
                if (m_isAbstract != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_isAbstract = value;
            }
        }

        /// <summary>
        /// Raised when the IsAbstract attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadIsAbstract;

        /// <summary>
        /// Raised when the IsAbstract attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteIsAbstract;

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (!NodeId.IsNull(SuperTypeId))
            {
                node.ReferenceTable.Add(ReferenceTypeIds.HasSubtype, true, SuperTypeId);
            }

            switch (NodeClass)
            {
                case NodeClass.ObjectType:
                    ((ObjectTypeNode)node).IsAbstract = IsAbstract;
                    break;

                case NodeClass.VariableType:
                    ((VariableTypeNode)node).IsAbstract = IsAbstract;
                    break;

                case NodeClass.DataType:
                    ((DataTypeNode)node).IsAbstract = IsAbstract;
                    break;

                case NodeClass.ReferenceType:
                    ((ReferenceTypeNode)node).IsAbstract = IsAbstract;
                    break;
            }
        }

        /// <summary>
        /// Saves the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public override void Save(ISystemContext context, XmlEncoder encoder)
        {
            base.Save(context, encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            if (!NodeId.IsNull(m_superTypeId))
            {
                encoder.WriteNodeId("SuperTypeId", m_superTypeId);
            }

            if (m_isAbstract)
            {
                encoder.WriteBoolean("IsAbstract", m_isAbstract);
            }

            encoder.PopNamespace();
        }

        /// <summary>
        /// Updates the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        public override void Update(ISystemContext context, XmlDecoder decoder)
        {
            base.Update(context, decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            if (decoder.Peek("SuperTypeId"))
            {
                SuperTypeId = decoder.ReadNodeId("SuperTypeId");
            }

            if (decoder.Peek("IsAbstract"))
            {
                IsAbstract = decoder.ReadBoolean("IsAbstract");
            }

            decoder.PopNamespace();
        }

        /// <summary>
        /// Returns a mask which indicates which attributes have non-default value.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <returns>A mask the specifies the available attributes.</returns>
        public override AttributesToSave GetAttributesToSave(ISystemContext context)
        {
            AttributesToSave attributesToSave = base.GetAttributesToSave(context);

            if (!NodeId.IsNull(m_superTypeId))
            {
                attributesToSave |= AttributesToSave.SuperTypeId;
            }

            if (m_isAbstract)
            {
                attributesToSave |= AttributesToSave.IsAbstract;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context user.</param>
        /// <param name="encoder">The encoder to write to.</param>
        /// <param name="attributesToSave">The masks indicating what attributes to write.</param>
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
        {
            base.Save(context, encoder, attributesToSave);

            if ((attributesToSave & AttributesToSave.SuperTypeId) != 0)
            {
                encoder.WriteNodeId(null, m_superTypeId);
            }

            if ((attributesToSave & AttributesToSave.IsAbstract) != 0)
            {
                encoder.WriteBoolean(null, m_isAbstract);
            }
        }

        /// <summary>
        /// Updates the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="decoder">The decoder.</param>
        /// <param name="attributesToLoad">The attributes to load.</param>
        public override void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attributesToLoad)
        {
            base.Update(context, decoder, attributesToLoad);

            if ((attributesToLoad & AttributesToSave.SuperTypeId) != 0)
            {
                m_superTypeId = decoder.ReadNodeId(null);
            }

            if ((attributesToLoad & AttributesToSave.IsAbstract) != 0)
            {
                m_isAbstract = decoder.ReadBoolean(null);
            }
        }

        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult ReadNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            ref object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.IsAbstract:
                    bool isAbstract = m_isAbstract;

                    NodeAttributeEventHandler<bool> onReadIsAbstract = OnReadIsAbstract;

                    if (onReadIsAbstract != null)
                    {
                        result = onReadIsAbstract(context, this, ref isAbstract);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = isAbstract;
                    }

                    return result;
            }

            return base.ReadNonValueAttribute(context, attributeId, ref value);
        }

        /// <summary>
        /// Write the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult WriteNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.IsAbstract:
                    bool? isAbstractRef = value as bool?;

                    if (isAbstractRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.IsAbstract) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    bool isAbstract = isAbstractRef.Value;

                    NodeAttributeEventHandler<bool> onWriteIsAbstract = OnWriteIsAbstract;

                    if (onWriteIsAbstract != null)
                    {
                        result = onWriteIsAbstract(context, this, ref isAbstract);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        IsAbstract = isAbstract;
                    }

                    return result;
            }

            return base.WriteNonValueAttribute(context, attributeId, value);
        }

        /// <summary>
        /// Populates the browser with references that meet the criteria.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="browser">The browser to populate.</param>
        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            NodeId superTypeId = m_superTypeId;

            if (!NodeId.IsNull(superTypeId) && browser.IsRequired(ReferenceTypeIds.HasSubtype, true))
            {
                browser.Add(ReferenceTypeIds.HasSubtype, true, superTypeId);
            }

            NodeId nodeId = NodeId;

            // use the type table to find the subtypes.
            if (context.TypeTable != null && nodeId != null && browser.IsRequired(ReferenceTypeIds.HasSubtype, false))
            {
                IList<NodeId> subtypeIds = context.TypeTable.FindSubTypes(nodeId);

                for (int ii = 0; ii < subtypeIds.Count; ii++)
                {
                    browser.Add(ReferenceTypeIds.HasSubtype, false, subtypeIds[ii]);
                }
            }
        }

        private NodeId m_superTypeId;
        private bool m_isAbstract;
    }
}
