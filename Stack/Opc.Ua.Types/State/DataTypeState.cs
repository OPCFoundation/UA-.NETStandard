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
using System.Linq;
using System.Threading.Tasks;
using Opc.Ua.Schema.Types;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all reference type nodes.
    /// </summary>
    public class DataTypeState : BaseTypeState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public DataTypeState()
            : base(NodeClass.DataType)
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

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new DataTypeState();
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not DataTypeState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                EqualityComparer<ExtensionObject>.Default.Equals(
                    state.DataTypeDefinition,
                    DataTypeDefinition) &&
                state.Purpose == Purpose;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(DataTypeDefinition);
            hash.Add(Purpose);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is DataTypeState state)
            {
                state.DataTypeDefinition = CoreUtils.Clone(DataTypeDefinition);
                state.Purpose = Purpose;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The abstract definition of the data type.
        /// </summary>
        public ExtensionObject DataTypeDefinition
        {
            get => m_dataTypeDefinition;
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
        public Export.DataTypePurpose Purpose { get; set; }

        /// <summary>
        /// Saves the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public override void Save(ISystemContext context, XmlEncoder encoder)
        {
            base.Save(context, encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            if (!m_dataTypeDefinition.IsNull)
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

            if (!m_dataTypeDefinition.IsNull)
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
        public override void Save(
            ISystemContext context,
            BinaryEncoder encoder,
            AttributesToSave attributesToSave)
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
        public override void Update(
            ISystemContext context,
            BinaryDecoder decoder,
            AttributesToSave attributesToLoad)
        {
            base.Update(context, decoder, attributesToLoad);

            if ((attributesToLoad & AttributesToSave.DataTypeDefinition) != 0)
            {
                DataTypeDefinition = decoder.ReadExtensionObject(null);
            }
        }

        /// <summary>
        /// Raised when the DataTypeDefinition attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<ExtensionObject> OnReadDataTypeDefinition;

        /// <summary>
        /// Raised when the DataTypeDefinition attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<ExtensionObject> OnWriteDataTypeDefinition;

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
                    ExtensionObject dataTypeDefinition = m_dataTypeDefinition;

                    NodeAttributeEventHandler<ExtensionObject> onReadDataTypeDefinition
                        = OnReadDataTypeDefinition;

                    if (onReadDataTypeDefinition != null)
                    {
                        result = onReadDataTypeDefinition(context, this, ref dataTypeDefinition);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        if (dataTypeDefinition.Body is StructureDefinition structureType &&
                            structureType.DefaultEncodingId.IsNull)
                        {
                            // one time set the id for binary encoding, currently the only supported encoding
                            structureType.SetDefaultEncodingId(context, NodeId, null);
                        }
                        value = dataTypeDefinition;
                    }

                    if (value == null && result == null)
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }

                    return result;
                default:
                    return base.ReadNonValueAttribute(context, attributeId, ref value);
            }
        }

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
                    ExtensionObject dataTypeDefinition = value is ExtensionObject eo ? eo : default;

                    if ((WriteMask & AttributeWriteMask.DataTypeDefinition) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<ExtensionObject> onWriteDataTypeDefinition
                        = OnWriteDataTypeDefinition;

                    if (onWriteDataTypeDefinition != null)
                    {
                        result = onWriteDataTypeDefinition(context, this, ref dataTypeDefinition);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_dataTypeDefinition = dataTypeDefinition;
                    }

                    return result;
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
        }

        private ExtensionObject m_dataTypeDefinition;
    }
}
