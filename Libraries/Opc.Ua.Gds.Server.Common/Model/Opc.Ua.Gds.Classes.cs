/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Threading;
using Opc.Ua;


#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CA1515 // Consider making public types internal
#pragma warning disable CA1707 // Identifiers should not contain underscores
#pragma warning disable CA1028 // Enum Storage should be Int32

namespace Opc.Ua.Gds
{
    #region FindApplicationsMethodState Class
    #if (!OPCUA_EXCLUDE_FindApplicationsMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class FindApplicationsMethodState : MethodState
    {
        #region Constructors
        public FindApplicationsMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new FindApplicationsMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<FindApplicationsMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=2</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FindApplicationsMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=2</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>2</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=3</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>3</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=4</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>4</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Applications</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FindApplicationsMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public FindApplicationsMethodStateMethodCallHandler OnCall;

        public FindApplicationsMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            string applicationUri = (string)_inputArguments[0];

            ApplicationRecordDataType[] applications = (ApplicationRecordDataType[])_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationUri,
                    ref applications);
            }

            _outputArguments[0] = applications;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            FindApplicationsMethodStateResult _result = null;

            string applicationUri = (string)_inputArguments[0];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationUri,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.Applications;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult FindApplicationsMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string applicationUri,
        ref ApplicationRecordDataType[] applications);

    /// <exclude />
    public partial class FindApplicationsMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public ApplicationRecordDataType[] Applications { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<FindApplicationsMethodStateResult> FindApplicationsMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string applicationUri,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region RegisterApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_RegisterApplicationMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class RegisterApplicationMethodState : MethodState
    {
        #region Constructors
        public RegisterApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new RegisterApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<RegisterApplicationMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=5</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RegisterApplicationMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=5</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>5</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=6</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>6</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=7</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>7</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RegisterApplicationMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public RegisterApplicationMethodStateMethodCallHandler OnCall;

        public RegisterApplicationMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            ApplicationRecordDataType application = (ApplicationRecordDataType)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[0]);

            NodeId applicationId = (NodeId)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    application,
                    ref applicationId);
            }

            _outputArguments[0] = applicationId;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            RegisterApplicationMethodStateResult _result = null;

            ApplicationRecordDataType application = (ApplicationRecordDataType)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[0]);

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    application,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.ApplicationId;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult RegisterApplicationMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        ApplicationRecordDataType application,
        ref NodeId applicationId);

    /// <exclude />
    public partial class RegisterApplicationMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public NodeId ApplicationId { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<RegisterApplicationMethodStateResult> RegisterApplicationMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        ApplicationRecordDataType application,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region UpdateApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_UpdateApplicationMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class UpdateApplicationMethodState : MethodState
    {
        #region Constructors
        public UpdateApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new UpdateApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<UpdateApplicationMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=186</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UpdateApplicationMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=186</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>186</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=187</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>187</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</UpdateApplicationMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public UpdateApplicationMethodStateMethodCallHandler OnCall;

        public UpdateApplicationMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            ApplicationRecordDataType application = (ApplicationRecordDataType)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[0]);

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    application);
            }

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            UpdateApplicationMethodStateResult _result = null;

            ApplicationRecordDataType application = (ApplicationRecordDataType)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[0]);

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    application,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult UpdateApplicationMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        ApplicationRecordDataType application);

    /// <exclude />
    public partial class UpdateApplicationMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<UpdateApplicationMethodStateResult> UpdateApplicationMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        ApplicationRecordDataType application,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region UnregisterApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_UnregisterApplicationMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class UnregisterApplicationMethodState : MethodState
    {
        #region Constructors
        public UnregisterApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new UnregisterApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<UnregisterApplicationMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=8</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UnregisterApplicationMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=8</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>8</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=9</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>9</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</UnregisterApplicationMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public UnregisterApplicationMethodStateMethodCallHandler OnCall;

        public UnregisterApplicationMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId);
            }

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            UnregisterApplicationMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult UnregisterApplicationMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId);

    /// <exclude />
    public partial class UnregisterApplicationMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<UnregisterApplicationMethodStateResult> UnregisterApplicationMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region GetApplicationMethodState Class
    #if (!OPCUA_EXCLUDE_GetApplicationMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class GetApplicationMethodState : MethodState
    {
        #region Constructors
        public GetApplicationMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new GetApplicationMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<GetApplicationMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=207</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetApplicationMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=207</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>207</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=208</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>208</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=209</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>209</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetApplicationMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public GetApplicationMethodStateMethodCallHandler OnCall;

        public GetApplicationMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];

            ApplicationRecordDataType application = (ApplicationRecordDataType)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    ref application);
            }

            _outputArguments[0] = application;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            GetApplicationMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.Application;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult GetApplicationMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        ref ApplicationRecordDataType application);

    /// <exclude />
    public partial class GetApplicationMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public ApplicationRecordDataType Application { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<GetApplicationMethodStateResult> GetApplicationMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region QueryApplicationsMethodState Class
    #if (!OPCUA_EXCLUDE_QueryApplicationsMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class QueryApplicationsMethodState : MethodState
    {
        #region Constructors
        public QueryApplicationsMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new QueryApplicationsMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<QueryApplicationsMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=865</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>QueryApplicationsMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=865</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>865</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=866</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>866</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>StartingRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>MaxRecordsToReturn</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationType</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ProductUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Capabilities</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>7</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=867</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>867</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>LastCounterResetTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NextRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Applications</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=308</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</QueryApplicationsMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public QueryApplicationsMethodStateMethodCallHandler OnCall;

        public QueryApplicationsMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            uint startingRecordId = (uint)_inputArguments[0];
            uint maxRecordsToReturn = (uint)_inputArguments[1];
            string applicationName = (string)_inputArguments[2];
            string applicationUri = (string)_inputArguments[3];
            uint applicationType = (uint)_inputArguments[4];
            string productUri = (string)_inputArguments[5];
            string[] capabilities = (string[])_inputArguments[6];

            DateTime lastCounterResetTime = (DateTime)_outputArguments[0];
            uint nextRecordId = (uint)_outputArguments[1];
            Opc.Ua.ApplicationDescription[] applications = (Opc.Ua.ApplicationDescription[])_outputArguments[2];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
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

            _outputArguments[0] = lastCounterResetTime;
            _outputArguments[1] = nextRecordId;
            _outputArguments[2] = applications;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            QueryApplicationsMethodStateResult _result = null;

            uint startingRecordId = (uint)_inputArguments[0];
            uint maxRecordsToReturn = (uint)_inputArguments[1];
            string applicationName = (string)_inputArguments[2];
            string applicationUri = (string)_inputArguments[3];
            uint applicationType = (uint)_inputArguments[4];
            string productUri = (string)_inputArguments[5];
            string[] capabilities = (string[])_inputArguments[6];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    startingRecordId,
                    maxRecordsToReturn,
                    applicationName,
                    applicationUri,
                    applicationType,
                    productUri,
                    capabilities,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.LastCounterResetTime;
            _outputArguments[1] = _result.NextRecordId;
            _outputArguments[2] = _result.Applications;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult QueryApplicationsMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint startingRecordId,
        uint maxRecordsToReturn,
        string applicationName,
        string applicationUri,
        uint applicationType,
        string productUri,
        string[] capabilities,
        ref DateTime lastCounterResetTime,
        ref uint nextRecordId,
        ref Opc.Ua.ApplicationDescription[] applications);

    /// <exclude />
    public partial class QueryApplicationsMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public DateTime LastCounterResetTime { get; set; }
        public uint NextRecordId { get; set; }
        public Opc.Ua.ApplicationDescription[] Applications { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<QueryApplicationsMethodStateResult> QueryApplicationsMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint startingRecordId,
        uint maxRecordsToReturn,
        string applicationName,
        string applicationUri,
        uint applicationType,
        string productUri,
        string[] capabilities,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region QueryServersMethodState Class
    #if (!OPCUA_EXCLUDE_QueryServersMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class QueryServersMethodState : MethodState
    {
        #region Constructors
        public QueryServersMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new QueryServersMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<QueryServersMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=10</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>QueryServersMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=10</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>10</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=11</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>11</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>StartingRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>MaxRecordsToReturn</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ProductUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServerCapabilities</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>6</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=12</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>12</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>LastCounterResetTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Servers</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12189</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</QueryServersMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public QueryServersMethodStateMethodCallHandler OnCall;

        public QueryServersMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            uint startingRecordId = (uint)_inputArguments[0];
            uint maxRecordsToReturn = (uint)_inputArguments[1];
            string applicationName = (string)_inputArguments[2];
            string applicationUri = (string)_inputArguments[3];
            string productUri = (string)_inputArguments[4];
            string[] serverCapabilities = (string[])_inputArguments[5];

            DateTime lastCounterResetTime = (DateTime)_outputArguments[0];
            Opc.Ua.ServerOnNetwork[] servers = (Opc.Ua.ServerOnNetwork[])_outputArguments[1];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    startingRecordId,
                    maxRecordsToReturn,
                    applicationName,
                    applicationUri,
                    productUri,
                    serverCapabilities,
                    ref lastCounterResetTime,
                    ref servers);
            }

            _outputArguments[0] = lastCounterResetTime;
            _outputArguments[1] = servers;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            QueryServersMethodStateResult _result = null;

            uint startingRecordId = (uint)_inputArguments[0];
            uint maxRecordsToReturn = (uint)_inputArguments[1];
            string applicationName = (string)_inputArguments[2];
            string applicationUri = (string)_inputArguments[3];
            string productUri = (string)_inputArguments[4];
            string[] serverCapabilities = (string[])_inputArguments[5];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    startingRecordId,
                    maxRecordsToReturn,
                    applicationName,
                    applicationUri,
                    productUri,
                    serverCapabilities,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.LastCounterResetTime;
            _outputArguments[1] = _result.Servers;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult QueryServersMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint startingRecordId,
        uint maxRecordsToReturn,
        string applicationName,
        string applicationUri,
        string productUri,
        string[] serverCapabilities,
        ref DateTime lastCounterResetTime,
        ref Opc.Ua.ServerOnNetwork[] servers);

    /// <exclude />
    public partial class QueryServersMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public DateTime LastCounterResetTime { get; set; }
        public Opc.Ua.ServerOnNetwork[] Servers { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<QueryServersMethodStateResult> QueryServersMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint startingRecordId,
        uint maxRecordsToReturn,
        string applicationName,
        string applicationUri,
        string productUri,
        string[] serverCapabilities,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region DirectoryState Class
    #if (!OPCUA_EXCLUDE_DirectoryState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class DirectoryState : FolderState
    {
        #region Constructors
        public DirectoryState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.DirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<DirectoryTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=13</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>DirectoryTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=13</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>13</uax:NumericId>" +
           "<Applications>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=14</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>Applications</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=61</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>14</uax:NumericId>" +
           "</Applications>" +
           "<FindApplications>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=15</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FindApplications</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=15</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>15</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=16</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>16</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=17</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>17</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Applications</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FindApplications>" +
           "<RegisterApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=18</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RegisterApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=18</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>18</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<uax:References>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=41</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>ns=1;i=26</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "</uax:References>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=19</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>19</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=20</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>20</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RegisterApplication>" +
           "<UpdateApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=188</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UpdateApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=188</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>188</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=189</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>189</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</UpdateApplication>" +
           "<UnregisterApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=21</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UnregisterApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=21</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>21</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=22</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>22</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</UnregisterApplication>" +
           "<GetApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=210</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=210</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>210</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=211</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>211</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=212</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>212</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetApplication>" +
           "<QueryApplications>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=868</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>QueryApplications</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=868</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>868</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=869</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>869</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>StartingRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>MaxRecordsToReturn</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationType</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ProductUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Capabilities</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>7</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=870</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>870</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>LastCounterResetTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NextRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Applications</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=308</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</QueryApplications>" +
           "<QueryServers>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=23</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>QueryServers</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=23</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>23</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=24</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>24</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>StartingRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>MaxRecordsToReturn</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ProductUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServerCapabilities</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>6</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=25</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>25</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>LastCounterResetTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Servers</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12189</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</QueryServers>" +
           "</DirectoryTypeInstance>";
        #endregion
        #endif
        #endregion

        #region Public Properties
        public FolderState Applications
        {
            get => m_applications;

            set
            {
                if (!Object.ReferenceEquals(m_applications, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_applications = value;
            }
        }

        public FindApplicationsMethodState FindApplications
        {
            get => m_findApplicationsMethod;

            set
            {
                if (!Object.ReferenceEquals(m_findApplicationsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_findApplicationsMethod = value;
            }
        }

        public RegisterApplicationMethodState RegisterApplication
        {
            get => m_registerApplicationMethod;

            set
            {
                if (!Object.ReferenceEquals(m_registerApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_registerApplicationMethod = value;
            }
        }

        public UpdateApplicationMethodState UpdateApplication
        {
            get => m_updateApplicationMethod;

            set
            {
                if (!Object.ReferenceEquals(m_updateApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_updateApplicationMethod = value;
            }
        }

        public UnregisterApplicationMethodState UnregisterApplication
        {
            get => m_unregisterApplicationMethod;

            set
            {
                if (!Object.ReferenceEquals(m_unregisterApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_unregisterApplicationMethod = value;
            }
        }

        public GetApplicationMethodState GetApplication
        {
            get => m_getApplicationMethod;

            set
            {
                if (!Object.ReferenceEquals(m_getApplicationMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getApplicationMethod = value;
            }
        }

        public QueryApplicationsMethodState QueryApplications
        {
            get => m_queryApplicationsMethod;

            set
            {
                if (!Object.ReferenceEquals(m_queryApplicationsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_queryApplicationsMethod = value;
            }
        }

        public QueryServersMethodState QueryServers
        {
            get => m_queryServersMethod;

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
            
        protected override void RemoveExplicitlyDefinedChild(BaseInstanceState child)
        {
            if (Object.ReferenceEquals(m_applications, child))
            {
                m_applications = null;
                return;
            }

            if (Object.ReferenceEquals(m_findApplicationsMethod, child))
            {
                m_findApplicationsMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_registerApplicationMethod, child))
            {
                m_registerApplicationMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_updateApplicationMethod, child))
            {
                m_updateApplicationMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_unregisterApplicationMethod, child))
            {
                m_unregisterApplicationMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_getApplicationMethod, child))
            {
                m_getApplicationMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_queryApplicationsMethod, child))
            {
                m_queryApplicationsMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_queryServersMethod, child))
            {
                m_queryServersMethod = null;
                return;
            }

            base.RemoveExplicitlyDefinedChild(child);
        }

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
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class ApplicationRegistrationChangedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        public ApplicationRegistrationChangedAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.ApplicationRegistrationChangedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<ApplicationRegistrationChangedAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=26</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ApplicationRegistrationChangedAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=26</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>26</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000001</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000001</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000002</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000002</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000003</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000003</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000004</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000004</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000005</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000005</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000006</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000006</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000008</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000008</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000009</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000009</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000014</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000014</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000015</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000015</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000016</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000016</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000017</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000017</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000018</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000018</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000020</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000020</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000022</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000022</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</ApplicationRegistrationChangedAuditEventTypeInstance>";
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
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class StartSigningRequestMethodState : MethodState
    {
        #region Constructors
        public StartSigningRequestMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new StartSigningRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<StartSigningRequestMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=51</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartSigningRequestMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=51</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>51</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=52</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>52</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateRequest</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=53</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>53</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartSigningRequestMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public StartSigningRequestMethodStateMethodCallHandler OnCall;

        public StartSigningRequestMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];
            NodeId certificateTypeId = (NodeId)_inputArguments[2];
            byte[] certificateRequest = (byte[])_inputArguments[3];

            NodeId requestId = (NodeId)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    certificateRequest,
                    ref requestId);
            }

            _outputArguments[0] = requestId;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            StartSigningRequestMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];
            NodeId certificateTypeId = (NodeId)_inputArguments[2];
            byte[] certificateRequest = (byte[])_inputArguments[3];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    certificateRequest,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.RequestId;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult StartSigningRequestMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        byte[] certificateRequest,
        ref NodeId requestId);

    /// <exclude />
    public partial class StartSigningRequestMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public NodeId RequestId { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<StartSigningRequestMethodStateResult> StartSigningRequestMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        byte[] certificateRequest,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region StartNewKeyPairRequestMethodState Class
    #if (!OPCUA_EXCLUDE_StartNewKeyPairRequestMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class StartNewKeyPairRequestMethodState : MethodState
    {
        #region Constructors
        public StartNewKeyPairRequestMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new StartNewKeyPairRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<StartNewKeyPairRequestMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=48</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartNewKeyPairRequestMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=48</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>48</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=49</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>49</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SubjectName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>DomainNames</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PrivateKeyFormat</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PrivateKeyPassword</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>7</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=50</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>50</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartNewKeyPairRequestMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public StartNewKeyPairRequestMethodStateMethodCallHandler OnCall;

        public StartNewKeyPairRequestMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];
            NodeId certificateTypeId = (NodeId)_inputArguments[2];
            string subjectName = (string)_inputArguments[3];
            string[] domainNames = (string[])_inputArguments[4];
            string privateKeyFormat = (string)_inputArguments[5];
            string privateKeyPassword = (string)_inputArguments[6];

            NodeId requestId = (NodeId)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    privateKeyPassword,
                    ref requestId);
            }

            _outputArguments[0] = requestId;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            StartNewKeyPairRequestMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];
            NodeId certificateTypeId = (NodeId)_inputArguments[2];
            string subjectName = (string)_inputArguments[3];
            string[] domainNames = (string[])_inputArguments[4];
            string privateKeyFormat = (string)_inputArguments[5];
            string privateKeyPassword = (string)_inputArguments[6];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    privateKeyPassword,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.RequestId;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult StartNewKeyPairRequestMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        string subjectName,
        string[] domainNames,
        string privateKeyFormat,
        string privateKeyPassword,
        ref NodeId requestId);

    /// <exclude />
    public partial class StartNewKeyPairRequestMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public NodeId RequestId { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<StartNewKeyPairRequestMethodStateResult> StartNewKeyPairRequestMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        string subjectName,
        string[] domainNames,
        string privateKeyFormat,
        string privateKeyPassword,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region FinishRequestMethodState Class
    #if (!OPCUA_EXCLUDE_FinishRequestMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class FinishRequestMethodState : MethodState
    {
        #region Constructors
        public FinishRequestMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new FinishRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<FinishRequestMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=57</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FinishRequestMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=57</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>57</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=58</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>58</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=59</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>59</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PrivateKey</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>IssuerCertificates</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FinishRequestMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public FinishRequestMethodStateMethodCallHandler OnCall;

        public FinishRequestMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId requestId = (NodeId)_inputArguments[1];

            byte[] certificate = (byte[])_outputArguments[0];
            byte[] privateKey = (byte[])_outputArguments[1];
            byte[][] issuerCertificates = (byte[][])_outputArguments[2];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    requestId,
                    ref certificate,
                    ref privateKey,
                    ref issuerCertificates);
            }

            _outputArguments[0] = certificate;
            _outputArguments[1] = privateKey;
            _outputArguments[2] = issuerCertificates;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            FinishRequestMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId requestId = (NodeId)_inputArguments[1];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    requestId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.Certificate;
            _outputArguments[1] = _result.PrivateKey;
            _outputArguments[2] = _result.IssuerCertificates;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult FinishRequestMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId requestId,
        ref byte[] certificate,
        ref byte[] privateKey,
        ref byte[][] issuerCertificates);

    /// <exclude />
    public partial class FinishRequestMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public byte[] Certificate { get; set; }
        public byte[] PrivateKey { get; set; }
        public byte[][] IssuerCertificates { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<FinishRequestMethodStateResult> FinishRequestMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId requestId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region GetCertificateGroupsMethodState Class
    #if (!OPCUA_EXCLUDE_GetCertificateGroupsMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class GetCertificateGroupsMethodState : MethodState
    {
        #region Constructors
        public GetCertificateGroupsMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new GetCertificateGroupsMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<GetCertificateGroupsMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=230</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetCertificateGroupsMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=230</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>230</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=231</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>231</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=232</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>232</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupIds</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetCertificateGroupsMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public GetCertificateGroupsMethodStateMethodCallHandler OnCall;

        public GetCertificateGroupsMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];

            NodeId[] certificateGroupIds = (NodeId[])_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    ref certificateGroupIds);
            }

            _outputArguments[0] = certificateGroupIds;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            GetCertificateGroupsMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.CertificateGroupIds;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult GetCertificateGroupsMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        ref NodeId[] certificateGroupIds);

    /// <exclude />
    public partial class GetCertificateGroupsMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public NodeId[] CertificateGroupIds { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<GetCertificateGroupsMethodStateResult> GetCertificateGroupsMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region GetTrustListMethodState Class
    #if (!OPCUA_EXCLUDE_GetTrustListMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class GetTrustListMethodState : MethodState
    {
        #region Constructors
        public GetTrustListMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new GetTrustListMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<GetTrustListMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=190</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetTrustListMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=190</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>190</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=191</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>191</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=192</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>192</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>TrustListId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetTrustListMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public GetTrustListMethodStateMethodCallHandler OnCall;

        public GetTrustListMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];

            NodeId trustListId = (NodeId)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    ref trustListId);
            }

            _outputArguments[0] = trustListId;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            GetTrustListMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.TrustListId;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult GetTrustListMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        ref NodeId trustListId);

    /// <exclude />
    public partial class GetTrustListMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public NodeId TrustListId { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<GetTrustListMethodStateResult> GetTrustListMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region RevokeCertificateMethodState Class
    #if (!OPCUA_EXCLUDE_RevokeCertificateMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class RevokeCertificateMethodState : MethodState
    {
        #region Constructors
        public RevokeCertificateMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new RevokeCertificateMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<RevokeCertificateMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=15001</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RevokeCertificateMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=15001</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>15001</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=15002</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>15002</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</RevokeCertificateMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public RevokeCertificateMethodStateMethodCallHandler OnCall;

        public RevokeCertificateMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            byte[] certificate = (byte[])_inputArguments[1];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificate);
            }

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            RevokeCertificateMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            byte[] certificate = (byte[])_inputArguments[1];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificate,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult RevokeCertificateMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        byte[] certificate);

    /// <exclude />
    public partial class RevokeCertificateMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<RevokeCertificateMethodStateResult> RevokeCertificateMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        byte[] certificate,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region GetCertificateStatusMethodState Class
    #if (!OPCUA_EXCLUDE_GetCertificateStatusMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class GetCertificateStatusMethodState : MethodState
    {
        #region Constructors
        public GetCertificateStatusMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new GetCertificateStatusMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<GetCertificateStatusMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=219</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetCertificateStatusMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=219</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>219</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=220</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>220</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=221</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>221</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UpdateRequired</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetCertificateStatusMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public GetCertificateStatusMethodStateMethodCallHandler OnCall;

        public GetCertificateStatusMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];
            NodeId certificateTypeId = (NodeId)_inputArguments[2];

            bool updateRequired = (bool)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    ref updateRequired);
            }

            _outputArguments[0] = updateRequired;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            GetCertificateStatusMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];
            NodeId certificateTypeId = (NodeId)_inputArguments[2];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.UpdateRequired;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult GetCertificateStatusMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        ref bool updateRequired);

    /// <exclude />
    public partial class GetCertificateStatusMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public bool UpdateRequired { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<GetCertificateStatusMethodStateResult> GetCertificateStatusMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        NodeId certificateTypeId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region GetCertificatesMethodState Class
    #if (!OPCUA_EXCLUDE_GetCertificatesMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class GetCertificatesMethodState : MethodState
    {
        #region Constructors
        public GetCertificatesMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new GetCertificatesMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<GetCertificatesMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=43</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetCertificatesMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=43</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>43</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=44</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>44</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=45</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>45</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeIds</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificates</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetCertificatesMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public GetCertificatesMethodStateMethodCallHandler OnCall;

        public GetCertificatesMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];

            NodeId[] certificateTypeIds = (NodeId[])_outputArguments[0];
            byte[][] certificates = (byte[][])_outputArguments[1];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    ref certificateTypeIds,
                    ref certificates);
            }

            _outputArguments[0] = certificateTypeIds;
            _outputArguments[1] = certificates;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            GetCertificatesMethodStateResult _result = null;

            NodeId applicationId = (NodeId)_inputArguments[0];
            NodeId certificateGroupId = (NodeId)_inputArguments[1];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationId,
                    certificateGroupId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.CertificateTypeIds;
            _outputArguments[1] = _result.Certificates;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult GetCertificatesMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        ref NodeId[] certificateTypeIds,
        ref byte[][] certificates);

    /// <exclude />
    public partial class GetCertificatesMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public NodeId[] CertificateTypeIds { get; set; }
        public byte[][] Certificates { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<GetCertificatesMethodStateResult> GetCertificatesMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId applicationId,
        NodeId certificateGroupId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region CheckRevocationStatusMethodState Class
    #if (!OPCUA_EXCLUDE_CheckRevocationStatusMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class CheckRevocationStatusMethodState : MethodState
    {
        #region Constructors
        public CheckRevocationStatusMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new CheckRevocationStatusMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<CheckRevocationStatusMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=46</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CheckRevocationStatusMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=46</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>46</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=47</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>47</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=54</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>54</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateStatus</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=19</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ValidityTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</CheckRevocationStatusMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public CheckRevocationStatusMethodStateMethodCallHandler OnCall;

        public CheckRevocationStatusMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            byte[] certificate = (byte[])_inputArguments[0];

            StatusCode certificateStatus = (StatusCode)_outputArguments[0];
            DateTime validityTime = (DateTime)_outputArguments[1];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    certificate,
                    ref certificateStatus,
                    ref validityTime);
            }

            _outputArguments[0] = certificateStatus;
            _outputArguments[1] = validityTime;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            CheckRevocationStatusMethodStateResult _result = null;

            byte[] certificate = (byte[])_inputArguments[0];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    certificate,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.CertificateStatus;
            _outputArguments[1] = _result.ValidityTime;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult CheckRevocationStatusMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        byte[] certificate,
        ref StatusCode certificateStatus,
        ref DateTime validityTime);

    /// <exclude />
    public partial class CheckRevocationStatusMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public StatusCode CertificateStatus { get; set; }
        public DateTime ValidityTime { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<CheckRevocationStatusMethodStateResult> CheckRevocationStatusMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        byte[] certificate,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region CertificateDirectoryState Class
    #if (!OPCUA_EXCLUDE_CertificateDirectoryState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class CertificateDirectoryState : DirectoryState
    {
        #region Constructors
        public CertificateDirectoryState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.CertificateDirectoryType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);

            if (RevokeCertificate != null)
            {
                RevokeCertificate.Initialize(context, RevokeCertificate_InitializationString);
            }

            if (GetCertificates != null)
            {
                GetCertificates.Initialize(context, GetCertificates_InitializationString);
            }

            if (CheckRevocationStatus != null)
            {
                CheckRevocationStatus.Initialize(context, CheckRevocationStatus_InitializationString);
            }
        }

        #region Initialization String
        private const string RevokeCertificate_InitializationString =
           "<RevokeCertificate xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=15003</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RevokeCertificate</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=15003</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>15003</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=15004</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>15004</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</RevokeCertificate>";

        private const string GetCertificates_InitializationString =
           "<GetCertificates xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=89</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetCertificates</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=89</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>89</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=90</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>90</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=108</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>108</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeIds</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificates</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetCertificates>";

        private const string CheckRevocationStatus_InitializationString =
           "<CheckRevocationStatus xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=126</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CheckRevocationStatus</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=126</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>126</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=160</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>160</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=161</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>161</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateStatus</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=19</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ValidityTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</CheckRevocationStatus>";

        private const string InitializationString =
           "<CertificateDirectoryTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=63</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateDirectoryTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=63</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>63</uax:NumericId>" +
           "<Applications>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000024</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>Applications</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=61</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000024</uax:NumericId>" +
           "</Applications>" +
           "<FindApplications>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000025</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FindApplications</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=15</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000025</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000026</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000026</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000027</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000027</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Applications</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FindApplications>" +
           "<RegisterApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000028</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RegisterApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=18</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000028</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<uax:References>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=41</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>ns=1;i=26</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "</uax:References>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000029</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000029</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000030</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000030</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RegisterApplication>" +
           "<UpdateApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000031</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UpdateApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=188</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000031</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000032</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000032</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</UpdateApplication>" +
           "<UnregisterApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000033</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UnregisterApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=21</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000033</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000034</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000034</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</UnregisterApplication>" +
           "<GetApplication>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000035</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetApplication</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=210</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000035</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000036</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000036</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000037</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000037</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Application</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>ns=1;i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetApplication>" +
           "<QueryApplications>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000038</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>QueryApplications</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=868</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000038</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000039</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000039</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>StartingRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>MaxRecordsToReturn</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationType</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ProductUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Capabilities</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>7</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000040</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000040</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>LastCounterResetTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NextRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Applications</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=308</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</QueryApplications>" +
           "<QueryServers>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000041</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>QueryServers</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=23</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000041</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000042</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000042</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>StartingRecordId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>MaxRecordsToReturn</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ProductUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServerCapabilities</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>6</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000043</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000043</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>LastCounterResetTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Servers</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12189</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</QueryServers>" +
           "<CertificateGroups>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=511</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateGroups</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=35</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=13813</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>511</uax:NumericId>" +
           "<DefaultApplicationGroup xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=512</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>DefaultApplicationGroup</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=12555</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>512</uax:NumericId>" +
           "<uax:References>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=9006</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>i=13225</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=9006</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>i=19297</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "</uax:References>" +
           "<TrustList>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=513</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>TrustList</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=12522</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>513</uax:NumericId>" +
           "<Size>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=514</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Size</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>514</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=9</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Size>" +
           "<Writable>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=515</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Writable</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>515</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Writable>" +
           "<UserWritable>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=516</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>UserWritable</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>516</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</UserWritable>" +
           "<OpenCount>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=517</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OpenCount</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>517</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OpenCount>" +
           "<Open>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=519</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Open</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=11580</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>519</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=520</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>520</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Mode</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=3</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=521</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>521</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</Open>" +
           "<Close>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=522</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Close</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=11583</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>522</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=523</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>523</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</Close>" +
           "<Read>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=524</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Read</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=11585</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>524</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=525</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>525</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Length</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=6</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=526</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>526</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Data</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</Read>" +
           "<Write>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=527</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Write</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=11588</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>527</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=528</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>528</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Data</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</Write>" +
           "<GetPosition>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=529</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>GetPosition</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=11590</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>529</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=530</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>530</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=531</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>531</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Position</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=9</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetPosition>" +
           "<SetPosition>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=532</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SetPosition</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=11593</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>532</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=533</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>533</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Position</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=9</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</SetPosition>" +
           "<LastUpdateTime>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=534</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>LastUpdateTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>534</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</LastUpdateTime>" +
           "<OpenWithMasks>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=535</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OpenWithMasks</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=12543</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>535</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=536</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>536</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Masks</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=537</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>537</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</OpenWithMasks>" +
           "<CloseAndUpdate>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=538</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>CloseAndUpdate</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=12546</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>538</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=539</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>539</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>FileHandle</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=7</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=540</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>540</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplyChangesRequired</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</CloseAndUpdate>" +
           "<AddCertificate>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=541</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>AddCertificate</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=12548</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>541</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=542</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>542</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>IsTrustedCertificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</AddCertificate>" +
           "<RemoveCertificate>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=543</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>RemoveCertificate</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=12550</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>543</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=544</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>544</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Thumbprint</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>IsTrustedCertificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</RemoveCertificate>" +
           "</TrustList>" +
           "<CertificateTypes>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=545</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>CertificateTypes</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>545</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</CertificateTypes>" +
           "</DefaultApplicationGroup>" +
           "</CertificateGroups>" +
           "<StartSigningRequest>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=79</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartSigningRequest</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=79</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>79</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=80</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>80</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateRequest</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=81</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>81</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartSigningRequest>" +
           "<StartNewKeyPairRequest>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=76</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartNewKeyPairRequest</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=76</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>76</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=77</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>77</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SubjectName</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>DomainNames</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PrivateKeyFormat</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PrivateKeyPassword</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>7</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=78</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>78</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartNewKeyPairRequest>" +
           "<FinishRequest>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=85</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FinishRequest</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=85</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>85</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=86</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>86</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=87</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>87</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PrivateKey</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>IssuerCertificates</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FinishRequest>" +
           "<RevokeCertificate>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=15003</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RevokeCertificate</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=15003</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>15003</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=15004</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>15004</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</RevokeCertificate>" +
           "<GetCertificateGroups>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=369</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetCertificateGroups</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=369</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>369</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=370</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>370</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=371</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>371</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupIds</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetCertificateGroups>" +
           "<GetCertificates>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=89</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetCertificates</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=89</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>89</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=90</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>90</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=108</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>108</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeIds</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificates</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetCertificates>" +
           "<GetTrustList>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=197</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetTrustList</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=197</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>197</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=198</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>198</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=199</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>199</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>TrustListId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetTrustList>" +
           "<GetCertificateStatus>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=222</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetCertificateStatus</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=222</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>222</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=223</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>223</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateGroupId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateTypeId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=224</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>224</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UpdateRequired</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetCertificateStatus>" +
           "<CheckRevocationStatus>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=126</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CheckRevocationStatus</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=126</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>126</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=160</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>160</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>Certificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=161</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>161</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateStatus</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=19</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ValidityTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</CheckRevocationStatus>" +
           "</CertificateDirectoryTypeInstance>";
        #endregion
        #endif
        #endregion

        #region Public Properties
        public CertificateGroupFolderState CertificateGroups
        {
            get => m_certificateGroups;

            set
            {
                if (!Object.ReferenceEquals(m_certificateGroups, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateGroups = value;
            }
        }

        public StartSigningRequestMethodState StartSigningRequest
        {
            get => m_startSigningRequestMethod;

            set
            {
                if (!Object.ReferenceEquals(m_startSigningRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startSigningRequestMethod = value;
            }
        }

        public StartNewKeyPairRequestMethodState StartNewKeyPairRequest
        {
            get => m_startNewKeyPairRequestMethod;

            set
            {
                if (!Object.ReferenceEquals(m_startNewKeyPairRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startNewKeyPairRequestMethod = value;
            }
        }

        public FinishRequestMethodState FinishRequest
        {
            get => m_finishRequestMethod;

            set
            {
                if (!Object.ReferenceEquals(m_finishRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_finishRequestMethod = value;
            }
        }

        public RevokeCertificateMethodState RevokeCertificate
        {
            get => m_revokeCertificateMethod;

            set
            {
                if (!Object.ReferenceEquals(m_revokeCertificateMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_revokeCertificateMethod = value;
            }
        }

        public GetCertificateGroupsMethodState GetCertificateGroups
        {
            get => m_getCertificateGroupsMethod;

            set
            {
                if (!Object.ReferenceEquals(m_getCertificateGroupsMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getCertificateGroupsMethod = value;
            }
        }

        public GetCertificatesMethodState GetCertificates
        {
            get => m_getCertificatesMethod;

            set
            {
                if (!Object.ReferenceEquals(m_getCertificatesMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getCertificatesMethod = value;
            }
        }

        public GetTrustListMethodState GetTrustList
        {
            get => m_getTrustListMethod;

            set
            {
                if (!Object.ReferenceEquals(m_getTrustListMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getTrustListMethod = value;
            }
        }

        public GetCertificateStatusMethodState GetCertificateStatus
        {
            get => m_getCertificateStatusMethod;

            set
            {
                if (!Object.ReferenceEquals(m_getCertificateStatusMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getCertificateStatusMethod = value;
            }
        }

        public CheckRevocationStatusMethodState CheckRevocationStatus
        {
            get => m_checkRevocationStatusMethod;

            set
            {
                if (!Object.ReferenceEquals(m_checkRevocationStatusMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_checkRevocationStatusMethod = value;
            }
        }
        #endregion

        #region Overridden Methods
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

            if (m_getCertificatesMethod != null)
            {
                children.Add(m_getCertificatesMethod);
            }

            if (m_getTrustListMethod != null)
            {
                children.Add(m_getTrustListMethod);
            }

            if (m_getCertificateStatusMethod != null)
            {
                children.Add(m_getCertificateStatusMethod);
            }

            if (m_checkRevocationStatusMethod != null)
            {
                children.Add(m_checkRevocationStatusMethod);
            }

            base.GetChildren(context, children);
        }
            
        protected override void RemoveExplicitlyDefinedChild(BaseInstanceState child)
        {
            if (Object.ReferenceEquals(m_certificateGroups, child))
            {
                m_certificateGroups = null;
                return;
            }

            if (Object.ReferenceEquals(m_startSigningRequestMethod, child))
            {
                m_startSigningRequestMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_startNewKeyPairRequestMethod, child))
            {
                m_startNewKeyPairRequestMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_finishRequestMethod, child))
            {
                m_finishRequestMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_revokeCertificateMethod, child))
            {
                m_revokeCertificateMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_getCertificateGroupsMethod, child))
            {
                m_getCertificateGroupsMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_getCertificatesMethod, child))
            {
                m_getCertificatesMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_getTrustListMethod, child))
            {
                m_getTrustListMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_getCertificateStatusMethod, child))
            {
                m_getCertificateStatusMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_checkRevocationStatusMethod, child))
            {
                m_checkRevocationStatusMethod = null;
                return;
            }

            base.RemoveExplicitlyDefinedChild(child);
        }

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

                case Opc.Ua.Gds.BrowseNames.GetCertificates:
                {
                    if (createOrReplace)
                    {
                        if (GetCertificates == null)
                        {
                            if (replacement == null)
                            {
                                GetCertificates = new GetCertificatesMethodState(this);
                            }
                            else
                            {
                                GetCertificates = (GetCertificatesMethodState)replacement;
                            }
                        }
                    }

                    instance = GetCertificates;
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

                case Opc.Ua.Gds.BrowseNames.CheckRevocationStatus:
                {
                    if (createOrReplace)
                    {
                        if (CheckRevocationStatus == null)
                        {
                            if (replacement == null)
                            {
                                CheckRevocationStatus = new CheckRevocationStatusMethodState(this);
                            }
                            else
                            {
                                CheckRevocationStatus = (CheckRevocationStatusMethodState)replacement;
                            }
                        }
                    }

                    instance = CheckRevocationStatus;
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
        private GetCertificatesMethodState m_getCertificatesMethod;
        private GetTrustListMethodState m_getTrustListMethod;
        private GetCertificateStatusMethodState m_getCertificateStatusMethod;
        private CheckRevocationStatusMethodState m_checkRevocationStatusMethod;
        #endregion
    }
    #endif
    #endregion

    #region CertificateRequestedAuditEventState Class
    #if (!OPCUA_EXCLUDE_CertificateRequestedAuditEventState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class CertificateRequestedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        public CertificateRequestedAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.CertificateRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<CertificateRequestedAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=91</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateRequestedAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=91</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>91</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000044</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000044</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000045</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000045</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000046</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000046</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000047</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000047</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000048</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000048</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000049</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000049</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000051</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000051</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000052</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000052</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000057</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000057</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000058</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000058</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000059</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000059</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000060</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000060</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000061</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000061</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000063</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000063</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000065</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000065</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<CertificateGroup>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=717</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateGroup</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>717</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</CertificateGroup>" +
           "<CertificateType>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=718</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>718</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</CertificateType>" +
           "</CertificateRequestedAuditEventTypeInstance>";
        #endregion
        #endif
        #endregion

        #region Public Properties
        public PropertyState<NodeId> CertificateGroup
        {
            get => m_certificateGroup;

            set
            {
                if (!Object.ReferenceEquals(m_certificateGroup, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateGroup = value;
            }
        }

        public PropertyState<NodeId> CertificateType
        {
            get => m_certificateType;

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
            
        protected override void RemoveExplicitlyDefinedChild(BaseInstanceState child)
        {
            if (Object.ReferenceEquals(m_certificateGroup, child))
            {
                m_certificateGroup = null;
                return;
            }

            if (Object.ReferenceEquals(m_certificateType, child))
            {
                m_certificateType = null;
                return;
            }

            base.RemoveExplicitlyDefinedChild(child);
        }

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
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class CertificateDeliveredAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        public CertificateDeliveredAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.CertificateDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<CertificateDeliveredAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=109</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateDeliveredAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=109</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>109</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000067</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000067</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000068</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000068</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000069</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000069</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000070</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000070</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000071</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000071</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000072</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000072</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000074</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000074</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000075</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000075</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000080</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000080</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000081</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000081</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000082</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000082</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000083</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000083</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000084</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000084</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000086</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000086</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000088</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000088</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<CertificateGroup>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=719</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateGroup</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>719</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</CertificateGroup>" +
           "<CertificateType>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=720</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>720</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</CertificateType>" +
           "</CertificateDeliveredAuditEventTypeInstance>";
        #endregion
        #endif
        #endregion

        #region Public Properties
        public PropertyState<NodeId> CertificateGroup
        {
            get => m_certificateGroup;

            set
            {
                if (!Object.ReferenceEquals(m_certificateGroup, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_certificateGroup = value;
            }
        }

        public PropertyState<NodeId> CertificateType
        {
            get => m_certificateType;

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
            
        protected override void RemoveExplicitlyDefinedChild(BaseInstanceState child)
        {
            if (Object.ReferenceEquals(m_certificateGroup, child))
            {
                m_certificateGroup = null;
                return;
            }

            if (Object.ReferenceEquals(m_certificateType, child))
            {
                m_certificateType = null;
                return;
            }

            base.RemoveExplicitlyDefinedChild(child);
        }

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

    #region CertificateRevokedAuditEventState Class
    #if (!OPCUA_EXCLUDE_CertificateRevokedAuditEventState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class CertificateRevokedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        public CertificateRevokedAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.CertificateRevokedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<CertificateRevokedAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=27</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>CertificateRevokedAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=27</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>27</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000090</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000090</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000091</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000091</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000092</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000092</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000093</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000093</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000094</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000094</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000095</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000095</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000097</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000097</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000098</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000098</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000103</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000103</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000104</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000104</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000105</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000105</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000106</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000106</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000107</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000107</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000109</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000109</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000111</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000111</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</CertificateRevokedAuditEventTypeInstance>";
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

    #region KeyCredentialManagementFolderState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialManagementFolderState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialManagementFolderState : FolderState
    {
        #region Constructors
        public KeyCredentialManagementFolderState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialManagementFolderType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<KeyCredentialManagementFolderTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=55</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialManagementFolderTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=55</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>55</uax:NumericId>" +
           "<ServiceName_Placeholder>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=61</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>&lt;ServiceName&gt;</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1020</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>61</uax:NumericId>" +
           "<ResourceUri>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=83</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ResourceUri</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>83</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ResourceUri>" +
           "<ProfileUris>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=162</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ProfileUris</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>162</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ProfileUris>" +
           "<StartRequest>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=168</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartRequest</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1023</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>168</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=171</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>171</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PublicKey</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SecurityPolicyUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=195</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>195</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartRequest>" +
           "<FinishRequest>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=196</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FinishRequest</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1026</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>196</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=202</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>202</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CancelRequest</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=203</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>203</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialSecret</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateThumbprint</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SecurityPolicyUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>GrantedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>5</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FinishRequest>" +
           "</ServiceName_Placeholder>" +
           "</KeyCredentialManagementFolderTypeInstance>";
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

    #region KeyCredentialServiceState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialServiceState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialServiceState : BaseObjectState
    {
        #region Constructors
        public KeyCredentialServiceState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);

            if (SecurityPolicyUris != null)
            {
                SecurityPolicyUris.Initialize(context, SecurityPolicyUris_InitializationString);
            }

            if (Revoke != null)
            {
                Revoke.Initialize(context, Revoke_InitializationString);
            }
        }

        #region Initialization String
        private const string SecurityPolicyUris_InitializationString =
           "<SecurityPolicyUris xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=495</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>SecurityPolicyUris</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>495</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SecurityPolicyUris>";

        private const string Revoke_InitializationString =
           "<Revoke xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1029</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>Revoke</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1029</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1029</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1030</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1030</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</Revoke>";

        private const string InitializationString =
           "<KeyCredentialServiceTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1020</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialServiceTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1020</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1020</uax:NumericId>" +
           "<ResourceUri>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1021</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ResourceUri</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1021</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ResourceUri>" +
           "<ProfileUris>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1022</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ProfileUris</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1022</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ProfileUris>" +
           "<SecurityPolicyUris>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=495</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>SecurityPolicyUris</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>495</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SecurityPolicyUris>" +
           "<StartRequest>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1023</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartRequest</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1023</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1023</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1024</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1024</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PublicKey</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SecurityPolicyUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1025</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1025</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartRequest>" +
           "<FinishRequest>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1026</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FinishRequest</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1026</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1026</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1027</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1027</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CancelRequest</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1028</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1028</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialSecret</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateThumbprint</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SecurityPolicyUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>GrantedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>5</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FinishRequest>" +
           "<Revoke>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1029</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>Revoke</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1029</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1029</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1030</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1030</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</Revoke>" +
           "</KeyCredentialServiceTypeInstance>";
        #endregion
        #endif
        #endregion

        #region Public Properties
        public PropertyState<string> ResourceUri
        {
            get => m_resourceUri;

            set
            {
                if (!Object.ReferenceEquals(m_resourceUri, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_resourceUri = value;
            }
        }

        public PropertyState<string[]> ProfileUris
        {
            get => m_profileUris;

            set
            {
                if (!Object.ReferenceEquals(m_profileUris, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_profileUris = value;
            }
        }

        public PropertyState<string[]> SecurityPolicyUris
        {
            get => m_securityPolicyUris;

            set
            {
                if (!Object.ReferenceEquals(m_securityPolicyUris, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_securityPolicyUris = value;
            }
        }

        public KeyCredentialStartRequestMethodState StartRequest
        {
            get => m_startRequestMethod;

            set
            {
                if (!Object.ReferenceEquals(m_startRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startRequestMethod = value;
            }
        }

        public KeyCredentialFinishRequestMethodState FinishRequest
        {
            get => m_finishRequestMethod;

            set
            {
                if (!Object.ReferenceEquals(m_finishRequestMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_finishRequestMethod = value;
            }
        }

        public KeyCredentialRevokeMethodState Revoke
        {
            get => m_revokeMethod;

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

            if (m_securityPolicyUris != null)
            {
                children.Add(m_securityPolicyUris);
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
            
        protected override void RemoveExplicitlyDefinedChild(BaseInstanceState child)
        {
            if (Object.ReferenceEquals(m_resourceUri, child))
            {
                m_resourceUri = null;
                return;
            }

            if (Object.ReferenceEquals(m_profileUris, child))
            {
                m_profileUris = null;
                return;
            }

            if (Object.ReferenceEquals(m_securityPolicyUris, child))
            {
                m_securityPolicyUris = null;
                return;
            }

            if (Object.ReferenceEquals(m_startRequestMethod, child))
            {
                m_startRequestMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_finishRequestMethod, child))
            {
                m_finishRequestMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_revokeMethod, child))
            {
                m_revokeMethod = null;
                return;
            }

            base.RemoveExplicitlyDefinedChild(child);
        }

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

                case Opc.Ua.Gds.BrowseNames.SecurityPolicyUris:
                {
                    if (createOrReplace)
                    {
                        if (SecurityPolicyUris == null)
                        {
                            if (replacement == null)
                            {
                                SecurityPolicyUris = new PropertyState<string[]>(this);
                            }
                            else
                            {
                                SecurityPolicyUris = (PropertyState<string[]>)replacement;
                            }
                        }
                    }

                    instance = SecurityPolicyUris;
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
        private PropertyState<string[]> m_securityPolicyUris;
        private KeyCredentialStartRequestMethodState m_startRequestMethod;
        private KeyCredentialFinishRequestMethodState m_finishRequestMethod;
        private KeyCredentialRevokeMethodState m_revokeMethod;
        #endregion
    }
    #endif
    #endregion

    #region KeyCredentialStartRequestMethodState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialStartRequestMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialStartRequestMethodState : MethodState
    {
        #region Constructors
        public KeyCredentialStartRequestMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new KeyCredentialStartRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<KeyCredentialStartRequestMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1031</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialStartRequestMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1031</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1031</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1032</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1032</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ApplicationUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PublicKey</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SecurityPolicyUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1033</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1033</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</KeyCredentialStartRequestMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public KeyCredentialStartRequestMethodStateMethodCallHandler OnCall;

        public KeyCredentialStartRequestMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            string applicationUri = (string)_inputArguments[0];
            byte[] publicKey = (byte[])_inputArguments[1];
            string securityPolicyUri = (string)_inputArguments[2];
            NodeId[] requestedRoles = (NodeId[])_inputArguments[3];

            NodeId requestId = (NodeId)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    applicationUri,
                    publicKey,
                    securityPolicyUri,
                    requestedRoles,
                    ref requestId);
            }

            _outputArguments[0] = requestId;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            KeyCredentialStartRequestMethodStateResult _result = null;

            string applicationUri = (string)_inputArguments[0];
            byte[] publicKey = (byte[])_inputArguments[1];
            string securityPolicyUri = (string)_inputArguments[2];
            NodeId[] requestedRoles = (NodeId[])_inputArguments[3];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    applicationUri,
                    publicKey,
                    securityPolicyUri,
                    requestedRoles,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.RequestId;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult KeyCredentialStartRequestMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string applicationUri,
        byte[] publicKey,
        string securityPolicyUri,
        NodeId[] requestedRoles,
        ref NodeId requestId);

    /// <exclude />
    public partial class KeyCredentialStartRequestMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public NodeId RequestId { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<KeyCredentialStartRequestMethodStateResult> KeyCredentialStartRequestMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string applicationUri,
        byte[] publicKey,
        string securityPolicyUri,
        NodeId[] requestedRoles,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region KeyCredentialFinishRequestMethodState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialFinishRequestMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialFinishRequestMethodState : MethodState
    {
        #region Constructors
        public KeyCredentialFinishRequestMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new KeyCredentialFinishRequestMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<KeyCredentialFinishRequestMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1034</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialFinishRequestMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1034</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1034</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1035</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1035</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CancelRequest</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1036</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1036</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialSecret</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CertificateThumbprint</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>SecurityPolicyUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>GrantedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>5</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</KeyCredentialFinishRequestMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public KeyCredentialFinishRequestMethodStateMethodCallHandler OnCall;

        public KeyCredentialFinishRequestMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            NodeId requestId = (NodeId)_inputArguments[0];
            bool cancelRequest = (bool)_inputArguments[1];

            string credentialId = (string)_outputArguments[0];
            byte[] credentialSecret = (byte[])_outputArguments[1];
            string certificateThumbprint = (string)_outputArguments[2];
            string securityPolicyUri = (string)_outputArguments[3];
            NodeId[] grantedRoles = (NodeId[])_outputArguments[4];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    requestId,
                    cancelRequest,
                    ref credentialId,
                    ref credentialSecret,
                    ref certificateThumbprint,
                    ref securityPolicyUri,
                    ref grantedRoles);
            }

            _outputArguments[0] = credentialId;
            _outputArguments[1] = credentialSecret;
            _outputArguments[2] = certificateThumbprint;
            _outputArguments[3] = securityPolicyUri;
            _outputArguments[4] = grantedRoles;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            KeyCredentialFinishRequestMethodStateResult _result = null;

            NodeId requestId = (NodeId)_inputArguments[0];
            bool cancelRequest = (bool)_inputArguments[1];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    requestId,
                    cancelRequest,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.CredentialId;
            _outputArguments[1] = _result.CredentialSecret;
            _outputArguments[2] = _result.CertificateThumbprint;
            _outputArguments[3] = _result.SecurityPolicyUri;
            _outputArguments[4] = _result.GrantedRoles;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult KeyCredentialFinishRequestMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId requestId,
        bool cancelRequest,
        ref string credentialId,
        ref byte[] credentialSecret,
        ref string certificateThumbprint,
        ref string securityPolicyUri,
        ref NodeId[] grantedRoles);

    /// <exclude />
    public partial class KeyCredentialFinishRequestMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public string CredentialId { get; set; }
        public byte[] CredentialSecret { get; set; }
        public string CertificateThumbprint { get; set; }
        public string SecurityPolicyUri { get; set; }
        public NodeId[] GrantedRoles { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<KeyCredentialFinishRequestMethodStateResult> KeyCredentialFinishRequestMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        NodeId requestId,
        bool cancelRequest,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region KeyCredentialRevokeMethodState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialRevokeMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialRevokeMethodState : MethodState
    {
        #region Constructors
        public KeyCredentialRevokeMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new KeyCredentialRevokeMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<KeyCredentialRevokeMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1037</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialRevokeMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1037</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1037</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1038</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1038</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CredentialId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</KeyCredentialRevokeMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public KeyCredentialRevokeMethodStateMethodCallHandler OnCall;

        public KeyCredentialRevokeMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            string credentialId = (string)_inputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    credentialId);
            }

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            KeyCredentialRevokeMethodStateResult _result = null;

            string credentialId = (string)_inputArguments[0];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    credentialId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult KeyCredentialRevokeMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string credentialId);

    /// <exclude />
    public partial class KeyCredentialRevokeMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<KeyCredentialRevokeMethodStateResult> KeyCredentialRevokeMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string credentialId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region KeyCredentialRequestedAuditEventState Class
    #if (!OPCUA_EXCLUDE_KeyCredentialRequestedAuditEventState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialRequestedAuditEventState : KeyCredentialAuditEventState
    {
        #region Constructors
        public KeyCredentialRequestedAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<KeyCredentialRequestedAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1039</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialRequestedAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1039</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1039</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000125</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000125</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000126</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000126</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000127</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000127</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000128</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000128</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000129</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000129</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000130</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000130</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000132</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000132</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000133</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000133</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000138</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000138</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000139</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000139</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000140</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000140</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000141</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000141</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000142</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000142</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000144</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000144</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000146</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000146</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<ResourceUri xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000148</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ResourceUri</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000148</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ResourceUri>" +
           "</KeyCredentialRequestedAuditEventTypeInstance>";
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
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialDeliveredAuditEventState : KeyCredentialAuditEventState
    {
        #region Constructors
        public KeyCredentialDeliveredAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialDeliveredAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<KeyCredentialDeliveredAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1057</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialDeliveredAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1057</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1057</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000149</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000149</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000150</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000150</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000151</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000151</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000152</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000152</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000153</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000153</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000154</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000154</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000156</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000156</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000157</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000157</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000162</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000162</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000163</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000163</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000164</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000164</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000165</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000165</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000166</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000166</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000168</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000168</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000170</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000170</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<ResourceUri xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000172</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ResourceUri</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000172</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ResourceUri>" +
           "</KeyCredentialDeliveredAuditEventTypeInstance>";
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
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class KeyCredentialRevokedAuditEventState : KeyCredentialAuditEventState
    {
        #region Constructors
        public KeyCredentialRevokedAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.KeyCredentialRevokedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<KeyCredentialRevokedAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1075</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>KeyCredentialRevokedAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1075</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1075</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000173</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000173</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000174</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000174</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000175</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000175</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000176</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000176</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000177</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000177</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000178</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000178</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000180</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000180</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000181</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000181</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000186</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000186</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000187</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000187</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000188</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000188</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000189</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000189</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000190</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000190</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000192</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000192</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000194</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000194</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<ResourceUri xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000196</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ResourceUri</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000196</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ResourceUri>" +
           "</KeyCredentialRevokedAuditEventTypeInstance>";
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

    #region AuthorizationServicesFolderState Class
    #if (!OPCUA_EXCLUDE_AuthorizationServicesFolderState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class AuthorizationServicesFolderState : FolderState
    {
        #region Constructors
        public AuthorizationServicesFolderState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.AuthorizationServicesFolderType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<AuthorizationServicesFolderTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=233</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>AuthorizationServicesFolderTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=233</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>233</uax:NumericId>" +
           "<ServiceName_Placeholder>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=234</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>&lt;ServiceName&gt;</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=35</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=966</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>234</uax:NumericId>" +
           "<uax:References>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=41</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>ns=1;i=111</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=41</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>ns=1;i=975</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "</uax:References>" +
           "<ServiceUri>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=235</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ServiceUri</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>235</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServiceUri>" +
           "<ServiceCertificate>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=236</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ServiceCertificate</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>236</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServiceCertificate>" +
           "<GetServiceDescription>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=238</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetServiceDescription</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1004</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>238</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=239</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>239</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceCertificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserTokenPolicies</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=304</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetServiceDescription>" +
           "</ServiceName_Placeholder>" +
           "</AuthorizationServicesFolderTypeInstance>";
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
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class AuthorizationServiceState : BaseObjectState
    {
        #region Constructors
        public AuthorizationServiceState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.AuthorizationServiceType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);

            if (UserTokenPolicies != null)
            {
                UserTokenPolicies.Initialize(context, UserTokenPolicies_InitializationString);
            }

            if (SupportedRoles != null)
            {
                SupportedRoles.Initialize(context, SupportedRoles_InitializationString);
            }

            if (RequestAccessToken != null)
            {
                RequestAccessToken.Initialize(context, RequestAccessToken_InitializationString);
            }

            if (StartRequestToken != null)
            {
                StartRequestToken.Initialize(context, StartRequestToken_InitializationString);
            }

            if (FinishRequestToken != null)
            {
                FinishRequestToken.Initialize(context, FinishRequestToken_InitializationString);
            }

            if (RefreshToken != null)
            {
                RefreshToken.Initialize(context, RefreshToken_InitializationString);
            }
        }

        #region Initialization String
        private const string UserTokenPolicies_InitializationString =
           "<UserTokenPolicies xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=967</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UserTokenPolicies</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>967</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=304</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</UserTokenPolicies>";

        private const string SupportedRoles_InitializationString =
           "<SupportedRoles xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=110</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>SupportedRoles</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>110</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SupportedRoles>";

        private const string RequestAccessToken_InitializationString =
           "<RequestAccessToken xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=969</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RequestAccessToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=969</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>969</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=970</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>970</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>IdentityToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=316</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=971</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>971</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RequestAccessToken>";

        private const string StartRequestToken_InitializationString =
           "<StartRequestToken xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=95</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartRequestToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=95</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>95</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=96</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>96</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PolicyId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestorData</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=97</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>97</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceData</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=14</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartRequestToken>";

        private const string FinishRequestToken_InitializationString =
           "<FinishRequestToken xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=98</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FinishRequestToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=98</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>98</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=99</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>99</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=14</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserIdentityToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=316</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserTokenSignature</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=456</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=100</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>100</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RefreshTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FinishRequestToken>";

        private const string RefreshToken_InitializationString =
           "<RefreshToken xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=64</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RefreshToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=64</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>64</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=65</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>65</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CurrentRefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=66</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>66</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NewRefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NewRefreshTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RefreshToken>";

        private const string InitializationString =
           "<AuthorizationServiceTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=966</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>AuthorizationServiceTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=966</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>966</uax:NumericId>" +
           "<uax:References>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=41</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>ns=1;i=111</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "<uax:Reference>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=41</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TargetId>" +
           "<uax:Identifier>ns=1;i=975</uax:Identifier>" +
           "</uax:TargetId>" +
           "</uax:Reference>" +
           "</uax:References>" +
           "<ServiceUri>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1003</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ServiceUri</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1003</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServiceUri>" +
           "<ServiceCertificate>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=968</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>ServiceCertificate</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>968</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServiceCertificate>" +
           "<UserTokenPolicies>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=967</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>UserTokenPolicies</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>967</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=304</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</UserTokenPolicies>" +
           "<SupportedRoles>" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=110</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>SupportedRoles</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>110</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SupportedRoles>" +
           "<GetServiceDescription>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1004</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetServiceDescription</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1004</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1004</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1005</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1005</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceCertificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserTokenPolicies</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=304</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetServiceDescription>" +
           "<RequestAccessToken>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=969</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RequestAccessToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=969</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>969</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=970</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>970</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>IdentityToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=316</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=971</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>971</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RequestAccessToken>" +
           "<StartRequestToken>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=95</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartRequestToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=95</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>95</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=96</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>96</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PolicyId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestorData</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=97</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>97</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceData</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=14</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartRequestToken>" +
           "<FinishRequestToken>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=98</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FinishRequestToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=98</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>98</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=99</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>99</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=14</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserIdentityToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=316</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserTokenSignature</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=456</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=100</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>100</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RefreshTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FinishRequestToken>" +
           "<RefreshToken>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=64</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RefreshToken</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=64</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>64</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=65</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>65</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CurrentRefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=66</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>66</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NewRefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NewRefreshTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RefreshToken>" +
           "</AuthorizationServiceTypeInstance>";
        #endregion
        #endif
        #endregion

        #region Public Properties
        public PropertyState<string> ServiceUri
        {
            get => m_serviceUri;

            set
            {
                if (!Object.ReferenceEquals(m_serviceUri, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serviceUri = value;
            }
        }

        public PropertyState<byte[]> ServiceCertificate
        {
            get => m_serviceCertificate;

            set
            {
                if (!Object.ReferenceEquals(m_serviceCertificate, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serviceCertificate = value;
            }
        }

        public PropertyState<UserTokenPolicy[]> UserTokenPolicies
        {
            get => m_userTokenPolicies;

            set
            {
                if (!Object.ReferenceEquals(m_userTokenPolicies, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_userTokenPolicies = value;
            }
        }

        public PropertyState<string[]> SupportedRoles
        {
            get => m_supportedRoles;

            set
            {
                if (!Object.ReferenceEquals(m_supportedRoles, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_supportedRoles = value;
            }
        }

        public GetServiceDescriptionMethodState GetServiceDescription
        {
            get => m_getServiceDescriptionMethod;

            set
            {
                if (!Object.ReferenceEquals(m_getServiceDescriptionMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_getServiceDescriptionMethod = value;
            }
        }

        public RequestAccessTokenMethodState RequestAccessToken
        {
            get => m_requestAccessTokenMethod;

            set
            {
                if (!Object.ReferenceEquals(m_requestAccessTokenMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_requestAccessTokenMethod = value;
            }
        }

        public StartRequestTokenMethodState StartRequestToken
        {
            get => m_startRequestTokenMethod;

            set
            {
                if (!Object.ReferenceEquals(m_startRequestTokenMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startRequestTokenMethod = value;
            }
        }

        public FinishRequestTokenMethodState FinishRequestToken
        {
            get => m_finishRequestTokenMethod;

            set
            {
                if (!Object.ReferenceEquals(m_finishRequestTokenMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_finishRequestTokenMethod = value;
            }
        }

        public RefreshTokenMethodState RefreshToken
        {
            get => m_refreshTokenMethod;

            set
            {
                if (!Object.ReferenceEquals(m_refreshTokenMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_refreshTokenMethod = value;
            }
        }
        #endregion

        #region Overridden Methods
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

            if (m_supportedRoles != null)
            {
                children.Add(m_supportedRoles);
            }

            if (m_getServiceDescriptionMethod != null)
            {
                children.Add(m_getServiceDescriptionMethod);
            }

            if (m_requestAccessTokenMethod != null)
            {
                children.Add(m_requestAccessTokenMethod);
            }

            if (m_startRequestTokenMethod != null)
            {
                children.Add(m_startRequestTokenMethod);
            }

            if (m_finishRequestTokenMethod != null)
            {
                children.Add(m_finishRequestTokenMethod);
            }

            if (m_refreshTokenMethod != null)
            {
                children.Add(m_refreshTokenMethod);
            }

            base.GetChildren(context, children);
        }
            
        protected override void RemoveExplicitlyDefinedChild(BaseInstanceState child)
        {
            if (Object.ReferenceEquals(m_serviceUri, child))
            {
                m_serviceUri = null;
                return;
            }

            if (Object.ReferenceEquals(m_serviceCertificate, child))
            {
                m_serviceCertificate = null;
                return;
            }

            if (Object.ReferenceEquals(m_userTokenPolicies, child))
            {
                m_userTokenPolicies = null;
                return;
            }

            if (Object.ReferenceEquals(m_supportedRoles, child))
            {
                m_supportedRoles = null;
                return;
            }

            if (Object.ReferenceEquals(m_getServiceDescriptionMethod, child))
            {
                m_getServiceDescriptionMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_requestAccessTokenMethod, child))
            {
                m_requestAccessTokenMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_startRequestTokenMethod, child))
            {
                m_startRequestTokenMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_finishRequestTokenMethod, child))
            {
                m_finishRequestTokenMethod = null;
                return;
            }

            if (Object.ReferenceEquals(m_refreshTokenMethod, child))
            {
                m_refreshTokenMethod = null;
                return;
            }

            base.RemoveExplicitlyDefinedChild(child);
        }

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

                case Opc.Ua.Gds.BrowseNames.SupportedRoles:
                {
                    if (createOrReplace)
                    {
                        if (SupportedRoles == null)
                        {
                            if (replacement == null)
                            {
                                SupportedRoles = new PropertyState<string[]>(this);
                            }
                            else
                            {
                                SupportedRoles = (PropertyState<string[]>)replacement;
                            }
                        }
                    }

                    instance = SupportedRoles;
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

                case Opc.Ua.Gds.BrowseNames.StartRequestToken:
                {
                    if (createOrReplace)
                    {
                        if (StartRequestToken == null)
                        {
                            if (replacement == null)
                            {
                                StartRequestToken = new StartRequestTokenMethodState(this);
                            }
                            else
                            {
                                StartRequestToken = (StartRequestTokenMethodState)replacement;
                            }
                        }
                    }

                    instance = StartRequestToken;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.FinishRequestToken:
                {
                    if (createOrReplace)
                    {
                        if (FinishRequestToken == null)
                        {
                            if (replacement == null)
                            {
                                FinishRequestToken = new FinishRequestTokenMethodState(this);
                            }
                            else
                            {
                                FinishRequestToken = (FinishRequestTokenMethodState)replacement;
                            }
                        }
                    }

                    instance = FinishRequestToken;
                    break;
                }

                case Opc.Ua.Gds.BrowseNames.RefreshToken:
                {
                    if (createOrReplace)
                    {
                        if (RefreshToken == null)
                        {
                            if (replacement == null)
                            {
                                RefreshToken = new RefreshTokenMethodState(this);
                            }
                            else
                            {
                                RefreshToken = (RefreshTokenMethodState)replacement;
                            }
                        }
                    }

                    instance = RefreshToken;
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
        private PropertyState<string[]> m_supportedRoles;
        private GetServiceDescriptionMethodState m_getServiceDescriptionMethod;
        private RequestAccessTokenMethodState m_requestAccessTokenMethod;
        private StartRequestTokenMethodState m_startRequestTokenMethod;
        private FinishRequestTokenMethodState m_finishRequestTokenMethod;
        private RefreshTokenMethodState m_refreshTokenMethod;
        #endregion
    }
    #endif
    #endregion

    #region GetServiceDescriptionMethodState Class
    #if (!OPCUA_EXCLUDE_GetServiceDescriptionMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class GetServiceDescriptionMethodState : MethodState
    {
        #region Constructors
        public GetServiceDescriptionMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new GetServiceDescriptionMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<GetServiceDescriptionMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1006</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>GetServiceDescriptionMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=1006</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1006</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1007</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1007</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceUri</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceCertificate</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserTokenPolicies</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=304</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</GetServiceDescriptionMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public GetServiceDescriptionMethodStateMethodCallHandler OnCall;

        public GetServiceDescriptionMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            string serviceUri = (string)_outputArguments[0];
            byte[] serviceCertificate = (byte[])_outputArguments[1];
            Opc.Ua.UserTokenPolicy[] userTokenPolicies = (Opc.Ua.UserTokenPolicy[])_outputArguments[2];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    ref serviceUri,
                    ref serviceCertificate,
                    ref userTokenPolicies);
            }

            _outputArguments[0] = serviceUri;
            _outputArguments[1] = serviceCertificate;
            _outputArguments[2] = userTokenPolicies;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            GetServiceDescriptionMethodStateResult _result = null;

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.ServiceUri;
            _outputArguments[1] = _result.ServiceCertificate;
            _outputArguments[2] = _result.UserTokenPolicies;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult GetServiceDescriptionMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        ref string serviceUri,
        ref byte[] serviceCertificate,
        ref Opc.Ua.UserTokenPolicy[] userTokenPolicies);

    /// <exclude />
    public partial class GetServiceDescriptionMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public string ServiceUri { get; set; }
        public byte[] ServiceCertificate { get; set; }
        public Opc.Ua.UserTokenPolicy[] UserTokenPolicies { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<GetServiceDescriptionMethodStateResult> GetServiceDescriptionMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region RequestAccessTokenMethodState Class
    #if (!OPCUA_EXCLUDE_RequestAccessTokenMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class RequestAccessTokenMethodState : MethodState
    {
        #region Constructors
        public RequestAccessTokenMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new RequestAccessTokenMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<RequestAccessTokenMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=995</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RequestAccessTokenMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=995</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>995</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=996</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>996</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>IdentityToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=316</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=997</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>997</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>1</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RequestAccessTokenMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public RequestAccessTokenMethodStateMethodCallHandler OnCall;

        public RequestAccessTokenMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            Opc.Ua.UserIdentityToken identityToken = (Opc.Ua.UserIdentityToken)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[0]);
            string resourceId = (string)_inputArguments[1];

            string accessToken = (string)_outputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    identityToken,
                    resourceId,
                    ref accessToken);
            }

            _outputArguments[0] = accessToken;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            RequestAccessTokenMethodStateResult _result = null;

            Opc.Ua.UserIdentityToken identityToken = (Opc.Ua.UserIdentityToken)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[0]);
            string resourceId = (string)_inputArguments[1];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    identityToken,
                    resourceId,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.AccessToken;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult RequestAccessTokenMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        Opc.Ua.UserIdentityToken identityToken,
        string resourceId,
        ref string accessToken);

    /// <exclude />
    public partial class RequestAccessTokenMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public string AccessToken { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<RequestAccessTokenMethodStateResult> RequestAccessTokenMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        Opc.Ua.UserIdentityToken identityToken,
        string resourceId,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region StartRequestTokenMethodState Class
    #if (!OPCUA_EXCLUDE_StartRequestTokenMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class StartRequestTokenMethodState : MethodState
    {
        #region Constructors
        public StartRequestTokenMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new StartRequestTokenMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<StartRequestTokenMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=101</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>StartRequestTokenMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=101</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>101</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=102</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>102</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>PolicyId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestorData</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>3</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=103</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>103</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ServiceData</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=14</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</StartRequestTokenMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public StartRequestTokenMethodStateMethodCallHandler OnCall;

        public StartRequestTokenMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            string resourceId = (string)_inputArguments[0];
            string policyId = (string)_inputArguments[1];
            byte[] requestorData = (byte[])_inputArguments[2];

            byte[] serviceData = (byte[])_outputArguments[0];
            Uuid requestId = (Uuid)_outputArguments[1];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    resourceId,
                    policyId,
                    requestorData,
                    ref serviceData,
                    ref requestId);
            }

            _outputArguments[0] = serviceData;
            _outputArguments[1] = requestId;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            StartRequestTokenMethodStateResult _result = null;

            string resourceId = (string)_inputArguments[0];
            string policyId = (string)_inputArguments[1];
            byte[] requestorData = (byte[])_inputArguments[2];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    resourceId,
                    policyId,
                    requestorData,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.ServiceData;
            _outputArguments[1] = _result.RequestId;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult StartRequestTokenMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string resourceId,
        string policyId,
        byte[] requestorData,
        ref byte[] serviceData,
        ref Uuid requestId);

    /// <exclude />
    public partial class StartRequestTokenMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public byte[] ServiceData { get; set; }
        public Uuid RequestId { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<StartRequestTokenMethodStateResult> StartRequestTokenMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string resourceId,
        string policyId,
        byte[] requestorData,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region FinishRequestTokenMethodState Class
    #if (!OPCUA_EXCLUDE_FinishRequestTokenMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class FinishRequestTokenMethodState : MethodState
    {
        #region Constructors
        public FinishRequestTokenMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new FinishRequestTokenMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<FinishRequestTokenMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=104</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>FinishRequestTokenMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=104</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>104</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=105</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>105</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=14</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RequestedRoles</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>" +
           "<uax:UInt32>0</uax:UInt32>" +
           "</uax:ArrayDimensions>" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserIdentityToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=316</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>UserTokenSignature</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=456</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=106</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>106</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>RefreshTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</FinishRequestTokenMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public FinishRequestTokenMethodStateMethodCallHandler OnCall;

        public FinishRequestTokenMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            Uuid requestId = (Uuid)_inputArguments[0];
            string[] requestedRoles = (string[])_inputArguments[1];
            Opc.Ua.UserIdentityToken userIdentityToken = (Opc.Ua.UserIdentityToken)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[2]);
            Opc.Ua.SignatureData userTokenSignature = (Opc.Ua.SignatureData)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[3]);

            string accessToken = (string)_outputArguments[0];
            DateTime accessTokenExpiryTime = (DateTime)_outputArguments[1];
            string refreshToken = (string)_outputArguments[2];
            DateTime refreshTokenExpiryTime = (DateTime)_outputArguments[3];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    requestId,
                    requestedRoles,
                    userIdentityToken,
                    userTokenSignature,
                    ref accessToken,
                    ref accessTokenExpiryTime,
                    ref refreshToken,
                    ref refreshTokenExpiryTime);
            }

            _outputArguments[0] = accessToken;
            _outputArguments[1] = accessTokenExpiryTime;
            _outputArguments[2] = refreshToken;
            _outputArguments[3] = refreshTokenExpiryTime;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            FinishRequestTokenMethodStateResult _result = null;

            Uuid requestId = (Uuid)_inputArguments[0];
            string[] requestedRoles = (string[])_inputArguments[1];
            Opc.Ua.UserIdentityToken userIdentityToken = (Opc.Ua.UserIdentityToken)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[2]);
            Opc.Ua.SignatureData userTokenSignature = (Opc.Ua.SignatureData)ExtensionObject.ToEncodeable((ExtensionObject)_inputArguments[3]);

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    requestId,
                    requestedRoles,
                    userIdentityToken,
                    userTokenSignature,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.AccessToken;
            _outputArguments[1] = _result.AccessTokenExpiryTime;
            _outputArguments[2] = _result.RefreshToken;
            _outputArguments[3] = _result.RefreshTokenExpiryTime;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult FinishRequestTokenMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        Uuid requestId,
        string[] requestedRoles,
        Opc.Ua.UserIdentityToken userIdentityToken,
        Opc.Ua.SignatureData userTokenSignature,
        ref string accessToken,
        ref DateTime accessTokenExpiryTime,
        ref string refreshToken,
        ref DateTime refreshTokenExpiryTime);

    /// <exclude />
    public partial class FinishRequestTokenMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public string AccessToken { get; set; }
        public DateTime AccessTokenExpiryTime { get; set; }
        public string RefreshToken { get; set; }
        public DateTime RefreshTokenExpiryTime { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<FinishRequestTokenMethodStateResult> FinishRequestTokenMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        Uuid requestId,
        string[] requestedRoles,
        Opc.Ua.UserIdentityToken userIdentityToken,
        Opc.Ua.SignatureData userTokenSignature,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region RefreshTokenMethodState Class
    #if (!OPCUA_EXCLUDE_RefreshTokenMethodState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class RefreshTokenMethodState : MethodState
    {
        #region Constructors
        public RefreshTokenMethodState(NodeState parent) : base(parent)
        {
        }

        public new static NodeState Construct(NodeState parent)
        {
            return new RefreshTokenMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<RefreshTokenMethodType xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Method_4</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=70</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>RefreshTokenMethodType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=47</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=70</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>70</uax:NumericId>" +
           "<uax:Executable>true</uax:Executable>" +
           "<uax:UserExecutable>true</uax:UserExecutable>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=71</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>71</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>ResourceId</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>CurrentRefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>2</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "<OutputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=72</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>OutputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>72</uax:NumericId>" +
           "<uax:Value>" +
           "<uax:Value>" +
           "<uax:ListOfExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>AccessTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NewRefreshToken</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "<uax:ExtensionObject>" +
           "<uax:TypeId>" +
           "<uax:Identifier>i=297</uax:Identifier>" +
           "</uax:TypeId>" +
           "<uax:Body>" +
           "<uax:Argument>" +
           "<uax:Name>NewRefreshTokenExpiryTime</uax:Name>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=13</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:ArrayDimensions />" +
           "</uax:Argument>" +
           "</uax:Body>" +
           "</uax:ExtensionObject>" +
           "</uax:ListOfExtensionObject>" +
           "</uax:Value>" +
           "</uax:Value>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=296</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>4</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</OutputArguments>" +
           "</RefreshTokenMethodType>";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        public RefreshTokenMethodStateMethodCallHandler OnCall;

        public RefreshTokenMethodStateMethodAsyncCallHandler OnCallAsync;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        protected override ServiceResult Call(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments)
        {
            if (OnCall == null)
            {
                return base.Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            ServiceResult _result = null;

            string resourceId = (string)_inputArguments[0];
            string currentRefreshToken = (string)_inputArguments[1];

            string accessToken = (string)_outputArguments[0];
            DateTime accessTokenExpiryTime = (DateTime)_outputArguments[1];
            string newRefreshToken = (string)_outputArguments[2];
            DateTime newRefreshTokenExpiryTime = (DateTime)_outputArguments[3];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    resourceId,
                    currentRefreshToken,
                    ref accessToken,
                    ref accessTokenExpiryTime,
                    ref newRefreshToken,
                    ref newRefreshTokenExpiryTime);
            }

            _outputArguments[0] = accessToken;
            _outputArguments[1] = accessTokenExpiryTime;
            _outputArguments[2] = newRefreshToken;
            _outputArguments[3] = newRefreshTokenExpiryTime;

            return _result;
        }

        #if (OPCUA_INCLUDE_ASYNC)
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext _context,
            NodeId _objectId,
            IList<object> _inputArguments,
            IList<object> _outputArguments,
            CancellationToken cancellationToken = default)
        {
            if (OnCall == null && OnCallAsync == null)
            {
                return await base.CallAsync(_context, _objectId, _inputArguments, _outputArguments, cancellationToken).ConfigureAwait(false);
            }

            RefreshTokenMethodStateResult _result = null;

            string resourceId = (string)_inputArguments[0];
            string currentRefreshToken = (string)_inputArguments[1];

            if (OnCallAsync != null)
            {
                _result = await OnCallAsync(
                    _context,
                    this,
                    _objectId,
                    resourceId,
                    currentRefreshToken,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (OnCall != null)
            {
                return Call(_context, _objectId, _inputArguments, _outputArguments);
            }

            _outputArguments[0] = _result.AccessToken;
            _outputArguments[1] = _result.AccessTokenExpiryTime;
            _outputArguments[2] = _result.NewRefreshToken;
            _outputArguments[3] = _result.NewRefreshTokenExpiryTime;

            return _result.ServiceResult;
        }
        #endif

        #endregion

        #region Private Fields
        #endregion
    }

    /// <exclude />
    public delegate ServiceResult RefreshTokenMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string resourceId,
        string currentRefreshToken,
        ref string accessToken,
        ref DateTime accessTokenExpiryTime,
        ref string newRefreshToken,
        ref DateTime newRefreshTokenExpiryTime);

    /// <exclude />
    public partial class RefreshTokenMethodStateResult
    {
        public ServiceResult ServiceResult { get; set; }
        public string AccessToken { get; set; }
        public DateTime AccessTokenExpiryTime { get; set; }
        public string NewRefreshToken { get; set; }
        public DateTime NewRefreshTokenExpiryTime { get; set; }
    }

    /// <exclude />
    public delegate ValueTask<RefreshTokenMethodStateResult> RefreshTokenMethodStateMethodAsyncCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string resourceId,
        string currentRefreshToken,
        CancellationToken cancellationToken);
    #endif
    #endregion

    #region AccessTokenRequestedAuditEventState Class
    #if (!OPCUA_EXCLUDE_AccessTokenRequestedAuditEventState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class AccessTokenRequestedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        public AccessTokenRequestedAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.AccessTokenRequestedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<AccessTokenRequestedAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=111</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>AccessTokenRequestedAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=111</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>111</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000216</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000216</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000217</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000217</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000218</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000218</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000219</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000219</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000220</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000220</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000221</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000221</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000223</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000223</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000224</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000224</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000229</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000229</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000230</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000230</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000231</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000231</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000232</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000232</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000233</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000233</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000235</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000235</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000237</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000237</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</AccessTokenRequestedAuditEventTypeInstance>";
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

    #region AccessTokenIssuedAuditEventState Class
    #if (!OPCUA_EXCLUDE_AccessTokenIssuedAuditEventState)
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    public partial class AccessTokenIssuedAuditEventState : AuditUpdateMethodEventState
    {
        #region Constructors
        public AccessTokenIssuedAuditEventState(NodeState parent) : base(parent)
        {
        }

        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Gds.ObjectTypes.AccessTokenIssuedAuditEventType, Opc.Ua.Gds.Namespaces.OpcUaGds, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "<AccessTokenIssuedAuditEventTypeInstance xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\" xmlns=\"http://opcfoundation.org/UA/GDS/\">" +
           "<uax:NamespaceUris>" +
           "<uax:NamespaceUri>http://opcfoundation.org/UA/GDS/</uax:NamespaceUri>" +
           "</uax:NamespaceUris>" +
           "<uax:NodeClass>Object_1</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=975</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>1</uax:NamespaceIndex>" +
           "<uax:Name>AccessTokenIssuedAuditEventTypeInstance</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>ns=1;i=975</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>975</uax:NumericId>" +
           "<EventId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000239</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000239</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=15</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventId>" +
           "<EventType xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000240</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>EventType</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000240</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</EventType>" +
           "<SourceNode xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000241</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceNode</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000241</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceNode>" +
           "<SourceName xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000242</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>SourceName</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000242</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</SourceName>" +
           "<Time xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000243</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Time</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000243</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Time>" +
           "<ReceiveTime xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000244</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ReceiveTime</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000244</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ReceiveTime>" +
           "<Message xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000246</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Message</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000246</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=21</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Message>" +
           "<Severity xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000247</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Severity</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000247</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=5</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Severity>" +
           "<ActionTimeStamp xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000252</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ActionTimeStamp</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000252</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=294</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ActionTimeStamp>" +
           "<Status xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000253</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>Status</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000253</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=1</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</Status>" +
           "<ServerId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000254</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ServerId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000254</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ServerId>" +
           "<ClientAuditEntryId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000255</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientAuditEntryId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000255</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientAuditEntryId>" +
           "<ClientUserId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000256</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>ClientUserId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000256</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=12</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</ClientUserId>" +
           "<MethodId xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000258</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>MethodId</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000258</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=17</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>-1</uax:ValueRank>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</MethodId>" +
           "<InputArguments xmlns=\"http://opcfoundation.org/UA/\">" +
           "<uax:NodeClass>Variable_2</uax:NodeClass>" +
           "<uax:NodeId>" +
           "<uax:Identifier>ns=1;i=1000260</uax:Identifier>" +
           "</uax:NodeId>" +
           "<uax:BrowseName>" +
           "<uax:NamespaceIndex>0</uax:NamespaceIndex>" +
           "<uax:Name>InputArguments</uax:Name>" +
           "</uax:BrowseName>" +
           "<uax:ReferenceTypeId>" +
           "<uax:Identifier>i=46</uax:Identifier>" +
           "</uax:ReferenceTypeId>" +
           "<uax:TypeDefinitionId>" +
           "<uax:Identifier>i=68</uax:Identifier>" +
           "</uax:TypeDefinitionId>" +
           "<uax:NumericId>1000260</uax:NumericId>" +
           "<uax:DataType>" +
           "<uax:Identifier>i=24</uax:Identifier>" +
           "</uax:DataType>" +
           "<uax:ValueRank>1</uax:ValueRank>" +
           "<uax:ArrayDimensions>0</uax:ArrayDimensions>" +
           "<uax:AccessLevel>1</uax:AccessLevel>" +
           "<uax:UserAccessLevel>1</uax:UserAccessLevel>" +
           "</InputArguments>" +
           "</AccessTokenIssuedAuditEventTypeInstance>";
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
