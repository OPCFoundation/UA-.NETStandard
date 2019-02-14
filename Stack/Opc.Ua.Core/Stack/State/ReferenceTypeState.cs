/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
    /// The base class for all reference type nodes.
    /// </summary>
    public class ReferenceTypeState : BaseTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public ReferenceTypeState() : base(NodeClass.ReferenceType)
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
        #endregion

        #region Initialization
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
            ReferenceTypeState type = source as ReferenceTypeState;

            if (type != null)
            {
                m_inverseName = type.m_inverseName;
                m_symmetric = type.m_symmetric;
            }

            base.Initialize(context, source);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The inverse name for the reference.
        /// </summary>
        public LocalizedText InverseName
        {
            get
            {
                return m_inverseName;
            }

            set
            {
                if (!Object.ReferenceEquals(m_inverseName, value))
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
            get
            {
                return m_symmetric;
            }

            set
            {
                if (m_symmetric != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_symmetric = value;
            }
        }
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

            ReferenceTypeNode referenceTypeNode = node as ReferenceTypeNode;

            if (referenceTypeNode != null)
            {
                referenceTypeNode.InverseName = this.InverseName;
                referenceTypeNode.Symmetric = this.Symmetric;
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

            if (!LocalizedText.IsNullOrEmpty(m_inverseName))
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

            if (!LocalizedText.IsNullOrEmpty(m_inverseName))
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
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
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
        /// <param name="attibutesToLoad">The attributes to load.</param>
        public override void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attibutesToLoad)
        {
            base.Update(context, decoder, attibutesToLoad);

            if ((attibutesToLoad & AttributesToSave.InverseName) != 0)
            {
                m_inverseName = decoder.ReadLocalizedText(null);
            }

            if ((attibutesToLoad & AttributesToSave.Symmetric) != 0)
            {
                m_symmetric = decoder.ReadBoolean(null);
            }
        }
        #endregion
        
        #region Event Callbacks
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
                case Attributes.InverseName:
                {
                    LocalizedText inverseName = m_inverseName;

                    if (OnReadInverseName != null)
                    {
                        result = OnReadInverseName(context, this, ref inverseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = inverseName;
                    }

                    return result;
                }

                case Attributes.Symmetric:
                {
                    bool symmetric = m_symmetric;

                    if (OnReadSymmetric != null)
                    {
                        result = OnReadSymmetric(context, this, ref symmetric);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = symmetric;
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
                case Attributes.InverseName:
                {
                    LocalizedText inverseName = value as LocalizedText;

                    if (inverseName == null && value != null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.InverseName) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteInverseName != null)
                    {
                        result = OnWriteInverseName(context, this, ref inverseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        InverseName = inverseName;
                    }

                    return result;
                }

                case Attributes.Symmetric:
                {
                    bool? symmetricRef = value as bool?;

                    if (symmetricRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.Symmetric) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    bool symmetric = symmetricRef.Value;

                    if (OnWriteSymmetric != null)
                    {
                        result = OnWriteSymmetric(context, this, ref symmetric);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        Symmetric = symmetric;
                    }

                    return result;
                }
            }

            return base.WriteNonValueAttribute(context, attributeId, value);
        }
        #endregion
        
        #region Private Fields
        private LocalizedText m_inverseName;
        private bool m_symmetric;
        #endregion
    }
}
