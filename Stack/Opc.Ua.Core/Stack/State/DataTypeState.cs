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
    /// The base class for all reference type nodes.
    /// </summary>
    public class DataTypeState : BaseTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public DataTypeState() : base(NodeClass.DataType)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new DataTypeState();
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
            DataTypeState clone = (DataTypeState)Activator.CreateInstance(this.GetType());
            return CloneChildren(clone);
        }
        #endregion


        /// <summary>
        /// The abstract definition of the data type.
        /// </summary>
        public ExtensionObject DataTypeDefinition
        {
            get
            {
                return m_dataTypeDefinition;
            }

            set
            {
                if (m_dataTypeDefinition != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_dataTypeDefinition = value;
            }
        }

        /// <summary>
        /// The purpose of the data type.
        /// </summary>
        public Opc.Ua.Export.DataTypePurpose Purpose { get; set; }

        #region Serialization Functions
        /// <summary>
        /// Saves the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public override void Save(ISystemContext context, XmlEncoder encoder)
        {
            base.Save(context, encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            if (m_dataTypeDefinition != null)
            {
                encoder.WriteExtensionObject("DataTypeDefinition", m_dataTypeDefinition);
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

            if (decoder.Peek("DataTypeDefinition"))
            {
                DataTypeDefinition = decoder.ReadExtensionObject("DataTypeDefinition");
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

            if (m_dataTypeDefinition != null)
            {
                attributesToSave |= AttributesToSave.DataTypeDefinition;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder to write to.</param>
        /// <param name="attributesToSave">The masks indicating what attributes to write.</param>
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
        {
            base.Save(context, encoder, attributesToSave);

            if ((attributesToSave & AttributesToSave.DataTypeDefinition) != 0)
            {
                encoder.WriteExtensionObject(null, DataTypeDefinition);
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

            if ((attributesToLoad & AttributesToSave.DataTypeDefinition) != 0)
            {
                DataTypeDefinition = decoder.ReadExtensionObject(null);
            }
        }
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the DataTypeDefinition attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<ExtensionObject> OnReadDataTypeDefinition;

        /// <summary>
        /// Raised when the DataTypeDefinition attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<ExtensionObject> OnWriteDataTypeDefinition;
        #endregion

        #region Read Support Functions
        /// <summary>
        /// Reads the value for DataTypeDefinition attribute.
        /// </summary>
        protected override ServiceResult ReadNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            ref object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.DataTypeDefinition:
                {
                    ExtensionObject dataTypeDefinition = m_dataTypeDefinition;

                    if (OnReadDataTypeDefinition != null)
                    {
                        result = OnReadDataTypeDefinition(context, this, ref dataTypeDefinition);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        if (dataTypeDefinition?.Body is StructureDefinition structureType &&
                            (structureType.DefaultEncodingId == null ||
                             structureType.DefaultEncodingId.IsNullNodeId))
                        {
                            // one time set the id for binary encoding, currently the only supported encoding
                            structureType.SetDefaultEncodingId(context, NodeId, null);
                        }
                        value = dataTypeDefinition;
                    }

                    return result;
                }
            }

            return base.ReadNonValueAttribute(context, attributeId, ref value);
        }
        #endregion

        #region Write Support Functions
        /// <summary>
        /// Write the value for DataTypeDefinition attribute.
        /// </summary>
        protected override ServiceResult WriteNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.DataTypeDefinition:
                {
                    ExtensionObject dataTypeDefinition = value as ExtensionObject;

                    if ((WriteMask & AttributeWriteMask.DataTypeDefinition) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteDataTypeDefinition != null)
                    {
                        result = OnWriteDataTypeDefinition(context, this, ref dataTypeDefinition);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_dataTypeDefinition = dataTypeDefinition;
                    }

                    return result;
                }
            }

            return base.WriteNonValueAttribute(context, attributeId, value);
        }
        #endregion

        #region Private Fields
        private ExtensionObject m_dataTypeDefinition;
        #endregion
    }
}
