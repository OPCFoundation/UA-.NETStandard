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
using System.Collections.Generic;
using System.Text;
using System.Reflection;
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAHQAA" +
           "AEdlbmVyaWNDb250cm9sbGVyVHlwZUluc3RhbmNlAQHSAAEB0gD/////AwAAABVgiQoCAAAAAQALAAAA" +
           "TWVhc3VyZW1lbnQBAdwDAC4ARNwDAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAgAAABTZXRQb2lu" +
           "dAEB3QMALgBE3QMAAAAL/////wMD/////wAAAAAVYIkKAgAAAAEACgAAAENvbnRyb2xPdXQBAd4DAC4A" +
           "RN4DAAAAC/////8BAf////8AAAAA";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAGQAA" +
           "AEdlbmVyaWNTZW5zb3JUeXBlSW5zdGFuY2UBAd8DAQHfA/////8BAAAAFWCJCgIAAAABAAYAAABPdXRw" +
           "dXQBAeADAC8BAEAJ4AMAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAeMDAC4A" +
           "ROMDAAABAHQD/////wEB/////wAAAAA=";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAGwAA" +
           "AEdlbmVyaWNBY3R1YXRvclR5cGVJbnN0YW5jZQEB5gMBAeYD/////wEAAAAVYIkKAgAAAAEABQAAAElu" +
           "cHV0AQHnAwAvAQBACecDAAAAC/////8CAv////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQHqAwAu" +
           "AETqAwAAAQB0A/////8BAf////8AAAAA";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAHAAA" +
           "AEN1c3RvbUNvbnRyb2xsZXJUeXBlSW5zdGFuY2UBAQECAQEBAv////8FAAAAFWCJCgIAAAABAAYAAABJ" +
           "bnB1dDEBAe0DAC4ARO0DAAAAC/////8CAv////8AAAAAFWCJCgIAAAABAAYAAABJbnB1dDIBAe4DAC4A" +
           "RO4DAAAAC/////8CAv////8AAAAAFWCJCgIAAAABAAYAAABJbnB1dDMBAe8DAC4ARO8DAAAAC/////8C" +
           "Av////8AAAAAFWCJCgIAAAABAAoAAABDb250cm9sT3V0AQHwAwAuAETwAwAAAAv/////AQH/////AAAA" +
           "ABVgyQoCAAAADAAAAERlc2NyaXB0aW9uWAEACwAAAERlc2NyaXB0aW9uAQHxAwAuAETxAwAAABX/////" +
           "AQH/////AAAAAA==";
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

        /// <summary>
        /// A description for the Description Property.
        /// </summary>
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAEQAA" +
           "AFZhbHZlVHlwZUluc3RhbmNlAQHyAwEB8gP/////AQAAABVgiQoCAAAAAQAFAAAASW5wdXQBAfMDAC8B" +
           "AEAJ8wMAAAAL/////wIC/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAfYDAC4ARPYDAAABAHQD" +
           "/////wEB/////wAAAAA=";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAGwAA" +
           "AExldmVsQ29udHJvbGxlclR5cGVJbnN0YW5jZQEB+QMBAfkD/////wMAAAAVYIkKAgAAAAEACwAAAE1l" +
           "YXN1cmVtZW50AQH6AwAuAET6AwAAAAv/////AQH/////AAAAABVgiQoCAAAAAQAIAAAAU2V0UG9pbnQB" +
           "AfsDAC4ARPsDAAAAC/////8DA/////8AAAAAFWCJCgIAAAABAAoAAABDb250cm9sT3V0AQH8AwAuAET8" +
           "AwAAAAv/////AQH/////AAAAAA==";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAGgAA" +
           "AEZsb3dDb250cm9sbGVyVHlwZUluc3RhbmNlAQH9AwEB/QP/////AwAAABVgiQoCAAAAAQALAAAATWVh" +
           "c3VyZW1lbnQBAf4DAC4ARP4DAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAgAAABTZXRQb2ludAEB" +
           "/wMALgBE/wMAAAAL/////wMD/////wAAAAAVYIkKAgAAAAEACgAAAENvbnRyb2xPdXQBAQAEAC4ARAAE" +
           "AAAAC/////8BAf////8AAAAA";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAGgAA" +
           "AExldmVsSW5kaWNhdG9yVHlwZUluc3RhbmNlAQEBBAEBAQT/////AQAAABVgiQoCAAAAAQAGAAAAT3V0" +
           "cHV0AQECBAAvAQBACQIEAAAAC/////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQEFBAAu" +
           "AEQFBAAAAQB0A/////8BAf////8AAAAA";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAGwAA" +
           "AEZsb3dUcmFuc21pdHRlclR5cGVJbnN0YW5jZQEBCAQBAQgE/////wEAAAAVYIkKAgAAAAEABgAAAE91" +
           "dHB1dAEBCQQALwEAQAkJBAAAAAv/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBDAQA" +
           "LgBEDAQAAAEAdAP/////AQH/////AAAAAA==";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAHgAA" +
           "AEJvaWxlclN0YXRlTWFjaGluZVR5cGVJbnN0YW5jZQEBDwQBAQ8E/////woAAAAVYIkKAgAAAAAADAAA" +
           "AEN1cnJlbnRTdGF0ZQEBEAQALwEAyAoQBAAAABX/////AQH/////AgAAABVgiQoCAAAAAAACAAAASWQB" +
           "AREEAC4ARBEEAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAYAAABOdW1iZXIBARMEAC4ARBMEAAAA" +
           "B/////8BAf////8AAAAAFWCJCgIAAAAAAA4AAABMYXN0VHJhbnNpdGlvbgEBFQQALwEAzwoVBAAAABX/" +
           "////AQH/////AwAAABVgiQoCAAAAAAACAAAASWQBARYEAC4ARBYEAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAYAAABOdW1iZXIBARgEAC4ARBgEAAAAB/////8BAf////8AAAAAFWCJCgIAAAAAAA4AAABU" +
           "cmFuc2l0aW9uVGltZQEBGQQALgBEGQQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAJAAAARGVs" +
           "ZXRhYmxlAQEbBAAuAEQbBAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAAUmVjeWNsZUNvdW50" +
           "AQEdBAAuAEQdBAAAAAb/////AQH/////AAAAACRhggoEAAAAAAAFAAAAU3RhcnQBAUcEAwAAAABLAAAA" +
           "Q2F1c2VzIHRoZSBQcm9ncmFtIHRvIHRyYW5zaXRpb24gZnJvbSB0aGUgUmVhZHkgc3RhdGUgdG8gdGhl" +
           "IFJ1bm5pbmcgc3RhdGUuAC8BAHoJRwQAAAEBAQAAAAA1AQEBNwQAAAAAJGGCCgQAAAAAAAcAAABTdXNw" +
           "ZW5kAQFIBAMAAAAATwAAAENhdXNlcyB0aGUgUHJvZ3JhbSB0byB0cmFuc2l0aW9uIGZyb20gdGhlIFJ1" +
           "bm5pbmcgc3RhdGUgdG8gdGhlIFN1c3BlbmRlZCBzdGF0ZS4ALwEAewlIBAAAAQEBAAAAADUBAQE9BAAA" +
           "AAAkYYIKBAAAAAAABgAAAFJlc3VtZQEBSQQDAAAAAE8AAABDYXVzZXMgdGhlIFByb2dyYW0gdG8gdHJh" +
           "bnNpdGlvbiBmcm9tIHRoZSBTdXNwZW5kZWQgc3RhdGUgdG8gdGhlIFJ1bm5pbmcgc3RhdGUuAC8BAHwJ" +
           "SQQAAAEBAQAAAAA1AQEBPwQAAAAAJGGCCgQAAAAAAAQAAABIYWx0AQFKBAMAAAAAYAAAAENhdXNlcyB0" +
           "aGUgUHJvZ3JhbSB0byB0cmFuc2l0aW9uIGZyb20gdGhlIFJlYWR5LCBSdW5uaW5nIG9yIFN1c3BlbmRl" +
           "ZCBzdGF0ZSB0byB0aGUgSGFsdGVkIHN0YXRlLgAvAQB9CUoEAAABAQMAAAAANQEBATkEADUBAQFBBAA1" +
           "AQEBRQQAAAAAJGGCCgQAAAAAAAUAAABSZXNldAEBSwQDAAAAAEoAAABDYXVzZXMgdGhlIFByb2dyYW0g" +
           "dG8gdHJhbnNpdGlvbiBmcm9tIHRoZSBIYWx0ZWQgc3RhdGUgdG8gdGhlIFJlYWR5IHN0YXRlLgAvAQB+" +
           "CUsEAAABAQEAAAAANQEBATUEAAAAADVgiQoCAAAAAQAKAAAAVXBkYXRlUmF0ZQEBTAQDAAAAACYAAABU" +
           "aGUgcmF0ZSBhdCB3aGljaCB0aGUgc2ltdWxhdGlvbiBydW5zLgAuAERMBAAAAAf/////AwP/////AAAA" +
           "AA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// The rate at which the simulation runs.
        /// </summary>
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAGwAA" +
           "AEJvaWxlcklucHV0UGlwZVR5cGVJbnN0YW5jZQEBTQQBAU0EAQAAAAAwAAEBTgQCAAAAhGDACgEAAAAQ" +
           "AAAARmxvd1RyYW5zbWl0dGVyMQEABgAAAEZUWDAwMQEBTgQALwEBCAROBAAAAQEAAAAAMAEBAU0EAQAA" +
           "ABVgiQoCAAAAAQAGAAAAT3V0cHV0AQFPBAAvAQBACU8EAAAAC/////8BAf////8BAAAAFWCJCgIAAAAA" +
           "AAcAAABFVVJhbmdlAQFSBAAuAERSBAAAAQB0A/////8BAf////8AAAAAhGDACgEAAAAFAAAAVmFsdmUB" +
           "AAkAAABWYWx2ZVgwMDEBAVUEAC8BAfIDVQQAAAH/////AQAAABVgiQoCAAAAAQAFAAAASW5wdXQBAVYE" +
           "AC8BAEAJVgQAAAAL/////wIC/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAVkEAC4ARFkEAAAB" +
           "AHQD/////wEB/////wAAAAA=";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAFgAA" +
           "AEJvaWxlckRydW1UeXBlSW5zdGFuY2UBAVwEAQFcBAEAAAAAMAABAV0EAQAAAIRgwAoBAAAADgAAAExl" +
           "dmVsSW5kaWNhdG9yAQAGAAAATElYMDAxAQFdBAAvAQEBBF0EAAABAQAAAAAwAQEBXAQBAAAAFWCJCgIA" +
           "AAABAAYAAABPdXRwdXQBAV4EAC8BAEAJXgQAAAAL/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVV" +
           "UmFuZ2UBAWEEAC4ARGEEAAABAHQD/////wEB/////wAAAAA=";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////8EYIAAAQAAAAEAHAAA" +
           "AEJvaWxlck91dHB1dFBpcGVUeXBlSW5zdGFuY2UBAWQEAQFkBAEAAAAAMAABAWUEAQAAAIRgwAoBAAAA" +
           "EAAAAEZsb3dUcmFuc21pdHRlcjIBAAYAAABGVFgwMDIBAWUEAC8BAQgEZQQAAAEBAAAAADABAQFkBAEA" +
           "AAAVYIkKAgAAAAEABgAAAE91dHB1dAEBZgQALwEAQAlmBAAAAAv/////AQH/////AQAAABVgiQoCAAAA" +
           "AAAHAAAARVVSYW5nZQEBaQQALgBEaQQAAAEAdAP/////AQH/////AAAAAA==";
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
           "AQAAACMAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvQm9pbGVyL/////+EYIAAAQAAAAEAEgAA" +
           "AEJvaWxlclR5cGVJbnN0YW5jZQEBbAQBAWwEAQQAAAAAMAABAW0EADAAAQF8BAAwAAEBhAQAJAABAZoE" +
           "BwAAAIRgwAoBAAAACQAAAElucHV0UGlwZQEACAAAAFBpcGVYMDAxAQFtBAAvAQFNBG0EAAABAwAAAAAw" +
           "AQEBbAQAMAABAW4EAQHZAwABAXwEAgAAAIRgwAoBAAAAEAAAAEZsb3dUcmFuc21pdHRlcjEBAAYAAABG" +
           "VFgwMDEBAW4EAC8BAQgEbgQAAAEBAAAAADABAQFtBAEAAAAVYIkKAgAAAAEABgAAAE91dHB1dAEBbwQA" +
           "LwEAQAlvBAAAAAv/////AQECAAAAAQHbAwABAY0EAQHbAwABAZYEAQAAABVgiQoCAAAAAAAHAAAARVVS" +
           "YW5nZQEBcgQALgBEcgQAAAEAdAP/////AQH/////AAAAAIRgwAoBAAAABQAAAFZhbHZlAQAJAAAAVmFs" +
           "dmVYMDAxAQF1BAAvAQHyA3UEAAAB/////wEAAAAVYIkKAgAAAAEABQAAAElucHV0AQF2BAAvAQBACXYE" +
           "AAAAC/////8CAgEAAAABAdsDAQEBjwQBAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQF5BAAuAER5BAAA" +
           "AQB0A/////8BAf////8AAAAAhGDACgEAAAAEAAAARHJ1bQEACAAAAERydW1YMDAxAQF8BAAvAQFcBHwE" +
           "AAABBAAAAAAwAQEBbAQBAdkDAQEBbQQAMAABAX0EAQHaAwABAYQEAQAAAIRgwAoBAAAADgAAAExldmVs" +
           "SW5kaWNhdG9yAQAGAAAATElYMDAxAQF9BAAvAQEBBH0EAAABAQAAAAAwAQEBfAQBAAAAFWCJCgIAAAAB" +
           "AAYAAABPdXRwdXQBAX4EAC8BAEAJfgQAAAAa/////wEBAQAAAAEB2wMAAQGRBAEAAAAVYIkKAgAAAAAA" +
           "BwAAAEVVUmFuZ2UBAYEEAC4ARIEEAAABAHQD/////wEB/////wAAAACEYMAKAQAAAAoAAABPdXRwdXRQ" +
           "aXBlAQAIAAAAUGlwZVgwMDIBAYQEAC8BAWQEhAQAAAEDAAAAADABAQFsBAEB2gMBAQF8BAAwAAEBhQQB" +
           "AAAAhGDACgEAAAAQAAAARmxvd1RyYW5zbWl0dGVyMgEABgAAAEZUWDAwMgEBhQQALwEBCASFBAAAAQEA" +
           "AAAAMAEBAYQEAQAAABVgiQoCAAAAAQAGAAAAT3V0cHV0AQGGBAAvAQBACYYEAAAAC/////8BAQEAAAAB" +
           "AdsDAAEBlwQBAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGJBAAuAESJBAAAAQB0A/////8BAf////8A" +
           "AAAABGDACgEAAAAOAAAARmxvd0NvbnRyb2xsZXIBAAYAAABGQ1gwMDEBAYwEAC8BAf0DjAQAAP////8D" +
           "AAAAFWCJCgIAAAABAAsAAABNZWFzdXJlbWVudAEBjQQALgBEjQQAAAAL/////wEBAQAAAAEB2wMBAQFv" +
           "BAAAAAAVYIkKAgAAAAEACAAAAFNldFBvaW50AQGOBAAuAESOBAAAAAv/////AwMBAAAAAQHbAwEBAZgE" +
           "AAAAABVgiQoCAAAAAQAKAAAAQ29udHJvbE91dAEBjwQALgBEjwQAAAAL/////wEBAQAAAAEB2wMAAQF2" +
           "BAAAAAAEYMAKAQAAAA8AAABMZXZlbENvbnRyb2xsZXIBAAYAAABMQ1gwMDEBAZAEAC8BAfkDkAQAAP//" +
           "//8DAAAAFWCJCgIAAAABAAsAAABNZWFzdXJlbWVudAEBkQQALgBEkQQAAAAL/////wEBAQAAAAEB2wMB" +
           "AQF+BAAAAAAVYIkKAgAAAAEACAAAAFNldFBvaW50AQGSBAAuAESSBAAAAAv/////AwP/////AAAAABVg" +
           "iQoCAAAAAQAKAAAAQ29udHJvbE91dAEBkwQALgBEkwQAAAAL/////wEBAQAAAAEB2wMAAQGVBAAAAAAE" +
           "YMAKAQAAABAAAABDdXN0b21Db250cm9sbGVyAQAGAAAAQ0NYMDAxAQGUBAAvAQEBApQEAAD/////BQAA" +
           "ABVgiQoCAAAAAQAGAAAASW5wdXQxAQGVBAAuAESVBAAAAAv/////AgIBAAAAAQHbAwEBAZMEAAAAABVg" +
           "iQoCAAAAAQAGAAAASW5wdXQyAQGWBAAuAESWBAAAAAv/////AgIBAAAAAQHbAwEBAW8EAAAAABVgiQoC" +
           "AAAAAQAGAAAASW5wdXQzAQGXBAAuAESXBAAAAAv/////AgIBAAAAAQHbAwEBAYYEAAAAABVgiQoCAAAA" +
           "AQAKAAAAQ29udHJvbE91dAEBmAQALgBEmAQAAAAL/////wEBAQAAAAEB2wMAAQGOBAAAAAAVYMkKAgAA" +
           "AAwAAABEZXNjcmlwdGlvblgBAAsAAABEZXNjcmlwdGlvbgEBmQQALgBEmQQAAAAV/////wEB/////wAA" +
           "AACEYIAKAQAAAAEACgAAAFNpbXVsYXRpb24BAZoEAC8BAQ8EmgQAAAEBAAAAACQBAQFsBAoAAAAVYIkK" +
           "AgAAAAAADAAAAEN1cnJlbnRTdGF0ZQEBmwQALwEAyAqbBAAAABX/////AQH/////AgAAABVgiQoCAAAA" +
           "AAACAAAASWQBAZwEAC4ARJwEAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAYAAABOdW1iZXIBAZ4E" +
           "AC4ARJ4EAAAAB/////8BAf////8AAAAAFWCJCgIAAAAAAA4AAABMYXN0VHJhbnNpdGlvbgEBoAQALwEA" +
           "zwqgBAAAABX/////AQH/////AwAAABVgiQoCAAAAAAACAAAASWQBAaEEAC4ARKEEAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAYAAABOdW1iZXIBAaMEAC4ARKMEAAAAB/////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAA4AAABUcmFuc2l0aW9uVGltZQEBpAQALgBEpAQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAA" +
           "AAAJAAAARGVsZXRhYmxlAQGmBAAuAESmBAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAAUmVj" +
           "eWNsZUNvdW50AQGoBAAuAESoBAAAAAb/////AQH/////AAAAACRhggoEAAAAAAAFAAAAU3RhcnQBAdIE" +
           "AwAAAABLAAAAQ2F1c2VzIHRoZSBQcm9ncmFtIHRvIHRyYW5zaXRpb24gZnJvbSB0aGUgUmVhZHkgc3Rh" +
           "dGUgdG8gdGhlIFJ1bm5pbmcgc3RhdGUuAC8BAHoJ0gQAAAEB/////wAAAAAkYYIKBAAAAAAABwAAAFN1" +
           "c3BlbmQBAdMEAwAAAABPAAAAQ2F1c2VzIHRoZSBQcm9ncmFtIHRvIHRyYW5zaXRpb24gZnJvbSB0aGUg" +
           "UnVubmluZyBzdGF0ZSB0byB0aGUgU3VzcGVuZGVkIHN0YXRlLgAvAQB7CdMEAAABAf////8AAAAAJGGC" +
           "CgQAAAAAAAYAAABSZXN1bWUBAdQEAwAAAABPAAAAQ2F1c2VzIHRoZSBQcm9ncmFtIHRvIHRyYW5zaXRp" +
           "b24gZnJvbSB0aGUgU3VzcGVuZGVkIHN0YXRlIHRvIHRoZSBSdW5uaW5nIHN0YXRlLgAvAQB8CdQEAAAB" +
           "Af////8AAAAAJGGCCgQAAAAAAAQAAABIYWx0AQHVBAMAAAAAYAAAAENhdXNlcyB0aGUgUHJvZ3JhbSB0" +
           "byB0cmFuc2l0aW9uIGZyb20gdGhlIFJlYWR5LCBSdW5uaW5nIG9yIFN1c3BlbmRlZCBzdGF0ZSB0byB0" +
           "aGUgSGFsdGVkIHN0YXRlLgAvAQB9CdUEAAABAf////8AAAAAJGGCCgQAAAAAAAUAAABSZXNldAEB1gQD" +
           "AAAAAEoAAABDYXVzZXMgdGhlIFByb2dyYW0gdG8gdHJhbnNpdGlvbiBmcm9tIHRoZSBIYWx0ZWQgc3Rh" +
           "dGUgdG8gdGhlIFJlYWR5IHN0YXRlLgAvAQB+CdYEAAABAf////8AAAAANWCJCgIAAAABAAoAAABVcGRh" +
           "dGVSYXRlAQHXBAMAAAAAJgAAAFRoZSByYXRlIGF0IHdoaWNoIHRoZSBzaW11bGF0aW9uIHJ1bnMuAC4A" +
           "RNcEAAAAB/////8DA/////8AAAAA";
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

        /// <summary>
        /// A description for the Simulation Object.
        /// </summary>
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