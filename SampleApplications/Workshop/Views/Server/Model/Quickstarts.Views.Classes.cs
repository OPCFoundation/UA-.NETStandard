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
using Quickstarts.Engineering;
using Quickstarts.Operations;

namespace Quickstarts.Views
{
    #region GenericControllerState Class
    #if (!OPCUA_EXCLUDE_GenericControllerState)
    /// <summary>
    /// Stores an instance of the GenericControllerType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GenericControllerState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GenericControllerState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Views.ObjectTypes.GenericControllerType, Quickstarts.Views.Namespaces.Views, namespaceUris);
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
           "AwAAADMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMvRW5naW5lZXJpbmcy" +
           "AAAAaHR0cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL09wZXJhdGlvbnMtAAAAaHR0" +
           "cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL1ZpZXdz/////wRggAABAAAAAwAdAAAA" +
           "R2VuZXJpY0NvbnRyb2xsZXJUeXBlSW5zdGFuY2UBA1kBAQNZAf////8EAAAAFWCJCgIAAAABAAwAAABT" +
           "ZXJpYWxOdW1iZXIBA1oBAC4ARFoBAAAADP////8BAf////8AAAAAFWCJCgIAAAABAAwAAABNYW51ZmFj" +
           "dHVyZXIBA1sBAC4ARFsBAAAADP////8BAf////8AAAAAFWCJCgIAAAACAAgAAABTZXRQb2ludAEDXAEA" +
           "LwEAQAlcAQAAAAv/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEDXwEALgBEXwEAAAEA" +
           "dAP/////AQH/////AAAAABVgiQoCAAAAAgALAAAATWVhc3VyZW1lbnQBA2IBAC8BAEAJYgEAAAAL////" +
           "/wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBA2UBAC4ARGUBAAABAHQD/////wEB/////wAA" +
           "AAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the SerialNumber Property.
        /// </summary>
        public PropertyState<string> SerialNumber
        {
            get
            { 
                return m_serialNumber;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_serialNumber, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serialNumber = value;
            }
        }

        /// <summary>
        /// A description for the Manufacturer Property.
        /// </summary>
        public PropertyState<string> Manufacturer
        {
            get
            { 
                return m_manufacturer;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_manufacturer, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_manufacturer = value;
            }
        }

        /// <summary>
        /// A description for the SetPoint Variable.
        /// </summary>
        public AnalogItemState<double> SetPoint
        {
            get
            { 
                return m_setPoint;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_setPoint, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_setPoint = value;
            }
        }

        /// <summary>
        /// A description for the Measurement Variable.
        /// </summary>
        public AnalogItemState<double> Measurement
        {
            get
            { 
                return m_measurement;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_measurement, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_measurement = value;
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
            if (m_serialNumber != null)
            {
                children.Add(m_serialNumber);
            }

            if (m_manufacturer != null)
            {
                children.Add(m_manufacturer);
            }

            if (m_setPoint != null)
            {
                children.Add(m_setPoint);
            }

            if (m_measurement != null)
            {
                children.Add(m_measurement);
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
                case Quickstarts.Engineering.BrowseNames.SerialNumber:
                {
                    if (createOrReplace)
                    {
                        if (SerialNumber == null)
                        {
                            if (replacement == null)
                            {
                                SerialNumber = new PropertyState<string>(this);
                            }
                            else
                            {
                                SerialNumber = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = SerialNumber;
                    break;
                }

                case Quickstarts.Engineering.BrowseNames.Manufacturer:
                {
                    if (createOrReplace)
                    {
                        if (Manufacturer == null)
                        {
                            if (replacement == null)
                            {
                                Manufacturer = new PropertyState<string>(this);
                            }
                            else
                            {
                                Manufacturer = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = Manufacturer;
                    break;
                }

                case Quickstarts.Operations.BrowseNames.SetPoint:
                {
                    if (createOrReplace)
                    {
                        if (SetPoint == null)
                        {
                            if (replacement == null)
                            {
                                SetPoint = new AnalogItemState<double>(this);
                            }
                            else
                            {
                                SetPoint = (AnalogItemState<double>)replacement;
                            }
                        }
                    }

                    instance = SetPoint;
                    break;
                }

                case Quickstarts.Operations.BrowseNames.Measurement:
                {
                    if (createOrReplace)
                    {
                        if (Measurement == null)
                        {
                            if (replacement == null)
                            {
                                Measurement = new AnalogItemState<double>(this);
                            }
                            else
                            {
                                Measurement = (AnalogItemState<double>)replacement;
                            }
                        }
                    }

                    instance = Measurement;
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
        private PropertyState<string> m_serialNumber;
        private PropertyState<string> m_manufacturer;
        private AnalogItemState<double> m_setPoint;
        private AnalogItemState<double> m_measurement;
        #endregion
    }
    #endif
    #endregion

    #region FlowControllerState Class
    #if (!OPCUA_EXCLUDE_FlowControllerState)
    /// <summary>
    /// Stores an instance of the FlowControllerType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class FlowControllerState : GenericControllerState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public FlowControllerState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Views.ObjectTypes.FlowControllerType, Quickstarts.Views.Namespaces.Views, namespaceUris);
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
           "AwAAADMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMvRW5naW5lZXJpbmcy" +
           "AAAAaHR0cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL09wZXJhdGlvbnMtAAAAaHR0" +
           "cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL1ZpZXdz/////wRggAABAAAAAwAaAAAA" +
           "Rmxvd0NvbnRyb2xsZXJUeXBlSW5zdGFuY2UBA2gBAQNoAf////8EAAAAFWCJCgIAAAABAAwAAABTZXJp" +
           "YWxOdW1iZXIBA2kBAC4ARGkBAAAADP////8BAf////8AAAAAFWCJCgIAAAABAAwAAABNYW51ZmFjdHVy" +
           "ZXIBA2oBAC4ARGoBAAAADP////8BAf////8AAAAAFWCJCgIAAAACAAgAAABTZXRQb2ludAEDawEALwEA" +
           "QAlrAQAAAAv/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEDbgEALgBEbgEAAAEAdAP/" +
           "////AQH/////AAAAABVgiQoCAAAAAgALAAAATWVhc3VyZW1lbnQBA3EBAC8BAEAJcQEAAAAL/////wEB" +
           "/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBA3QBAC4ARHQBAAABAHQD/////wEB/////wAAAAA=";
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

    #region LevelControllerState Class
    #if (!OPCUA_EXCLUDE_LevelControllerState)
    /// <summary>
    /// Stores an instance of the LevelControllerType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class LevelControllerState : GenericControllerState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public LevelControllerState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Views.ObjectTypes.LevelControllerType, Quickstarts.Views.Namespaces.Views, namespaceUris);
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
           "AwAAADMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMvRW5naW5lZXJpbmcy" +
           "AAAAaHR0cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL09wZXJhdGlvbnMtAAAAaHR0" +
           "cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL1ZpZXdz/////wRggAABAAAAAwAbAAAA" +
           "TGV2ZWxDb250cm9sbGVyVHlwZUluc3RhbmNlAQN3AQEDdwH/////BAAAABVgiQoCAAAAAQAMAAAAU2Vy" +
           "aWFsTnVtYmVyAQN4AQAuAER4AQAAAAz/////AQH/////AAAAABVgiQoCAAAAAQAMAAAATWFudWZhY3R1" +
           "cmVyAQN5AQAuAER5AQAAAAz/////AQH/////AAAAABVgiQoCAAAAAgAIAAAAU2V0UG9pbnQBA3oBAC8B" +
           "AEAJegEAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBA30BAC4ARH0BAAABAHQD" +
           "/////wEB/////wAAAAAVYIkKAgAAAAIACwAAAE1lYXN1cmVtZW50AQOAAQAvAQBACYABAAAAC/////8B" +
           "Af////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQODAQAuAESDAQAAAQB0A/////8BAf////8AAAAA";
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

    #region BoilerState Class
    #if (!OPCUA_EXCLUDE_BoilerState)
    /// <summary>
    /// Stores an instance of the BoilerType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class BoilerState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public BoilerState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Views.ObjectTypes.BoilerType, Quickstarts.Views.Namespaces.Views, namespaceUris);
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
           "AwAAADMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMvRW5naW5lZXJpbmcy" +
           "AAAAaHR0cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL09wZXJhdGlvbnMtAAAAaHR0" +
           "cDovL29wY2ZvdW5kYXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL1ZpZXdz/////wRggAABAAAAAwASAAAA" +
           "Qm9pbGVyVHlwZUluc3RhbmNlAQOGAQEDhgH/////AwAAAARggAoBAAAAAwAHAAAAV2F0ZXJJbgEDhwEA" +
           "LwA6hwEAAP////8BAAAABGCACgEAAAADAAQAAABGbG93AQOIAQAvAQNoAYgBAAD/////BAAAABVgiQoC" +
           "AAAAAQAMAAAAU2VyaWFsTnVtYmVyAQOJAQAuAESJAQAAAAz/////AQH/////AAAAABVgiQoCAAAAAQAM" +
           "AAAATWFudWZhY3R1cmVyAQOKAQAuAESKAQAAAAz/////AQH/////AAAAABVgiQoCAAAAAgAIAAAAU2V0" +
           "UG9pbnQBA4sBAC8BAEAJiwEAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBA44B" +
           "AC4ARI4BAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAIACwAAAE1lYXN1cmVtZW50AQORAQAvAQBA" +
           "CZEBAAAAC/////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQOUAQAuAESUAQAAAQB0A///" +
           "//8BAf////8AAAAABGCACgEAAAADAAgAAABTdGVhbU91dAEDlwEALwA6lwEAAP////8BAAAABGCACgEA" +
           "AAADAAQAAABGbG93AQOYAQAvAQNoAZgBAAD/////BAAAABVgiQoCAAAAAQAMAAAAU2VyaWFsTnVtYmVy" +
           "AQOZAQAuAESZAQAAAAz/////AQH/////AAAAABVgiQoCAAAAAQAMAAAATWFudWZhY3R1cmVyAQOaAQAu" +
           "AESaAQAAAAz/////AQH/////AAAAABVgiQoCAAAAAgAIAAAAU2V0UG9pbnQBA5sBAC8BAEAJmwEAAAAL" +
           "/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBA54BAC4ARJ4BAAABAHQD/////wEB////" +
           "/wAAAAAVYIkKAgAAAAIACwAAAE1lYXN1cmVtZW50AQOhAQAvAQBACaEBAAAAC/////8BAf////8BAAAA" +
           "FWCJCgIAAAAAAAcAAABFVVJhbmdlAQOkAQAuAESkAQAAAQB0A/////8BAf////8AAAAABGCACgEAAAAD" +
           "AAQAAABEcnVtAQOnAQAvADqnAQAA/////wEAAAAEYIAKAQAAAAMABQAAAExldmVsAQOoAQAvAQN3AagB" +
           "AAD/////BAAAABVgiQoCAAAAAQAMAAAAU2VyaWFsTnVtYmVyAQOpAQAuAESpAQAAAAz/////AQH/////" +
           "AAAAABVgiQoCAAAAAQAMAAAATWFudWZhY3R1cmVyAQOqAQAuAESqAQAAAAz/////AQH/////AAAAABVg" +
           "iQoCAAAAAgAIAAAAU2V0UG9pbnQBA6sBAC8BAEAJqwEAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAA" +
           "BwAAAEVVUmFuZ2UBA64BAC4ARK4BAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAIACwAAAE1lYXN1" +
           "cmVtZW50AQOxAQAvAQBACbEBAAAAC/////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQO0" +
           "AQAuAES0AQAAAQB0A/////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the WaterIn Object.
        /// </summary>
        public BaseObjectState WaterIn
        {
            get
            { 
                return m_waterIn;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_waterIn, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_waterIn = value;
            }
        }

        /// <summary>
        /// A description for the SteamOut Object.
        /// </summary>
        public BaseObjectState SteamOut
        {
            get
            { 
                return m_steamOut;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_steamOut, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_steamOut = value;
            }
        }

        /// <summary>
        /// A description for the Drum Object.
        /// </summary>
        public BaseObjectState Drum
        {
            get
            { 
                return m_drum;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_drum, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_drum = value;
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
            if (m_waterIn != null)
            {
                children.Add(m_waterIn);
            }

            if (m_steamOut != null)
            {
                children.Add(m_steamOut);
            }

            if (m_drum != null)
            {
                children.Add(m_drum);
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
                case Quickstarts.Views.BrowseNames.WaterIn:
                {
                    if (createOrReplace)
                    {
                        if (WaterIn == null)
                        {
                            if (replacement == null)
                            {
                                WaterIn = new BaseObjectState(this);
                            }
                            else
                            {
                                WaterIn = (BaseObjectState)replacement;
                            }
                        }
                    }

                    instance = WaterIn;
                    break;
                }

                case Quickstarts.Views.BrowseNames.SteamOut:
                {
                    if (createOrReplace)
                    {
                        if (SteamOut == null)
                        {
                            if (replacement == null)
                            {
                                SteamOut = new BaseObjectState(this);
                            }
                            else
                            {
                                SteamOut = (BaseObjectState)replacement;
                            }
                        }
                    }

                    instance = SteamOut;
                    break;
                }

                case Quickstarts.Views.BrowseNames.Drum:
                {
                    if (createOrReplace)
                    {
                        if (Drum == null)
                        {
                            if (replacement == null)
                            {
                                Drum = new BaseObjectState(this);
                            }
                            else
                            {
                                Drum = (BaseObjectState)replacement;
                            }
                        }
                    }

                    instance = Drum;
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
        private BaseObjectState m_waterIn;
        private BaseObjectState m_steamOut;
        private BaseObjectState m_drum;
        #endregion
    }
    #endif
    #endregion
}
