/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Opc.Ua.Com
{
    #region ObjectType Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <summary>
        /// The identifier for the ComServerStatusType ObjectType.
        /// </summary>
        public const uint ComServerStatusType = 9;
    }
    #endregion

    #region Variable Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class Variables
    {
        /// <summary>
        /// The identifier for the ComServerStatusType_ServerUrl Variable.
        /// </summary>
        public const uint ComServerStatusType_ServerUrl = 10;

        /// <summary>
        /// The identifier for the ComServerStatusType_VendorInfo Variable.
        /// </summary>
        public const uint ComServerStatusType_VendorInfo = 11;

        /// <summary>
        /// The identifier for the ComServerStatusType_SoftwareVersion Variable.
        /// </summary>
        public const uint ComServerStatusType_SoftwareVersion = 12;

        /// <summary>
        /// The identifier for the ComServerStatusType_ServerState Variable.
        /// </summary>
        public const uint ComServerStatusType_ServerState = 13;

        /// <summary>
        /// The identifier for the ComServerStatusType_CurrentTime Variable.
        /// </summary>
        public const uint ComServerStatusType_CurrentTime = 14;

        /// <summary>
        /// The identifier for the ComServerStatusType_StartTime Variable.
        /// </summary>
        public const uint ComServerStatusType_StartTime = 15;

        /// <summary>
        /// The identifier for the ComServerStatusType_LastUpdateTime Variable.
        /// </summary>
        public const uint ComServerStatusType_LastUpdateTime = 16;
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <summary>
        /// The identifier for the ComServerStatusType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType = new ExpandedNodeId(Opc.Ua.Com.ObjectTypes.ComServerStatusType, Opc.Ua.Com.Namespaces.OpcUaCom);
    }
    #endregion

    #region Variable Node Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <summary>
        /// The identifier for the ComServerStatusType_ServerUrl Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_ServerUrl = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_ServerUrl, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_VendorInfo Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_VendorInfo = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_VendorInfo, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_SoftwareVersion Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_SoftwareVersion = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_SoftwareVersion, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_ServerState Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_ServerState = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_ServerState, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_CurrentTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_CurrentTime = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_CurrentTime, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_StartTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_StartTime = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_StartTime, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_LastUpdateTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_LastUpdateTime, Opc.Ua.Com.Namespaces.OpcUaCom);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the ComServerStatusType component.
        /// </summary>
        public const string ComServerStatusType = "ComServerStatusType";

        /// <summary>
        /// The BrowseName for the CurrentTime component.
        /// </summary>
        public const string CurrentTime = "CurrentTime";

        /// <summary>
        /// The BrowseName for the LastUpdateTime component.
        /// </summary>
        public const string LastUpdateTime = "LastUpdateTime";

        /// <summary>
        /// The BrowseName for the ServerState component.
        /// </summary>
        public const string ServerState = "ServerState";

        /// <summary>
        /// The BrowseName for the ServerUrl component.
        /// </summary>
        public const string ServerUrl = "ServerUrl";

        /// <summary>
        /// The BrowseName for the SoftwareVersion component.
        /// </summary>
        public const string SoftwareVersion = "SoftwareVersion";

        /// <summary>
        /// The BrowseName for the StartTime component.
        /// </summary>
        public const string StartTime = "StartTime";

        /// <summary>
        /// The BrowseName for the VendorInfo component.
        /// </summary>
        public const string VendorInfo = "VendorInfo";
    }
    #endregion

    #region Namespace Declarations
    /// <summary>
    /// Defines constants for all namespaces referenced by the model design.
    /// </summary>
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaCom namespace (.NET code namespace is 'Opc.Ua.Com').
        /// </summary>
        public const string OpcUaCom = "http://opcfoundation.org/UA/SDK/COMInterop";

        /// <summary>
        /// Returns a namespace table with all of the URIs defined.
        /// </summary>
        /// <remarks>
        /// This table is was used to create any relative paths in the model design.
        /// </remarks>
        public static NamespaceTable GetNamespaceTable()
        {
            FieldInfo[] fields = typeof(Namespaces).GetFields(BindingFlags.Public | BindingFlags.Static);

            NamespaceTable namespaceTable = new NamespaceTable();

            foreach (FieldInfo field in fields)
            {
                string namespaceUri = (string)field.GetValue(typeof(Namespaces));

                if (namespaceTable.GetIndex(namespaceUri) == -1)
                {
                    namespaceTable.Append(namespaceUri);
                }
            }

            return namespaceTable;
        }
    }
    #endregion
    
    #region ComServerStatusState Class
    #if (!OPCUA_EXCLUDE_ComServerStatusState)
    /// <summary>
    /// Stores an instance of the ComServerStatusType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public partial class ComServerStatusState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ComServerStatusState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Com.ObjectTypes.ComServerStatusType, Opc.Ua.Com.Namespaces.OpcUaCom, namespaceUris);
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
           "AQAAACoAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvU0RLL0NPTUludGVyb3D/////BGCAAQEA" +
           "AAABABsAAABDb21TZXJ2ZXJTdGF0dXNUeXBlSW5zdGFuY2UBAQkAAQEJAABO/////wcAAAAVYIkLAgAA" +
           "AAEACQAAAFNlcnZlclVybAEBCgAALgBEAE4KAAAAAAz/////AQH/////AAAAABVgiQsCAAAAAQAKAAAA" +
           "VmVuZG9ySW5mbwEBCwAALgBEAE4LAAAAAAz/////AQH/////AAAAABVgiQsCAAAAAQAPAAAAU29mdHdh" +
           "cmVWZXJzaW9uAQEMAAAuAEQATgwAAAAADP////8BAf////8AAAAAFWCJCwIAAAABAAsAAABTZXJ2ZXJT" +
           "dGF0ZQEBDQAALgBEAE4NAAAAAAz/////AQH/////AAAAABVgiQsCAAAAAQALAAAAQ3VycmVudFRpbWUB" +
           "AQ4AAC4ARABODgAAAAAN/////wEB/////wAAAAAVYIkLAgAAAAEACQAAAFN0YXJ0VGltZQEBDwAALgBE" +
           "AE4PAAAAAA3/////AQH/////AAAAABVgiQsCAAAAAQAOAAAATGFzdFVwZGF0ZVRpbWUBARAAAC4ARABO" +
           "EAAAAAAN/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the ServerUrl Property.
        /// </summary>
        public PropertyState<string> ServerUrl
        {
            get
            { 
                return m_serverUrl;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_serverUrl, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serverUrl = value;
            }
        }

        /// <summary>
        /// A description for the VendorInfo Property.
        /// </summary>
        public PropertyState<string> VendorInfo
        {
            get
            { 
                return m_vendorInfo;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_vendorInfo, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vendorInfo = value;
            }
        }

        /// <summary>
        /// A description for the SoftwareVersion Property.
        /// </summary>
        public PropertyState<string> SoftwareVersion
        {
            get
            { 
                return m_softwareVersion;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_softwareVersion, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_softwareVersion = value;
            }
        }

        /// <summary>
        /// A description for the ServerState Property.
        /// </summary>
        public PropertyState<string> ServerState
        {
            get
            { 
                return m_serverState;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_serverState, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serverState = value;
            }
        }

        /// <summary>
        /// A description for the CurrentTime Property.
        /// </summary>
        public PropertyState<DateTime> CurrentTime
        {
            get
            { 
                return m_currentTime;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_currentTime, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_currentTime = value;
            }
        }

        /// <summary>
        /// A description for the StartTime Property.
        /// </summary>
        public PropertyState<DateTime> StartTime
        {
            get
            { 
                return m_startTime;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_startTime, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startTime = value;
            }
        }

        /// <summary>
        /// A description for the LastUpdateTime Property.
        /// </summary>
        public PropertyState<DateTime> LastUpdateTime
        {
            get
            { 
                return m_lastUpdateTime;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_lastUpdateTime, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_lastUpdateTime = value;
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
            if (m_serverUrl != null)
            {
                children.Add(m_serverUrl);
            }

            if (m_vendorInfo != null)
            {
                children.Add(m_vendorInfo);
            }

            if (m_softwareVersion != null)
            {
                children.Add(m_softwareVersion);
            }

            if (m_serverState != null)
            {
                children.Add(m_serverState);
            }

            if (m_currentTime != null)
            {
                children.Add(m_currentTime);
            }

            if (m_startTime != null)
            {
                children.Add(m_startTime);
            }

            if (m_lastUpdateTime != null)
            {
                children.Add(m_lastUpdateTime);
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
                case Opc.Ua.Com.BrowseNames.ServerUrl:
                {
                    if (createOrReplace)
                    {
                        if (ServerUrl == null)
                        {
                            if (replacement == null)
                            {
                                ServerUrl = new PropertyState<string>(this);
                            }
                            else
                            {
                                ServerUrl = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = ServerUrl;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.VendorInfo:
                {
                    if (createOrReplace)
                    {
                        if (VendorInfo == null)
                        {
                            if (replacement == null)
                            {
                                VendorInfo = new PropertyState<string>(this);
                            }
                            else
                            {
                                VendorInfo = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = VendorInfo;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.SoftwareVersion:
                {
                    if (createOrReplace)
                    {
                        if (SoftwareVersion == null)
                        {
                            if (replacement == null)
                            {
                                SoftwareVersion = new PropertyState<string>(this);
                            }
                            else
                            {
                                SoftwareVersion = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = SoftwareVersion;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.ServerState:
                {
                    if (createOrReplace)
                    {
                        if (ServerState == null)
                        {
                            if (replacement == null)
                            {
                                ServerState = new PropertyState<string>(this);
                            }
                            else
                            {
                                ServerState = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = ServerState;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.CurrentTime:
                {
                    if (createOrReplace)
                    {
                        if (CurrentTime == null)
                        {
                            if (replacement == null)
                            {
                                CurrentTime = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                CurrentTime = (PropertyState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = CurrentTime;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.StartTime:
                {
                    if (createOrReplace)
                    {
                        if (StartTime == null)
                        {
                            if (replacement == null)
                            {
                                StartTime = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                StartTime = (PropertyState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = StartTime;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.LastUpdateTime:
                {
                    if (createOrReplace)
                    {
                        if (LastUpdateTime == null)
                        {
                            if (replacement == null)
                            {
                                LastUpdateTime = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                LastUpdateTime = (PropertyState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = LastUpdateTime;
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
        private PropertyState<string> m_serverUrl;
        private PropertyState<string> m_vendorInfo;
        private PropertyState<string> m_softwareVersion;
        private PropertyState<string> m_serverState;
        private PropertyState<DateTime> m_currentTime;
        private PropertyState<DateTime> m_startTime;
        private PropertyState<DateTime> m_lastUpdateTime;
        #endregion
    }
    #endif
    #endregion
}
