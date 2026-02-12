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
    /// The base class for all view nodes.
    /// </summary>
    public class ViewState : NodeState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public ViewState()
            : base(NodeClass.View)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new ViewState();
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = "View1";
            NodeId = default;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = new LocalizedText(SymbolicName);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            EventNotifier = EventNotifiers.None;
            ContainsNoLoops = false;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is ViewState instance)
            {
                m_eventNotifier = instance.m_eventNotifier;
                m_containsNoLoops = instance.m_containsNoLoops;
            }

            base.Initialize(context, source);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = (ViewState)Activator.CreateInstance(GetType());
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not ViewState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                state.ContainsNoLoops == ContainsNoLoops &&
                state.EventNotifier == EventNotifier;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(ContainsNoLoops);
            hash.Add(EventNotifier);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is ViewState state)
            {
                state.EventNotifier = EventNotifier;
                state.ContainsNoLoops = ContainsNoLoops;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The inverse name for the reference.
        /// </summary>
        public byte EventNotifier
        {
            get => m_eventNotifier;
            set
            {
                if (m_eventNotifier != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_eventNotifier = value;
            }
        }

        /// <summary>
        /// Whether the reference is containsNoLoops.
        /// </summary>
        public bool ContainsNoLoops
        {
            get => m_containsNoLoops;
            set
            {
                if (m_containsNoLoops != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_containsNoLoops = value;
            }
        }

        /// <summary>
        /// Raised when the EventNotifier attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<byte> OnReadEventNotifier;

        /// <summary>
        /// Raised when the EventNotifier attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<byte> OnWriteEventNotifier;

        /// <summary>
        /// Raised when the ContainsNoLoops attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadContainsNoLoops;

        /// <summary>
        /// Raised when the ContainsNoLoops attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteContainsNoLoops;

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (node is ViewNode viewNode)
            {
                viewNode.EventNotifier = EventNotifier;
                viewNode.ContainsNoLoops = ContainsNoLoops;
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

            if (m_eventNotifier != 0)
            {
                encoder.WriteByte("EventNotifier", m_eventNotifier);
            }

            if (m_containsNoLoops)
            {
                encoder.WriteBoolean("ContainsNoLoops", m_containsNoLoops);
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

            if (decoder.Peek("EventNotifier"))
            {
                EventNotifier = decoder.ReadByte("EventNotifier");
            }

            if (decoder.Peek("ContainsNoLoops"))
            {
                ContainsNoLoops = decoder.ReadBoolean("ContainsNoLoops");
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

            if (m_eventNotifier != 0)
            {
                attributesToSave |= AttributesToSave.EventNotifier;
            }

            if (m_containsNoLoops)
            {
                attributesToSave |= AttributesToSave.ContainsNoLoops;
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

            if ((attributesToSave & AttributesToSave.EventNotifier) != 0)
            {
                encoder.WriteByte(null, m_eventNotifier);
            }

            if ((attributesToSave & AttributesToSave.ContainsNoLoops) != 0)
            {
                encoder.WriteBoolean(null, m_containsNoLoops);
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

            if ((attributesToLoad & AttributesToSave.EventNotifier) != 0)
            {
                m_eventNotifier = decoder.ReadByte(null);
            }

            if ((attributesToLoad & AttributesToSave.ContainsNoLoops) != 0)
            {
                m_containsNoLoops = decoder.ReadBoolean(null);
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
                case Attributes.EventNotifier:
                    byte eventNotifier = m_eventNotifier;

                    NodeAttributeEventHandler<byte> onReadEventNotifier = OnReadEventNotifier;

                    if (onReadEventNotifier != null)
                    {
                        result = onReadEventNotifier(context, this, ref eventNotifier);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = eventNotifier;
                    }

                    return result;
                case Attributes.ContainsNoLoops:
                    bool containsNoLoops = m_containsNoLoops;

                    NodeAttributeEventHandler<bool> onReadContainsNoLoops = OnReadContainsNoLoops;

                    if (onReadContainsNoLoops != null)
                    {
                        result = onReadContainsNoLoops(context, this, ref containsNoLoops);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = containsNoLoops;
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
            object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.EventNotifier:
                    byte? eventNotifierRef = value as byte?;

                    if (eventNotifierRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.EventNotifier) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    byte eventNotifier = eventNotifierRef.Value;

                    NodeAttributeEventHandler<byte> onWriteEventNotifier = OnWriteEventNotifier;

                    if (onWriteEventNotifier != null)
                    {
                        result = onWriteEventNotifier(context, this, ref eventNotifier);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        EventNotifier = eventNotifier;
                    }

                    return result;
                case Attributes.ContainsNoLoops:
                    bool? containsNoLoopsRef = value as bool?;

                    if (containsNoLoopsRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.ContainsNoLoops) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    bool containsNoLoops = containsNoLoopsRef.Value;

                    NodeAttributeEventHandler<bool> onWriteContainsNoLoops = OnWriteContainsNoLoops;

                    if (onWriteContainsNoLoops != null)
                    {
                        result = onWriteContainsNoLoops(context, this, ref containsNoLoops);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        ContainsNoLoops = containsNoLoops;
                    }

                    return result;
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
        }

        private byte m_eventNotifier;
        private bool m_containsNoLoops;
    }
}
