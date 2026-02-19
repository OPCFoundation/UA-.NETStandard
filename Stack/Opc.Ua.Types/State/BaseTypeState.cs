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
using Opc.Ua.Types;

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
        protected BaseTypeState(NodeClass nodeClass)
            : base(nodeClass)
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
            var clone = new BaseTypeState(NodeClass);
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not BaseTypeState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                state.SuperTypeId == SuperTypeId &&
                state.IsAbstract == IsAbstract;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(SuperTypeId);
            hash.Add(IsAbstract);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is BaseTypeState state)
            {
                state.SuperTypeId = SuperTypeId;
                state.IsAbstract = IsAbstract;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The identifier for the supertype node.
        /// </summary>
        public NodeId SuperTypeId
        {
            get => m_superTypeId;
            set
            {
                if (m_superTypeId != value)
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

            if (!SuperTypeId.IsNull)
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

            if (!m_superTypeId.IsNull)
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

            if (!m_superTypeId.IsNull)
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
        public override void Save(
            ISystemContext context,
            BinaryEncoder encoder,
            AttributesToSave attributesToSave)
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
        public override void Update(
            ISystemContext context,
            BinaryDecoder decoder,
            AttributesToSave attributesToLoad)
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
            ref Variant value)
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
                default:
                    return base.ReadNonValueAttribute(context, attributeId, ref value);
            }
        }

        /// <summary>
        /// Write the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult WriteNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            Variant value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.IsAbstract:
                    if (!value.TryGet(out bool isAbstract))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.IsAbstract) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

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
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
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

            if (!superTypeId.IsNull &&
                browser.IsRequired(ReferenceTypeIds.HasSubtype, true))
            {
                browser.Add(ReferenceTypeIds.HasSubtype, true, superTypeId);
            }

            NodeId nodeId = NodeId;

            // use the type table to find the subtypes.
            if (context.TypeTable != null &&
                !nodeId.IsNull &&
                browser.IsRequired(ReferenceTypeIds.HasSubtype, false))
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
