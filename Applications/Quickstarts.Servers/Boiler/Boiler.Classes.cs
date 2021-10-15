/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

namespace Boiler
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.GenericControllerType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAHQAA" +
           "AEdlbmVyaWNDb250cm9sbGVyVHlwZUluc3RhbmNlAQHSAAEB0gDSAAAA/////wMAAAAVYIkKAgAAAAEA" +
           "CwAAAE1lYXN1cmVtZW50AQHcAwAuAETcAwAAAAv/////AQH/////AAAAABVgiQoCAAAAAQAIAAAAU2V0" +
           "UG9pbnQBAd0DAC4ARN0DAAAAC/////8DA/////8AAAAAFWCJCgIAAAABAAoAAABDb250cm9sT3V0AQHe" +
           "AwAuAETeAwAAAAv/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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
                case Boiler.BrowseNames.Measurement:
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

                case Boiler.BrowseNames.SetPoint:
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

                case Boiler.BrowseNames.ControlOut:
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.GenericSensorType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAGQAA" +
           "AEdlbmVyaWNTZW5zb3JUeXBlSW5zdGFuY2UBAd8DAQHfA98DAAD/////AQAAABVgiQoCAAAAAQAGAAAA" +
           "T3V0cHV0AQHgAwAvAQBACeADAAAAC/////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQHj" +
           "AwAuAETjAwAAAQB0A/////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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
                case Boiler.BrowseNames.Output:
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.GenericActuatorType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAGwAA" +
           "AEdlbmVyaWNBY3R1YXRvclR5cGVJbnN0YW5jZQEB5gMBAeYD5gMAAP////8BAAAAFWCJCgIAAAABAAUA" +
           "AABJbnB1dAEB5wMALwEAQAnnAwAAAAv/////AgL/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEB" +
           "6gMALgBE6gMAAAEAdAP/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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
                case Boiler.BrowseNames.Input:
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.CustomControllerType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAHAAA" +
           "AEN1c3RvbUNvbnRyb2xsZXJUeXBlSW5zdGFuY2UBAQECAQEBAgECAAD/////BQAAABVgiQoCAAAAAQAG" +
           "AAAASW5wdXQxAQHtAwAuAETtAwAAAAv/////AgL/////AAAAABVgiQoCAAAAAQAGAAAASW5wdXQyAQHu" +
           "AwAuAETuAwAAAAv/////AgL/////AAAAABVgiQoCAAAAAQAGAAAASW5wdXQzAQHvAwAuAETvAwAAAAv/" +
           "////AgL/////AAAAABVgiQoCAAAAAQAKAAAAQ29udHJvbE91dAEB8AMALgBE8AMAAAAL/////wEB////" +
           "/wAAAAAVYMkKAgAAAAwAAABEZXNjcmlwdGlvblgBAAsAAABEZXNjcmlwdGlvbgEB8QMALgBE8QMAAAAV" +
           "/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
        public PropertyState<LocalizedText> DescriptionX
        {
            get
            {
                return m_descriptionX;
            }

            set
            {
                if (!Object.ReferenceEquals(m_descriptionX, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_descriptionX = value;
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

            if (m_descriptionX != null)
            {
                children.Add(m_descriptionX);
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
                case Boiler.BrowseNames.Input1:
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

                case Boiler.BrowseNames.Input2:
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

                case Boiler.BrowseNames.Input3:
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

                case Boiler.BrowseNames.ControlOut:
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

                case Boiler.BrowseNames.DescriptionX:
                {
                    if (createOrReplace)
                    {
                        if (DescriptionX == null)
                        {
                            if (replacement == null)
                            {
                                DescriptionX = new PropertyState<LocalizedText>(this);
                            }
                            else
                            {
                                DescriptionX = (PropertyState<LocalizedText>)replacement;
                            }
                        }
                    }

                    instance = DescriptionX;
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
        private PropertyState<LocalizedText> m_descriptionX;
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.ValveType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAEQAA" +
           "AFZhbHZlVHlwZUluc3RhbmNlAQHyAwEB8gPyAwAA/////wEAAAAVYIkKAgAAAAEABQAAAElucHV0AQHz" +
           "AwAvAQBACfMDAAAAC/////8CAv////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQH2AwAuAET2AwAA" +
           "AQB0A/////8BAf////8AAAAA";
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.LevelControllerType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAGwAA" +
           "AExldmVsQ29udHJvbGxlclR5cGVJbnN0YW5jZQEB+QMBAfkD+QMAAP////8DAAAAFWCJCgIAAAABAAsA" +
           "AABNZWFzdXJlbWVudAEB+gMALgBE+gMAAAAL/////wEB/////wAAAAAVYIkKAgAAAAEACAAAAFNldFBv" +
           "aW50AQH7AwAuAET7AwAAAAv/////AwP/////AAAAABVgiQoCAAAAAQAKAAAAQ29udHJvbE91dAEB/AMA" +
           "LgBE/AMAAAAL/////wEB/////wAAAAA=";
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.FlowControllerType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAGgAA" +
           "AEZsb3dDb250cm9sbGVyVHlwZUluc3RhbmNlAQH9AwEB/QP9AwAA/////wMAAAAVYIkKAgAAAAEACwAA" +
           "AE1lYXN1cmVtZW50AQH+AwAuAET+AwAAAAv/////AQH/////AAAAABVgiQoCAAAAAQAIAAAAU2V0UG9p" +
           "bnQBAf8DAC4ARP8DAAAAC/////8DA/////8AAAAAFWCJCgIAAAABAAoAAABDb250cm9sT3V0AQEABAAu" +
           "AEQABAAAAAv/////AQH/////AAAAAA==";
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.LevelIndicatorType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAGgAA" +
           "AExldmVsSW5kaWNhdG9yVHlwZUluc3RhbmNlAQEBBAEBAQQBBAAA/////wEAAAAVYIkKAgAAAAEABgAA" +
           "AE91dHB1dAEBAgQALwEAQAkCBAAAAAv/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEB" +
           "BQQALgBEBQQAAAEAdAP/////AQH/////AAAAAA==";
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.FlowTransmitterType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAGwAA" +
           "AEZsb3dUcmFuc21pdHRlclR5cGVJbnN0YW5jZQEBCAQBAQgECAQAAP////8BAAAAFWCJCgIAAAABAAYA" +
           "AABPdXRwdXQBAQkEAC8BAEAJCQQAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UB" +
           "AQwEAC4ARAwEAAABAHQD/////wEB/////wAAAAA=";
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

    #region BoilerStateMachineState Class
    #if (!OPCUA_EXCLUDE_BoilerStateMachineState)
    /// <summary>
    /// Stores an instance of the BoilerStateMachineType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class BoilerStateMachineState : ProgramStateMachineState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public BoilerStateMachineState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.BoilerStateMachineType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAHgAA" +
           "AEJvaWxlclN0YXRlTWFjaGluZVR5cGVJbnN0YW5jZQEBDwQBAQ8EDwQAAP////8LAAAAFWCJCgIAAAAA" +
           "AAwAAABDdXJyZW50U3RhdGUBARAEAC8BAMgKEAQAAAAV/////wEB/////wIAAAAVYIkKAgAAAAAAAgAA" +
           "AElkAQERBAAuAEQRBAAAABH/////AQH/////AAAAABVgiQoCAAAAAAAGAAAATnVtYmVyAQETBAAuAEQT" +
           "BAAAAAf/////AQH/////AAAAABVgiQoCAAAAAAAOAAAATGFzdFRyYW5zaXRpb24BARUEAC8BAM8KFQQA" +
           "AAAV/////wEB/////wMAAAAVYIkKAgAAAAAAAgAAAElkAQEWBAAuAEQWBAAAABH/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAGAAAATnVtYmVyAQEYBAAuAEQYBAAAAAf/////AQH/////AAAAABVgiQoCAAAAAAAO" +
           "AAAAVHJhbnNpdGlvblRpbWUBARkEAC4ARBkEAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACQAA" +
           "AERlbGV0YWJsZQEBGwQALgBEGwQAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAACgAAAEF1dG9EZWxl" +
           "dGUBARwEAC4ARBwEAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABSZWN5Y2xlQ291bnQBAR0E" +
           "AC4ARB0EAAAABv////8BAf////8AAAAAJGGCCgQAAAABAAUAAABTdGFydAEBRwQDAAAAAEsAAABDYXVz" +
           "ZXMgdGhlIFByb2dyYW0gdG8gdHJhbnNpdGlvbiBmcm9tIHRoZSBSZWFkeSBzdGF0ZSB0byB0aGUgUnVu" +
           "bmluZyBzdGF0ZS4ALwEAeglHBAAAAQEBAAAAADUBAQE3BAAAAAAkYYIKBAAAAAEABwAAAFN1c3BlbmQB" +
           "AUgEAwAAAABPAAAAQ2F1c2VzIHRoZSBQcm9ncmFtIHRvIHRyYW5zaXRpb24gZnJvbSB0aGUgUnVubmlu" +
           "ZyBzdGF0ZSB0byB0aGUgU3VzcGVuZGVkIHN0YXRlLgAvAQB7CUgEAAABAQEAAAAANQEBAT0EAAAAACRh" +
           "ggoEAAAAAQAGAAAAUmVzdW1lAQFJBAMAAAAATwAAAENhdXNlcyB0aGUgUHJvZ3JhbSB0byB0cmFuc2l0" +
           "aW9uIGZyb20gdGhlIFN1c3BlbmRlZCBzdGF0ZSB0byB0aGUgUnVubmluZyBzdGF0ZS4ALwEAfAlJBAAA" +
           "AQEBAAAAADUBAQE/BAAAAAAkYYIKBAAAAAEABAAAAEhhbHQBAUoEAwAAAABgAAAAQ2F1c2VzIHRoZSBQ" +
           "cm9ncmFtIHRvIHRyYW5zaXRpb24gZnJvbSB0aGUgUmVhZHksIFJ1bm5pbmcgb3IgU3VzcGVuZGVkIHN0" +
           "YXRlIHRvIHRoZSBIYWx0ZWQgc3RhdGUuAC8BAH0JSgQAAAEBAwAAAAA1AQEBOQQANQEBAUEEADUBAQFF" +
           "BAAAAAAkYYIKBAAAAAEABQAAAFJlc2V0AQFLBAMAAAAASgAAAENhdXNlcyB0aGUgUHJvZ3JhbSB0byB0" +
           "cmFuc2l0aW9uIGZyb20gdGhlIEhhbHRlZCBzdGF0ZSB0byB0aGUgUmVhZHkgc3RhdGUuAC8BAH4JSwQA" +
           "AAEBAQAAAAA1AQEBNQQAAAAANWCJCgIAAAABAAoAAABVcGRhdGVSYXRlAQFMBAMAAAAAJgAAAFRoZSBy" +
           "YXRlIGF0IHdoaWNoIHRoZSBzaW11bGF0aW9uIHJ1bnMuAC4AREwEAAAAB/////8DA/////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<uint> UpdateRate
        {
            get
            {
                return m_updateRate;
            }

            set
            {
                if (!Object.ReferenceEquals(m_updateRate, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_updateRate = value;
            }
        }

        /// <remarks />
        public MethodState Start
        {
            get
            {
                return m_startMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_startMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startMethod = value;
            }
        }

        /// <remarks />
        public MethodState Suspend
        {
            get
            {
                return m_suspendMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_suspendMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_suspendMethod = value;
            }
        }

        /// <remarks />
        public MethodState Resume
        {
            get
            {
                return m_resumeMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_resumeMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_resumeMethod = value;
            }
        }

        /// <remarks />
        public MethodState Halt
        {
            get
            {
                return m_haltMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_haltMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_haltMethod = value;
            }
        }

        /// <remarks />
        public MethodState Reset
        {
            get
            {
                return m_resetMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_resetMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_resetMethod = value;
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
            if (m_updateRate != null)
            {
                children.Add(m_updateRate);
            }

            if (m_startMethod != null)
            {
                children.Add(m_startMethod);
            }

            if (m_suspendMethod != null)
            {
                children.Add(m_suspendMethod);
            }

            if (m_resumeMethod != null)
            {
                children.Add(m_resumeMethod);
            }

            if (m_haltMethod != null)
            {
                children.Add(m_haltMethod);
            }

            if (m_resetMethod != null)
            {
                children.Add(m_resetMethod);
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
                case Boiler.BrowseNames.UpdateRate:
                {
                    if (createOrReplace)
                    {
                        if (UpdateRate == null)
                        {
                            if (replacement == null)
                            {
                                UpdateRate = new PropertyState<uint>(this);
                            }
                            else
                            {
                                UpdateRate = (PropertyState<uint>)replacement;
                            }
                        }
                    }

                    instance = UpdateRate;
                    break;
                }

                case Boiler.BrowseNames.Start:
                {
                    if (createOrReplace)
                    {
                        if (Start == null)
                        {
                            if (replacement == null)
                            {
                                Start = new MethodState(this);
                            }
                            else
                            {
                                Start = (MethodState)replacement;
                            }
                        }
                    }

                    instance = Start;
                    break;
                }

                case Boiler.BrowseNames.Suspend:
                {
                    if (createOrReplace)
                    {
                        if (Suspend == null)
                        {
                            if (replacement == null)
                            {
                                Suspend = new MethodState(this);
                            }
                            else
                            {
                                Suspend = (MethodState)replacement;
                            }
                        }
                    }

                    instance = Suspend;
                    break;
                }

                case Boiler.BrowseNames.Resume:
                {
                    if (createOrReplace)
                    {
                        if (Resume == null)
                        {
                            if (replacement == null)
                            {
                                Resume = new MethodState(this);
                            }
                            else
                            {
                                Resume = (MethodState)replacement;
                            }
                        }
                    }

                    instance = Resume;
                    break;
                }

                case Boiler.BrowseNames.Halt:
                {
                    if (createOrReplace)
                    {
                        if (Halt == null)
                        {
                            if (replacement == null)
                            {
                                Halt = new MethodState(this);
                            }
                            else
                            {
                                Halt = (MethodState)replacement;
                            }
                        }
                    }

                    instance = Halt;
                    break;
                }

                case Boiler.BrowseNames.Reset:
                {
                    if (createOrReplace)
                    {
                        if (Reset == null)
                        {
                            if (replacement == null)
                            {
                                Reset = new MethodState(this);
                            }
                            else
                            {
                                Reset = (MethodState)replacement;
                            }
                        }
                    }

                    instance = Reset;
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
        private PropertyState<uint> m_updateRate;
        private MethodState m_startMethod;
        private MethodState m_suspendMethod;
        private MethodState m_resumeMethod;
        private MethodState m_haltMethod;
        private MethodState m_resetMethod;
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.BoilerInputPipeType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAGwAA" +
           "AEJvaWxlcklucHV0UGlwZVR5cGVJbnN0YW5jZQEBTQQBAU0ETQQAAAEAAAAAMAABAU4EAgAAAIRgwAoB" +
           "AAAAEAAAAEZsb3dUcmFuc21pdHRlcjEBAAYAAABGVFgwMDEBAU4EAC8BAQgETgQAAAEBAAAAADABAQFN" +
           "BAEAAAAVYIkKAgAAAAEABgAAAE91dHB1dAEBTwQALwEAQAlPBAAAAAv/////AQH/////AQAAABVgiQoC" +
           "AAAAAAAHAAAARVVSYW5nZQEBUgQALgBEUgQAAAEAdAP/////AQH/////AAAAAIRgwAoBAAAABQAAAFZh" +
           "bHZlAQAJAAAAVmFsdmVYMDAxAQFVBAAvAQHyA1UEAAAB/////wEAAAAVYIkKAgAAAAEABQAAAElucHV0" +
           "AQFWBAAvAQBACVYEAAAAC/////8CAv////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQFZBAAuAERZ" +
           "BAAAAQB0A/////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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

        /// <remarks />
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
                case Boiler.BrowseNames.FlowTransmitter1:
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

                case Boiler.BrowseNames.Valve:
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.BoilerDrumType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAFgAA" +
           "AEJvaWxlckRydW1UeXBlSW5zdGFuY2UBAVwEAQFcBFwEAAABAAAAADAAAQFdBAEAAACEYMAKAQAAAA4A" +
           "AABMZXZlbEluZGljYXRvcgEABgAAAExJWDAwMQEBXQQALwEBAQRdBAAAAQEAAAAAMAEBAVwEAQAAABVg" +
           "iQoCAAAAAQAGAAAAT3V0cHV0AQFeBAAvAQBACV4EAAAAC/////8BAf////8BAAAAFWCJCgIAAAAAAAcA" +
           "AABFVVJhbmdlAQFhBAAuAERhBAAAAQB0A/////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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
                case Boiler.BrowseNames.LevelIndicator:
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.BoilerOutputPipeType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIACAQAAAAEAHAAA" +
           "AEJvaWxlck91dHB1dFBpcGVUeXBlSW5zdGFuY2UBAWQEAQFkBGQEAAABAAAAADAAAQFlBAEAAACEYMAK" +
           "AQAAABAAAABGbG93VHJhbnNtaXR0ZXIyAQAGAAAARlRYMDAyAQFlBAAvAQEIBGUEAAABAQAAAAAwAQEB" +
           "ZAQBAAAAFWCJCgIAAAABAAYAAABPdXRwdXQBAWYEAC8BAEAJZgQAAAAL/////wEB/////wEAAAAVYIkK" +
           "AgAAAAAABwAAAEVVUmFuZ2UBAWkEAC4ARGkEAAABAHQD/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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
                case Boiler.BrowseNames.FlowTransmitter2:
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
            return Opc.Ua.NodeId.Create(Boiler.ObjectTypes.BoilerType, Boiler.Namespaces.Boiler, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////+EYIACAQAAAAEAEgAA" +
           "AEJvaWxlclR5cGVJbnN0YW5jZQEBbAQBAWwEbAQAAAEEAAAAADAAAQFtBAAwAAEBfAQAMAABAYQEACQA" +
           "AQGaBAcAAACEYMAKAQAAAAkAAABJbnB1dFBpcGUBAAgAAABQaXBlWDAwMQEBbQQALwEBTQRtBAAAAQMA" +
           "AAAAMAEBAWwEADAAAQFuBAEB2QMAAQF8BAIAAACEYMAKAQAAABAAAABGbG93VHJhbnNtaXR0ZXIxAQAG" +
           "AAAARlRYMDAxAQFuBAAvAQEIBG4EAAABAQAAAAAwAQEBbQQBAAAAFWCJCgIAAAABAAYAAABPdXRwdXQB" +
           "AW8EAC8BAEAJbwQAAAAL/////wEBAgAAAAEB2wMAAQGNBAEB2wMAAQGWBAEAAAAVYIkKAgAAAAAABwAA" +
           "AEVVUmFuZ2UBAXIEAC4ARHIEAAABAHQD/////wEB/////wAAAACEYMAKAQAAAAUAAABWYWx2ZQEACQAA" +
           "AFZhbHZlWDAwMQEBdQQALwEB8gN1BAAAAf////8BAAAAFWCJCgIAAAABAAUAAABJbnB1dAEBdgQALwEA" +
           "QAl2BAAAAAv/////AgIBAAAAAQHbAwEBAY8EAQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBeQQALgBE" +
           "eQQAAAEAdAP/////AQH/////AAAAAIRgwAoBAAAABAAAAERydW0BAAgAAABEcnVtWDAwMQEBfAQALwEB" +
           "XAR8BAAAAQQAAAAAMAEBAWwEAQHZAwEBAW0EADAAAQF9BAEB2gMAAQGEBAEAAACEYMAKAQAAAA4AAABM" +
           "ZXZlbEluZGljYXRvcgEABgAAAExJWDAwMQEBfQQALwEBAQR9BAAAAQEAAAAAMAEBAXwEAQAAABVgiQoC" +
           "AAAAAQAGAAAAT3V0cHV0AQF+BAAvAQBACX4EAAAAGv////8BAQEAAAABAdsDAAEBkQQBAAAAFWCJCgIA" +
           "AAAAAAcAAABFVVJhbmdlAQGBBAAuAESBBAAAAQB0A/////8BAf////8AAAAAhGDACgEAAAAKAAAAT3V0" +
           "cHV0UGlwZQEACAAAAFBpcGVYMDAyAQGEBAAvAQFkBIQEAAABAwAAAAAwAQEBbAQBAdoDAQEBfAQAMAAB" +
           "AYUEAQAAAIRgwAoBAAAAEAAAAEZsb3dUcmFuc21pdHRlcjIBAAYAAABGVFgwMDIBAYUEAC8BAQgEhQQA" +
           "AAEBAAAAADABAQGEBAEAAAAVYIkKAgAAAAEABgAAAE91dHB1dAEBhgQALwEAQAmGBAAAAAv/////AQEB" +
           "AAAAAQHbAwABAZcEAQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBiQQALgBEiQQAAAEAdAP/////AQH/" +
           "////AAAAAARgwAoBAAAADgAAAEZsb3dDb250cm9sbGVyAQAGAAAARkNYMDAxAQGMBAAvAQH9A4wEAAD/" +
           "////AwAAABVgiQoCAAAAAQALAAAATWVhc3VyZW1lbnQBAY0EAC4ARI0EAAAAC/////8BAQEAAAABAdsD" +
           "AQEBbwQAAAAAFWCJCgIAAAABAAgAAABTZXRQb2ludAEBjgQALgBEjgQAAAAL/////wMDAQAAAAEB2wMB" +
           "AQGYBAAAAAAVYIkKAgAAAAEACgAAAENvbnRyb2xPdXQBAY8EAC4ARI8EAAAAC/////8BAQEAAAABAdsD" +
           "AAEBdgQAAAAABGDACgEAAAAPAAAATGV2ZWxDb250cm9sbGVyAQAGAAAATENYMDAxAQGQBAAvAQH5A5AE" +
           "AAD/////AwAAABVgiQoCAAAAAQALAAAATWVhc3VyZW1lbnQBAZEEAC4ARJEEAAAAC/////8BAQEAAAAB" +
           "AdsDAQEBfgQAAAAAFWCJCgIAAAABAAgAAABTZXRQb2ludAEBkgQALgBEkgQAAAAL/////wMD/////wAA" +
           "AAAVYIkKAgAAAAEACgAAAENvbnRyb2xPdXQBAZMEAC4ARJMEAAAAC/////8BAQEAAAABAdsDAAEBlQQA" +
           "AAAABGDACgEAAAAQAAAAQ3VzdG9tQ29udHJvbGxlcgEABgAAAENDWDAwMQEBlAQALwEBAQKUBAAA////" +
           "/wUAAAAVYIkKAgAAAAEABgAAAElucHV0MQEBlQQALgBElQQAAAAL/////wICAQAAAAEB2wMBAQGTBAAA" +
           "AAAVYIkKAgAAAAEABgAAAElucHV0MgEBlgQALgBElgQAAAAL/////wICAQAAAAEB2wMBAQFvBAAAAAAV" +
           "YIkKAgAAAAEABgAAAElucHV0MwEBlwQALgBElwQAAAAL/////wICAQAAAAEB2wMBAQGGBAAAAAAVYIkK" +
           "AgAAAAEACgAAAENvbnRyb2xPdXQBAZgEAC4ARJgEAAAAC/////8BAQEAAAABAdsDAAEBjgQAAAAAFWDJ" +
           "CgIAAAAMAAAARGVzY3JpcHRpb25YAQALAAAARGVzY3JpcHRpb24BAZkEAC4ARJkEAAAAFf////8BAf//" +
           "//8AAAAAhGCACgEAAAABAAoAAABTaW11bGF0aW9uAQGaBAAvAQEPBJoEAAABAQAAAAAkAQEBbAQLAAAA" +
           "FWCJCgIAAAAAAAwAAABDdXJyZW50U3RhdGUBAZsEAC8BAMgKmwQAAAAV/////wEB/////wIAAAAVYIkK" +
           "AgAAAAAAAgAAAElkAQGcBAAuAEScBAAAABH/////AQH/////AAAAABVgiQoCAAAAAAAGAAAATnVtYmVy" +
           "AQGeBAAuAESeBAAAAAf/////AQH/////AAAAABVgiQoCAAAAAAAOAAAATGFzdFRyYW5zaXRpb24BAaAE" +
           "AC8BAM8KoAQAAAAV/////wEB/////wMAAAAVYIkKAgAAAAAAAgAAAElkAQGhBAAuAEShBAAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAAGAAAATnVtYmVyAQGjBAAuAESjBAAAAAf/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAOAAAAVHJhbnNpdGlvblRpbWUBAaQEAC4ARKQEAAABACYB/////wEB/////wAAAAAVYIkK" +
           "AgAAAAAACQAAAERlbGV0YWJsZQEBpgQALgBEpgQAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAACgAA" +
           "AEF1dG9EZWxldGUBAacEAC4ARKcEAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABSZWN5Y2xl" +
           "Q291bnQBAagEAC4ARKgEAAAABv////8BAf////8AAAAANWCJCgIAAAABAAoAAABVcGRhdGVSYXRlAQHX" +
           "BAMAAAAAJgAAAFRoZSByYXRlIGF0IHdoaWNoIHRoZSBzaW11bGF0aW9uIHJ1bnMuAC4ARNcEAAAAB///" +
           "//8DA/////8AAAAAJGGCCgQAAAABAAUAAABTdGFydAEBpToDAAAAAEsAAABDYXVzZXMgdGhlIFByb2dy" +
           "YW0gdG8gdHJhbnNpdGlvbiBmcm9tIHRoZSBSZWFkeSBzdGF0ZSB0byB0aGUgUnVubmluZyBzdGF0ZS4A" +
           "LwEBRwSlOgAAAQH/////AAAAACRhggoEAAAAAQAHAAAAU3VzcGVuZAEBpjoDAAAAAE8AAABDYXVzZXMg" +
           "dGhlIFByb2dyYW0gdG8gdHJhbnNpdGlvbiBmcm9tIHRoZSBSdW5uaW5nIHN0YXRlIHRvIHRoZSBTdXNw" +
           "ZW5kZWQgc3RhdGUuAC8BAUgEpjoAAAEB/////wAAAAAkYYIKBAAAAAEABgAAAFJlc3VtZQEBpzoDAAAA" +
           "AE8AAABDYXVzZXMgdGhlIFByb2dyYW0gdG8gdHJhbnNpdGlvbiBmcm9tIHRoZSBTdXNwZW5kZWQgc3Rh" +
           "dGUgdG8gdGhlIFJ1bm5pbmcgc3RhdGUuAC8BAUkEpzoAAAEB/////wAAAAAkYYIKBAAAAAEABAAAAEhh" +
           "bHQBAag6AwAAAABgAAAAQ2F1c2VzIHRoZSBQcm9ncmFtIHRvIHRyYW5zaXRpb24gZnJvbSB0aGUgUmVh" +
           "ZHksIFJ1bm5pbmcgb3IgU3VzcGVuZGVkIHN0YXRlIHRvIHRoZSBIYWx0ZWQgc3RhdGUuAC8BAUoEqDoA" +
           "AAEB/////wAAAAAkYYIKBAAAAAEABQAAAFJlc2V0AQGpOgMAAAAASgAAAENhdXNlcyB0aGUgUHJvZ3Jh" +
           "bSB0byB0cmFuc2l0aW9uIGZyb20gdGhlIEhhbHRlZCBzdGF0ZSB0byB0aGUgUmVhZHkgc3RhdGUuAC8B" +
           "AUsEqToAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
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

        /// <remarks />
        public BoilerStateMachineState Simulation
        {
            get
            {
                return m_simulation;
            }

            set
            {
                if (!Object.ReferenceEquals(m_simulation, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_simulation = value;
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

            if (m_simulation != null)
            {
                children.Add(m_simulation);
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
                case Boiler.BrowseNames.InputPipe:
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

                case Boiler.BrowseNames.Drum:
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

                case Boiler.BrowseNames.OutputPipe:
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

                case Boiler.BrowseNames.FlowController:
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

                case Boiler.BrowseNames.LevelController:
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

                case Boiler.BrowseNames.CustomController:
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

                case Boiler.BrowseNames.Simulation:
                {
                    if (createOrReplace)
                    {
                        if (Simulation == null)
                        {
                            if (replacement == null)
                            {
                                Simulation = new BoilerStateMachineState(this);
                            }
                            else
                            {
                                Simulation = (BoilerStateMachineState)replacement;
                            }
                        }
                    }

                    instance = Simulation;
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
        private BoilerStateMachineState m_simulation;
        #endregion
    }
    #endif
    #endregion
}