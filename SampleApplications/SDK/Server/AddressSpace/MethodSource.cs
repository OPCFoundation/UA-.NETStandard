/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;

#pragma warning disable 0618

namespace Opc.Ua.Server
{    
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// The state of a state machine.
    /// </summary>
    [Obsolete("The MethodSource class is obsolete and is not supported. See Opc.Ua.MethodState for a replacement.")]
    public class MethodSource : BaseInstanceSource, ICallable, IMethod
    {       
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MethodSource(IServerInternal server, NodeSource parent) : base(server, parent)
        {
            m_arguments = new MethodArguments();
            m_callbacks = new Dictionary<NodeId,CallbackParameters>();
            m_executable = m_userExecutable = true;
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static MethodSource Construct(
            IServerInternal server, 
            NodeSource      parent, 
            NodeId          referenceTypeId,
            NodeId          nodeId,
            QualifiedName   browseName,
            uint            numericId)
        {
            MethodSource instance = new MethodSource(server, parent);
            instance.Initialize(referenceTypeId, nodeId, browseName, numericId, null);
            return instance;
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MethodSource(IServerInternal server, MethodArguments arguments) : base(server, null)
        {
            if (arguments == null) throw new ArgumentNullException("arguments");

            m_arguments = arguments;
            m_callbacks = new Dictionary<NodeId,CallbackParameters>();
            m_executable = m_userExecutable = true;
        }
        #endregion
        
        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                MethodSource clone = new MethodSource(Server, parent);
                clone.Initialize(this);
                return clone;
            }
        }
        #endregion

        #region INode Members
        /// <summary cref="INode.NodeClass" />
        public override NodeClass NodeClass
        {
            get
            {
                return NodeClass.Method;
            }
        }
        #endregion

        #region ILocalNode Members
        /// <summary cref="ILocalNode.SupportsAttribute" />
        public override bool SupportsAttribute(uint attributeId)
        {
            lock (DataLock)
            {
                switch (attributeId)
                {
                    case Attributes.Executable:
                    case Attributes.UserExecutable:
                    {
                        return true;
                    }

                    default:
                    {
                        return base.SupportsAttribute(attributeId);
                    }
                }
            }
        }
        #endregion

        #region IMethod Members
        /// <summary cref="IMethod.Executable" />
        public bool Executable
        {
            get
            {
                lock (DataLock)
                {
                    return m_executable;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_executable = value;
                }
            }
        }

        /// <summary cref="IMethod.UserExecutable" />
        public bool UserExecutable
        {
            get
            {
                lock (DataLock)
                {
                    return m_userExecutable;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_userExecutable = value;
                }
            }
        }
        #endregion
       
        #region ICallable Members
        /// <summary cref="ICallable.Call" />
        public virtual ServiceResult Call(
            OperationContext     context, 
            NodeId               methodId, 
            object               methodHandle, 
            NodeId               objectId, 
            IList<object>        inputArguments,
            IList<ServiceResult> argumentErrors, 
            IList<object>        outputArguments)
        {
            // find the method to call for the object.
            CallbackParameters callbackParameters = null;
            
            lock (DataLock)
            {
                if (!m_executable || !m_userExecutable)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                if (!m_callbacks.TryGetValue(objectId, out callbackParameters))
                {
                    return ServiceResult.Create(StatusCodes.BadInternalError, "No handler specified for the method.");
                }
            }
  
            try
            {  
                // dereference extension objects.
                List<object> convertedArguments = new List<object>();
                
                foreach (object inputArgument in inputArguments)
                {
                    ExtensionObject extension = inputArgument as ExtensionObject;

                    if (extension != null)
                    {
                        IEncodeable encodeable = extension.Body as IEncodeable;

                        if (encodeable != null)
                        {
                            convertedArguments.Add(encodeable);
                            continue;
                        }
                    }

                    convertedArguments.Add(inputArgument);
                }

                // invoke the method. 
                Call(
                    context, 
                    callbackParameters.Target, 
                    callbackParameters.Callback, 
                    convertedArguments, 
                    argumentErrors, 
                    outputArguments);
                 
                // check for argument errors.
                foreach (ServiceResult argumentError in argumentErrors)
                {
                    if (ServiceResult.IsBad(argumentError))
                    {
                        return StatusCodes.BadInvalidArgument;
                    }
                }

                // everthing ok.
                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                return new ServiceResult(e);
            }
        }
        
        /// <summary>
        /// Invokes the specified method.
        /// </summary>
        /// <remarks>
        /// Returning non-good results in the argumentErrors list indicates that the call was not processed.
        /// Other types of errors are reported by throwing an exception.
        /// </remarks>
        protected virtual void Call(
            OperationContext     context, 
            NodeSource          target,
            Delegate             methodToCall,
            IList<object>        inputArguments,
            IList<ServiceResult> argumentErrors,
            IList<object>        outputArguments)
        {                     
            // check for standard handler.
            GenericMethodHandler GenericHandlerToCall = methodToCall as GenericMethodHandler;

            if (GenericHandlerToCall != null)
            {
                GenericHandlerToCall(context, target, inputArguments, argumentErrors, outputArguments);
                return;
            }            

            // check for simple handler.
            NoArgumentsMethodHandler SimpleHandlerToCall = methodToCall as NoArgumentsMethodHandler;

            if (SimpleHandlerToCall != null)
            {
                SimpleHandlerToCall(context, target);
                return;
            }            
            
            // subclass may define different handlers.
            throw new ServiceResultException(StatusCodes.BadNotImplemented);
        }
        #endregion

        #region Public Interface
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);

                MethodSource method = (MethodSource)source;

                m_arguments = method.m_arguments;
                m_executable = method.m_executable;
                m_userExecutable = method.m_userExecutable;
            }
        }

        /// <summary>
        /// Returns the arguments for the method.
        /// </summary>
        public MethodArguments Arguments
        {
            get { return m_arguments; }
            protected set { m_arguments = value; }
        }

        /// <summary>
        /// Associates an object id with a callback.
        /// </summary>
        public void SetCallback(NodeSource target, Delegate callback)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            lock (DataLock)
            {
                if (callback != null)
                {
                    m_callbacks[target.NodeId] = new CallbackParameters(target, callback);
                }
                else
                {
                    m_callbacks.Remove(target.NodeId);
                }
            }
        }

        /// <summary>
        /// Calls the method with no arguments.
        /// </summary>
        public void Call(OperationContext context, NodeSource target)
        {    
            List<object> inputArguments = new List<object>();
            List<ServiceResult> argumentErrors = new List<ServiceResult>();
            List<object> outputArguments = new List<object>();

            ServiceResult result = Call(
                context, 
                NodeId, 
                null, 
                target.NodeId, 
                inputArguments, 
                argumentErrors, 
                outputArguments);

            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }                
        }
        #endregion

        #region Overridden Methods
        /// <summary cref="NodeSource.CreateNode" />
        protected override void CreateNode(NodeId parentId, NodeId referenceTypeId)
        {
            CheckNodeManagerState();

            MethodAttributes attributes = new MethodAttributes();

            attributes.SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName;
            attributes.DisplayName         = DisplayName;

            NodeManager.CreateMethod(
                parentId,
                referenceTypeId,
                NodeId,
                BrowseName,
                attributes);
        }

        /// <summary cref="NodeSource.UpdateAttributes" />
        protected override void UpdateAttributes(ILocalNode source)
        {
            base.UpdateAttributes(source);

            IMethod method = source as IMethod;

            if (method != null)
            {
                m_executable = method.Executable;
                m_userExecutable = method.UserExecutable;
            }
        }

        /// <summary cref="NodeSource.ReadAttribute" />
        protected override ServiceResult ReadAttribute(IOperationContext context, uint attributeId, DataValue value)
        {
            switch (attributeId)
            {
                case Attributes.Executable:
                {
                    if (m_callbacks.Count == 0)
                    {
                        value.Value = false;
                        break;
                    }

                    value.Value = Executable;
                    break;
                }
                    
                case Attributes.UserExecutable:
                {
                    if (!Executable || m_callbacks.Count == 0)
                    {
                        value.Value = false;
                        break;
                    }

                    value.Value = UserExecutable;
                    break;
                }

                default:
                {
                    return base.ReadAttribute(context, attributeId, value);
                }
            }

            return ServiceResult.Good;
        }

        /// <summary cref="NodeSource.WriteAttribute" />
        protected override ServiceResult WriteAttribute(uint attributeId, DataValue value)
        {
            // check for status/timestamp writes.
            if (value.StatusCode != StatusCodes.Good || value.ServerTimestamp != DateTime.MinValue || value.SourceTimestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            switch (attributeId)
            {
                case Attributes.Executable:
                {
                    Executable = (bool)value.Value;
                    break;
                }
                    
                case Attributes.UserExecutable:
                {
                    UserExecutable = (bool)value.Value;
                    break;
                }

                default:
                {
                    return  base.WriteAttribute(attributeId, value);
                }
            }

            return ServiceResult.Good;
        }  
        #endregion
        
        #region CallbackParameters
        /// <summary>
        /// Stores the parameters associated with a callback.
        /// </summary>
        private class CallbackParameters
        {
            public NodeSource Target;
            public Delegate Callback;

            public CallbackParameters(NodeSource target, Delegate callback)
            {
                Target = target;
                Callback = callback;
            }
        }
        #endregion

        #region Private Fields
        private bool m_executable;
        private bool m_userExecutable;
        private MethodArguments m_arguments;
        private Dictionary<NodeId,CallbackParameters> m_callbacks;
        #endregion
    }

    
    /// <summary>
    /// Handles a method call which has input/output arguments defined.
    /// </summary>
    /// <remarks>
    /// Returning non-good results in the argumentErrors list indicates that the call was not processed.
    /// Other types of errors are reported by throwing an exception.
    /// </remarks>
    public delegate void GenericMethodHandler(
        OperationContext     context, 
        NodeSource           target,
        IList<object>        inputArguments,
        IList<ServiceResult> argumentErrors,
        IList<object>        outputArguments);

    /// <summary>
    /// Handles a method call which has no arguments defined.
    /// </summary>
    public delegate void NoArgumentsMethodHandler(
        OperationContext context, 
        NodeSource       target);
        
    #region MethodArguments Class
    /// <summary>
    /// The state of a state machine.
    /// </summary>
    public class MethodArguments
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MethodArguments()
        {
            m_inputArguments = new List<Argument>();
            m_outputArguments = new List<Argument>();
        }
        #endregion
                
        #region Public Interface
        /// <summary>
        /// The input arguments.
        /// </summary>
        public IList<Argument> Input
        {
            get { return m_inputArguments; }
        }

        /// <summary>
        /// The output arguments.
        /// </summary>
        public IList<Argument> Output
        {
            get { return m_outputArguments; }
        }

        /// <summary>
        /// Adds an input argument.
        /// </summary>
        public void AddInput(string name, NodeId dataTypeId, int valueRank, string description)
        {
            Argument argument = new Argument();

            argument.Name        = name;
            argument.DataType    = dataTypeId;
            argument.ValueRank   = valueRank;
            argument.Description = description;

            m_inputArguments.Add(argument);
        }
        
        /// <summary>
        /// Adds an output argument.
        /// </summary>
        public void AddOutput(string name, NodeId dataTypeId, int valueRank, string description)
        {
            Argument argument = new Argument();

            argument.Name        = name;
            argument.DataType    = dataTypeId;
            argument.ValueRank   = valueRank;
            argument.Description = description;

            m_outputArguments.Add(argument);
        }
        #endregion

        #region Private Fields
        private List<Argument> m_inputArguments;
        private List<Argument> m_outputArguments;
        #endregion
    }
    #endregion
       
    #region CreateObjectMethod Class
    /// <summary>
    /// A method used to start the simulation.
    /// </summary>
    public class CreateObjectMethodSource : MethodSource
    {        
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public CreateObjectMethodSource(IServerInternal server) : base(server, (NodeSource)null)
        {
            Argument arg = new Argument();

            arg.Name        = "ParentId";
            arg.Description = "The NodeId of the Parent Node.";
            arg.DataType    = DataTypes.NodeId;
            arg.ValueRank   = ValueRanks.Scalar;

            Arguments.Input.Add(arg);      
            
            arg = new Argument();

            arg.Name        = "ReferenceTypeId";
            arg.Description = "The NodeId of the ReferenceTypeId from the Parent Node.";
            arg.DataType    = DataTypes.NodeId;
            arg.ValueRank   = ValueRanks.Scalar;

            Arguments.Input.Add(arg);     
            
            arg = new Argument();

            arg.Name        = "BrowseName";
            arg.Description = "The BrowseName of the Object to create.";
            arg.DataType    = DataTypes.QualifiedName;
            arg.ValueRank   = ValueRanks.Scalar;

            Arguments.Input.Add(arg);

            arg = new Argument();

            arg.Name        = "NodeId";
            arg.Description = "The NodeId of the Object created.";
            arg.DataType    = DataTypes.NodeId;
            arg.ValueRank   = ValueRanks.Scalar;

            Arguments.Output.Add(arg);               
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Called when the method is invoked.
        /// </summary>
        public delegate void MethodCallHandler(
            OperationContext context,
            NodeSource       target,
            NodeId           parentId,
            NodeId           referenceTypeId,
            QualifiedName    browseName, 
            out NodeId       objectId);
        #endregion
             
        #region Overridden Methods
        /// <summary>
        /// Handles a call.
        /// </summary>
        protected override void Call(
            OperationContext     context, 
            NodeSource           target,
            Delegate             methodToCall,
            IList<object>        inputArguments,
            IList<ServiceResult> argumentErrors,
            IList<object>        outputArguments)
        {
            NodeId parentId = (NodeId)inputArguments[0];
            NodeId referenceTypeId = (NodeId)inputArguments[1];
            QualifiedName browseName = (QualifiedName)inputArguments[2];
            NodeId nodeId = null;

            MethodCallHandler handler = methodToCall as MethodCallHandler;

            if (handler != null)
            {
                handler(context, target, parentId, referenceTypeId, browseName, out nodeId);
                return;
            }

            base.Call(context, target, methodToCall, inputArguments, argumentErrors, outputArguments);
        }
        #endregion
    }
    #endregion
#endif
}
