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

namespace Quickstarts.Boiler
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
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.GenericControllerType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAdAAAAR2VuZXJpY0NvbnRyb2xsZXJUeXBlSW5zdGFuY2UBAQMAAwAAAAAYAAAAQSBnZW5lcmlj" +
           "IFBJRCBjb250cm9sbGVyAQEDAP////8DAAAAFWCJCgIAAAABAAsAAABNZWFzdXJlbWVudAEBBAAALgBE" +
           "BAAAAAAL/////wEB/////wAAAAAVYIkKAgAAAAEACAAAAFNldFBvaW50AQEFAAAuAEQFAAAAAAv/////" +
           "AwP/////AAAAABVgiQoCAAAAAQAKAAAAQ29udHJvbE91dAEBBgAALgBEBgAAAAAL/////wEB/////wAA" +
           "AAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the Measurement Property.
        /// </summary>
        public PropertyState<double> Measurement
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

        /// <summary>
        /// A description for the SetPoint Property.
        /// </summary>
        public PropertyState<double> SetPoint
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
        /// A description for the ControlOut Property.
        /// </summary>
        public PropertyState<double> ControlOut
        {
            get
            { 
                return m_controlOut;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_controlOut, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_controlOut = value;
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
            if (m_measurement != null)
            {
                children.Add(m_measurement);
            }

            if (m_setPoint != null)
            {
                children.Add(m_setPoint);
            }

            if (m_controlOut != null)
            {
                children.Add(m_controlOut);
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
                case Quickstarts.Boiler.BrowseNames.Measurement:
                {
                    if (createOrReplace)
                    {
                        if (Measurement == null)
                        {
                            if (replacement == null)
                            {
                                Measurement = new PropertyState<double>(this);
                            }
                            else
                            {
                                Measurement = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = Measurement;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.SetPoint:
                {
                    if (createOrReplace)
                    {
                        if (SetPoint == null)
                        {
                            if (replacement == null)
                            {
                                SetPoint = new PropertyState<double>(this);
                            }
                            else
                            {
                                SetPoint = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = SetPoint;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.ControlOut:
                {
                    if (createOrReplace)
                    {
                        if (ControlOut == null)
                        {
                            if (replacement == null)
                            {
                                ControlOut = new PropertyState<double>(this);
                            }
                            else
                            {
                                ControlOut = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = ControlOut;
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
        private PropertyState<double> m_measurement;
        private PropertyState<double> m_setPoint;
        private PropertyState<double> m_controlOut;
        #endregion
    }
    #endif
    #endregion

    #region GenericSensorState Class
    #if (!OPCUA_EXCLUDE_GenericSensorState)
    /// <summary>
    /// Stores an instance of the GenericSensorType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GenericSensorState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GenericSensorState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.GenericSensorType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAZAAAAR2VuZXJpY1NlbnNvclR5cGVJbnN0YW5jZQEBBwADAAAAACsAAABBIGdlbmVyaWMgc2Vu" +
           "c29yIHRoYXQgcmVhZCBhIHByb2Nlc3MgdmFsdWUuAQEHAP////8BAAAAFWCJCgIAAAABAAYAAABPdXRw" +
           "dXQBAQgAAC8BAEAJCAAAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAQsAAC4A" +
           "RAsAAAABAHQD/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the Output Variable.
        /// </summary>
        public AnalogItemState<double> Output
        {
            get
            { 
                return m_output;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_output, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_output = value;
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
            if (m_output != null)
            {
                children.Add(m_output);
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
                case Quickstarts.Boiler.BrowseNames.Output:
                {
                    if (createOrReplace)
                    {
                        if (Output == null)
                        {
                            if (replacement == null)
                            {
                                Output = new AnalogItemState<double>(this);
                            }
                            else
                            {
                                Output = (AnalogItemState<double>)replacement;
                            }
                        }
                    }

                    instance = Output;
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
        private AnalogItemState<double> m_output;
        #endregion
    }
    #endif
    #endregion

    #region GenericActuatorState Class
    #if (!OPCUA_EXCLUDE_GenericActuatorState)
    /// <summary>
    /// Stores an instance of the GenericActuatorType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GenericActuatorState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GenericActuatorState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.GenericActuatorType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAbAAAAR2VuZXJpY0FjdHVhdG9yVHlwZUluc3RhbmNlAQEOAAMAAAAAQQAAAFJlcHJlc2VudHMg" +
           "YSBwaWVjZSBvZiBlcXVpcG1lbnQgdGhhdCBjYXVzZXMgc29tZSBhY3Rpb24gdG8gb2NjdXIuAQEOAP//" +
           "//8BAAAAFWCJCgIAAAABAAUAAABJbnB1dAEBDwAALwEAQAkPAAAAAAv/////AgL/////AQAAABVgiQoC" +
           "AAAAAAAHAAAARVVSYW5nZQEBEgAALgBEEgAAAAEAdAP/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the Input Variable.
        /// </summary>
        public AnalogItemState<double> Input
        {
            get
            { 
                return m_input;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_input, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_input = value;
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
            if (m_input != null)
            {
                children.Add(m_input);
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
                case Quickstarts.Boiler.BrowseNames.Input:
                {
                    if (createOrReplace)
                    {
                        if (Input == null)
                        {
                            if (replacement == null)
                            {
                                Input = new AnalogItemState<double>(this);
                            }
                            else
                            {
                                Input = (AnalogItemState<double>)replacement;
                            }
                        }
                    }

                    instance = Input;
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
        private AnalogItemState<double> m_input;
        #endregion
    }
    #endif
    #endregion

    #region CustomControllerState Class
    #if (!OPCUA_EXCLUDE_CustomControllerState)
    /// <summary>
    /// Stores an instance of the CustomControllerType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class CustomControllerState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public CustomControllerState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.CustomControllerType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAcAAAAQ3VzdG9tQ29udHJvbGxlclR5cGVJbnN0YW5jZQEBFQADAAAAACUAAABBIGN1c3RvbSBQ" +
           "SUQgY29udHJvbGxlciB3aXRoIDMgaW5wdXRzAQEVAP////8EAAAAFWCJCgIAAAABAAYAAABJbnB1dDEB" +
           "ARYAAC4ARBYAAAAAC/////8CAv////8AAAAAFWCJCgIAAAABAAYAAABJbnB1dDIBARcAAC4ARBcAAAAA" +
           "C/////8CAv////8AAAAAFWCJCgIAAAABAAYAAABJbnB1dDMBARgAAC4ARBgAAAAAC/////8CAv////8A" +
           "AAAAFWCJCgIAAAABAAoAAABDb250cm9sT3V0AQEZAAAuAEQZAAAAAAv/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the Input1 Property.
        /// </summary>
        public PropertyState<double> Input1
        {
            get
            { 
                return m_input1;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_input1, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_input1 = value;
            }
        }

        /// <summary>
        /// A description for the Input2 Property.
        /// </summary>
        public PropertyState<double> Input2
        {
            get
            { 
                return m_input2;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_input2, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_input2 = value;
            }
        }

        /// <summary>
        /// A description for the Input3 Property.
        /// </summary>
        public PropertyState<double> Input3
        {
            get
            { 
                return m_input3;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_input3, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_input3 = value;
            }
        }

        /// <summary>
        /// A description for the ControlOut Property.
        /// </summary>
        public PropertyState<double> ControlOut
        {
            get
            { 
                return m_controlOut;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_controlOut, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_controlOut = value;
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
            if (m_input1 != null)
            {
                children.Add(m_input1);
            }

            if (m_input2 != null)
            {
                children.Add(m_input2);
            }

            if (m_input3 != null)
            {
                children.Add(m_input3);
            }

            if (m_controlOut != null)
            {
                children.Add(m_controlOut);
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
                case Quickstarts.Boiler.BrowseNames.Input1:
                {
                    if (createOrReplace)
                    {
                        if (Input1 == null)
                        {
                            if (replacement == null)
                            {
                                Input1 = new PropertyState<double>(this);
                            }
                            else
                            {
                                Input1 = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = Input1;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.Input2:
                {
                    if (createOrReplace)
                    {
                        if (Input2 == null)
                        {
                            if (replacement == null)
                            {
                                Input2 = new PropertyState<double>(this);
                            }
                            else
                            {
                                Input2 = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = Input2;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.Input3:
                {
                    if (createOrReplace)
                    {
                        if (Input3 == null)
                        {
                            if (replacement == null)
                            {
                                Input3 = new PropertyState<double>(this);
                            }
                            else
                            {
                                Input3 = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = Input3;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.ControlOut:
                {
                    if (createOrReplace)
                    {
                        if (ControlOut == null)
                        {
                            if (replacement == null)
                            {
                                ControlOut = new PropertyState<double>(this);
                            }
                            else
                            {
                                ControlOut = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = ControlOut;
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
        private PropertyState<double> m_input1;
        private PropertyState<double> m_input2;
        private PropertyState<double> m_input3;
        private PropertyState<double> m_controlOut;
        #endregion
    }
    #endif
    #endregion

    #region ValveState Class
    #if (!OPCUA_EXCLUDE_ValveState)
    /// <summary>
    /// Stores an instance of the ValveType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ValveState : GenericActuatorState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ValveState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.ValveType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQARAAAAVmFsdmVUeXBlSW5zdGFuY2UBARoAAwAAAAAyAAAAQW4gYWN0dWF0b3IgdGhhdCBjb250" +
           "cm9scyB0aGUgZmxvdyB0aHJvdWdoIGEgcGlwZS4BARoA/////wEAAAAVYIkKAgAAAAEABQAAAElucHV0" +
           "AQEbAAAvAQBACRsAAAAAC/////8CAv////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQEeAAAuAEQe" +
           "AAAAAQB0A/////8BAf////8AAAAA";
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
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.LevelControllerType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAbAAAATGV2ZWxDb250cm9sbGVyVHlwZUluc3RhbmNlAQEhAAMAAAAAMAAAAEEgY29udHJvbGxl" +
           "ciBmb3IgdGhlIGxldmVsIG9mIGEgZmx1aWQgaW4gYSBkcnVtLgEBIQD/////AwAAABVgiQoCAAAAAQAL" +
           "AAAATWVhc3VyZW1lbnQBASIAAC4ARCIAAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAgAAABTZXRQ" +
           "b2ludAEBIwAALgBEIwAAAAAL/////wMD/////wAAAAAVYIkKAgAAAAEACgAAAENvbnRyb2xPdXQBASQA" +
           "AC4ARCQAAAAAC/////8BAf////8AAAAA";
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
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.FlowControllerType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAaAAAARmxvd0NvbnRyb2xsZXJUeXBlSW5zdGFuY2UBASUAAwAAAAA0AAAAQSBjb250cm9sbGVy" +
           "IGZvciB0aGUgZmxvdyBvZiBhIGZsdWlkIHRocm91Z2ggYSBwaXBlLgEBJQD/////AwAAABVgiQoCAAAA" +
           "AQALAAAATWVhc3VyZW1lbnQBASYAAC4ARCYAAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAgAAABT" +
           "ZXRQb2ludAEBJwAALgBEJwAAAAAL/////wMD/////wAAAAAVYIkKAgAAAAEACgAAAENvbnRyb2xPdXQB" +
           "ASgAAC4ARCgAAAAAC/////8BAf////8AAAAA";
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

    #region LevelIndicatorState Class
    #if (!OPCUA_EXCLUDE_LevelIndicatorState)
    /// <summary>
    /// Stores an instance of the LevelIndicatorType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class LevelIndicatorState : GenericSensorState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public LevelIndicatorState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.LevelIndicatorType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAaAAAATGV2ZWxJbmRpY2F0b3JUeXBlSW5zdGFuY2UBASkAAwAAAAA2AAAAQSBzZW5zb3IgdGhh" +
           "dCByZXBvcnRzIHRoZSBsZXZlbCBvZiBhIGxpcXVpZCBpbiBhIHRhbmsuAQEpAP////8BAAAAFWCJCgIA" +
           "AAABAAYAAABPdXRwdXQBASoAAC8BAEAJKgAAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVV" +
           "UmFuZ2UBAS0AAC4ARC0AAAABAHQD/////wEB/////wAAAAA=";
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

    #region FlowTransmitterState Class
    #if (!OPCUA_EXCLUDE_FlowTransmitterState)
    /// <summary>
    /// Stores an instance of the FlowTransmitterType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class FlowTransmitterState : GenericSensorState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public FlowTransmitterState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.FlowTransmitterType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////yRggAAB" +
           "AAAAAQAbAAAARmxvd1RyYW5zbWl0dGVyVHlwZUluc3RhbmNlAQEwAAMAAAAAOgAAAEEgc2Vuc29yIHRo" +
           "YXQgcmVwb3J0cyB0aGUgZmxvdyBvZiBhIGxpcXVpZCB0aHJvdWdoIGEgcGlwZS4BATAA/////wEAAAAV" +
           "YIkKAgAAAAEABgAAAE91dHB1dAEBMQAALwEAQAkxAAAAAAv/////AQH/////AQAAABVgiQoCAAAAAAAH" +
           "AAAARVVSYW5nZQEBNAAALgBENAAAAAEAdAP/////AQH/////AAAAAA==";
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

    #region BoilerInputPipeState Class
    #if (!OPCUA_EXCLUDE_BoilerInputPipeState)
    /// <summary>
    /// Stores an instance of the BoilerInputPipeType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class BoilerInputPipeState : FolderState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public BoilerInputPipeState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.BoilerInputPipeType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////wRggAAB" +
           "AAAAAQAbAAAAQm9pbGVySW5wdXRQaXBlVHlwZUluc3RhbmNlAQFJAAEBSQABAAAAADAAAQFKAAIAAACE" +
           "YMAKAQAAABAAAABGbG93VHJhbnNtaXR0ZXIxAQAGAAAARlRYMDAxAQFKAAAvAQEwAEoAAAABAQAAAAAw" +
           "AQEBSQABAAAAFWCJCgIAAAABAAYAAABPdXRwdXQBAUsAAC8BAEAJSwAAAAAL/////wEB/////wEAAAAV" +
           "YIkKAgAAAAAABwAAAEVVUmFuZ2UBAU4AAC4ARE4AAAABAHQD/////wEB/////wAAAACEYMAKAQAAAAUA" +
           "AABWYWx2ZQEACQAAAFZhbHZlWDAwMQEBUQAALwEBGgBRAAAAAf////8BAAAAFWCJCgIAAAABAAUAAABJ" +
           "bnB1dAEBUgAALwEAQAlSAAAAAAv/////AgL/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBVQAA" +
           "LgBEVQAAAAEAdAP/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the FTX001 Object.
        /// </summary>
        public FlowTransmitterState FlowTransmitter1
        {
            get
            { 
                return m_flowTransmitter1;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_flowTransmitter1, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_flowTransmitter1 = value;
            }
        }

        /// <summary>
        /// A description for the ValveX001 Object.
        /// </summary>
        public ValveState Valve
        {
            get
            { 
                return m_valve;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_valve, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_valve = value;
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
            if (m_flowTransmitter1 != null)
            {
                children.Add(m_flowTransmitter1);
            }

            if (m_valve != null)
            {
                children.Add(m_valve);
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
                case Quickstarts.Boiler.BrowseNames.FlowTransmitter1:
                {
                    if (createOrReplace)
                    {
                        if (FlowTransmitter1 == null)
                        {
                            if (replacement == null)
                            {
                                FlowTransmitter1 = new FlowTransmitterState(this);
                            }
                            else
                            {
                                FlowTransmitter1 = (FlowTransmitterState)replacement;
                            }
                        }
                    }

                    instance = FlowTransmitter1;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.Valve:
                {
                    if (createOrReplace)
                    {
                        if (Valve == null)
                        {
                            if (replacement == null)
                            {
                                Valve = new ValveState(this);
                            }
                            else
                            {
                                Valve = (ValveState)replacement;
                            }
                        }
                    }

                    instance = Valve;
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
        private FlowTransmitterState m_flowTransmitter1;
        private ValveState m_valve;
        #endregion
    }
    #endif
    #endregion

    #region BoilerDrumState Class
    #if (!OPCUA_EXCLUDE_BoilerDrumState)
    /// <summary>
    /// Stores an instance of the BoilerDrumType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class BoilerDrumState : FolderState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public BoilerDrumState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.BoilerDrumType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////wRggAAB" +
           "AAAAAQAWAAAAQm9pbGVyRHJ1bVR5cGVJbnN0YW5jZQEBWAABAVgAAQAAAAAwAAEBWQABAAAAhGDACgEA" +
           "AAAOAAAATGV2ZWxJbmRpY2F0b3IBAAYAAABMSVgwMDEBAVkAAC8BASkAWQAAAAEBAAAAADABAQFYAAEA" +
           "AAAVYIkKAgAAAAEABgAAAE91dHB1dAEBWgAALwEAQAlaAAAAAAv/////AQH/////AQAAABVgiQoCAAAA" +
           "AAAHAAAARVVSYW5nZQEBXQAALgBEXQAAAAEAdAP/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the LIX001 Object.
        /// </summary>
        public LevelIndicatorState LevelIndicator
        {
            get
            { 
                return m_levelIndicator;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_levelIndicator, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_levelIndicator = value;
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
            if (m_levelIndicator != null)
            {
                children.Add(m_levelIndicator);
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
                case Quickstarts.Boiler.BrowseNames.LevelIndicator:
                {
                    if (createOrReplace)
                    {
                        if (LevelIndicator == null)
                        {
                            if (replacement == null)
                            {
                                LevelIndicator = new LevelIndicatorState(this);
                            }
                            else
                            {
                                LevelIndicator = (LevelIndicatorState)replacement;
                            }
                        }
                    }

                    instance = LevelIndicator;
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
        private LevelIndicatorState m_levelIndicator;
        #endregion
    }
    #endif
    #endregion

    #region BoilerOutputPipeState Class
    #if (!OPCUA_EXCLUDE_BoilerOutputPipeState)
    /// <summary>
    /// Stores an instance of the BoilerOutputPipeType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class BoilerOutputPipeState : FolderState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public BoilerOutputPipeState(NodeState parent) : base(parent)
        {
        }
        
        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.BoilerOutputPipeType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////wRggAAB" +
           "AAAAAQAcAAAAQm9pbGVyT3V0cHV0UGlwZVR5cGVJbnN0YW5jZQEBYAABAWAAAQAAAAAwAAEBYQABAAAA" +
           "hGDACgEAAAAQAAAARmxvd1RyYW5zbWl0dGVyMgEABgAAAEZUWDAwMgEBYQAALwEBMABhAAAAAQEAAAAA" +
           "MAEBAWAAAQAAABVgiQoCAAAAAQAGAAAAT3V0cHV0AQFiAAAvAQBACWIAAAAAC/////8BAf////8BAAAA" +
           "FWCJCgIAAAAAAAcAAABFVVJhbmdlAQFlAAAuAERlAAAAAQB0A/////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the FTX002 Object.
        /// </summary>
        public FlowTransmitterState FlowTransmitter2
        {
            get
            { 
                return m_flowTransmitter2;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_flowTransmitter2, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_flowTransmitter2 = value;
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
            if (m_flowTransmitter2 != null)
            {
                children.Add(m_flowTransmitter2);
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
                case Quickstarts.Boiler.BrowseNames.FlowTransmitter2:
                {
                    if (createOrReplace)
                    {
                        if (FlowTransmitter2 == null)
                        {
                            if (replacement == null)
                            {
                                FlowTransmitter2 = new FlowTransmitterState(this);
                            }
                            else
                            {
                                FlowTransmitter2 = (FlowTransmitterState)replacement;
                            }
                        }
                    }

                    instance = FlowTransmitter2;
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
        private FlowTransmitterState m_flowTransmitter2;
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
            return Opc.Ua.NodeId.Create(Quickstarts.Boiler.ObjectTypes.BoilerType, Quickstarts.Boiler.Namespaces.Boiler, namespaceUris);
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
           "AQAAACsAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvUXVpY2tzdGFydHMvQm9pbGVy/////6RggAAB" +
           "AAAAAQASAAAAQm9pbGVyVHlwZUluc3RhbmNlAQE3AAMAAAAALQAAAEEgYm9pbGVyIHVzZWQgdG8gcHJv" +
           "ZHVjZSBzdGVhbSBmb3IgYSB0dXJiaW5lLgEBNwABAwAAAAAwAAEBOAAAMAABATkAADAAAQFBAAYAAACE" +
           "YMAKAQAAAAkAAABJbnB1dFBpcGUBAAgAAABQaXBlWDAwMQEBOAAALwEBSQA4AAAAAQMAAAAAMAEBATcA" +
           "ADAAAQFoAAEBAQAAAQE5AAIAAACEYMAKAQAAABAAAABGbG93VHJhbnNtaXR0ZXIxAQAGAAAARlRYMDAx" +
           "AQFoAAAvAQEwAGgAAAABAQAAAAAwAQEBOAABAAAAFWCJCgIAAAABAAYAAABPdXRwdXQBAWkAAC8BAEAJ" +
           "aQAAAAAL/////wEBAgAAAAEBAgAAAQF+AAEBAgAAAQGHAAEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UB" +
           "AWwAAC4ARGwAAAABAHQD/////wEB/////wAAAACEYMAKAQAAAAUAAABWYWx2ZQEACQAAAFZhbHZlWDAw" +
           "MQEBbwAALwEBGgBvAAAAAf////8BAAAAFWCJCgIAAAABAAUAAABJbnB1dAEBcAAALwEAQAlwAAAAAAv/" +
           "////AgIBAAAAAQECAAEBAYAAAQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBcwAALgBEcwAAAAEAdAP/" +
           "////AQH/////AAAAAIRgwAoBAAAABAAAAERydW0BAAgAAABEcnVtWDAwMQEBOQAALwEBWAA5AAAAAQQA" +
           "AAAAMAEBATcAAQEBAAEBATgAADAAAQE6AAEBAQAAAQFBAAEAAACEYMAKAQAAAA4AAABMZXZlbEluZGlj" +
           "YXRvcgEABgAAAExJWDAwMQEBOgAALwEBKQA6AAAAAQEAAAAAMAEBATkAAQAAABVgiQoCAAAAAQAGAAAA" +
           "T3V0cHV0AQE7AAAvAQBACTsAAAAAC/////8BAQEAAAABAQIAAAEBggABAAAAFWCJCgIAAAAAAAcAAABF" +
           "VVJhbmdlAQE+AAAuAEQ+AAAAAQB0A/////8BAf////8AAAAAhGDACgEAAAAKAAAAT3V0cHV0UGlwZQEA" +
           "CAAAAFBpcGVYMDAyAQFBAAAvAQFgAEEAAAABAwAAAAAwAQEBNwABAQEAAQEBOQAAMAABAXYAAQAAAIRg" +
           "wAoBAAAAEAAAAEZsb3dUcmFuc21pdHRlcjIBAAYAAABGVFgwMDIBAXYAAC8BATAAdgAAAAEBAAAAADAB" +
           "AQFBAAEAAAAVYIkKAgAAAAEABgAAAE91dHB1dAEBdwAALwEAQAl3AAAAAAv/////AQEBAAAAAQECAAAB" +
           "AYgAAQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBegAALgBEegAAAAEAdAP/////AQH/////AAAAAARg" +
           "wAoBAAAADgAAAEZsb3dDb250cm9sbGVyAQAGAAAARkNYMDAxAQF9AAAvAQElAH0AAAD/////AwAAABVg" +
           "iQoCAAAAAQALAAAATWVhc3VyZW1lbnQBAX4AAC4ARH4AAAAAC/////8BAQEAAAABAQIAAQEBaQAAAAAA" +
           "FWCJCgIAAAABAAgAAABTZXRQb2ludAEBfwAALgBEfwAAAAAL/////wMDAQAAAAEBAgABAQGJAAAAAAAV" +
           "YIkKAgAAAAEACgAAAENvbnRyb2xPdXQBAYAAAC4ARIAAAAAAC/////8BAQEAAAABAQIAAAEBcAAAAAAA" +
           "BGDACgEAAAAPAAAATGV2ZWxDb250cm9sbGVyAQAGAAAATENYMDAxAQGBAAAvAQEhAIEAAAD/////AwAA" +
           "ABVgiQoCAAAAAQALAAAATWVhc3VyZW1lbnQBAYIAAC4ARIIAAAAAC/////8BAQEAAAABAQIAAQEBOwAA" +
           "AAAAFWCJCgIAAAABAAgAAABTZXRQb2ludAEBgwAALgBEgwAAAAAL/////wMD/////wAAAAAVYIkKAgAA" +
           "AAEACgAAAENvbnRyb2xPdXQBAYQAAC4ARIQAAAAAC/////8BAQEAAAABAQIAAAEBhgAAAAAABGDACgEA" +
           "AAAQAAAAQ3VzdG9tQ29udHJvbGxlcgEABgAAAENDWDAwMQEBhQAALwEBFQCFAAAA/////wQAAAAVYIkK" +
           "AgAAAAEABgAAAElucHV0MQEBhgAALgBEhgAAAAAL/////wICAQAAAAEBAgABAQGEAAAAAAAVYIkKAgAA" +
           "AAEABgAAAElucHV0MgEBhwAALgBEhwAAAAAL/////wICAQAAAAEBAgABAQFpAAAAAAAVYIkKAgAAAAEA" +
           "BgAAAElucHV0MwEBiAAALgBEiAAAAAAL/////wICAQAAAAEBAgABAQF3AAAAAAAVYIkKAgAAAAEACgAA" +
           "AENvbnRyb2xPdXQBAYkAAC4ARIkAAAAAC/////8BAQEAAAABAQIAAAEBfwAAAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the PipeX001 Object.
        /// </summary>
        public BoilerInputPipeState InputPipe
        {
            get
            { 
                return m_inputPipe;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_inputPipe, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_inputPipe = value;
            }
        }

        /// <summary>
        /// A description for the DrumX001 Object.
        /// </summary>
        public BoilerDrumState Drum
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

        /// <summary>
        /// A description for the PipeX002 Object.
        /// </summary>
        public BoilerOutputPipeState OutputPipe
        {
            get
            { 
                return m_outputPipe;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_outputPipe, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_outputPipe = value;
            }
        }

        /// <summary>
        /// A description for the FCX001 Object.
        /// </summary>
        public FlowControllerState FlowController
        {
            get
            { 
                return m_flowController;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_flowController, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_flowController = value;
            }
        }

        /// <summary>
        /// A description for the LCX001 Object.
        /// </summary>
        public LevelControllerState LevelController
        {
            get
            { 
                return m_levelController;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_levelController, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_levelController = value;
            }
        }

        /// <summary>
        /// A description for the CCX001 Object.
        /// </summary>
        public CustomControllerState CustomController
        {
            get
            { 
                return m_customController;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_customController, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_customController = value;
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
            if (m_inputPipe != null)
            {
                children.Add(m_inputPipe);
            }

            if (m_drum != null)
            {
                children.Add(m_drum);
            }

            if (m_outputPipe != null)
            {
                children.Add(m_outputPipe);
            }

            if (m_flowController != null)
            {
                children.Add(m_flowController);
            }

            if (m_levelController != null)
            {
                children.Add(m_levelController);
            }

            if (m_customController != null)
            {
                children.Add(m_customController);
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
                case Quickstarts.Boiler.BrowseNames.InputPipe:
                {
                    if (createOrReplace)
                    {
                        if (InputPipe == null)
                        {
                            if (replacement == null)
                            {
                                InputPipe = new BoilerInputPipeState(this);
                            }
                            else
                            {
                                InputPipe = (BoilerInputPipeState)replacement;
                            }
                        }
                    }

                    instance = InputPipe;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.Drum:
                {
                    if (createOrReplace)
                    {
                        if (Drum == null)
                        {
                            if (replacement == null)
                            {
                                Drum = new BoilerDrumState(this);
                            }
                            else
                            {
                                Drum = (BoilerDrumState)replacement;
                            }
                        }
                    }

                    instance = Drum;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.OutputPipe:
                {
                    if (createOrReplace)
                    {
                        if (OutputPipe == null)
                        {
                            if (replacement == null)
                            {
                                OutputPipe = new BoilerOutputPipeState(this);
                            }
                            else
                            {
                                OutputPipe = (BoilerOutputPipeState)replacement;
                            }
                        }
                    }

                    instance = OutputPipe;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.FlowController:
                {
                    if (createOrReplace)
                    {
                        if (FlowController == null)
                        {
                            if (replacement == null)
                            {
                                FlowController = new FlowControllerState(this);
                            }
                            else
                            {
                                FlowController = (FlowControllerState)replacement;
                            }
                        }
                    }

                    instance = FlowController;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.LevelController:
                {
                    if (createOrReplace)
                    {
                        if (LevelController == null)
                        {
                            if (replacement == null)
                            {
                                LevelController = new LevelControllerState(this);
                            }
                            else
                            {
                                LevelController = (LevelControllerState)replacement;
                            }
                        }
                    }

                    instance = LevelController;
                    break;
                }

                case Quickstarts.Boiler.BrowseNames.CustomController:
                {
                    if (createOrReplace)
                    {
                        if (CustomController == null)
                        {
                            if (replacement == null)
                            {
                                CustomController = new CustomControllerState(this);
                            }
                            else
                            {
                                CustomController = (CustomControllerState)replacement;
                            }
                        }
                    }

                    instance = CustomController;
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
        private BoilerInputPipeState m_inputPipe;
        private BoilerDrumState m_drum;
        private BoilerOutputPipeState m_outputPipe;
        private FlowControllerState m_flowController;
        private LevelControllerState m_levelController;
        private CustomControllerState m_customController;
        #endregion
    }
    #endif
    #endregion
}
