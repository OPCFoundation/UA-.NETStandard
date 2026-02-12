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
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all reference type nodes.
    /// </summary>
    public class ReferenceTypeState : BaseTypeState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public ReferenceTypeState()
            : base(NodeClass.ReferenceType)
        {
            m_inverseName = null;
            m_symmetric = false;
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new ReferenceTypeState();
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            InverseName = null;
            Symmetric = false;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is ReferenceTypeState type)
            {
                m_inverseName = type.m_inverseName;
                m_symmetric = type.m_symmetric;
            }

            base.Initialize(context, source);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = (ReferenceTypeState)Activator.CreateInstance(GetType());
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not ReferenceTypeState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                state.InverseName == InverseName &&
                state.Symmetric == Symmetric;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(InverseName);
            hash.Add(InverseName);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is ReferenceTypeState state)
            {
                state.InverseName = InverseName;
                state.Symmetric = Symmetric;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The inverse name for the reference.
        /// </summary>
        public LocalizedText InverseName
        {
            get => m_inverseName;
            set
            {
                if (m_inverseName != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_inverseName = value;
            }
        }

        /// <summary>
        /// Whether the reference is symmetric.
        /// </summary>
        public bool Symmetric
        {
            get => m_symmetric;
            set
            {
                if (m_symmetric != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_symmetric = value;
            }
        }

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (node is ReferenceTypeNode referenceTypeNode)
            {
                referenceTypeNode.InverseName = InverseName;
                referenceTypeNode.Symmetric = Symmetric;
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

            if (!m_inverseName.IsNullOrEmpty)
            {
                encoder.WriteLocalizedText("InverseName", m_inverseName);
            }

            if (m_symmetric)
            {
                encoder.WriteBoolean("Symmetric", m_symmetric);
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

            if (decoder.Peek("InverseName"))
            {
                InverseName = decoder.ReadLocalizedText("InverseName");
            }

            if (decoder.Peek("Symmetric"))
            {
                Symmetric = decoder.ReadBoolean("Symmetric");
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

            if (!m_inverseName.IsNullOrEmpty)
            {
                attributesToSave |= AttributesToSave.InverseName;
            }

            if (m_symmetric)
            {
                attributesToSave |= AttributesToSave.Symmetric;
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

            if ((attributesToSave & AttributesToSave.InverseName) != 0)
            {
                encoder.WriteLocalizedText(null, m_inverseName);
            }

            if ((attributesToSave & AttributesToSave.Symmetric) != 0)
            {
                encoder.WriteBoolean(null, m_symmetric);
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

            if ((attributesToLoad & AttributesToSave.InverseName) != 0)
            {
                m_inverseName = decoder.ReadLocalizedText(null);
            }

            if ((attributesToLoad & AttributesToSave.Symmetric) != 0)
            {
                m_symmetric = decoder.ReadBoolean(null);
            }
        }

        /// <summary>
        /// Raised when the InverseName attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<LocalizedText> OnReadInverseName;

        /// <summary>
        /// Raised when the InverseName attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<LocalizedText> OnWriteInverseName;

        /// <summary>
        /// Raised when the Symmetric attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadSymmetric;

        /// <summary>
        /// Raised when the Symmetric attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteSymmetric;

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
                case Attributes.InverseName:
                    LocalizedText inverseName = m_inverseName;

                    NodeAttributeEventHandler<LocalizedText> onReadInverseName = OnReadInverseName;

                    if (onReadInverseName != null)
                    {
                        result = onReadInverseName(context, this, ref inverseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        if (inverseName.IsNullOrEmpty)
                        {
                            result = StatusCodes.BadAttributeIdInvalid;
                        }
                        else
                        {
                            value = inverseName;
                        }
                    }

                    return result;
                case Attributes.Symmetric:
                    bool symmetric = m_symmetric;

                    NodeAttributeEventHandler<bool> onReadSymmetric = OnReadSymmetric;

                    if (onReadSymmetric != null)
                    {
                        result = onReadSymmetric(context, this, ref symmetric);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = symmetric;
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
                case Attributes.InverseName:

                    if (!value.TryGet(out LocalizedText inverseName))
                    {
                        if (!value.IsNull)
                        {
                            return StatusCodes.BadTypeMismatch;
                        }
                        inverseName = LocalizedText.Null;
                    }

                    if ((WriteMask & AttributeWriteMask.InverseName) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<LocalizedText> onWriteInverseName
                        = OnWriteInverseName;

                    if (onWriteInverseName != null)
                    {
                        result = onWriteInverseName(context, this, ref inverseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        InverseName = inverseName;
                    }

                    return result;
                case Attributes.Symmetric:
                    if (!value.TryGet(out bool symmetric))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.Symmetric) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<bool> onWriteSymmetric = OnWriteSymmetric;

                    if (onWriteSymmetric != null)
                    {
                        result = onWriteSymmetric(context, this, ref symmetric);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        Symmetric = symmetric;
                    }

                    return result;
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
        }

        private LocalizedText m_inverseName;
        private bool m_symmetric;
    }
}
