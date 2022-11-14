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

namespace Opc.Ua
{
    /// <summary> 
    /// The base class for all view nodes.
    /// </summary>
    public class ViewState : NodeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public ViewState() : base(NodeClass.View)
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
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = "View1";
            NodeId = null;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = SymbolicName;
            Description = null;
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
            ViewState instance = source as ViewState;

            if (instance != null)
            {
                m_eventNotifier = instance.m_eventNotifier;
                m_containsNoLoops = instance.m_containsNoLoops;
            }

            base.Initialize(context, source);
        }
        #endregion

        #region ICloneable Members
        /// <inheritdoc/>
        public override object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Makes a copy of the node and all children.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            ViewState clone = (ViewState)Activator.CreateInstance(this.GetType());
            return CloneChildren(clone);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The inverse name for the reference.
        /// </summary>
        public byte EventNotifier
        {
            get
            {
                return m_eventNotifier;
            }

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
            get
            {
                return m_containsNoLoops;
            }

            set
            {
                if (m_containsNoLoops != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_containsNoLoops = value;
            }
        }
        #endregion

        #region Event Callbacks
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
        #endregion

        #region Serialization Functions
        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            ViewNode viewNode = node as ViewNode;

            if (viewNode != null)
            {
                viewNode.EventNotifier = this.EventNotifier;
                viewNode.ContainsNoLoops = this.ContainsNoLoops;
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
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
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
        /// <param name="attibutesToLoad">The attributes to load.</param>
        public override void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attibutesToLoad)
        {
            base.Update(context, decoder, attibutesToLoad);

            if ((attibutesToLoad & AttributesToSave.EventNotifier) != 0)
            {
                m_eventNotifier = decoder.ReadByte(null);
            }

            if ((attibutesToLoad & AttributesToSave.ContainsNoLoops) != 0)
            {
                m_containsNoLoops = decoder.ReadBoolean(null);
            }
        }
        #endregion

        #region Read Support Functions
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
                {
                    byte eventNotifier = m_eventNotifier;

                    if (OnReadEventNotifier != null)
                    {
                        result = OnReadEventNotifier(context, this, ref eventNotifier);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = eventNotifier;
                    }

                    return result;
                }

                case Attributes.ContainsNoLoops:
                {
                    bool containsNoLoops = m_containsNoLoops;

                    if (OnReadContainsNoLoops != null)
                    {
                        result = OnReadContainsNoLoops(context, this, ref containsNoLoops);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = containsNoLoops;
                    }

                    return result;
                }
            }

            return base.ReadNonValueAttribute(context, attributeId, ref value);
        }
        #endregion

        #region Write Support Functions
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
                {
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

                    if (OnWriteEventNotifier != null)
                    {
                        result = OnWriteEventNotifier(context, this, ref eventNotifier);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        EventNotifier = eventNotifier;
                    }

                    return result;
                }

                case Attributes.ContainsNoLoops:
                {
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

                    if (OnWriteContainsNoLoops != null)
                    {
                        result = OnWriteContainsNoLoops(context, this, ref containsNoLoops);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        ContainsNoLoops = containsNoLoops;
                    }

                    return result;
                }
            }

            return base.WriteNonValueAttribute(context, attributeId, value);
        }
        #endregion

        #region Private Fields
        private byte m_eventNotifier;
        private bool m_containsNoLoops;
        #endregion
    }
}
