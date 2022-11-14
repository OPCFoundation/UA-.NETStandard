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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;

namespace Opc.Ua
{
    /// <summary> 
    /// The base class for all object nodes.
    /// </summary>
    public class BaseObjectState : BaseInstanceState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public BaseObjectState(NodeState parent) : base(NodeClass.Object, parent)
        {
            m_eventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            }
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new BaseObjectState(parent);
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = Utils.Format("{0}_Instance1", Opc.Ua.BrowseNames.BaseObjectType);
            NodeId = null;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = SymbolicName;
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            TypeDefinitionId = GetDefaultTypeDefinitionId(context.NamespaceUris);
            NumericId = Opc.Ua.ObjectTypes.BaseObjectType;
            EventNotifier = EventNotifiers.None;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            BaseObjectState instance = source as BaseObjectState;

            if (instance != null)
            {
                m_eventNotifier = instance.m_eventNotifier;
            }

            base.Initialize(context, source);
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return ObjectTypes.BaseObjectType;
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
            BaseObjectState clone = (BaseObjectState)Activator.CreateInstance(this.GetType(), this.Parent);
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
        #endregion

        #region Serialization Functions
        /// <summary>
        /// Exports a copt of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            ObjectNode objectNode = node as ObjectNode;

            if (objectNode != null)
            {
                objectNode.EventNotifier = this.EventNotifier;
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

            if (m_eventNotifier != EventNotifiers.None)
            {
                attributesToSave |= AttributesToSave.EventNotifier;
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
            }

            return base.WriteNonValueAttribute(context, attributeId, value);
        }
        #endregion

        #region Private Fields
        private byte m_eventNotifier;
        #endregion
    }

    /// <summary> 
    /// The base class for all folder nodes.
    /// </summary>
    public class FolderState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public FolderState(NodeState parent) : base(parent)
        {
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = Utils.Format("{0}_Instance1", Opc.Ua.BrowseNames.FolderType);
            NodeId = null;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = SymbolicName;
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            TypeDefinitionId = GetDefaultTypeDefinitionId(context.NamespaceUris);
            NumericId = Opc.Ua.ObjectTypes.FolderType;
            EventNotifier = EventNotifiers.None;
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return ObjectTypes.FolderType;
        }
        #endregion
    }
}
