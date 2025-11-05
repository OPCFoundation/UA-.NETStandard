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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all method nodes.
    /// </summary>
    public class MethodState : BaseInstanceState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public MethodState(NodeState parent)
            : base(NodeClass.Method, parent)
        {
            m_executable = true;
            m_userExecutable = true;
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Executable = true;
            UserExecutable = true;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is MethodState method)
            {
                m_executable = method.m_executable;
                m_userExecutable = method.m_userExecutable;
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
            var clone = (MethodState)Activator.CreateInstance(GetType(), Parent);
            return CloneChildren(clone);
        }

        /// <summary>
        /// The identifier for the declaration of the method in the type model.
        /// </summary>
        public NodeId MethodDeclarationId
        {
            get => TypeDefinitionId;
            set => TypeDefinitionId = value;
        }

        /// <summary>
        /// Whether the method can be called.
        /// </summary>
        public bool Executable
        {
            get => m_executable;
            set
            {
                if (m_executable != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_executable = value;
            }
        }

        /// <summary>
        /// Whether the method can be called by the current user.
        /// </summary>
        public bool UserExecutable
        {
            get => m_userExecutable;
            set
            {
                if (m_userExecutable != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_userExecutable = value;
            }
        }

        /// <summary>
        /// Raised when the Executable attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadExecutable;

        /// <summary>
        /// Raised when the Executable attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteExecutable;

        /// <summary>
        /// Raised when the UserExecutable attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadUserExecutable;

        /// <summary>
        /// Raised when the UserExecutable attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteUserExecutable;

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (node is MethodNode methodNode)
            {
                methodNode.Executable = Executable;
                methodNode.UserExecutable = UserExecutable;
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

            if (m_executable)
            {
                encoder.WriteBoolean("Executable", m_executable);
            }

            if (m_userExecutable)
            {
                encoder.WriteBoolean("UserExecutable", m_executable);
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

            if (decoder.Peek("Executable"))
            {
                Executable = decoder.ReadBoolean("Executable");
            }

            if (decoder.Peek("UserExecutable"))
            {
                UserExecutable = decoder.ReadBoolean("UserExecutable");
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

            if (m_executable)
            {
                attributesToSave |= AttributesToSave.Executable;
            }

            if (m_userExecutable)
            {
                attributesToSave |= AttributesToSave.UserExecutable;
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

            if ((attributesToSave & AttributesToSave.Executable) != 0)
            {
                encoder.WriteBoolean(null, m_executable);
            }

            if ((attributesToSave & AttributesToSave.UserExecutable) != 0)
            {
                encoder.WriteBoolean(null, m_userExecutable);
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

            if ((attributesToLoad & AttributesToSave.Executable) != 0)
            {
                m_executable = decoder.ReadBoolean(null);
            }

            if ((attributesToLoad & AttributesToSave.UserExecutable) != 0)
            {
                m_userExecutable = decoder.ReadBoolean(null);
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
                case Attributes.Executable:
                    bool executable = m_executable;

                    NodeAttributeEventHandler<bool> onReadExecutable = OnReadExecutable;

                    if (onReadExecutable != null)
                    {
                        result = onReadExecutable(context, this, ref executable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = executable;
                    }

                    return result;
                case Attributes.UserExecutable:
                    bool userExecutable = m_userExecutable;

                    NodeAttributeEventHandler<bool> onReadUserExecutable = OnReadUserExecutable;

                    if (onReadUserExecutable != null)
                    {
                        result = onReadUserExecutable(context, this, ref userExecutable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = userExecutable;
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
                case Attributes.Executable:
                    bool? executableRef = value as bool?;

                    if (executableRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.Executable) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    bool executable = executableRef.Value;

                    NodeAttributeEventHandler<bool> onWriteExecutable = OnWriteExecutable;

                    if (onWriteExecutable != null)
                    {
                        result = onWriteExecutable(context, this, ref executable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        Executable = executable;
                    }

                    return result;
                case Attributes.UserExecutable:
                    bool? userExecutableRef = value as bool?;

                    if (userExecutableRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.UserExecutable) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    bool userExecutable = userExecutableRef.Value;

                    NodeAttributeEventHandler<bool> onWriteUserExecutable = OnWriteUserExecutable;

                    if (onWriteUserExecutable != null)
                    {
                        result = onWriteUserExecutable(context, this, ref userExecutable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        UserExecutable = userExecutable;
                    }

                    return result;
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
        }

        /// <summary>
        /// The input arguments for the method.
        /// </summary>
        public PropertyState<Argument[]> InputArguments
        {
            get => m_inputArguments;
            set
            {
                if (!ReferenceEquals(m_inputArguments, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_inputArguments = value;
            }
        }

        /// <summary>
        /// The output arguments for the method.
        /// </summary>
        public PropertyState<Argument[]> OutputArguments
        {
            get => m_outputArguments;
            set
            {
                if (!ReferenceEquals(m_outputArguments, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_outputArguments = value;
            }
        }

        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        public override void GetChildren(ISystemContext context, IList<BaseInstanceState> children)
        {
            PropertyState<Argument[]> inputArguments = m_inputArguments;

            if (inputArguments != null)
            {
                children.Add(inputArguments);
            }

            PropertyState<Argument[]> outputArguments = m_outputArguments;

            if (outputArguments != null)
            {
                children.Add(outputArguments);
            }

            base.GetChildren(context, children);
        }

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

            switch (browseName.Name)
            {
                case BrowseNames.InputArguments:
                    if (createOrReplace && InputArguments == null)
                    {
                        if (replacement == null)
                        {
                            InputArguments = new PropertyState<Argument[]>(this);
                        }
                        else
                        {
                            InputArguments = (PropertyState<Argument[]>)replacement;
                        }
                    }
                    return InputArguments ?? base.FindChild(context, browseName, createOrReplace, replacement);
                case BrowseNames.OutputArguments:
                    if (createOrReplace && OutputArguments == null)
                    {
                        if (replacement == null)
                        {
                            OutputArguments = new PropertyState<Argument[]>(this);
                        }
                        else
                        {
                            OutputArguments = (PropertyState<Argument[]>)replacement;
                        }
                    }
                    return OutputArguments ?? base.FindChild(context, browseName, createOrReplace, replacement);
                default:
                    return base.FindChild(context, browseName, createOrReplace, replacement);
            }
        }

        private bool m_executable;
        private bool m_userExecutable;
        private PropertyState<Argument[]> m_inputArguments;
        private PropertyState<Argument[]> m_outputArguments;
    }
}
