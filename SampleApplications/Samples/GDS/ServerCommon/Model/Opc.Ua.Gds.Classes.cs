/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Opc.Ua.Gds
{
    #region FindApplicationsMethodState Class
    #if (!OPCUA_EXCLUDE_FindApplicationsMethodState)
    /// <summary>
    /// Stores an instance of the FindApplicationsMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class FindApplicationsMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public FindApplicationsMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new FindApplicationsMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAGgAAAEZp" +
           "bmRBcHBsaWNhdGlvbnNNZXRob2RUeXBlAQECAAAvAQECAAIAAAABAf////8CAAAAFWCpCgIAAAAAAA4A" +
           "AABJbnB1dEFyZ3VtZW50cwEBAwAALgBEAwAAAJYBAAAAAQAqAQEdAAAADgAAAEFwcGxpY2F0aW9uVXJp" +
           "AAz/////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEB" +
           "BAAALgBEBAAAAJYBAAAAAQAqAQEdAAAADAAAAEFwcGxpY2F0aW9ucwEBAQABAAAAAAAAAAABACgBAQAA" +
           "AAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public FindApplicationsMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            string applicationUri = (string)inputArguments[0];

            ApplicationRecordDataType[] applications = (ApplicationRecordDataType[])outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationUri,
                    ref applications);
            }

            outputArguments[0] = applications;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult FindApplicationsMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        string applicationUri,
        ref ApplicationRecordDataType[] applications);
    #endif
    #endregion

    #region RegisterApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_RegisterApplicationMethodState)
    /// <summary>
    /// Stores an instance of the RegisterApplicationMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class RegisterApplicationMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public RegisterApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new RegisterApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHQAAAFJl" +
           "Z2lzdGVyQXBwbGljYXRpb25NZXRob2RUeXBlAQEFAAAvAQEFAAUAAAABAf////8CAAAAFWCpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBBgAALgBEBgAAAJYBAAAAAQAqAQEcAAAACwAAAEFwcGxpY2F0aW9u" +
           "AQEBAP////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRz" +
           "AQEHAAAuAEQHAAAAlgEAAAABACoBARwAAAANAAAAQXBwbGljYXRpb25JZAAR/////wAAAAAAAQAoAQEA" +
           "AAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public RegisterApplicationMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            ApplicationRecordDataType application = (ApplicationRecordDataType)ExtensionObject.ToEncodeable((ExtensionObject)inputArguments[0]);

            NodeId applicationId = (NodeId)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    application,
                    ref applicationId);
            }

            outputArguments[0] = applicationId;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult RegisterApplicationMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        ApplicationRecordDataType application,
        ref NodeId applicationId);
    #endif
    #endregion

    #region UpdateApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_UpdateApplicationMethodState)
    /// <summary>
    /// Stores an instance of the UpdateApplicationMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UpdateApplicationMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UpdateApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new UpdateApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAGwAAAFVw" +
           "ZGF0ZUFwcGxpY2F0aW9uTWV0aG9kVHlwZQEBugAALwEBugC6AAAAAQH/////AQAAABVgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMBAbsAAC4ARLsAAACWAQAAAAEAKgEBHAAAAAsAAABBcHBsaWNhdGlvbgEB" +
           "AQD/////AAAAAAABACgBAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public UpdateApplicationMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            ApplicationRecordDataType application = (ApplicationRecordDataType)ExtensionObject.ToEncodeable((ExtensionObject)inputArguments[0]);

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    application);
            }

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult UpdateApplicationMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        ApplicationRecordDataType application);
    #endif
    #endregion

    #region UnregisterApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_UnregisterApplicationMethodState)
    /// <summary>
    /// Stores an instance of the UnregisterApplicationMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UnregisterApplicationMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UnregisterApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new UnregisterApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHwAAAFVu" +
           "cmVnaXN0ZXJBcHBsaWNhdGlvbk1ldGhvZFR5cGUBAQgAAC8BAQgACAAAAAEB/////wEAAAAVYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQEJAAAuAEQJAAAAlgEAAAABACoBARwAAAANAAAAQXBwbGljYXRp" +
           "b25JZAAR/////wAAAAAAAQAoAQEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public UnregisterApplicationMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId);
            }

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult UnregisterApplicationMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId);
    #endif
    #endregion

    #region GetApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_GetApplicationMethodState)
    /// <summary>
    /// Stores an instance of the GetApplicationMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GetApplicationMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GetApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new GetApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAGAAAAEdl" +
           "dEFwcGxpY2F0aW9uTWV0aG9kVHlwZQEBzwAALwEBzwDPAAAAAQH/////AgAAABVgqQoCAAAAAAAOAAAA" +
           "SW5wdXRBcmd1bWVudHMBAdAAAC4ARNAAAACWAQAAAAEAKgEBHAAAAA0AAABBcHBsaWNhdGlvbklkABH/" +
           "////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEB0QAA" +
           "LgBE0QAAAJYBAAAAAQAqAQEcAAAACwAAAEFwcGxpY2F0aW9uAQEBAP////8AAAAAAAEAKAEBAAAAAQH/" +
           "////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public GetApplicationMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];

            ApplicationRecordDataType application = (ApplicationRecordDataType)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    ref application);
            }

            outputArguments[0] = application;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult GetApplicationMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        ref ApplicationRecordDataType application);
    #endif
    #endregion

    #region QueryApplicationsMethodState Class
    #if (!OPCUA_EXCLUDE_QueryApplicationsMethodState)
    /// <summary>
    /// Stores an instance of the QueryApplicationsMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class QueryApplicationsMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public QueryApplicationsMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new QueryApplicationsMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAGwAAAFF1" +
           "ZXJ5QXBwbGljYXRpb25zTWV0aG9kVHlwZQEBYQMALwEBYQNhAwAAAQH/////AgAAABVgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMBAWIDAC4ARGIDAACWBwAAAAEAKgEBHwAAABAAAABTdGFydGluZ1JlY29y" +
           "ZElkAAf/////AAAAAAABACoBASEAAAASAAAATWF4UmVjb3Jkc1RvUmV0dXJuAAf/////AAAAAAABACoB" +
           "AR4AAAAPAAAAQXBwbGljYXRpb25OYW1lAAz/////AAAAAAABACoBAR0AAAAOAAAAQXBwbGljYXRpb25V" +
           "cmkADP////8AAAAAAAEAKgEBHgAAAA8AAABBcHBsaWNhdGlvblR5cGUAB/////8AAAAAAAEAKgEBGQAA" +
           "AAoAAABQcm9kdWN0VXJpAAz/////AAAAAAABACoBARsAAAAMAAAAQ2FwYWJpbGl0aWVzAAwBAAAAAAAA" +
           "AAABACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBYwMALgBEYwMA" +
           "AJYDAAAAAQAqAQElAAAAFAAAAExhc3RDb3VudGVyUmVzZXRUaW1lAQAmAf////8AAAAAAAEAKgEBGwAA" +
           "AAwAAABOZXh0UmVjb3JkSWQAB/////8AAAAAAAEAKgEBHQAAAAwAAABBcHBsaWNhdGlvbnMBADQBAQAA" +
           "AAAAAAAAAQAoAQEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public QueryApplicationsMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            uint startingRecordId = (uint)inputArguments[0];
            uint maxRecordsToReturn = (uint)inputArguments[1];
            string applicationName = (string)inputArguments[2];
            string applicationUri = (string)inputArguments[3];
            uint applicationType = (uint)inputArguments[4];
            string productUri = (string)inputArguments[5];
            string[] capabilities = (string[])inputArguments[6];

            DateTime lastCounterResetTime = (DateTime)outputArguments[0];
            uint nextRecordId = (uint)outputArguments[1];
            ApplicationDescription[] applications = (ApplicationDescription[])outputArguments[2];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    startingRecordId,
                    maxRecordsToReturn,
                    applicationName,
                    applicationUri,
                    applicationType,
                    productUri,
                    capabilities,
                    ref lastCounterResetTime,
                    ref nextRecordId,
                    ref applications);
            }

            outputArguments[0] = lastCounterResetTime;
            outputArguments[1] = nextRecordId;
            outputArguments[2] = applications;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult QueryApplicationsMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        uint startingRecordId,
        uint maxRecordsToReturn,
        string applicationName,
        string applicationUri,
        uint applicationType,
        string productUri,
        string[] capabilities,
        ref DateTime lastCounterResetTime,
        ref uint nextRecordId,
        ref ApplicationDescription[] applications);
    #endif
    #endregion

    #region QueryServersMethodState Class
    #if (!OPCUA_EXCLUDE_QueryServersMethodState)
    /// <summary>
    /// Stores an instance of the QueryServersMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class QueryServersMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public QueryServersMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new QueryServersMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAFgAAAFF1" +
           "ZXJ5U2VydmVyc01ldGhvZFR5cGUBAQoAAC8BAQoACgAAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElu" +
           "cHV0QXJndW1lbnRzAQELAAAuAEQLAAAAlgYAAAABACoBAR8AAAAQAAAAU3RhcnRpbmdSZWNvcmRJZAAH" +
           "/////wAAAAAAAQAqAQEhAAAAEgAAAE1heFJlY29yZHNUb1JldHVybgAH/////wAAAAAAAQAqAQEeAAAA" +
           "DwAAAEFwcGxpY2F0aW9uTmFtZQAM/////wAAAAAAAQAqAQEdAAAADgAAAEFwcGxpY2F0aW9uVXJpAAz/" +
           "////AAAAAAABACoBARkAAAAKAAAAUHJvZHVjdFVyaQAM/////wAAAAAAAQAqAQEhAAAAEgAAAFNlcnZl" +
           "ckNhcGFiaWxpdGllcwAMAQAAAAAAAAAAAQAoAQEAAAABAf////8AAAAAFWCpCgIAAAAAAA8AAABPdXRw" +
           "dXRBcmd1bWVudHMBAQwAAC4ARAwAAACWAgAAAAEAKgEBJQAAABQAAABMYXN0Q291bnRlclJlc2V0VGlt" +
           "ZQEAJgH/////AAAAAAABACoBARgAAAAHAAAAU2VydmVycwEAnS8BAAAAAAAAAAABACgBAQAAAAEB////" +
           "/wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public QueryServersMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            uint startingRecordId = (uint)inputArguments[0];
            uint maxRecordsToReturn = (uint)inputArguments[1];
            string applicationName = (string)inputArguments[2];
            string applicationUri = (string)inputArguments[3];
            string productUri = (string)inputArguments[4];
            string[] serverCapabilities = (string[])inputArguments[5];

            DateTime lastCounterResetTime = (DateTime)outputArguments[0];
            ServerOnNetwork[] servers = (ServerOnNetwork[])outputArguments[1];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    startingRecordId,
                    maxRecordsToReturn,
                    applicationName,
                    applicationUri,
                    productUri,
                    serverCapabilities,
                    ref lastCounterResetTime,
                    ref servers);
            }

            outputArguments[0] = lastCounterResetTime;
            outputArguments[1] = servers;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult QueryServersMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        uint startingRecordId,
        uint maxRecordsToReturn,
        string applicationName,
        string applicationUri,
        string productUri,
        string[] serverCapabilities,
        ref DateTime lastCounterResetTime,
        ref ServerOnNetwork[] servers);
    #endif
    #endregion

    #region DirectoryState Class
    #if (!OPCUA_EXCLUDE_DirectoryState)
    /// <summary>
    /// Stores an instance of the DirectoryType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class DirectoryState : FolderState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public DirectoryState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.DirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEAFQAAAERp" +
           "cmVjdG9yeVR5cGVJbnN0YW5jZQEBDQABAQ0A/////wgAAAAEYIAKAQAAAAEADAAAAEFwcGxpY2F0aW9u" +
           "cwEBDgAALwA9DgAAAP////8AAAAABGGCCgQAAAABABAAAABGaW5kQXBwbGljYXRpb25zAQEPAAAvAQEP" +
           "AA8AAAABAf////8CAAAAFWCpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBEAAALgBEEAAAAJYBAAAA" +
           "AQAqAQEdAAAADgAAAEFwcGxpY2F0aW9uVXJpAAz/////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkK" +
           "AgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBEQAALgBEEQAAAJYBAAAAAQAqAQEdAAAADAAAAEFwcGxp" +
           "Y2F0aW9ucwEBAQABAAAAAAAAAAABACgBAQAAAAEB/////wAAAAAEYYIKBAAAAAEAEwAAAFJlZ2lzdGVy" +
           "QXBwbGljYXRpb24BARIAAC8BARIAEgAAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQETAAAuAEQTAAAAlgEAAAABACoBARwAAAALAAAAQXBwbGljYXRpb24BAQEA/////wAAAAAAAQAo" +
           "AQEAAAABAf////8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBARQAAC4ARBQAAACWAQAA" +
           "AAEAKgEBHAAAAA0AAABBcHBsaWNhdGlvbklkABH/////AAAAAAABACgBAQAAAAEB/////wAAAAAEYYIK" +
           "BAAAAAEAEQAAAFVwZGF0ZUFwcGxpY2F0aW9uAQG8AAAvAQG8ALwAAAABAf////8BAAAAFWCpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBvQAALgBEvQAAAJYBAAAAAQAqAQEcAAAACwAAAEFwcGxpY2F0aW9u" +
           "AQEBAP////8AAAAAAAEAKAEBAAAAAQH/////AAAAAARhggoEAAAAAQAVAAAAVW5yZWdpc3RlckFwcGxp" +
           "Y2F0aW9uAQEVAAAvAQEVABUAAAABAf////8BAAAAFWCpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEB" +
           "FgAALgBEFgAAAJYBAAAAAQAqAQEcAAAADQAAAEFwcGxpY2F0aW9uSWQAEf////8AAAAAAAEAKAEBAAAA" +
           "AQH/////AAAAAARhggoEAAAAAQAOAAAAR2V0QXBwbGljYXRpb24BAdIAAC8BAdIA0gAAAAEB/////wIA" +
           "AAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQHTAAAuAETTAAAAlgEAAAABACoBARwAAAANAAAA" +
           "QXBwbGljYXRpb25JZAAR/////wAAAAAAAQAoAQEAAAABAf////8AAAAAFWCpCgIAAAAAAA8AAABPdXRw" +
           "dXRBcmd1bWVudHMBAdQAAC4ARNQAAACWAQAAAAEAKgEBHAAAAAsAAABBcHBsaWNhdGlvbgEBAQD/////" +
           "AAAAAAABACgBAQAAAAEB/////wAAAAAEYYIKBAAAAAEAEQAAAFF1ZXJ5QXBwbGljYXRpb25zAQFkAwAv" +
           "AQFkA2QDAAABAf////8CAAAAFWCpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBZQMALgBEZQMAAJYH" +
           "AAAAAQAqAQEfAAAAEAAAAFN0YXJ0aW5nUmVjb3JkSWQAB/////8AAAAAAAEAKgEBIQAAABIAAABNYXhS" +
           "ZWNvcmRzVG9SZXR1cm4AB/////8AAAAAAAEAKgEBHgAAAA8AAABBcHBsaWNhdGlvbk5hbWUADP////8A" +
           "AAAAAAEAKgEBHQAAAA4AAABBcHBsaWNhdGlvblVyaQAM/////wAAAAAAAQAqAQEeAAAADwAAAEFwcGxp" +
           "Y2F0aW9uVHlwZQAH/////wAAAAAAAQAqAQEZAAAACgAAAFByb2R1Y3RVcmkADP////8AAAAAAAEAKgEB" +
           "GwAAAAwAAABDYXBhYmlsaXRpZXMADAEAAAAAAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoCAAAAAAAP" +
           "AAAAT3V0cHV0QXJndW1lbnRzAQFmAwAuAERmAwAAlgMAAAABACoBASUAAAAUAAAATGFzdENvdW50ZXJS" +
           "ZXNldFRpbWUBACYB/////wAAAAAAAQAqAQEbAAAADAAAAE5leHRSZWNvcmRJZAAH/////wAAAAAAAQAq" +
           "AQEdAAAADAAAAEFwcGxpY2F0aW9ucwEANAEBAAAAAAAAAAABACgBAQAAAAEB/////wAAAAAEYYIKBAAA" +
           "AAEADAAAAFF1ZXJ5U2VydmVycwEBFwAALwEBFwAXAAAAAQH/////AgAAABVgqQoCAAAAAAAOAAAASW5w" +
           "dXRBcmd1bWVudHMBARgAAC4ARBgAAACWBgAAAAEAKgEBHwAAABAAAABTdGFydGluZ1JlY29yZElkAAf/" +
           "////AAAAAAABACoBASEAAAASAAAATWF4UmVjb3Jkc1RvUmV0dXJuAAf/////AAAAAAABACoBAR4AAAAP" +
           "AAAAQXBwbGljYXRpb25OYW1lAAz/////AAAAAAABACoBAR0AAAAOAAAAQXBwbGljYXRpb25VcmkADP//" +
           "//8AAAAAAAEAKgEBGQAAAAoAAABQcm9kdWN0VXJpAAz/////AAAAAAABACoBASEAAAASAAAAU2VydmVy" +
           "Q2FwYWJpbGl0aWVzAAwBAAAAAAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1" +
           "dEFyZ3VtZW50cwEBGQAALgBEGQAAAJYCAAAAAQAqAQElAAAAFAAAAExhc3RDb3VudGVyUmVzZXRUaW1l" +
           "AQAmAf////8AAAAAAAEAKgEBGAAAAAcAAABTZXJ2ZXJzAQCdLwEAAAAAAAAAAAEAKAEBAAAAAQH/////" +
           "AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the Applications Object.
        /// </summary>
        public FolderState Applications
        {
            get
            {
                return m_applications;
            }

            set
            {
                if (!Object.ReferenceEquals(m_applications, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_applications = value;
            }
        }

        /// <summary>
        /// A description for the FindApplicationsMethodType Method.
        /// </summary>
        public FindApplicationsMethodState FindApplications
        {
            get
            {
                return m_findApplicationsMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_findApplicationsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_findApplicationsMethod = value;
            }
        }

        /// <summary>
        /// A description for the RegisterApplicationMethodType Method.
        /// </summary>
        public RegisterApplicationMethodState RegisterApplication
        {
            get
            {
                return m_registerApplicationMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_registerApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_registerApplicationMethod = value;
            }
        }

        /// <summary>
        /// A description for the UpdateApplicationMethodType Method.
        /// </summary>
        public UpdateApplicationMethodState UpdateApplication
        {
            get
            {
                return m_updateApplicationMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_updateApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_updateApplicationMethod = value;
            }
        }

        /// <summary>
        /// A description for the UnregisterApplicationMethodType Method.
        /// </summary>
        public UnregisterApplicationMethodState UnregisterApplication
        {
            get
            {
                return m_unregisterApplicationMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_unregisterApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_unregisterApplicationMethod = value;
            }
        }

        /// <summary>
        /// A description for the GetApplicationMethodType Method.
        /// </summary>
        public GetApplicationMethodState GetApplication
        {
            get
            {
                return m_getApplicationMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_getApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getApplicationMethod = value;
            }
        }

        /// <summary>
        /// A description for the QueryApplicationsMethodType Method.
        /// </summary>
        public QueryApplicationsMethodState QueryApplications
        {
            get
            {
                return m_queryApplicationsMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_queryApplicationsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_queryApplicationsMethod = value;
            }
        }

        /// <summary>
        /// A description for the QueryServersMethodType Method.
        /// </summary>
        public QueryServersMethodState QueryServers
        {
            get
            {
                return m_queryServersMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_queryServersMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_queryServersMethod = value;
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
            if (m_applications != null)
            {
                children.Add(m_applications);
            }

            if (m_findApplicationsMethod != null)
            {
                children.Add(m_findApplicationsMethod);
            }

            if (m_registerApplicationMethod != null)
            {
                children.Add(m_registerApplicationMethod);
            }

            if (m_updateApplicationMethod != null)
            {
                children.Add(m_updateApplicationMethod);
            }

            if (m_unregisterApplicationMethod != null)
            {
                children.Add(m_unregisterApplicationMethod);
            }

            if (m_getApplicationMethod != null)
            {
                children.Add(m_getApplicationMethod);
            }

            if (m_queryApplicationsMethod != null)
            {
                children.Add(m_queryApplicationsMethod);
            }

            if (m_queryServersMethod != null)
            {
                children.Add(m_queryServersMethod);
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
                case Opc.Ua.Gds.BrowseNames.Applications:
                {
                    if (createOrReplace)
                    {
                        if (Applications == null)
                        {
                            if (replacement == null)
                            {
                                Applications = new FolderState(this);
                            }
                            else
                            {
                                Applications = (FolderState)replacement;
                            }
                        }
                    }

                    instance = Applications;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.FindApplications:
                {
                    if (createOrReplace)
                    {
                        if (FindApplications == null)
                        {
                            if (replacement == null)
                            {
                                FindApplications = new FindApplicationsMethodState(this);
                            }
                            else
                            {
                                FindApplications = (FindApplicationsMethodState)replacement;
                            }
                        }
                    }

                    instance = FindApplications;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.RegisterApplication:
                {
                    if (createOrReplace)
                    {
                        if (RegisterApplication == null)
                        {
                            if (replacement == null)
                            {
                                RegisterApplication = new RegisterApplicationMethodState(this);
                            }
                            else
                            {
                                RegisterApplication = (RegisterApplicationMethodState)replacement;
                            }
                        }
                    }

                    instance = RegisterApplication;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.UpdateApplication:
                {
                    if (createOrReplace)
                    {
                        if (UpdateApplication == null)
                        {
                            if (replacement == null)
                            {
                                UpdateApplication = new UpdateApplicationMethodState(this);
                            }
                            else
                            {
                                UpdateApplication = (UpdateApplicationMethodState)replacement;
                            }
                        }
                    }

                    instance = UpdateApplication;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.UnregisterApplication:
                {
                    if (createOrReplace)
                    {
                        if (UnregisterApplication == null)
                        {
                            if (replacement == null)
                            {
                                UnregisterApplication = new UnregisterApplicationMethodState(this);
                            }
                            else
                            {
                                UnregisterApplication = (UnregisterApplicationMethodState)replacement;
                            }
                        }
                    }

                    instance = UnregisterApplication;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.GetApplication:
                {
                    if (createOrReplace)
                    {
                        if (GetApplication == null)
                        {
                            if (replacement == null)
                            {
                                GetApplication = new GetApplicationMethodState(this);
                            }
                            else
                            {
                                GetApplication = (GetApplicationMethodState)replacement;
                            }
                        }
                    }

                    instance = GetApplication;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.QueryApplications:
                {
                    if (createOrReplace)
                    {
                        if (QueryApplications == null)
                        {
                            if (replacement == null)
                            {
                                QueryApplications = new QueryApplicationsMethodState(this);
                            }
                            else
                            {
                                QueryApplications = (QueryApplicationsMethodState)replacement;
                            }
                        }
                    }

                    instance = QueryApplications;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.QueryServers:
                {
                    if (createOrReplace)
                    {
                        if (QueryServers == null)
                        {
                            if (replacement == null)
                            {
                                QueryServers = new QueryServersMethodState(this);
                            }
                            else
                            {
                                QueryServers = (QueryServersMethodState)replacement;
                            }
                        }
                    }

                    instance = QueryServers;
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

        #region Private Fields
        private FolderState m_applications;
        private FindApplicationsMethodState m_findApplicationsMethod;
        private RegisterApplicationMethodState m_registerApplicationMethod;
        private UpdateApplicationMethodState m_updateApplicationMethod;
        private UnregisterApplicationMethodState m_unregisterApplicationMethod;
        private GetApplicationMethodState m_getApplicationMethod;
        private QueryApplicationsMethodState m_queryApplicationsMethod;
        private QueryServersMethodState m_queryServersMethod;
        #endregion
    }
    #endif
    #endregion

    #region ApplicationRegistrationChangedAuditEventState Class
    #if (!OPCUA_EXCLUDE_ApplicationRegistrationChangedAuditEventState)
    /// <summary>
    /// Stores an instance of the ApplicationRegistrationChangedAuditEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ApplicationRegistrationChangedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ApplicationRegistrationChangedAuditEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.ApplicationRegistrationChangedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEANAAAAEFw" +
           "cGxpY2F0aW9uUmVnaXN0cmF0aW9uQ2hhbmdlZEF1ZGl0RXZlbnRUeXBlSW5zdGFuY2UBARoAAQEaAP//" +
           "//8PAAAANWCJCgIAAAAAAAcAAABFdmVudElkAQEbAAMAAAAAKwAAAEEgZ2xvYmFsbHkgdW5pcXVlIGlk" +
           "ZW50aWZpZXIgZm9yIHRoZSBldmVudC4ALgBEGwAAAAAP/////wEB/////wAAAAA1YIkKAgAAAAAACQAA" +
           "AEV2ZW50VHlwZQEBHAADAAAAACIAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHR5cGUuAC4A" +
           "RBwAAAAAEf////8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQEdAAMAAAAAGAAAAFRo" +
           "ZSBzb3VyY2Ugb2YgdGhlIGV2ZW50LgAuAEQdAAAAABH/////AQH/////AAAAADVgiQoCAAAAAAAKAAAA" +
           "U291cmNlTmFtZQEBHgADAAAAACkAAABBIGRlc2NyaXB0aW9uIG9mIHRoZSBzb3VyY2Ugb2YgdGhlIGV2" +
           "ZW50LgAuAEQeAAAAAAz/////AQH/////AAAAADVgiQoCAAAAAAAEAAAAVGltZQEBHwADAAAAABgAAABX" +
           "aGVuIHRoZSBldmVudCBvY2N1cnJlZC4ALgBEHwAAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAAL" +
           "AAAAUmVjZWl2ZVRpbWUBASAAAwAAAAA+AAAAV2hlbiB0aGUgc2VydmVyIHJlY2VpdmVkIHRoZSBldmVu" +
           "dCBmcm9tIHRoZSB1bmRlcmx5aW5nIHN5c3RlbS4ALgBEIAAAAAEAJgH/////AQH/////AAAAADVgiQoC" +
           "AAAAAAAHAAAATWVzc2FnZQEBIgADAAAAACUAAABBIGxvY2FsaXplZCBkZXNjcmlwdGlvbiBvZiB0aGUg" +
           "ZXZlbnQuAC4ARCIAAAAAFf////8BAf////8AAAAANWCJCgIAAAAAAAgAAABTZXZlcml0eQEBIwADAAAA" +
           "ACEAAABJbmRpY2F0ZXMgaG93IHVyZ2VudCBhbiBldmVudCBpcy4ALgBEIwAAAAAF/////wEB/////wAA" +
           "AAA1YIkKAgAAAAAADwAAAEFjdGlvblRpbWVTdGFtcAEBJAADAAAAAC4AAABXaGVuIHRoZSBhY3Rpb24g" +
           "dHJpZ2dlcmluZyB0aGUgZXZlbnQgb2NjdXJyZWQuAC4ARCQAAAABACYB/////wEB/////wAAAAA1YIkK" +
           "AgAAAAAABgAAAFN0YXR1cwEBJQADAAAAAGEAAABJZiBUUlVFIHRoZSBhY3Rpb24gd2FzIHBlcmZvcm1l" +
           "ZC4gSWYgRkFMU0UgdGhlIGFjdGlvbiBmYWlsZWQgYW5kIHRoZSBzZXJ2ZXIgc3RhdGUgZGlkIG5vdCBj" +
           "aGFuZ2UuAC4ARCUAAAAAAf////8BAf////8AAAAANWCJCgIAAAAAAAgAAABTZXJ2ZXJJZAEBJgADAAAA" +
           "ADoAAABUaGUgdW5pcXVlIGlkZW50aWZpZXIgZm9yIHRoZSBzZXJ2ZXIgZ2VuZXJhdGluZyB0aGUgZXZl" +
           "bnQuAC4ARCYAAAAADP////8BAf////8AAAAANWCJCgIAAAAAABIAAABDbGllbnRBdWRpdEVudHJ5SWQB" +
           "AScAAwAAAABDAAAAVGhlIGxvZyBlbnRyeSBpZCBwcm92aWRlZCBpbiB0aGUgcmVxdWVzdCB0aGF0IGlu" +
           "aXRpYXRlZCB0aGUgYWN0aW9uLgAuAEQnAAAAAAz/////AQH/////AAAAADVgiQoCAAAAAAAMAAAAQ2xp" +
           "ZW50VXNlcklkAQEoAAMAAAAASAAAAFRoZSB1c2VyIGlkZW50aXR5IGFzc29jaWF0ZWQgd2l0aCB0aGUg" +
           "c2Vzc2lvbiB0aGF0IGluaXRpYXRlZCB0aGUgYWN0aW9uLgAuAEQoAAAAAAz/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAIAAAATWV0aG9kSWQBASkAAC4ARCkAAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAA4A" +
           "AABJbnB1dEFyZ3VtZW50cwEBKgAALgBEKgAAAAAYAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }
    #endif
    #endregion

    #region StartSigningRequestMethodState Class
    #if (!OPCUA_EXCLUDE_StartSigningRequestMethodState)
    /// <summary>
    /// Stores an instance of the StartSigningRequestMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class StartSigningRequestMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public StartSigningRequestMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new StartSigningRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHQAAAFN0" +
           "YXJ0U2lnbmluZ1JlcXVlc3RNZXRob2RUeXBlAQEzAAAvAQEzADMAAAABAf////8CAAAAFWCpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBNAAALgBENAAAAJYEAAAAAQAqAQEcAAAADQAAAEFwcGxpY2F0aW9u" +
           "SWQAEf////8AAAAAAAEAKgEBIQAAABIAAABDZXJ0aWZpY2F0ZUdyb3VwSWQAEf////8AAAAAAAEAKgEB" +
           "IAAAABEAAABDZXJ0aWZpY2F0ZVR5cGVJZAAR/////wAAAAAAAQAqAQEhAAAAEgAAAENlcnRpZmljYXRl" +
           "UmVxdWVzdAAP/////wAAAAAAAQAoAQEAAAABAf////8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1" +
           "bWVudHMBATUAAC4ARDUAAACWAQAAAAEAKgEBGAAAAAkAAABSZXF1ZXN0SWQAEf////8AAAAAAAEAKAEB" +
           "AAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public StartSigningRequestMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];
            NodeId certificateGroupId = (NodeId)inputArguments[1];
            NodeId certificateTypeId = (NodeId)inputArguments[2];
            byte[] certificateRequest = (byte[])inputArguments[3];

            NodeId requestId = (NodeId)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    certificateRequest,
                    ref requestId);
            }

            outputArguments[0] = requestId;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult StartSigningRequestMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        byte[] certificateRequest,
        ref NodeId requestId);
    #endif
    #endregion

    #region StartNewKeyPairRequestMethodState Class
    #if (!OPCUA_EXCLUDE_StartNewKeyPairRequestMethodState)
    /// <summary>
    /// Stores an instance of the StartNewKeyPairRequestMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class StartNewKeyPairRequestMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public StartNewKeyPairRequestMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new StartNewKeyPairRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAIAAAAFN0" +
           "YXJ0TmV3S2V5UGFpclJlcXVlc3RNZXRob2RUeXBlAQEwAAAvAQEwADAAAAABAf////8CAAAAFWCpCgIA" +
           "AAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBMQAALgBEMQAAAJYHAAAAAQAqAQEcAAAADQAAAEFwcGxpY2F0" +
           "aW9uSWQAEf////8AAAAAAAEAKgEBIQAAABIAAABDZXJ0aWZpY2F0ZUdyb3VwSWQAEf////8AAAAAAAEA" +
           "KgEBIAAAABEAAABDZXJ0aWZpY2F0ZVR5cGVJZAAR/////wAAAAAAAQAqAQEaAAAACwAAAFN1YmplY3RO" +
           "YW1lAAz/////AAAAAAABACoBARoAAAALAAAARG9tYWluTmFtZXMADAEAAAAAAAAAAAEAKgEBHwAAABAA" +
           "AABQcml2YXRlS2V5Rm9ybWF0AAz/////AAAAAAABACoBASEAAAASAAAAUHJpdmF0ZUtleVBhc3N3b3Jk" +
           "AAz/////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEB" +
           "MgAALgBEMgAAAJYBAAAAAQAqAQEYAAAACQAAAFJlcXVlc3RJZAAR/////wAAAAAAAQAoAQEAAAABAf//" +
           "//8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public StartNewKeyPairRequestMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];
            NodeId certificateGroupId = (NodeId)inputArguments[1];
            NodeId certificateTypeId = (NodeId)inputArguments[2];
            string subjectName = (string)inputArguments[3];
            string[] domainNames = (string[])inputArguments[4];
            string privateKeyFormat = (string)inputArguments[5];
            string privateKeyPassword = (string)inputArguments[6];

            NodeId requestId = (NodeId)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    privateKeyPassword,
                    ref requestId);
            }

            outputArguments[0] = requestId;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult StartNewKeyPairRequestMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        string subjectName,
        string[] domainNames,
        string privateKeyFormat,
        string privateKeyPassword,
        ref NodeId requestId);
    #endif
    #endregion

    #region FinishRequestMethodState Class
    #if (!OPCUA_EXCLUDE_FinishRequestMethodState)
    /// <summary>
    /// Stores an instance of the FinishRequestMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class FinishRequestMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public FinishRequestMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new FinishRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAFwAAAEZp" +
           "bmlzaFJlcXVlc3RNZXRob2RUeXBlAQE5AAAvAQE5ADkAAAABAf////8CAAAAFWCpCgIAAAAAAA4AAABJ" +
           "bnB1dEFyZ3VtZW50cwEBOgAALgBEOgAAAJYCAAAAAQAqAQEcAAAADQAAAEFwcGxpY2F0aW9uSWQAEf//" +
           "//8AAAAAAAEAKgEBGAAAAAkAAABSZXF1ZXN0SWQAEf////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVg" +
           "qQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQE7AAAuAEQ7AAAAlgMAAAABACoBARoAAAALAAAAQ2Vy" +
           "dGlmaWNhdGUAD/////8AAAAAAAEAKgEBGQAAAAoAAABQcml2YXRlS2V5AA//////AAAAAAABACoBASEA" +
           "AAASAAAASXNzdWVyQ2VydGlmaWNhdGVzAA8BAAAAAAAAAAABACgBAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public FinishRequestMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];
            NodeId requestId = (NodeId)inputArguments[1];

            byte[] certificate = (byte[])outputArguments[0];
            byte[] privateKey = (byte[])outputArguments[1];
            byte[][] issuerCertificates = (byte[][])outputArguments[2];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    requestId,
                    ref certificate,
                    ref privateKey,
                    ref issuerCertificates);
            }

            outputArguments[0] = certificate;
            outputArguments[1] = privateKey;
            outputArguments[2] = issuerCertificates;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult FinishRequestMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        NodeId requestId,
        ref byte[] certificate,
        ref byte[] privateKey,
        ref byte[][] issuerCertificates);
    #endif
    #endregion

    #region GetCertificateGroupsMethodState Class
    #if (!OPCUA_EXCLUDE_GetCertificateGroupsMethodState)
    /// <summary>
    /// Stores an instance of the GetCertificateGroupsMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GetCertificateGroupsMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GetCertificateGroupsMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new GetCertificateGroupsMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHgAAAEdl" +
           "dENlcnRpZmljYXRlR3JvdXBzTWV0aG9kVHlwZQEB5gAALwEB5gDmAAAAAQH/////AgAAABVgqQoCAAAA" +
           "AAAOAAAASW5wdXRBcmd1bWVudHMBAecAAC4AROcAAACWAQAAAAEAKgEBHAAAAA0AAABBcHBsaWNhdGlv" +
           "bklkABH/////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50" +
           "cwEB6AAALgBE6AAAAJYBAAAAAQAqAQEiAAAAEwAAAENlcnRpZmljYXRlR3JvdXBJZHMAEQEAAAAAAAAA" +
           "AAEAKAEBAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public GetCertificateGroupsMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];

            NodeId[] certificateGroupIds = (NodeId[])outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    ref certificateGroupIds);
            }

            outputArguments[0] = certificateGroupIds;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult GetCertificateGroupsMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        ref NodeId[] certificateGroupIds);
    #endif
    #endregion

    #region GetTrustListMethodState Class
    #if (!OPCUA_EXCLUDE_GetTrustListMethodState)
    /// <summary>
    /// Stores an instance of the GetTrustListMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GetTrustListMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GetTrustListMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new GetTrustListMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAFgAAAEdl" +
           "dFRydXN0TGlzdE1ldGhvZFR5cGUBAb4AAC8BAb4AvgAAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElu" +
           "cHV0QXJndW1lbnRzAQG/AAAuAES/AAAAlgIAAAABACoBARwAAAANAAAAQXBwbGljYXRpb25JZAAR////" +
           "/wAAAAAAAQAqAQEhAAAAEgAAAENlcnRpZmljYXRlR3JvdXBJZAAR/////wAAAAAAAQAoAQEAAAABAf//" +
           "//8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAcAAAC4ARMAAAACWAQAAAAEAKgEBGgAA" +
           "AAsAAABUcnVzdExpc3RJZAAR/////wAAAAAAAQAoAQEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public GetTrustListMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];
            NodeId certificateGroupId = (NodeId)inputArguments[1];

            NodeId trustListId = (NodeId)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    certificateGroupId,
                    ref trustListId);
            }

            outputArguments[0] = trustListId;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult GetTrustListMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        ref NodeId trustListId);
    #endif
    #endregion

    #region RevokeCertificateMethodState Class
    #if (!OPCUA_EXCLUDE_RevokeCertificateMethodState)
    /// <summary>
    /// Stores an instance of the RevokeCertificateMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class RevokeCertificateMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public RevokeCertificateMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new RevokeCertificateMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAGwAAAFJl" +
           "dm9rZUNlcnRpZmljYXRlTWV0aG9kVHlwZQEBmToALwEBmTqZOgAAAQH/////AQAAABVgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMBAZo6AC4ARJo6AACWAgAAAAEAKgEBHAAAAA0AAABBcHBsaWNhdGlvbklk" +
           "ABH/////AAAAAAABACoBARoAAAALAAAAQ2VydGlmaWNhdGUAD/////8AAAAAAAEAKAEBAAAAAQH/////" +
           "AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public RevokeCertificateMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];
            byte[] certificate = (byte[])inputArguments[1];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    certificate);
            }

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult RevokeCertificateMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        byte[] certificate);
    #endif
    #endregion

    #region GetCertificateStatusMethodState Class
    #if (!OPCUA_EXCLUDE_GetCertificateStatusMethodState)
    /// <summary>
    /// Stores an instance of the GetCertificateStatusMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GetCertificateStatusMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GetCertificateStatusMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new GetCertificateStatusMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHgAAAEdl" +
           "dENlcnRpZmljYXRlU3RhdHVzTWV0aG9kVHlwZQEB2wAALwEB2wDbAAAAAQH/////AgAAABVgqQoCAAAA" +
           "AAAOAAAASW5wdXRBcmd1bWVudHMBAdwAAC4ARNwAAACWAwAAAAEAKgEBHAAAAA0AAABBcHBsaWNhdGlv" +
           "bklkABH/////AAAAAAABACoBASEAAAASAAAAQ2VydGlmaWNhdGVHcm91cElkABH/////AAAAAAABACoB" +
           "ASAAAAARAAAAQ2VydGlmaWNhdGVUeXBlSWQAEf////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoC" +
           "AAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQHdAAAuAETdAAAAlgEAAAABACoBAR0AAAAOAAAAVXBkYXRl" +
           "UmVxdWlyZWQAAf////8AAAAAAAEAKAEBAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public GetCertificateStatusMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId applicationId = (NodeId)inputArguments[0];
            NodeId certificateGroupId = (NodeId)inputArguments[1];
            NodeId certificateTypeId = (NodeId)inputArguments[2];

            bool updateRequired = (bool)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    ref updateRequired);
            }

            outputArguments[0] = updateRequired;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult GetCertificateStatusMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        ref bool updateRequired);
    #endif
    #endregion

    #region CertificateDirectoryState Class
    #if (!OPCUA_EXCLUDE_CertificateDirectoryState)
    /// <summary>
    /// Stores an instance of the CertificateDirectoryType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class CertificateDirectoryState : DirectoryState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public CertificateDirectoryState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.CertificateDirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);

            if (RevokeCertificate != null)
            {
                RevokeCertificate.Initialize(context, RevokeCertificate_InitializationString);
            }
        }

        #region Initialization String
        private const string RevokeCertificate_InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAEQAAAFJl" +
           "dm9rZUNlcnRpZmljYXRlAQGbOgAvAQGbOps6AAABAf////8BAAAAFWCpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBnDoALgBEnDoAAJYCAAAAAQAqAQEcAAAADQAAAEFwcGxpY2F0aW9uSWQAEf////8AAAAA" +
           "AAEAKgEBGgAAAAsAAABDZXJ0aWZpY2F0ZQAP/////wAAAAAAAQAoAQEAAAABAf////8AAAAA";

        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEAIAAAAENl" +
           "cnRpZmljYXRlRGlyZWN0b3J5VHlwZUluc3RhbmNlAQE/AAEBPwD/////EAAAAARggAoBAAAAAQAMAAAA" +
           "QXBwbGljYXRpb25zAQFAAAAvAD1AAAAA/////wAAAAAEYYIKBAAAAAEAEAAAAEZpbmRBcHBsaWNhdGlv" +
           "bnMBAUEAAC8BAQ8AQQAAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFCAAAu" +
           "AERCAAAAlgEAAAABACoBAR0AAAAOAAAAQXBwbGljYXRpb25VcmkADP////8AAAAAAAEAKAEBAAAAAQH/" +
           "////AAAAABVgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFDAAAuAERDAAAAlgEAAAABACoBAR0A" +
           "AAAMAAAAQXBwbGljYXRpb25zAQEBAAEAAAAAAAAAAAEAKAEBAAAAAQH/////AAAAAARhggoEAAAAAQAT" +
           "AAAAUmVnaXN0ZXJBcHBsaWNhdGlvbgEBRAAALwEBEgBEAAAAAQH/////AgAAABVgqQoCAAAAAAAOAAAA" +
           "SW5wdXRBcmd1bWVudHMBAUUAAC4AREUAAACWAQAAAAEAKgEBHAAAAAsAAABBcHBsaWNhdGlvbgEBAQD/" +
           "////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBRgAA" +
           "LgBERgAAAJYBAAAAAQAqAQEcAAAADQAAAEFwcGxpY2F0aW9uSWQAEf////8AAAAAAAEAKAEBAAAAAQH/" +
           "////AAAAAARhggoEAAAAAQARAAAAVXBkYXRlQXBwbGljYXRpb24BAcEAAC8BAbwAwQAAAAEB/////wEA" +
           "AAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQHCAAAuAETCAAAAlgEAAAABACoBARwAAAALAAAA" +
           "QXBwbGljYXRpb24BAQEA/////wAAAAAAAQAoAQEAAAABAf////8AAAAABGGCCgQAAAABABUAAABVbnJl" +
           "Z2lzdGVyQXBwbGljYXRpb24BAUcAAC8BARUARwAAAAEB/////wEAAAAVYKkKAgAAAAAADgAAAElucHV0" +
           "QXJndW1lbnRzAQFIAAAuAERIAAAAlgEAAAABACoBARwAAAANAAAAQXBwbGljYXRpb25JZAAR/////wAA" +
           "AAAAAQAoAQEAAAABAf////8AAAAABGGCCgQAAAABAA4AAABHZXRBcHBsaWNhdGlvbgEB1QAALwEB0gDV" +
           "AAAAAQH/////AgAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAdYAAC4ARNYAAACWAQAAAAEA" +
           "KgEBHAAAAA0AAABBcHBsaWNhdGlvbklkABH/////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAA" +
           "AAAADwAAAE91dHB1dEFyZ3VtZW50cwEB1wAALgBE1wAAAJYBAAAAAQAqAQEcAAAACwAAAEFwcGxpY2F0" +
           "aW9uAQEBAP////8AAAAAAAEAKAEBAAAAAQH/////AAAAAARhggoEAAAAAQARAAAAUXVlcnlBcHBsaWNh" +
           "dGlvbnMBAWcDAC8BAWQDZwMAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFo" +
           "AwAuAERoAwAAlgcAAAABACoBAR8AAAAQAAAAU3RhcnRpbmdSZWNvcmRJZAAH/////wAAAAAAAQAqAQEh" +
           "AAAAEgAAAE1heFJlY29yZHNUb1JldHVybgAH/////wAAAAAAAQAqAQEeAAAADwAAAEFwcGxpY2F0aW9u" +
           "TmFtZQAM/////wAAAAAAAQAqAQEdAAAADgAAAEFwcGxpY2F0aW9uVXJpAAz/////AAAAAAABACoBAR4A" +
           "AAAPAAAAQXBwbGljYXRpb25UeXBlAAf/////AAAAAAABACoBARkAAAAKAAAAUHJvZHVjdFVyaQAM////" +
           "/wAAAAAAAQAqAQEbAAAADAAAAENhcGFiaWxpdGllcwAMAQAAAAAAAAAAAQAoAQEAAAABAf////8AAAAA" +
           "FWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAWkDAC4ARGkDAACWAwAAAAEAKgEBJQAAABQAAABM" +
           "YXN0Q291bnRlclJlc2V0VGltZQEAJgH/////AAAAAAABACoBARsAAAAMAAAATmV4dFJlY29yZElkAAf/" +
           "////AAAAAAABACoBAR0AAAAMAAAAQXBwbGljYXRpb25zAQA0AQEAAAAAAAAAAAEAKAEBAAAAAQH/////" +
           "AAAAAARhggoEAAAAAQAMAAAAUXVlcnlTZXJ2ZXJzAQFJAAAvAQEXAEkAAAABAf////8CAAAAFWCpCgIA" +
           "AAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBSgAALgBESgAAAJYGAAAAAQAqAQEfAAAAEAAAAFN0YXJ0aW5n" +
           "UmVjb3JkSWQAB/////8AAAAAAAEAKgEBIQAAABIAAABNYXhSZWNvcmRzVG9SZXR1cm4AB/////8AAAAA" +
           "AAEAKgEBHgAAAA8AAABBcHBsaWNhdGlvbk5hbWUADP////8AAAAAAAEAKgEBHQAAAA4AAABBcHBsaWNh" +
           "dGlvblVyaQAM/////wAAAAAAAQAqAQEZAAAACgAAAFByb2R1Y3RVcmkADP////8AAAAAAAEAKgEBIQAA" +
           "ABIAAABTZXJ2ZXJDYXBhYmlsaXRpZXMADAEAAAAAAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoCAAAA" +
           "AAAPAAAAT3V0cHV0QXJndW1lbnRzAQFLAAAuAERLAAAAlgIAAAABACoBASUAAAAUAAAATGFzdENvdW50" +
           "ZXJSZXNldFRpbWUBACYB/////wAAAAAAAQAqAQEYAAAABwAAAFNlcnZlcnMBAJ0vAQAAAAAAAAAAAQAo" +
           "AQEAAAABAf////8AAAAABGCACgEAAAABABEAAABDZXJ0aWZpY2F0ZUdyb3VwcwEB/wEAIwEA9TX/AQAA" +
           "/////wEAAAAEYIAKAQAAAAAAFwAAAERlZmF1bHRBcHBsaWNhdGlvbkdyb3VwAQEAAgAvAQALMQACAAD/" +
           "////AgAAAARggAoBAAAAAAAJAAAAVHJ1c3RMaXN0AQEBAgAvAQDqMAECAAD/////DAAAADVgiQoCAAAA" +
           "AAAEAAAAU2l6ZQEBAgIDAAAAAB4AAABUaGUgc2l6ZSBvZiB0aGUgZmlsZSBpbiBieXRlcy4ALgBEAgIA" +
           "AAAJ/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFdyaXRhYmxlAQEDAgMAAAAAHQAAAFdoZXRoZXIg" +
           "dGhlIGZpbGUgaXMgd3JpdGFibGUuAC4ARAMCAAAAAf////8BAf////8AAAAANWCJCgIAAAAAAAwAAABV" +
           "c2VyV3JpdGFibGUBAQQCAwAAAAAxAAAAV2hldGhlciB0aGUgZmlsZSBpcyB3cml0YWJsZSBieSB0aGUg" +
           "Y3VycmVudCB1c2VyLgAuAEQEAgAAAAH/////AQH/////AAAAADVgiQoCAAAAAAAJAAAAT3BlbkNvdW50" +
           "AQEFAgMAAAAAKAAAAFRoZSBjdXJyZW50IG51bWJlciBvZiBvcGVuIGZpbGUgaGFuZGxlcy4ALgBEBQIA" +
           "AAAF/////wEB/////wAAAAAEYYIKBAAAAAAABAAAAE9wZW4BAQcCAC8BADwtBwIAAAEB/////wIAAAAV" +
           "YKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEIAgAuAEQIAgAAlgEAAAABACoBARMAAAAEAAAATW9k" +
           "ZQAD/////wAAAAAAAQAoAQEAAAABAf////8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMB" +
           "AQkCAC4ARAkCAACWAQAAAAEAKgEBGQAAAAoAAABGaWxlSGFuZGxlAAf/////AAAAAAABACgBAQAAAAEB" +
           "/////wAAAAAEYYIKBAAAAAAABQAAAENsb3NlAQEKAgAvAQA/LQoCAAABAf////8BAAAAFWCpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBCwIALgBECwIAAJYBAAAAAQAqAQEZAAAACgAAAEZpbGVIYW5kbGUA" +
           "B/////8AAAAAAAEAKAEBAAAAAQH/////AAAAAARhggoEAAAAAAAEAAAAUmVhZAEBDAIALwEAQS0MAgAA" +
           "AQH/////AgAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAQ0CAC4ARA0CAACWAgAAAAEAKgEB" +
           "GQAAAAoAAABGaWxlSGFuZGxlAAf/////AAAAAAABACoBARUAAAAGAAAATGVuZ3RoAAb/////AAAAAAAB" +
           "ACgBAQAAAAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBDgIALgBEDgIAAJYB" +
           "AAAAAQAqAQETAAAABAAAAERhdGEAD/////8AAAAAAAEAKAEBAAAAAQH/////AAAAAARhggoEAAAAAAAF" +
           "AAAAV3JpdGUBAQ8CAC8BAEQtDwIAAAEB/////wEAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRz" +
           "AQEQAgAuAEQQAgAAlgIAAAABACoBARkAAAAKAAAARmlsZUhhbmRsZQAH/////wAAAAAAAQAqAQETAAAA" +
           "BAAAAERhdGEAD/////8AAAAAAAEAKAEBAAAAAQH/////AAAAAARhggoEAAAAAAALAAAAR2V0UG9zaXRp" +
           "b24BARECAC8BAEYtEQIAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQESAgAu" +
           "AEQSAgAAlgEAAAABACoBARkAAAAKAAAARmlsZUhhbmRsZQAH/////wAAAAAAAQAoAQEAAAABAf////8A" +
           "AAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBARMCAC4ARBMCAACWAQAAAAEAKgEBFwAAAAgA" +
           "AABQb3NpdGlvbgAJ/////wAAAAAAAQAoAQEAAAABAf////8AAAAABGGCCgQAAAAAAAsAAABTZXRQb3Np" +
           "dGlvbgEBFAIALwEASS0UAgAAAQH/////AQAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBARUC" +
           "AC4ARBUCAACWAgAAAAEAKgEBGQAAAAoAAABGaWxlSGFuZGxlAAf/////AAAAAAABACoBARcAAAAIAAAA" +
           "UG9zaXRpb24ACf////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVgiQoCAAAAAAAOAAAATGFzdFVwZGF0" +
           "ZVRpbWUBARYCAC4ARBYCAAABACYB/////wEB/////wAAAAAEYYIKBAAAAAAADQAAAE9wZW5XaXRoTWFz" +
           "a3MBARcCAC8BAP8wFwIAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEYAgAu" +
           "AEQYAgAAlgEAAAABACoBARQAAAAFAAAATWFza3MAB/////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVg" +
           "qQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQEZAgAuAEQZAgAAlgEAAAABACoBARkAAAAKAAAARmls" +
           "ZUhhbmRsZQAH/////wAAAAAAAQAoAQEAAAABAf////8AAAAAFWCJCgIAAAAAABAAAABDZXJ0aWZpY2F0" +
           "ZVR5cGVzAQEhAgAuAEQhAgAAABEBAAAAAQH/////AAAAAARhggoEAAAAAQATAAAAU3RhcnRTaWduaW5n" +
           "UmVxdWVzdAEBTwAALwEBTwBPAAAAAQH/////AgAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMB" +
           "AVAAAC4ARFAAAACWBAAAAAEAKgEBHAAAAA0AAABBcHBsaWNhdGlvbklkABH/////AAAAAAABACoBASEA" +
           "AAASAAAAQ2VydGlmaWNhdGVHcm91cElkABH/////AAAAAAABACoBASAAAAARAAAAQ2VydGlmaWNhdGVU" +
           "eXBlSWQAEf////8AAAAAAAEAKgEBIQAAABIAAABDZXJ0aWZpY2F0ZVJlcXVlc3QAD/////8AAAAAAAEA" +
           "KAEBAAAAAQH/////AAAAABVgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFRAAAuAERRAAAAlgEA" +
           "AAABACoBARgAAAAJAAAAUmVxdWVzdElkABH/////AAAAAAABACgBAQAAAAEB/////wAAAAAEYYIKBAAA" +
           "AAEAFgAAAFN0YXJ0TmV3S2V5UGFpclJlcXVlc3QBAUwAAC8BAUwATAAAAAEB/////wIAAAAVYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQFNAAAuAERNAAAAlgcAAAABACoBARwAAAANAAAAQXBwbGljYXRp" +
           "b25JZAAR/////wAAAAAAAQAqAQEhAAAAEgAAAENlcnRpZmljYXRlR3JvdXBJZAAR/////wAAAAAAAQAq" +
           "AQEgAAAAEQAAAENlcnRpZmljYXRlVHlwZUlkABH/////AAAAAAABACoBARoAAAALAAAAU3ViamVjdE5h" +
           "bWUADP////8AAAAAAAEAKgEBGgAAAAsAAABEb21haW5OYW1lcwAMAQAAAAAAAAAAAQAqAQEfAAAAEAAA" +
           "AFByaXZhdGVLZXlGb3JtYXQADP////8AAAAAAAEAKgEBIQAAABIAAABQcml2YXRlS2V5UGFzc3dvcmQA" +
           "DP////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFO" +
           "AAAuAEROAAAAlgEAAAABACoBARgAAAAJAAAAUmVxdWVzdElkABH/////AAAAAAABACgBAQAAAAEB////" +
           "/wAAAAAEYYIKBAAAAAEADQAAAEZpbmlzaFJlcXVlc3QBAVUAAC8BAVUAVQAAAAEB/////wIAAAAVYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFWAAAuAERWAAAAlgIAAAABACoBARwAAAANAAAAQXBwbGlj" +
           "YXRpb25JZAAR/////wAAAAAAAQAqAQEYAAAACQAAAFJlcXVlc3RJZAAR/////wAAAAAAAQAoAQEAAAAB" +
           "Af////8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAVcAAC4ARFcAAACWAwAAAAEAKgEB" +
           "GgAAAAsAAABDZXJ0aWZpY2F0ZQAP/////wAAAAAAAQAqAQEZAAAACgAAAFByaXZhdGVLZXkAD/////8A" +
           "AAAAAAEAKgEBIQAAABIAAABJc3N1ZXJDZXJ0aWZpY2F0ZXMADwEAAAAAAAAAAAEAKAEBAAAAAQH/////" +
           "AAAAAARhggoEAAAAAQARAAAAUmV2b2tlQ2VydGlmaWNhdGUBAZs6AC8BAZs6mzoAAAEB/////wEAAAAV" +
           "YKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQGcOgAuAEScOgAAlgIAAAABACoBARwAAAANAAAAQXBw" +
           "bGljYXRpb25JZAAR/////wAAAAAAAQAqAQEaAAAACwAAAENlcnRpZmljYXRlAA//////AAAAAAABACgB" +
           "AQAAAAEB/////wAAAAAEYYIKBAAAAAEAFAAAAEdldENlcnRpZmljYXRlR3JvdXBzAQFxAQAvAQFxAXEB" +
           "AAABAf////8CAAAAFWCpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBcgEALgBEcgEAAJYBAAAAAQAq" +
           "AQEcAAAADQAAAEFwcGxpY2F0aW9uSWQAEf////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoCAAAA" +
           "AAAPAAAAT3V0cHV0QXJndW1lbnRzAQFzAQAuAERzAQAAlgEAAAABACoBASIAAAATAAAAQ2VydGlmaWNh" +
           "dGVHcm91cElkcwARAQAAAAAAAAAAAQAoAQEAAAABAf////8AAAAABGGCCgQAAAABAAwAAABHZXRUcnVz" +
           "dExpc3QBAcUAAC8BAcUAxQAAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQHG" +
           "AAAuAETGAAAAlgIAAAABACoBARwAAAANAAAAQXBwbGljYXRpb25JZAAR/////wAAAAAAAQAqAQEhAAAA" +
           "EgAAAENlcnRpZmljYXRlR3JvdXBJZAAR/////wAAAAAAAQAoAQEAAAABAf////8AAAAAFWCpCgIAAAAA" +
           "AA8AAABPdXRwdXRBcmd1bWVudHMBAccAAC4ARMcAAACWAQAAAAEAKgEBGgAAAAsAAABUcnVzdExpc3RJ" +
           "ZAAR/////wAAAAAAAQAoAQEAAAABAf////8AAAAABGGCCgQAAAABABQAAABHZXRDZXJ0aWZpY2F0ZVN0" +
           "YXR1cwEB3gAALwEB3gDeAAAAAQH/////AgAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAd8A" +
           "AC4ARN8AAACWAwAAAAEAKgEBHAAAAA0AAABBcHBsaWNhdGlvbklkABH/////AAAAAAABACoBASEAAAAS" +
           "AAAAQ2VydGlmaWNhdGVHcm91cElkABH/////AAAAAAABACoBASAAAAARAAAAQ2VydGlmaWNhdGVUeXBl" +
           "SWQAEf////8AAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRz" +
           "AQHgAAAuAETgAAAAlgEAAAABACoBAR0AAAAOAAAAVXBkYXRlUmVxdWlyZWQAAf////8AAAAAAAEAKAEB" +
           "AAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the CertificateGroups Object.
        /// </summary>
        public CertificateGroupFolderState CertificateGroups
        {
            get
            {
                return m_certificateGroups;
            }

            set
            {
                if (!Object.ReferenceEquals(m_certificateGroups, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateGroups = value;
            }
        }

        /// <summary>
        /// A description for the StartSigningRequestMethodType Method.
        /// </summary>
        public StartSigningRequestMethodState StartSigningRequest
        {
            get
            {
                return m_startSigningRequestMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_startSigningRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startSigningRequestMethod = value;
            }
        }

        /// <summary>
        /// A description for the StartNewKeyPairRequestMethodType Method.
        /// </summary>
        public StartNewKeyPairRequestMethodState StartNewKeyPairRequest
        {
            get
            {
                return m_startNewKeyPairRequestMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_startNewKeyPairRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startNewKeyPairRequestMethod = value;
            }
        }

        /// <summary>
        /// A description for the FinishRequestMethodType Method.
        /// </summary>
        public FinishRequestMethodState FinishRequest
        {
            get
            {
                return m_finishRequestMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_finishRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_finishRequestMethod = value;
            }
        }

        /// <summary>
        /// A description for the RevokeCertificateMethodType Method.
        /// </summary>
        public RevokeCertificateMethodState RevokeCertificate
        {
            get
            {
                return m_revokeCertificateMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_revokeCertificateMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_revokeCertificateMethod = value;
            }
        }

        /// <summary>
        /// A description for the GetCertificateGroupsMethodType Method.
        /// </summary>
        public GetCertificateGroupsMethodState GetCertificateGroups
        {
            get
            {
                return m_getCertificateGroupsMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_getCertificateGroupsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getCertificateGroupsMethod = value;
            }
        }

        /// <summary>
        /// A description for the GetTrustListMethodType Method.
        /// </summary>
        public GetTrustListMethodState GetTrustList
        {
            get
            {
                return m_getTrustListMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_getTrustListMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getTrustListMethod = value;
            }
        }

        /// <summary>
        /// A description for the GetCertificateStatusMethodType Method.
        /// </summary>
        public GetCertificateStatusMethodState GetCertificateStatus
        {
            get
            {
                return m_getCertificateStatusMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_getCertificateStatusMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getCertificateStatusMethod = value;
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
            if (m_certificateGroups != null)
            {
                children.Add(m_certificateGroups);
            }

            if (m_startSigningRequestMethod != null)
            {
                children.Add(m_startSigningRequestMethod);
            }

            if (m_startNewKeyPairRequestMethod != null)
            {
                children.Add(m_startNewKeyPairRequestMethod);
            }

            if (m_finishRequestMethod != null)
            {
                children.Add(m_finishRequestMethod);
            }

            if (m_revokeCertificateMethod != null)
            {
                children.Add(m_revokeCertificateMethod);
            }

            if (m_getCertificateGroupsMethod != null)
            {
                children.Add(m_getCertificateGroupsMethod);
            }

            if (m_getTrustListMethod != null)
            {
                children.Add(m_getTrustListMethod);
            }

            if (m_getCertificateStatusMethod != null)
            {
                children.Add(m_getCertificateStatusMethod);
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
                case Opc.Ua.Gds.BrowseNames.CertificateGroups:
                {
                    if (createOrReplace)
                    {
                        if (CertificateGroups == null)
                        {
                            if (replacement == null)
                            {
                                CertificateGroups = new CertificateGroupFolderState(this);
                            }
                            else
                            {
                                CertificateGroups = (CertificateGroupFolderState)replacement;
                            }
                        }
                    }

                    instance = CertificateGroups;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.StartSigningRequest:
                {
                    if (createOrReplace)
                    {
                        if (StartSigningRequest == null)
                        {
                            if (replacement == null)
                            {
                                StartSigningRequest = new StartSigningRequestMethodState(this);
                            }
                            else
                            {
                                StartSigningRequest = (StartSigningRequestMethodState)replacement;
                            }
                        }
                    }

                    instance = StartSigningRequest;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.StartNewKeyPairRequest:
                {
                    if (createOrReplace)
                    {
                        if (StartNewKeyPairRequest == null)
                        {
                            if (replacement == null)
                            {
                                StartNewKeyPairRequest = new StartNewKeyPairRequestMethodState(this);
                            }
                            else
                            {
                                StartNewKeyPairRequest = (StartNewKeyPairRequestMethodState)replacement;
                            }
                        }
                    }

                    instance = StartNewKeyPairRequest;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.FinishRequest:
                {
                    if (createOrReplace)
                    {
                        if (FinishRequest == null)
                        {
                            if (replacement == null)
                            {
                                FinishRequest = new FinishRequestMethodState(this);
                            }
                            else
                            {
                                FinishRequest = (FinishRequestMethodState)replacement;
                            }
                        }
                    }

                    instance = FinishRequest;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.RevokeCertificate:
                {
                    if (createOrReplace)
                    {
                        if (RevokeCertificate == null)
                        {
                            if (replacement == null)
                            {
                                RevokeCertificate = new RevokeCertificateMethodState(this);
                            }
                            else
                            {
                                RevokeCertificate = (RevokeCertificateMethodState)replacement;
                            }
                        }
                    }

                    instance = RevokeCertificate;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.GetCertificateGroups:
                {
                    if (createOrReplace)
                    {
                        if (GetCertificateGroups == null)
                        {
                            if (replacement == null)
                            {
                                GetCertificateGroups = new GetCertificateGroupsMethodState(this);
                            }
                            else
                            {
                                GetCertificateGroups = (GetCertificateGroupsMethodState)replacement;
                            }
                        }
                    }

                    instance = GetCertificateGroups;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.GetTrustList:
                {
                    if (createOrReplace)
                    {
                        if (GetTrustList == null)
                        {
                            if (replacement == null)
                            {
                                GetTrustList = new GetTrustListMethodState(this);
                            }
                            else
                            {
                                GetTrustList = (GetTrustListMethodState)replacement;
                            }
                        }
                    }

                    instance = GetTrustList;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.GetCertificateStatus:
                {
                    if (createOrReplace)
                    {
                        if (GetCertificateStatus == null)
                        {
                            if (replacement == null)
                            {
                                GetCertificateStatus = new GetCertificateStatusMethodState(this);
                            }
                            else
                            {
                                GetCertificateStatus = (GetCertificateStatusMethodState)replacement;
                            }
                        }
                    }

                    instance = GetCertificateStatus;
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

        #region Private Fields
        private CertificateGroupFolderState m_certificateGroups;
        private StartSigningRequestMethodState m_startSigningRequestMethod;
        private StartNewKeyPairRequestMethodState m_startNewKeyPairRequestMethod;
        private FinishRequestMethodState m_finishRequestMethod;
        private RevokeCertificateMethodState m_revokeCertificateMethod;
        private GetCertificateGroupsMethodState m_getCertificateGroupsMethod;
        private GetTrustListMethodState m_getTrustListMethod;
        private GetCertificateStatusMethodState m_getCertificateStatusMethod;
        #endregion
    }
    #endif
    #endregion

    #region CertificateRequestedAuditEventState Class
    #if (!OPCUA_EXCLUDE_CertificateRequestedAuditEventState)
    /// <summary>
    /// Stores an instance of the CertificateRequestedAuditEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class CertificateRequestedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public CertificateRequestedAuditEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.CertificateRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEAKgAAAENl" +
           "cnRpZmljYXRlUmVxdWVzdGVkQXVkaXRFdmVudFR5cGVJbnN0YW5jZQEBWwABAVsA/////xEAAAA1YIkK" +
           "AgAAAAAABwAAAEV2ZW50SWQBAVwAAwAAAAArAAAAQSBnbG9iYWxseSB1bmlxdWUgaWRlbnRpZmllciBm" +
           "b3IgdGhlIGV2ZW50LgAuAERcAAAAAA//////AQH/////AAAAADVgiQoCAAAAAAAJAAAARXZlbnRUeXBl" +
           "AQFdAAMAAAAAIgAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdHlwZS4ALgBEXQAAAAAR////" +
           "/wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAV4AAwAAAAAYAAAAVGhlIHNvdXJjZSBv" +
           "ZiB0aGUgZXZlbnQuAC4ARF4AAAAAEf////8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3VyY2VOYW1l" +
           "AQFfAAMAAAAAKQAAAEEgZGVzY3JpcHRpb24gb2YgdGhlIHNvdXJjZSBvZiB0aGUgZXZlbnQuAC4ARF8A" +
           "AAAADP////8BAf////8AAAAANWCJCgIAAAAAAAQAAABUaW1lAQFgAAMAAAAAGAAAAFdoZW4gdGhlIGV2" +
           "ZW50IG9jY3VycmVkLgAuAERgAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAsAAABSZWNlaXZl" +
           "VGltZQEBYQADAAAAAD4AAABXaGVuIHRoZSBzZXJ2ZXIgcmVjZWl2ZWQgdGhlIGV2ZW50IGZyb20gdGhl" +
           "IHVuZGVybHlpbmcgc3lzdGVtLgAuAERhAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAcAAABN" +
           "ZXNzYWdlAQFjAAMAAAAAJQAAAEEgbG9jYWxpemVkIGRlc2NyaXB0aW9uIG9mIHRoZSBldmVudC4ALgBE" +
           "YwAAAAAV/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNldmVyaXR5AQFkAAMAAAAAIQAAAEluZGlj" +
           "YXRlcyBob3cgdXJnZW50IGFuIGV2ZW50IGlzLgAuAERkAAAAAAX/////AQH/////AAAAADVgiQoCAAAA" +
           "AAAPAAAAQWN0aW9uVGltZVN0YW1wAQFlAAMAAAAALgAAAFdoZW4gdGhlIGFjdGlvbiB0cmlnZ2VyaW5n" +
           "IHRoZSBldmVudCBvY2N1cnJlZC4ALgBEZQAAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAAGAAAA" +
           "U3RhdHVzAQFmAAMAAAAAYQAAAElmIFRSVUUgdGhlIGFjdGlvbiB3YXMgcGVyZm9ybWVkLiBJZiBGQUxT" +
           "RSB0aGUgYWN0aW9uIGZhaWxlZCBhbmQgdGhlIHNlcnZlciBzdGF0ZSBkaWQgbm90IGNoYW5nZS4ALgBE" +
           "ZgAAAAAB/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNlcnZlcklkAQFnAAMAAAAAOgAAAFRoZSB1" +
           "bmlxdWUgaWRlbnRpZmllciBmb3IgdGhlIHNlcnZlciBnZW5lcmF0aW5nIHRoZSBldmVudC4ALgBEZwAA" +
           "AAAM/////wEB/////wAAAAA1YIkKAgAAAAAAEgAAAENsaWVudEF1ZGl0RW50cnlJZAEBaAADAAAAAEMA" +
           "AABUaGUgbG9nIGVudHJ5IGlkIHByb3ZpZGVkIGluIHRoZSByZXF1ZXN0IHRoYXQgaW5pdGlhdGVkIHRo" +
           "ZSBhY3Rpb24uAC4ARGgAAAAADP////8BAf////8AAAAANWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQB" +
           "AWkAAwAAAABIAAAAVGhlIHVzZXIgaWRlbnRpdHkgYXNzb2NpYXRlZCB3aXRoIHRoZSBzZXNzaW9uIHRo" +
           "YXQgaW5pdGlhdGVkIHRoZSBhY3Rpb24uAC4ARGkAAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAgA" +
           "AABNZXRob2RJZAEBagAALgBEagAAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAADgAAAElucHV0QXJn" +
           "dW1lbnRzAQFrAAAuAERrAAAAABgBAAAAAQH/////AAAAABVgiQoCAAAAAQAQAAAAQ2VydGlmaWNhdGVH" +
           "cm91cAEBzQIALgBEzQIAAAAR/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAENlcnRpZmljYXRlVHlw" +
           "ZQEBzgIALgBEzgIAAAAR/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the CertificateGroup Property.
        /// </summary>
        public PropertyState<NodeId> CertificateGroup
        {
            get
            {
                return m_certificateGroup;
            }

            set
            {
                if (!Object.ReferenceEquals(m_certificateGroup, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateGroup = value;
            }
        }

        /// <summary>
        /// A description for the CertificateType Property.
        /// </summary>
        public PropertyState<NodeId> CertificateType
        {
            get
            {
                return m_certificateType;
            }

            set
            {
                if (!Object.ReferenceEquals(m_certificateType, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateType = value;
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
            if (m_certificateGroup != null)
            {
                children.Add(m_certificateGroup);
            }

            if (m_certificateType != null)
            {
                children.Add(m_certificateType);
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
                case Opc.Ua.Gds.BrowseNames.CertificateGroup:
                {
                    if (createOrReplace)
                    {
                        if (CertificateGroup == null)
                        {
                            if (replacement == null)
                            {
                                CertificateGroup = new PropertyState<NodeId>(this);
                            }
                            else
                            {
                                CertificateGroup = (PropertyState<NodeId>)replacement;
                            }
                        }
                    }

                    instance = CertificateGroup;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.CertificateType:
                {
                    if (createOrReplace)
                    {
                        if (CertificateType == null)
                        {
                            if (replacement == null)
                            {
                                CertificateType = new PropertyState<NodeId>(this);
                            }
                            else
                            {
                                CertificateType = (PropertyState<NodeId>)replacement;
                            }
                        }
                    }

                    instance = CertificateType;
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

        #region Private Fields
        private PropertyState<NodeId> m_certificateGroup;
        private PropertyState<NodeId> m_certificateType;
        #endregion
    }
    #endif
    #endregion

    #region CertificateDeliveredAuditEventState Class
    #if (!OPCUA_EXCLUDE_CertificateDeliveredAuditEventState)
    /// <summary>
    /// Stores an instance of the CertificateDeliveredAuditEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class CertificateDeliveredAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public CertificateDeliveredAuditEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.CertificateDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEAKgAAAENl" +
           "cnRpZmljYXRlRGVsaXZlcmVkQXVkaXRFdmVudFR5cGVJbnN0YW5jZQEBbQABAW0A/////xEAAAA1YIkK" +
           "AgAAAAAABwAAAEV2ZW50SWQBAW4AAwAAAAArAAAAQSBnbG9iYWxseSB1bmlxdWUgaWRlbnRpZmllciBm" +
           "b3IgdGhlIGV2ZW50LgAuAERuAAAAAA//////AQH/////AAAAADVgiQoCAAAAAAAJAAAARXZlbnRUeXBl" +
           "AQFvAAMAAAAAIgAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdHlwZS4ALgBEbwAAAAAR////" +
           "/wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAXAAAwAAAAAYAAAAVGhlIHNvdXJjZSBv" +
           "ZiB0aGUgZXZlbnQuAC4ARHAAAAAAEf////8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3VyY2VOYW1l" +
           "AQFxAAMAAAAAKQAAAEEgZGVzY3JpcHRpb24gb2YgdGhlIHNvdXJjZSBvZiB0aGUgZXZlbnQuAC4ARHEA" +
           "AAAADP////8BAf////8AAAAANWCJCgIAAAAAAAQAAABUaW1lAQFyAAMAAAAAGAAAAFdoZW4gdGhlIGV2" +
           "ZW50IG9jY3VycmVkLgAuAERyAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAsAAABSZWNlaXZl" +
           "VGltZQEBcwADAAAAAD4AAABXaGVuIHRoZSBzZXJ2ZXIgcmVjZWl2ZWQgdGhlIGV2ZW50IGZyb20gdGhl" +
           "IHVuZGVybHlpbmcgc3lzdGVtLgAuAERzAAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAcAAABN" +
           "ZXNzYWdlAQF1AAMAAAAAJQAAAEEgbG9jYWxpemVkIGRlc2NyaXB0aW9uIG9mIHRoZSBldmVudC4ALgBE" +
           "dQAAAAAV/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNldmVyaXR5AQF2AAMAAAAAIQAAAEluZGlj" +
           "YXRlcyBob3cgdXJnZW50IGFuIGV2ZW50IGlzLgAuAER2AAAAAAX/////AQH/////AAAAADVgiQoCAAAA" +
           "AAAPAAAAQWN0aW9uVGltZVN0YW1wAQF3AAMAAAAALgAAAFdoZW4gdGhlIGFjdGlvbiB0cmlnZ2VyaW5n" +
           "IHRoZSBldmVudCBvY2N1cnJlZC4ALgBEdwAAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAAGAAAA" +
           "U3RhdHVzAQF4AAMAAAAAYQAAAElmIFRSVUUgdGhlIGFjdGlvbiB3YXMgcGVyZm9ybWVkLiBJZiBGQUxT" +
           "RSB0aGUgYWN0aW9uIGZhaWxlZCBhbmQgdGhlIHNlcnZlciBzdGF0ZSBkaWQgbm90IGNoYW5nZS4ALgBE" +
           "eAAAAAAB/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNlcnZlcklkAQF5AAMAAAAAOgAAAFRoZSB1" +
           "bmlxdWUgaWRlbnRpZmllciBmb3IgdGhlIHNlcnZlciBnZW5lcmF0aW5nIHRoZSBldmVudC4ALgBEeQAA" +
           "AAAM/////wEB/////wAAAAA1YIkKAgAAAAAAEgAAAENsaWVudEF1ZGl0RW50cnlJZAEBegADAAAAAEMA" +
           "AABUaGUgbG9nIGVudHJ5IGlkIHByb3ZpZGVkIGluIHRoZSByZXF1ZXN0IHRoYXQgaW5pdGlhdGVkIHRo" +
           "ZSBhY3Rpb24uAC4ARHoAAAAADP////8BAf////8AAAAANWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQB" +
           "AXsAAwAAAABIAAAAVGhlIHVzZXIgaWRlbnRpdHkgYXNzb2NpYXRlZCB3aXRoIHRoZSBzZXNzaW9uIHRo" +
           "YXQgaW5pdGlhdGVkIHRoZSBhY3Rpb24uAC4ARHsAAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAgA" +
           "AABNZXRob2RJZAEBfAAALgBEfAAAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAADgAAAElucHV0QXJn" +
           "dW1lbnRzAQF9AAAuAER9AAAAABgBAAAAAQH/////AAAAABVgiQoCAAAAAQAQAAAAQ2VydGlmaWNhdGVH" +
           "cm91cAEBzwIALgBEzwIAAAAR/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAENlcnRpZmljYXRlVHlw" +
           "ZQEB0AIALgBE0AIAAAAR/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the CertificateGroup Property.
        /// </summary>
        public PropertyState<NodeId> CertificateGroup
        {
            get
            {
                return m_certificateGroup;
            }

            set
            {
                if (!Object.ReferenceEquals(m_certificateGroup, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateGroup = value;
            }
        }

        /// <summary>
        /// A description for the CertificateType Property.
        /// </summary>
        public PropertyState<NodeId> CertificateType
        {
            get
            {
                return m_certificateType;
            }

            set
            {
                if (!Object.ReferenceEquals(m_certificateType, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateType = value;
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
            if (m_certificateGroup != null)
            {
                children.Add(m_certificateGroup);
            }

            if (m_certificateType != null)
            {
                children.Add(m_certificateType);
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
                case Opc.Ua.Gds.BrowseNames.CertificateGroup:
                {
                    if (createOrReplace)
                    {
                        if (CertificateGroup == null)
                        {
                            if (replacement == null)
                            {
                                CertificateGroup = new PropertyState<NodeId>(this);
                            }
                            else
                            {
                                CertificateGroup = (PropertyState<NodeId>)replacement;
                            }
                        }
                    }

                    instance = CertificateGroup;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.CertificateType:
                {
                    if (createOrReplace)
                    {
                        if (CertificateType == null)
                        {
                            if (replacement == null)
                            {
                                CertificateType = new PropertyState<NodeId>(this);
                            }
                            else
                            {
                                CertificateType = (PropertyState<NodeId>)replacement;
                            }
                        }
                    }

                    instance = CertificateType;
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

        #region Private Fields
        private PropertyState<NodeId> m_certificateGroup;
        private PropertyState<NodeId> m_certificateType;
        #endregion
    }
    #endif
    #endregion

    #region KeyCredentialServiceState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialServiceState)
    /// <summary>
    /// Stores an instance of the KeyCredentialServiceType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class KeyCredentialServiceState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public KeyCredentialServiceState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);

            if (Revoke != null)
            {
                Revoke.Initialize(context, Revoke_InitializationString);
            }
        }

        #region Initialization String
        private const string Revoke_InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEABgAAAFJl" +
           "dm9rZQEBBQQALwEBBQQFBAAAAQH/////AQAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAQYE" +
           "AC4ARAYEAACWAQAAAAEAKgEBGwAAAAwAAABDcmVkZW50aWFsSWQADP////8AAAAAAAEAKAEBAAAAAQH/" +
           "////AAAAAA==";

        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEAIAAAAEtl" +
           "eUNyZWRlbnRpYWxTZXJ2aWNlVHlwZUluc3RhbmNlAQH8AwEB/AP/////BQAAABVgiQoCAAAAAQALAAAA" +
           "UmVzb3VyY2VVcmkBAf0DAC4ARP0DAAAADP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABQcm9maWxl" +
           "VXJpcwEB/gMALgBE/gMAAAAMAQAAAAEB/////wAAAAAEYYIKBAAAAAEADAAAAFN0YXJ0UmVxdWVzdAEB" +
           "/wMALwEB/wP/AwAAAQH/////AgAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAQAEAC4ARAAE" +
           "AACWBAAAAAEAKgEBHQAAAA4AAABBcHBsaWNhdGlvblVyaQAM/////wAAAAAAAQAqAQEYAAAACQAAAFB1" +
           "YmxpY0tleQAP/////wAAAAAAAQAqAQEgAAAAEQAAAFNlY3VyaXR5UG9saWN5VXJpAAz/////AAAAAAAB" +
           "ACoBAR0AAAAOAAAAUmVxdWVzdGVkUm9sZXMAEQEAAAAAAAAAAAEAKAEBAAAAAQH/////AAAAABVgqQoC" +
           "AAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQEBBAAuAEQBBAAAlgEAAAABACoBARgAAAAJAAAAUmVxdWVz" +
           "dElkABH/////AAAAAAABACgBAQAAAAEB/////wAAAAAEYYIKBAAAAAEADQAAAEZpbmlzaFJlcXVlc3QB" +
           "AQIEAC8BAQIEAgQAAAEB/////wIAAAAVYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEDBAAuAEQD" +
           "BAAAlgIAAAABACoBARgAAAAJAAAAUmVxdWVzdElkABH/////AAAAAAABACoBARwAAAANAAAAQ2FuY2Vs" +
           "UmVxdWVzdAAB/////wAAAAAAAQAoAQEAAAABAf////8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1" +
           "bWVudHMBAQQEAC4ARAQEAACWBQAAAAEAKgEBGwAAAAwAAABDcmVkZW50aWFsSWQADP////8AAAAAAAEA" +
           "KgEBHwAAABAAAABDcmVkZW50aWFsU2VjcmV0AA//////AAAAAAABACoBASQAAAAVAAAAQ2VydGlmaWNh" +
           "dGVUaHVtYnByaW50AAz/////AAAAAAABACoBASAAAAARAAAAU2VjdXJpdHlQb2xpY3lVcmkADP////8A" +
           "AAAAAAEAKgEBGwAAAAwAAABHcmFudGVkUm9sZXMAEQEAAAAAAAAAAAEAKAEBAAAAAQH/////AAAAAARh" +
           "ggoEAAAAAQAGAAAAUmV2b2tlAQEFBAAvAQEFBAUEAAABAf////8BAAAAFWCpCgIAAAAAAA4AAABJbnB1" +
           "dEFyZ3VtZW50cwEBBgQALgBEBgQAAJYBAAAAAQAqAQEbAAAADAAAAENyZWRlbnRpYWxJZAAM/////wAA" +
           "AAAAAQAoAQEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the ResourceUri Property.
        /// </summary>
        public PropertyState<string> ResourceUri
        {
            get
            {
                return m_resourceUri;
            }

            set
            {
                if (!Object.ReferenceEquals(m_resourceUri, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_resourceUri = value;
            }
        }

        /// <summary>
        /// A description for the ProfileUris Property.
        /// </summary>
        public PropertyState<string[]> ProfileUris
        {
            get
            {
                return m_profileUris;
            }

            set
            {
                if (!Object.ReferenceEquals(m_profileUris, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_profileUris = value;
            }
        }

        /// <summary>
        /// A description for the KeyCredentialStartRequestMethodType Method.
        /// </summary>
        public KeyCredentialStartRequestMethodState StartRequest
        {
            get
            {
                return m_startRequestMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_startRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startRequestMethod = value;
            }
        }

        /// <summary>
        /// A description for the KeyCredentialFinishRequestMethodType Method.
        /// </summary>
        public KeyCredentialFinishRequestMethodState FinishRequest
        {
            get
            {
                return m_finishRequestMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_finishRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_finishRequestMethod = value;
            }
        }

        /// <summary>
        /// A description for the KeyCredentialRevokeMethodType Method.
        /// </summary>
        public KeyCredentialRevokeMethodState Revoke
        {
            get
            {
                return m_revokeMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_revokeMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_revokeMethod = value;
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
            if (m_resourceUri != null)
            {
                children.Add(m_resourceUri);
            }

            if (m_profileUris != null)
            {
                children.Add(m_profileUris);
            }

            if (m_startRequestMethod != null)
            {
                children.Add(m_startRequestMethod);
            }

            if (m_finishRequestMethod != null)
            {
                children.Add(m_finishRequestMethod);
            }

            if (m_revokeMethod != null)
            {
                children.Add(m_revokeMethod);
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
                case Opc.Ua.Gds.BrowseNames.ResourceUri:
                {
                    if (createOrReplace)
                    {
                        if (ResourceUri == null)
                        {
                            if (replacement == null)
                            {
                                ResourceUri = new PropertyState<string>(this);
                            }
                            else
                            {
                                ResourceUri = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = ResourceUri;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.ProfileUris:
                {
                    if (createOrReplace)
                    {
                        if (ProfileUris == null)
                        {
                            if (replacement == null)
                            {
                                ProfileUris = new PropertyState<string[]>(this);
                            }
                            else
                            {
                                ProfileUris = (PropertyState<string[]>)replacement;
                            }
                        }
                    }

                    instance = ProfileUris;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.StartRequest:
                {
                    if (createOrReplace)
                    {
                        if (StartRequest == null)
                        {
                            if (replacement == null)
                            {
                                StartRequest = new KeyCredentialStartRequestMethodState(this);
                            }
                            else
                            {
                                StartRequest = (KeyCredentialStartRequestMethodState)replacement;
                            }
                        }
                    }

                    instance = StartRequest;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.FinishRequest:
                {
                    if (createOrReplace)
                    {
                        if (FinishRequest == null)
                        {
                            if (replacement == null)
                            {
                                FinishRequest = new KeyCredentialFinishRequestMethodState(this);
                            }
                            else
                            {
                                FinishRequest = (KeyCredentialFinishRequestMethodState)replacement;
                            }
                        }
                    }

                    instance = FinishRequest;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.Revoke:
                {
                    if (createOrReplace)
                    {
                        if (Revoke == null)
                        {
                            if (replacement == null)
                            {
                                Revoke = new KeyCredentialRevokeMethodState(this);
                            }
                            else
                            {
                                Revoke = (KeyCredentialRevokeMethodState)replacement;
                            }
                        }
                    }

                    instance = Revoke;
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

        #region Private Fields
        private PropertyState<string> m_resourceUri;
        private PropertyState<string[]> m_profileUris;
        private KeyCredentialStartRequestMethodState m_startRequestMethod;
        private KeyCredentialFinishRequestMethodState m_finishRequestMethod;
        private KeyCredentialRevokeMethodState m_revokeMethod;
        #endregion
    }
    #endif
    #endregion

    #region KeyCredentialStartRequestMethodState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialStartRequestMethodState)
    /// <summary>
    /// Stores an instance of the KeyCredentialStartRequestMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class KeyCredentialStartRequestMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public KeyCredentialStartRequestMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new KeyCredentialStartRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAIwAAAEtl" +
           "eUNyZWRlbnRpYWxTdGFydFJlcXVlc3RNZXRob2RUeXBlAQEHBAAvAQEHBAcEAAABAf////8CAAAAFWCp" +
           "CgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBCAQALgBECAQAAJYEAAAAAQAqAQEdAAAADgAAAEFwcGxp" +
           "Y2F0aW9uVXJpAAz/////AAAAAAABACoBARgAAAAJAAAAUHVibGljS2V5AA//////AAAAAAABACoBASAA" +
           "AAARAAAAU2VjdXJpdHlQb2xpY3lVcmkADP////8AAAAAAAEAKgEBHQAAAA4AAABSZXF1ZXN0ZWRSb2xl" +
           "cwARAQAAAAAAAAAAAQAoAQEAAAABAf////8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMB" +
           "AQkEAC4ARAkEAACWAQAAAAEAKgEBGAAAAAkAAABSZXF1ZXN0SWQAEf////8AAAAAAAEAKAEBAAAAAQH/" +
           "////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public KeyCredentialStartRequestMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            string applicationUri = (string)inputArguments[0];
            byte[] publicKey = (byte[])inputArguments[1];
            string securityPolicyUri = (string)inputArguments[2];
            NodeId[] requestedRoles = (NodeId[])inputArguments[3];

            NodeId requestId = (NodeId)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    applicationUri,
                    publicKey,
                    securityPolicyUri,
                    requestedRoles,
                    ref requestId);
            }

            outputArguments[0] = requestId;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult KeyCredentialStartRequestMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        string applicationUri,
        byte[] publicKey,
        string securityPolicyUri,
        NodeId[] requestedRoles,
        ref NodeId requestId);
    #endif
    #endregion

    #region KeyCredentialFinishRequestMethodState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialFinishRequestMethodState)
    /// <summary>
    /// Stores an instance of the KeyCredentialFinishRequestMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class KeyCredentialFinishRequestMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public KeyCredentialFinishRequestMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new KeyCredentialFinishRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAJAAAAEtl" +
           "eUNyZWRlbnRpYWxGaW5pc2hSZXF1ZXN0TWV0aG9kVHlwZQEBCgQALwEBCgQKBAAAAQH/////AgAAABVg" +
           "qQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAQsEAC4ARAsEAACWAgAAAAEAKgEBGAAAAAkAAABSZXF1" +
           "ZXN0SWQAEf////8AAAAAAAEAKgEBHAAAAA0AAABDYW5jZWxSZXF1ZXN0AAH/////AAAAAAABACgBAQAA" +
           "AAEB/////wAAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBDAQALgBEDAQAAJYFAAAAAQAq" +
           "AQEbAAAADAAAAENyZWRlbnRpYWxJZAAM/////wAAAAAAAQAqAQEfAAAAEAAAAENyZWRlbnRpYWxTZWNy" +
           "ZXQAD/////8AAAAAAAEAKgEBJAAAABUAAABDZXJ0aWZpY2F0ZVRodW1icHJpbnQADP////8AAAAAAAEA" +
           "KgEBIAAAABEAAABTZWN1cml0eVBvbGljeVVyaQAM/////wAAAAAAAQAqAQEbAAAADAAAAEdyYW50ZWRS" +
           "b2xlcwARAQAAAAAAAAAAAQAoAQEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public KeyCredentialFinishRequestMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            NodeId requestId = (NodeId)inputArguments[0];
            bool cancelRequest = (bool)inputArguments[1];

            string credentialId = (string)outputArguments[0];
            byte[] credentialSecret = (byte[])outputArguments[1];
            string certificateThumbprint = (string)outputArguments[2];
            string securityPolicyUri = (string)outputArguments[3];
            NodeId[] grantedRoles = (NodeId[])outputArguments[4];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    requestId,
                    cancelRequest,
                    ref credentialId,
                    ref credentialSecret,
                    ref certificateThumbprint,
                    ref securityPolicyUri,
                    ref grantedRoles);
            }

            outputArguments[0] = credentialId;
            outputArguments[1] = credentialSecret;
            outputArguments[2] = certificateThumbprint;
            outputArguments[3] = securityPolicyUri;
            outputArguments[4] = grantedRoles;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult KeyCredentialFinishRequestMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        NodeId requestId,
        bool cancelRequest,
        ref string credentialId,
        ref byte[] credentialSecret,
        ref string certificateThumbprint,
        ref string securityPolicyUri,
        ref NodeId[] grantedRoles);
    #endif
    #endregion

    #region KeyCredentialRevokeMethodState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialRevokeMethodState)
    /// <summary>
    /// Stores an instance of the KeyCredentialRevokeMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class KeyCredentialRevokeMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public KeyCredentialRevokeMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new KeyCredentialRevokeMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHQAAAEtl" +
           "eUNyZWRlbnRpYWxSZXZva2VNZXRob2RUeXBlAQENBAAvAQENBA0EAAABAf////8BAAAAFWCpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBDgQALgBEDgQAAJYBAAAAAQAqAQEbAAAADAAAAENyZWRlbnRpYWxJ" +
           "ZAAM/////wAAAAAAAQAoAQEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public KeyCredentialRevokeMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            string credentialId = (string)inputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    credentialId);
            }

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult KeyCredentialRevokeMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        string credentialId);
    #endif
    #endregion

    #region KeyCredentialRequestedAuditEventState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialRequestedAuditEventState)
    /// <summary>
    /// Stores an instance of the KeyCredentialRequestedAuditEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class KeyCredentialRequestedAuditEventState : KeyCredentialAuditEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public KeyCredentialRequestedAuditEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEALAAAAEtl" +
           "eUNyZWRlbnRpYWxSZXF1ZXN0ZWRBdWRpdEV2ZW50VHlwZUluc3RhbmNlAQEPBAEBDwT/////EAAAADVg" +
           "iQoCAAAAAAAHAAAARXZlbnRJZAEBEAQDAAAAACsAAABBIGdsb2JhbGx5IHVuaXF1ZSBpZGVudGlmaWVy" +
           "IGZvciB0aGUgZXZlbnQuAC4ARBAEAAAAD/////8BAf////8AAAAANWCJCgIAAAAAAAkAAABFdmVudFR5" +
           "cGUBAREEAwAAAAAiAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0eXBlLgAuAEQRBAAAABH/" +
           "////AQH/////AAAAADVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBEgQDAAAAABgAAABUaGUgc291cmNl" +
           "IG9mIHRoZSBldmVudC4ALgBEEgQAAAAR/////wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5h" +
           "bWUBARMEAwAAAAApAAAAQSBkZXNjcmlwdGlvbiBvZiB0aGUgc291cmNlIG9mIHRoZSBldmVudC4ALgBE" +
           "EwQAAAAM/////wEB/////wAAAAA1YIkKAgAAAAAABAAAAFRpbWUBARQEAwAAAAAYAAAAV2hlbiB0aGUg" +
           "ZXZlbnQgb2NjdXJyZWQuAC4ARBQEAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAACwAAAFJlY2Vp" +
           "dmVUaW1lAQEVBAMAAAAAPgAAAFdoZW4gdGhlIHNlcnZlciByZWNlaXZlZCB0aGUgZXZlbnQgZnJvbSB0" +
           "aGUgdW5kZXJseWluZyBzeXN0ZW0uAC4ARBUEAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAABwAA" +
           "AE1lc3NhZ2UBARcEAwAAAAAlAAAAQSBsb2NhbGl6ZWQgZGVzY3JpcHRpb24gb2YgdGhlIGV2ZW50LgAu" +
           "AEQXBAAAABX/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBARgEAwAAAAAhAAAASW5k" +
           "aWNhdGVzIGhvdyB1cmdlbnQgYW4gZXZlbnQgaXMuAC4ARBgEAAAABf////8BAf////8AAAAANWCJCgIA" +
           "AAAAAA8AAABBY3Rpb25UaW1lU3RhbXABARkEAwAAAAAuAAAAV2hlbiB0aGUgYWN0aW9uIHRyaWdnZXJp" +
           "bmcgdGhlIGV2ZW50IG9jY3VycmVkLgAuAEQZBAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAYA" +
           "AABTdGF0dXMBARoEAwAAAABhAAAASWYgVFJVRSB0aGUgYWN0aW9uIHdhcyBwZXJmb3JtZWQuIElmIEZB" +
           "TFNFIHRoZSBhY3Rpb24gZmFpbGVkIGFuZCB0aGUgc2VydmVyIHN0YXRlIGRpZCBub3QgY2hhbmdlLgAu" +
           "AEQaBAAAAAH/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2VydmVySWQBARsEAwAAAAA6AAAAVGhl" +
           "IHVuaXF1ZSBpZGVudGlmaWVyIGZvciB0aGUgc2VydmVyIGdlbmVyYXRpbmcgdGhlIGV2ZW50LgAuAEQb" +
           "BAAAAAz/////AQH/////AAAAADVgiQoCAAAAAAASAAAAQ2xpZW50QXVkaXRFbnRyeUlkAQEcBAMAAAAA" +
           "QwAAAFRoZSBsb2cgZW50cnkgaWQgcHJvdmlkZWQgaW4gdGhlIHJlcXVlc3QgdGhhdCBpbml0aWF0ZWQg" +
           "dGhlIGFjdGlvbi4ALgBEHAQAAAAM/////wEB/////wAAAAA1YIkKAgAAAAAADAAAAENsaWVudFVzZXJJ" +
           "ZAEBHQQDAAAAAEgAAABUaGUgdXNlciBpZGVudGl0eSBhc3NvY2lhdGVkIHdpdGggdGhlIHNlc3Npb24g" +
           "dGhhdCBpbml0aWF0ZWQgdGhlIGFjdGlvbi4ALgBEHQQAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "CAAAAE1ldGhvZElkAQEeBAAuAEQeBAAAABH/////AQH/////AAAAABVgiQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBAR8EAC4ARB8EAAAAGAEAAAABAf////8AAAAAFWCJCgIAAAAAAAsAAABSZXNvdXJjZVVy" +
           "aQEBIAQALgBEIAQAAAAM/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }
    #endif
    #endregion

    #region KeyCredentialDeliveredAuditEventState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialDeliveredAuditEventState)
    /// <summary>
    /// Stores an instance of the KeyCredentialDeliveredAuditEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class KeyCredentialDeliveredAuditEventState : KeyCredentialAuditEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public KeyCredentialDeliveredAuditEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEALAAAAEtl" +
           "eUNyZWRlbnRpYWxEZWxpdmVyZWRBdWRpdEV2ZW50VHlwZUluc3RhbmNlAQEhBAEBIQT/////EAAAADVg" +
           "iQoCAAAAAAAHAAAARXZlbnRJZAEBIgQDAAAAACsAAABBIGdsb2JhbGx5IHVuaXF1ZSBpZGVudGlmaWVy" +
           "IGZvciB0aGUgZXZlbnQuAC4ARCIEAAAAD/////8BAf////8AAAAANWCJCgIAAAAAAAkAAABFdmVudFR5" +
           "cGUBASMEAwAAAAAiAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0eXBlLgAuAEQjBAAAABH/" +
           "////AQH/////AAAAADVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBJAQDAAAAABgAAABUaGUgc291cmNl" +
           "IG9mIHRoZSBldmVudC4ALgBEJAQAAAAR/////wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5h" +
           "bWUBASUEAwAAAAApAAAAQSBkZXNjcmlwdGlvbiBvZiB0aGUgc291cmNlIG9mIHRoZSBldmVudC4ALgBE" +
           "JQQAAAAM/////wEB/////wAAAAA1YIkKAgAAAAAABAAAAFRpbWUBASYEAwAAAAAYAAAAV2hlbiB0aGUg" +
           "ZXZlbnQgb2NjdXJyZWQuAC4ARCYEAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAACwAAAFJlY2Vp" +
           "dmVUaW1lAQEnBAMAAAAAPgAAAFdoZW4gdGhlIHNlcnZlciByZWNlaXZlZCB0aGUgZXZlbnQgZnJvbSB0" +
           "aGUgdW5kZXJseWluZyBzeXN0ZW0uAC4ARCcEAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAABwAA" +
           "AE1lc3NhZ2UBASkEAwAAAAAlAAAAQSBsb2NhbGl6ZWQgZGVzY3JpcHRpb24gb2YgdGhlIGV2ZW50LgAu" +
           "AEQpBAAAABX/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBASoEAwAAAAAhAAAASW5k" +
           "aWNhdGVzIGhvdyB1cmdlbnQgYW4gZXZlbnQgaXMuAC4ARCoEAAAABf////8BAf////8AAAAANWCJCgIA" +
           "AAAAAA8AAABBY3Rpb25UaW1lU3RhbXABASsEAwAAAAAuAAAAV2hlbiB0aGUgYWN0aW9uIHRyaWdnZXJp" +
           "bmcgdGhlIGV2ZW50IG9jY3VycmVkLgAuAEQrBAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAYA" +
           "AABTdGF0dXMBASwEAwAAAABhAAAASWYgVFJVRSB0aGUgYWN0aW9uIHdhcyBwZXJmb3JtZWQuIElmIEZB" +
           "TFNFIHRoZSBhY3Rpb24gZmFpbGVkIGFuZCB0aGUgc2VydmVyIHN0YXRlIGRpZCBub3QgY2hhbmdlLgAu" +
           "AEQsBAAAAAH/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2VydmVySWQBAS0EAwAAAAA6AAAAVGhl" +
           "IHVuaXF1ZSBpZGVudGlmaWVyIGZvciB0aGUgc2VydmVyIGdlbmVyYXRpbmcgdGhlIGV2ZW50LgAuAEQt" +
           "BAAAAAz/////AQH/////AAAAADVgiQoCAAAAAAASAAAAQ2xpZW50QXVkaXRFbnRyeUlkAQEuBAMAAAAA" +
           "QwAAAFRoZSBsb2cgZW50cnkgaWQgcHJvdmlkZWQgaW4gdGhlIHJlcXVlc3QgdGhhdCBpbml0aWF0ZWQg" +
           "dGhlIGFjdGlvbi4ALgBELgQAAAAM/////wEB/////wAAAAA1YIkKAgAAAAAADAAAAENsaWVudFVzZXJJ" +
           "ZAEBLwQDAAAAAEgAAABUaGUgdXNlciBpZGVudGl0eSBhc3NvY2lhdGVkIHdpdGggdGhlIHNlc3Npb24g" +
           "dGhhdCBpbml0aWF0ZWQgdGhlIGFjdGlvbi4ALgBELwQAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "CAAAAE1ldGhvZElkAQEwBAAuAEQwBAAAABH/////AQH/////AAAAABVgiQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBATEEAC4ARDEEAAAAGAEAAAABAf////8AAAAAFWCJCgIAAAAAAAsAAABSZXNvdXJjZVVy" +
           "aQEBMgQALgBEMgQAAAAM/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }
    #endif
    #endregion

    #region KeyCredentialRevokedAuditEventState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialRevokedAuditEventState)
    /// <summary>
    /// Stores an instance of the KeyCredentialRevokedAuditEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class KeyCredentialRevokedAuditEventState : KeyCredentialAuditEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public KeyCredentialRevokedAuditEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialRevokedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEAKgAAAEtl" +
           "eUNyZWRlbnRpYWxSZXZva2VkQXVkaXRFdmVudFR5cGVJbnN0YW5jZQEBMwQBATME/////xAAAAA1YIkK" +
           "AgAAAAAABwAAAEV2ZW50SWQBATQEAwAAAAArAAAAQSBnbG9iYWxseSB1bmlxdWUgaWRlbnRpZmllciBm" +
           "b3IgdGhlIGV2ZW50LgAuAEQ0BAAAAA//////AQH/////AAAAADVgiQoCAAAAAAAJAAAARXZlbnRUeXBl" +
           "AQE1BAMAAAAAIgAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdHlwZS4ALgBENQQAAAAR////" +
           "/wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBATYEAwAAAAAYAAAAVGhlIHNvdXJjZSBv" +
           "ZiB0aGUgZXZlbnQuAC4ARDYEAAAAEf////8BAf////8AAAAANWCJCgIAAAAAAAoAAABTb3VyY2VOYW1l" +
           "AQE3BAMAAAAAKQAAAEEgZGVzY3JpcHRpb24gb2YgdGhlIHNvdXJjZSBvZiB0aGUgZXZlbnQuAC4ARDcE" +
           "AAAADP////8BAf////8AAAAANWCJCgIAAAAAAAQAAABUaW1lAQE4BAMAAAAAGAAAAFdoZW4gdGhlIGV2" +
           "ZW50IG9jY3VycmVkLgAuAEQ4BAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAsAAABSZWNlaXZl" +
           "VGltZQEBOQQDAAAAAD4AAABXaGVuIHRoZSBzZXJ2ZXIgcmVjZWl2ZWQgdGhlIGV2ZW50IGZyb20gdGhl" +
           "IHVuZGVybHlpbmcgc3lzdGVtLgAuAEQ5BAAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAcAAABN" +
           "ZXNzYWdlAQE7BAMAAAAAJQAAAEEgbG9jYWxpemVkIGRlc2NyaXB0aW9uIG9mIHRoZSBldmVudC4ALgBE" +
           "OwQAAAAV/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNldmVyaXR5AQE8BAMAAAAAIQAAAEluZGlj" +
           "YXRlcyBob3cgdXJnZW50IGFuIGV2ZW50IGlzLgAuAEQ8BAAAAAX/////AQH/////AAAAADVgiQoCAAAA" +
           "AAAPAAAAQWN0aW9uVGltZVN0YW1wAQE9BAMAAAAALgAAAFdoZW4gdGhlIGFjdGlvbiB0cmlnZ2VyaW5n" +
           "IHRoZSBldmVudCBvY2N1cnJlZC4ALgBEPQQAAAEAJgH/////AQH/////AAAAADVgiQoCAAAAAAAGAAAA" +
           "U3RhdHVzAQE+BAMAAAAAYQAAAElmIFRSVUUgdGhlIGFjdGlvbiB3YXMgcGVyZm9ybWVkLiBJZiBGQUxT" +
           "RSB0aGUgYWN0aW9uIGZhaWxlZCBhbmQgdGhlIHNlcnZlciBzdGF0ZSBkaWQgbm90IGNoYW5nZS4ALgBE" +
           "PgQAAAAB/////wEB/////wAAAAA1YIkKAgAAAAAACAAAAFNlcnZlcklkAQE/BAMAAAAAOgAAAFRoZSB1" +
           "bmlxdWUgaWRlbnRpZmllciBmb3IgdGhlIHNlcnZlciBnZW5lcmF0aW5nIHRoZSBldmVudC4ALgBEPwQA" +
           "AAAM/////wEB/////wAAAAA1YIkKAgAAAAAAEgAAAENsaWVudEF1ZGl0RW50cnlJZAEBQAQDAAAAAEMA" +
           "AABUaGUgbG9nIGVudHJ5IGlkIHByb3ZpZGVkIGluIHRoZSByZXF1ZXN0IHRoYXQgaW5pdGlhdGVkIHRo" +
           "ZSBhY3Rpb24uAC4AREAEAAAADP////8BAf////8AAAAANWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQB" +
           "AUEEAwAAAABIAAAAVGhlIHVzZXIgaWRlbnRpdHkgYXNzb2NpYXRlZCB3aXRoIHRoZSBzZXNzaW9uIHRo" +
           "YXQgaW5pdGlhdGVkIHRoZSBhY3Rpb24uAC4AREEEAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAgA" +
           "AABNZXRob2RJZAEBQgQALgBEQgQAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAADgAAAElucHV0QXJn" +
           "dW1lbnRzAQFDBAAuAERDBAAAABgBAAAAAQH/////AAAAABVgiQoCAAAAAAALAAAAUmVzb3VyY2VVcmkB" +
           "AUQEAC4AREQEAAAADP////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }
    #endif
    #endregion

    #region AuthorizationServiceState Class
    #if (!OPCUA_EXCLUDE_AuthorizationServiceState)
    /// <summary>
    /// Stores an instance of the AuthorizationServiceType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class AuthorizationServiceState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public AuthorizationServiceState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.AuthorizationServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);

            if (UserTokenPolicies != null)
            {
                UserTokenPolicies.Initialize(context, UserTokenPolicies_InitializationString);
            }

            if (RequestAccessToken != null)
            {
                RequestAccessToken.Initialize(context, RequestAccessToken_InitializationString);
            }
        }

        #region Initialization String
        private const string UserTokenPolicies_InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8VYIkKAgAAAAEAEQAAAFVz" +
           "ZXJUb2tlblBvbGljaWVzAQHHAwAuAETHAwAAAQAwAQEAAAABAf////8AAAAA";

        private const string RequestAccessToken_InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAEgAAAFJl" +
           "cXVlc3RBY2Nlc3NUb2tlbgEByQMALwEByQPJAwAAAQH/////AgAAABVgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBAcoDAC4ARMoDAACWAgAAAAEAKgEBHgAAAA0AAABJZGVudGl0eVRva2VuAQA8Af////8A" +
           "AAAAAAEAKgEBGQAAAAoAAABSZXNvdXJjZUlkAAz/////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkK" +
           "AgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBywMALgBEywMAAJYBAAAAAQAqAQEaAAAACwAAAEFjY2Vz" +
           "c1Rva2VuAAz/////AAAAAAABACgBAQAAAAEB/////wAAAAA=";

        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEAIAAAAEF1" +
           "dGhvcml6YXRpb25TZXJ2aWNlVHlwZUluc3RhbmNlAQHGAwEBxgP/////BQAAABVgiQoCAAAAAQAKAAAA" +
           "U2VydmljZVVyaQEB6wMALgBE6wMAAAAM/////wEB/////wAAAAAVYIkKAgAAAAEAEgAAAFNlcnZpY2VD" +
           "ZXJ0aWZpY2F0ZQEByAMALgBEyAMAAAAP/////wEB/////wAAAAAVYIkKAgAAAAEAEQAAAFVzZXJUb2tl" +
           "blBvbGljaWVzAQHHAwAuAETHAwAAAQAwAQEAAAABAf////8AAAAABGGCCgQAAAABABUAAABHZXRTZXJ2" +
           "aWNlRGVzY3JpcHRpb24BAewDAC8BAewD7AMAAAEB/////wEAAAAVYKkKAgAAAAAADwAAAE91dHB1dEFy" +
           "Z3VtZW50cwEB7QMALgBE7QMAAJYDAAAAAQAqAQEZAAAACgAAAFNlcnZpY2VVcmkADP////8AAAAAAAEA" +
           "KgEBIQAAABIAAABTZXJ2aWNlQ2VydGlmaWNhdGUAD/////8AAAAAAAEAKgEBIgAAABEAAABVc2VyVG9r" +
           "ZW5Qb2xpY2llcwEAMAEBAAAAAAAAAAABACgBAQAAAAEB/////wAAAAAEYYIKBAAAAAEAEgAAAFJlcXVl" +
           "c3RBY2Nlc3NUb2tlbgEByQMALwEByQPJAwAAAQH/////AgAAABVgqQoCAAAAAAAOAAAASW5wdXRBcmd1" +
           "bWVudHMBAcoDAC4ARMoDAACWAgAAAAEAKgEBHgAAAA0AAABJZGVudGl0eVRva2VuAQA8Af////8AAAAA" +
           "AAEAKgEBGQAAAAoAAABSZXNvdXJjZUlkAAz/////AAAAAAABACgBAQAAAAEB/////wAAAAAVYKkKAgAA" +
           "AAAADwAAAE91dHB1dEFyZ3VtZW50cwEBywMALgBEywMAAJYBAAAAAQAqAQEaAAAACwAAAEFjY2Vzc1Rv" +
           "a2VuAAz/////AAAAAAABACgBAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the ServiceUri Property.
        /// </summary>
        public PropertyState<string> ServiceUri
        {
            get
            {
                return m_serviceUri;
            }

            set
            {
                if (!Object.ReferenceEquals(m_serviceUri, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serviceUri = value;
            }
        }

        /// <summary>
        /// A description for the ServiceCertificate Property.
        /// </summary>
        public PropertyState<byte[]> ServiceCertificate
        {
            get
            {
                return m_serviceCertificate;
            }

            set
            {
                if (!Object.ReferenceEquals(m_serviceCertificate, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serviceCertificate = value;
            }
        }

        /// <summary>
        /// A description for the UserTokenPolicies Property.
        /// </summary>
        public PropertyState<UserTokenPolicy[]> UserTokenPolicies
        {
            get
            {
                return m_userTokenPolicies;
            }

            set
            {
                if (!Object.ReferenceEquals(m_userTokenPolicies, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_userTokenPolicies = value;
            }
        }

        /// <summary>
        /// A description for the GetServiceDescriptionMethodType Method.
        /// </summary>
        public GetServiceDescriptionMethodState GetServiceDescription
        {
            get
            {
                return m_getServiceDescriptionMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_getServiceDescriptionMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getServiceDescriptionMethod = value;
            }
        }

        /// <summary>
        /// A description for the RequestAccessTokenMethodType Method.
        /// </summary>
        public RequestAccessTokenMethodState RequestAccessToken
        {
            get
            {
                return m_requestAccessTokenMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_requestAccessTokenMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_requestAccessTokenMethod = value;
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
            if (m_serviceUri != null)
            {
                children.Add(m_serviceUri);
            }

            if (m_serviceCertificate != null)
            {
                children.Add(m_serviceCertificate);
            }

            if (m_userTokenPolicies != null)
            {
                children.Add(m_userTokenPolicies);
            }

            if (m_getServiceDescriptionMethod != null)
            {
                children.Add(m_getServiceDescriptionMethod);
            }

            if (m_requestAccessTokenMethod != null)
            {
                children.Add(m_requestAccessTokenMethod);
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
                case Opc.Ua.Gds.BrowseNames.ServiceUri:
                {
                    if (createOrReplace)
                    {
                        if (ServiceUri == null)
                        {
                            if (replacement == null)
                            {
                                ServiceUri = new PropertyState<string>(this);
                            }
                            else
                            {
                                ServiceUri = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = ServiceUri;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.ServiceCertificate:
                {
                    if (createOrReplace)
                    {
                        if (ServiceCertificate == null)
                        {
                            if (replacement == null)
                            {
                                ServiceCertificate = new PropertyState<byte[]>(this);
                            }
                            else
                            {
                                ServiceCertificate = (PropertyState<byte[]>)replacement;
                            }
                        }
                    }

                    instance = ServiceCertificate;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.UserTokenPolicies:
                {
                    if (createOrReplace)
                    {
                        if (UserTokenPolicies == null)
                        {
                            if (replacement == null)
                            {
                                UserTokenPolicies = new PropertyState<UserTokenPolicy[]>(this);
                            }
                            else
                            {
                                UserTokenPolicies = (PropertyState<UserTokenPolicy[]>)replacement;
                            }
                        }
                    }

                    instance = UserTokenPolicies;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.GetServiceDescription:
                {
                    if (createOrReplace)
                    {
                        if (GetServiceDescription == null)
                        {
                            if (replacement == null)
                            {
                                GetServiceDescription = new GetServiceDescriptionMethodState(this);
                            }
                            else
                            {
                                GetServiceDescription = (GetServiceDescriptionMethodState)replacement;
                            }
                        }
                    }

                    instance = GetServiceDescription;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.RequestAccessToken:
                {
                    if (createOrReplace)
                    {
                        if (RequestAccessToken == null)
                        {
                            if (replacement == null)
                            {
                                RequestAccessToken = new RequestAccessTokenMethodState(this);
                            }
                            else
                            {
                                RequestAccessToken = (RequestAccessTokenMethodState)replacement;
                            }
                        }
                    }

                    instance = RequestAccessToken;
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

        #region Private Fields
        private PropertyState<string> m_serviceUri;
        private PropertyState<byte[]> m_serviceCertificate;
        private PropertyState<UserTokenPolicy[]> m_userTokenPolicies;
        private GetServiceDescriptionMethodState m_getServiceDescriptionMethod;
        private RequestAccessTokenMethodState m_requestAccessTokenMethod;
        #endregion
    }
    #endif
    #endregion

    #region GetServiceDescriptionMethodState Class
    #if (!OPCUA_EXCLUDE_GetServiceDescriptionMethodState)
    /// <summary>
    /// Stores an instance of the GetServiceDescriptionMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GetServiceDescriptionMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GetServiceDescriptionMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new GetServiceDescriptionMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHwAAAEdl" +
           "dFNlcnZpY2VEZXNjcmlwdGlvbk1ldGhvZFR5cGUBAe4DAC8BAe4D7gMAAAEB/////wEAAAAVYKkKAgAA" +
           "AAAADwAAAE91dHB1dEFyZ3VtZW50cwEB7wMALgBE7wMAAJYDAAAAAQAqAQEZAAAACgAAAFNlcnZpY2VV" +
           "cmkADP////8AAAAAAAEAKgEBIQAAABIAAABTZXJ2aWNlQ2VydGlmaWNhdGUAD/////8AAAAAAAEAKgEB" +
           "IgAAABEAAABVc2VyVG9rZW5Qb2xpY2llcwEAMAEBAAAAAAAAAAABACgBAQAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public GetServiceDescriptionMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            string serviceUri = (string)outputArguments[0];
            byte[] serviceCertificate = (byte[])outputArguments[1];
            UserTokenPolicy[] userTokenPolicies = (UserTokenPolicy[])outputArguments[2];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    ref serviceUri,
                    ref serviceCertificate,
                    ref userTokenPolicies);
            }

            outputArguments[0] = serviceUri;
            outputArguments[1] = serviceCertificate;
            outputArguments[2] = userTokenPolicies;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult GetServiceDescriptionMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        ref string serviceUri,
        ref byte[] serviceCertificate,
        ref UserTokenPolicy[] userTokenPolicies);
    #endif
    #endregion

    #region RequestAccessTokenMethodState Class
    #if (!OPCUA_EXCLUDE_RequestAccessTokenMethodState)
    /// <summary>
    /// Stores an instance of the RequestAccessTokenMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class RequestAccessTokenMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public RequestAccessTokenMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new RequestAccessTokenMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYYIKBAAAAAEAHAAAAFJl" +
           "cXVlc3RBY2Nlc3NUb2tlbk1ldGhvZFR5cGUBAeMDAC8BAeMD4wMAAAEB/////wIAAAAVYKkKAgAAAAAA" +
           "DgAAAElucHV0QXJndW1lbnRzAQHkAwAuAETkAwAAlgIAAAABACoBAR4AAAANAAAASWRlbnRpdHlUb2tl" +
           "bgEAPAH/////AAAAAAABACoBARkAAAAKAAAAUmVzb3VyY2VJZAAM/////wAAAAAAAQAoAQEAAAABAf//" +
           "//8AAAAAFWCpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAeUDAC4AROUDAACWAQAAAAEAKgEBGgAA" +
           "AAsAAABBY2Nlc3NUb2tlbgAM/////wAAAAAAAQAoAQEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public RequestAccessTokenMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="inputArguments">The input arguments which have been already validated.</param>
        /// <param name="outputArguments">The output arguments which have initialized with thier default values.</param>
        /// <returns></returns>
        protected override ServiceResult Call(
            ISystemContext context,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(context, objectId, inputArguments, outputArguments);
            }

            ServiceResult result = null;

            UserIdentityToken identityToken = (UserIdentityToken)ExtensionObject.ToEncodeable((ExtensionObject)inputArguments[0]);
            string resourceId = (string)inputArguments[1];

            string accessToken = (string)outputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    context,
                    this,
                    objectId,
                    identityToken,
                    resourceId,
                    ref accessToken);
            }

            outputArguments[0] = accessToken;

            return result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <summary>
    /// Used to receive notifications when the method is called.
    /// </summary>
    /// <exclude />
    public delegate ServiceResult RequestAccessTokenMethodStateMethodCallHandler(
        ISystemContext context,
        MethodState method,
        NodeId objectId,
        UserIdentityToken identityToken,
        string resourceId,
        ref string accessToken);
    #endif
    #endregion

    #region AccessTokenAuditIssuedAuditEventState Class
    #if (!OPCUA_EXCLUDE_AccessTokenAuditIssuedAuditEventState)
    /// <summary>
    /// Stores an instance of the AccessTokenAuditIssuedAuditEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class AccessTokenAuditIssuedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public AccessTokenAuditIssuedAuditEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.AccessTokenAuditIssuedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACAAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvR0RTL/////8EYIAAAQAAAAEALAAAAEFj" +
           "Y2Vzc1Rva2VuQXVkaXRJc3N1ZWRBdWRpdEV2ZW50VHlwZUluc3RhbmNlAQHPAwEBzwP/////DwAAADVg" +
           "iQoCAAAAAAAHAAAARXZlbnRJZAEB0AMDAAAAACsAAABBIGdsb2JhbGx5IHVuaXF1ZSBpZGVudGlmaWVy" +
           "IGZvciB0aGUgZXZlbnQuAC4ARNADAAAAD/////8BAf////8AAAAANWCJCgIAAAAAAAkAAABFdmVudFR5" +
           "cGUBAdEDAwAAAAAiAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0eXBlLgAuAETRAwAAABH/" +
           "////AQH/////AAAAADVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEB0gMDAAAAABgAAABUaGUgc291cmNl" +
           "IG9mIHRoZSBldmVudC4ALgBE0gMAAAAR/////wEB/////wAAAAA1YIkKAgAAAAAACgAAAFNvdXJjZU5h" +
           "bWUBAdMDAwAAAAApAAAAQSBkZXNjcmlwdGlvbiBvZiB0aGUgc291cmNlIG9mIHRoZSBldmVudC4ALgBE" +
           "0wMAAAAM/////wEB/////wAAAAA1YIkKAgAAAAAABAAAAFRpbWUBAdQDAwAAAAAYAAAAV2hlbiB0aGUg" +
           "ZXZlbnQgb2NjdXJyZWQuAC4ARNQDAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAACwAAAFJlY2Vp" +
           "dmVUaW1lAQHVAwMAAAAAPgAAAFdoZW4gdGhlIHNlcnZlciByZWNlaXZlZCB0aGUgZXZlbnQgZnJvbSB0" +
           "aGUgdW5kZXJseWluZyBzeXN0ZW0uAC4ARNUDAAABACYB/////wEB/////wAAAAA1YIkKAgAAAAAABwAA" +
           "AE1lc3NhZ2UBAdcDAwAAAAAlAAAAQSBsb2NhbGl6ZWQgZGVzY3JpcHRpb24gb2YgdGhlIGV2ZW50LgAu" +
           "AETXAwAAABX/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAdgDAwAAAAAhAAAASW5k" +
           "aWNhdGVzIGhvdyB1cmdlbnQgYW4gZXZlbnQgaXMuAC4ARNgDAAAABf////8BAf////8AAAAANWCJCgIA" +
           "AAAAAA8AAABBY3Rpb25UaW1lU3RhbXABAdkDAwAAAAAuAAAAV2hlbiB0aGUgYWN0aW9uIHRyaWdnZXJp" +
           "bmcgdGhlIGV2ZW50IG9jY3VycmVkLgAuAETZAwAAAQAmAf////8BAf////8AAAAANWCJCgIAAAAAAAYA" +
           "AABTdGF0dXMBAdoDAwAAAABhAAAASWYgVFJVRSB0aGUgYWN0aW9uIHdhcyBwZXJmb3JtZWQuIElmIEZB" +
           "TFNFIHRoZSBhY3Rpb24gZmFpbGVkIGFuZCB0aGUgc2VydmVyIHN0YXRlIGRpZCBub3QgY2hhbmdlLgAu" +
           "AETaAwAAAAH/////AQH/////AAAAADVgiQoCAAAAAAAIAAAAU2VydmVySWQBAdsDAwAAAAA6AAAAVGhl" +
           "IHVuaXF1ZSBpZGVudGlmaWVyIGZvciB0aGUgc2VydmVyIGdlbmVyYXRpbmcgdGhlIGV2ZW50LgAuAETb" +
           "AwAAAAz/////AQH/////AAAAADVgiQoCAAAAAAASAAAAQ2xpZW50QXVkaXRFbnRyeUlkAQHcAwMAAAAA" +
           "QwAAAFRoZSBsb2cgZW50cnkgaWQgcHJvdmlkZWQgaW4gdGhlIHJlcXVlc3QgdGhhdCBpbml0aWF0ZWQg" +
           "dGhlIGFjdGlvbi4ALgBE3AMAAAAM/////wEB/////wAAAAA1YIkKAgAAAAAADAAAAENsaWVudFVzZXJJ" +
           "ZAEB3QMDAAAAAEgAAABUaGUgdXNlciBpZGVudGl0eSBhc3NvY2lhdGVkIHdpdGggdGhlIHNlc3Npb24g" +
           "dGhhdCBpbml0aWF0ZWQgdGhlIGFjdGlvbi4ALgBE3QMAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "CAAAAE1ldGhvZElkAQHeAwAuAETeAwAAABH/////AQH/////AAAAABVgiQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBAd8DAC4ARN8DAAAAGAEAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }
    #endif
    #endregion
}
