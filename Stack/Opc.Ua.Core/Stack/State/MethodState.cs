/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
    /// The base class for all method nodes.
    /// </summary>
    public class MethodState : BaseInstanceState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public MethodState(NodeState parent) : base(NodeClass.Method, parent)
        {
            m_executable = true;
            m_userExecutable = true;
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new MethodState(parent);
        }
        #endregion

        #region Initialization
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
            MethodState method = source as MethodState;

            if (method != null)
            {
                m_executable = method.m_executable;
                m_userExecutable = method.m_userExecutable;
            }
            
            base.Initialize(context, source);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The identifier for the declaration of the method in the type model.
        /// </summary>
        public NodeId MethodDeclarationId
        {
            get
            {
                return base.TypeDefinitionId;
            }

            set
            {
                base.TypeDefinitionId = value;
            }
        }

        /// <summary>
        /// Whether the method can be called.
        /// </summary>
        public bool Executable
        {
            get
            { 
                return m_executable;  
            }
            
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
            get
            { 
                return m_userExecutable;  
            }
            
            set
            {
                if (m_userExecutable != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_userExecutable = value;
            }
        }
        #endregion

        #region Event Callbacks
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
        /// Raised when the method is called.
        /// </summary>
        public GenericMethodCalledEventHandler OnCallMethod;

        /// <summary>
        /// Raised when the method is called.
        /// </summary>
        public GenericMethodCalledEventHandler2 OnCallMethod2;
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

            MethodNode methodNode = node as MethodNode;

            if (methodNode != null)
            {
                methodNode.Executable = this.Executable;
                methodNode.UserExecutable = this.UserExecutable;
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
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
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
        /// <param name="attibutesToLoad">The attributes to load.</param>
        public override void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attibutesToLoad)
        {
            base.Update(context, decoder, attibutesToLoad);

            if ((attibutesToLoad & AttributesToSave.Executable) != 0)
            {
                m_executable = decoder.ReadBoolean(null);
            }

            if ((attibutesToLoad & AttributesToSave.UserExecutable) != 0)
            {
                m_userExecutable = decoder.ReadBoolean(null);
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
                case Attributes.Executable:
                {
                    bool executable = m_executable;

                    if (OnReadExecutable != null)
                    {
                        result = OnReadExecutable(context, this, ref executable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = executable;
                    }

                    return result;
                }

                case Attributes.UserExecutable:
                {
                    bool userExecutable = m_userExecutable;

                    if (OnReadUserExecutable != null)
                    {
                        result = OnReadUserExecutable(context, this, ref userExecutable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = userExecutable;
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
                case Attributes.Executable:
                {
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

                    if (OnWriteExecutable != null)
                    {
                        result = OnWriteExecutable(context, this, ref executable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        Executable = executable;
                    }

                    return result;
                }

                case Attributes.UserExecutable:
                {
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

                    if (OnWriteUserExecutable != null)
                    {
                        result = OnWriteUserExecutable(context, this, ref userExecutable);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        UserExecutable = userExecutable;
                    }

                    return result;
                }
            }

            return base.WriteNonValueAttribute(context, attributeId, value);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The input arguments for the method.
        /// </summary>
        public PropertyState<Argument[]> InputArguments
        {
            get
            { 
                return m_inputArguments;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_inputArguments, value))
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
            get
            { 
                return m_outputArguments;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_outputArguments, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_outputArguments = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_inputArguments != null)
            {
                children.Add(m_inputArguments);
            }

            if (m_outputArguments != null)
            {
                children.Add(m_outputArguments);
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

            BaseInstanceState instance = null;

            switch (browseName.Name)
            {
                case BrowseNames.InputArguments:
                {
                    if (createOrReplace)
                    {
                        if (InputArguments == null)
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
                    }

                    instance = InputArguments;
                    break;
                }

                case BrowseNames.OutputArguments:
                {
                    if (createOrReplace)
                    {
                        if (OutputArguments == null)
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
                    }

                    instance = OutputArguments;
                    break;
                }
            }

            if (instance != null)
            {
                return instance;
            }

            return base.FindChild(context, browseName, createOrReplace, replacement);
        }
        #endregion

        #region Method Invocation
        /// <summary>
        /// Invokes the methods and returns the output parameters.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="objectId">The object being called.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="argumentErrors">Any errors for the input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <returns>The result of the method call.</returns>
        public virtual ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<Variant> inputArguments,
            IList<ServiceResult> argumentErrors,
            IList<Variant> outputArguments)
        {
            // check if executable.
            object executable = null;
            ReadNonValueAttribute(context, Attributes.Executable, ref executable);

            if (executable is bool && (bool)executable == false)
            {
                return StatusCodes.BadNotExecutable;
            }

            // check if user executable.
            object userExecutable = null;
            ReadNonValueAttribute(context, Attributes.UserExecutable, ref userExecutable);

            if (userExecutable is bool && (bool)userExecutable == false)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // validate input arguments.
            List<object> inputs = new List<object>();

            // check for too few or too many arguments.
            int expectedCount = 0;

            if (InputArguments != null && InputArguments.Value != null)
            {
                expectedCount = InputArguments.Value.Length;
            }

            if (expectedCount != inputArguments.Count)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            
            // validate individual arguements.
            bool error = false;

            for (int ii = 0; ii < inputArguments.Count; ii++)
            {
                ServiceResult argumentError = ValidateInputArgument(context, inputArguments[ii], ii);

                if (ServiceResult.IsBad(argumentError))
                {
                    error = true;
                }

                inputs.Add(inputArguments[ii].Value);
                argumentErrors.Add(argumentError);
            }

            // return good - caller must check argument errors.
            if (error)
            {
                return ServiceResult.Good;
            }

            // set output arguments to default values.
            List<object> outputs = new List<object>();

            if (OutputArguments != null)
            {
                IList<Argument> arguments = OutputArguments.Value;

                if (arguments != null && arguments.Count > 0)
                {
                    for (int ii = 0; ii < arguments.Count; ii++)
                    {
                        outputs.Add(GetArgumentDefaultValue(context, arguments[ii]));
                    }
                }
            }

            // invoke method.
            ServiceResult result = null;

            try
            {
                result = Call(context, objectId, inputs, outputs);
            }
            catch (Exception e)
            {
                result = new ServiceResult(e);
            }

            // copy out arguments.
            if (ServiceResult.IsGood(result))
            {
                for (int ii = 0; ii < outputs.Count; ii++)
                {
                    outputArguments.Add(new Variant(outputs[ii]));
                }
            }

            return result;
        }

        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        protected virtual ServiceResult Call(
            ISystemContext context,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return Call(context, null, inputArguments, outputArguments);
        }

        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected virtual ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCallMethod2 != null)
            {
                return OnCallMethod2(context, this, objectId, inputArguments, outputArguments);
            }

            if (OnCallMethod != null)
            {
                return OnCallMethod(context, this, inputArguments, outputArguments);
            }

            if (Executable && UserExecutable)
            {
                return StatusCodes.BadNotImplemented;
            }

            return StatusCodes.BadUserAccessDenied;
        }

        /// <summary>
        /// Validates the input argument.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="inputArgument">The input argument.</param>
        /// <param name="index">The index in the the list of input argument.</param>
        /// <returns>Any error.</returns>
        protected ServiceResult ValidateInputArgument(
            ISystemContext context,
            Variant inputArgument,
            int index)
        {
            if (InputArguments == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            IList<Argument> arguments = InputArguments.Value;

            if (arguments == null || index < 0 || index >= arguments.Count)
            {
                return StatusCodes.BadInvalidArgument;
            }

            Argument expectedArgument = arguments[index];

            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                inputArgument.Value,
                expectedArgument.DataType,
                expectedArgument.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null)
            {
                return StatusCodes.BadTypeMismatch;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns the default value for the output argument.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="outputArgument">The output argument description.</param>
        /// <returns>The default value.</returns>
        protected object GetArgumentDefaultValue(
            ISystemContext context,
            Argument outputArgument)
        {
            return TypeInfo.GetDefaultValue(outputArgument.DataType, outputArgument.ValueRank, context.TypeTable);
        }
        #endregion

        #region Private Fields
        private bool m_executable;
        private bool m_userExecutable;
        private PropertyState<Argument[]> m_inputArguments;
        private PropertyState<Argument[]> m_outputArguments;
        #endregion
    }

    /// <summary>
    /// Used to process a method call.
    /// </summary>
    public delegate ServiceResult GenericMethodCalledEventHandler(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments);

    /// <summary>
    /// Used to process a method call.
    /// </summary>
    public delegate ServiceResult GenericMethodCalledEventHandler2(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        IList<object> inputArguments,
        IList<object> outputArguments);
}
