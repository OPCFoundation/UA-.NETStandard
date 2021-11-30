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

namespace TestData
{
    #region GenerateValuesMethodState Class
    #if (!OPCUA_EXCLUDE_GenerateValuesMethodState)
    /// <summary>
    /// Stores an instance of the GenerateValuesMethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GenerateValuesMethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GenerateValuesMethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new GenerateValuesMethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABgAAABHZW5lcmF0ZVZh" +
           "bHVlc01ldGhvZFR5cGUBAZkkAC+ZJAAAAQH/////AQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVu" +
           "dHMBAZokAC4ARJokAACWAQAAAAEAKgEBRgAAAAoAAABJdGVyYXRpb25zAAf/////AAAAAAMAAAAAJQAA" +
           "AFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5lcmF0ZS4BACgBAQAAAAEAAAAAAAAAAQH/////" +
           "AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public GenerateValuesMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            uint iterations = (uint)_inputArguments[0];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    iterations);
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
    public delegate ServiceResult GenerateValuesMethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        uint iterations);
    #endif
    #endregion

    #region GenerateValuesEventState Class
    #if (!OPCUA_EXCLUDE_GenerateValuesEventState)
    /// <summary>
    /// Stores an instance of the GenerateValuesEventType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GenerateValuesEventState : BaseEventState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public GenerateValuesEventState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.GenerateValuesEventType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABAB8AAABHZW5lcmF0ZVZh" +
           "bHVlc0V2ZW50VHlwZUluc3RhbmNlAQGbJJskAAD/////CgAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEB" +
           "nCQALgBEnCQAAAAP/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBnSQALgBEnSQA" +
           "AAAR/////wEB/////wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAZ4kAC4ARJ4kAAAAEf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOYW1lAQGfJAAuAESfJAAAAAz/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAEAAAAVGltZQEBoCQALgBEoCQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAL" +
           "AAAAUmVjZWl2ZVRpbWUBAaEkAC4ARKEkAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1l" +
           "c3NhZ2UBAaMkAC4ARKMkAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBpCQA" +
           "LgBEpCQAAAAF/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEl0ZXJhdGlvbnMBAaUkAC4ARKUkAAAA" +
           "B/////8BAf////8AAAAAFWCJCgIAAAABAA0AAABOZXdWYWx1ZUNvdW50AQGmJAAuAESmJAAAAAf/////" +
           "AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<uint> Iterations
        {
            get
            {
                return m_iterations;
            }

            set
            {
                if (!Object.ReferenceEquals(m_iterations, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_iterations = value;
            }
        }

        /// <remarks />
        public PropertyState<uint> NewValueCount
        {
            get
            {
                return m_newValueCount;
            }

            set
            {
                if (!Object.ReferenceEquals(m_newValueCount, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_newValueCount = value;
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
            if (m_iterations != null)
            {
                children.Add(m_iterations);
            }

            if (m_newValueCount != null)
            {
                children.Add(m_newValueCount);
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
                case TestData.BrowseNames.Iterations:
                {
                    if (createOrReplace)
                    {
                        if (Iterations == null)
                        {
                            if (replacement == null)
                            {
                                Iterations = new PropertyState<uint>(this);
                            }
                            else
                            {
                                Iterations = (PropertyState<uint>)replacement;
                            }
                        }
                    }

                    instance = Iterations;
                    break;
                }

                case TestData.BrowseNames.NewValueCount:
                {
                    if (createOrReplace)
                    {
                        if (NewValueCount == null)
                        {
                            if (replacement == null)
                            {
                                NewValueCount = new PropertyState<uint>(this);
                            }
                            else
                            {
                                NewValueCount = (PropertyState<uint>)replacement;
                            }
                        }
                    }

                    instance = NewValueCount;
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
        private PropertyState<uint> m_iterations;
        private PropertyState<uint> m_newValueCount;
        #endregion
    }
    #endif
    #endregion

    #region TestDataObjectState Class
    #if (!OPCUA_EXCLUDE_TestDataObjectState)
    /// <summary>
    /// Stores an instance of the TestDataObjectType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class TestDataObjectState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public TestDataObjectState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.TestDataObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABABoAAABUZXN0RGF0YU9i" +
           "amVjdFR5cGVJbnN0YW5jZQEBpySnJAAAAQAAAAAkAAEBqyQDAAAANWCJCgIAAAABABAAAABTaW11bGF0" +
           "aW9uQWN0aXZlAQGoJAMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHByb2R1Y2UgbmV3IHZh" +
           "bHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARKgkAAAAAf////8BAf////8AAAAABGEC" +
           "CgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBqSQAL6kkAAABAf////8BAAAAF2CpCgIAAAAAAA4AAABJ" +
           "bnB1dEFyZ3VtZW50cwEBqiQALgBEqiQAAJYBAAAAAQAqAQFGAAAACgAAAEl0ZXJhdGlvbnMAB/////8A" +
           "AAAAAwAAAAAlAAAAVGhlIG51bWJlciBvZiBuZXcgdmFsdWVzIHRvIGdlbmVyYXRlLgEAKAEBAAAAAQAA" +
           "AAAAAAABAf////8AAAAABGCACgEAAAABAA0AAABDeWNsZUNvbXBsZXRlAQGrJAAvAQBBC6skAAABAAAA" +
           "ACQBAQGnJBcAAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBAawkAC4ARKwkAAAAD/////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAa0kAC4ARK0kAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAoAAABTb3VyY2VOb2RlAQGuJAAuAESuJAAAABH/////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291" +
           "cmNlTmFtZQEBryQALgBEryQAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAbAkAC4A" +
           "RLAkAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVUaW1lAQGxJAAuAESxJAAA" +
           "AQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQGzJAAuAESzJAAAABX/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAbQkAC4ARLQkAAAABf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQE6LQAuAEQ6LQAAABH/////AQH/////AAAAABVgiQoC" +
           "AAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQE7LQAuAEQ7LQAAABX/////AQH/////AAAAABVgiQoC" +
           "AAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBJS0ALgBEJS0AAAAM/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "CAAAAEJyYW5jaElkAQG1JAAuAES1JAAAABH/////AQH/////AAAAABVgiQoCAAAAAAAGAAAAUmV0YWlu" +
           "AQG2JAAuAES2JAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAARW5hYmxlZFN0YXRlAQG3JAAv" +
           "AQAjI7ckAAAAFf////8BAQIAAAABACwjAAEBzCQBACwjAAEB1CQBAAAAFWCJCgIAAAAAAAIAAABJZAEB" +
           "uCQALgBEuCQAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxpdHkBAb0kAC8BACojvSQA" +
           "AAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBviQALgBEviQAAAEA" +
           "JgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQHBJAAvAQAqI8EkAAAABf//" +
           "//8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAcIkAC4ARMIkAAABACYB////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAcMkAC8BACojwyQAAAAV/////wEB/////wEA" +
           "AAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBxCQALgBExCQAAAEAJgH/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQHFJAAuAETFJAAAAAz/////AQH/////AAAAAARhggoE" +
           "AAAAAAAHAAAARGlzYWJsZQEBxyQALwEARCPHJAAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoEAAAAAAAG" +
           "AAAARW5hYmxlAQHGJAAvAQBDI8YkAAABAQEAAAABAPkLAAEA8woAAAAABGGCCgQAAAAAAAoAAABBZGRD" +
           "b21tZW50AQHIJAAvAQBFI8gkAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBySQALgBEySQAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD/////8AAAAAAwAAAAAo" +
           "AAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEBQgAAAAcAAABDb21t" +
           "ZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUgY29uZGl0aW9uLgEA" +
           "KAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAAAAAoAAABBY2tlZFN0YXRlAQHMJAAvAQAjI8wk" +
           "AAAAFf////8BAQEAAAABACwjAQEBtyQBAAAAFWCJCgIAAAAAAAIAAABJZAEBzSQALgBEzSQAAAAB////" +
           "/wEB/////wAAAAAEYYIKBAAAAAAACwAAAEFja25vd2xlZGdlAQHcJAAvAQCXI9wkAAABAQEAAAABAPkL" +
           "AAEA8CIBAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEB3SQALgBE3SQAAJYCAAAAAQAqAQFG" +
           "AAAABwAAAEV2ZW50SWQAD/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVu" +
           "dCB0byBjb21tZW50LgEAKgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21t" +
           "ZW50IHRvIGFkZCB0byB0aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<bool> SimulationActive
        {
            get
            {
                return m_simulationActive;
            }

            set
            {
                if (!Object.ReferenceEquals(m_simulationActive, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_simulationActive = value;
            }
        }

        /// <remarks />
        public GenerateValuesMethodState GenerateValues
        {
            get
            {
                return m_generateValuesMethod;
            }

            set
            {
                if (!Object.ReferenceEquals(m_generateValuesMethod, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_generateValuesMethod = value;
            }
        }

        /// <remarks />
        public AcknowledgeableConditionState CycleComplete
        {
            get
            {
                return m_cycleComplete;
            }

            set
            {
                if (!Object.ReferenceEquals(m_cycleComplete, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_cycleComplete = value;
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
            if (m_simulationActive != null)
            {
                children.Add(m_simulationActive);
            }

            if (m_generateValuesMethod != null)
            {
                children.Add(m_generateValuesMethod);
            }

            if (m_cycleComplete != null)
            {
                children.Add(m_cycleComplete);
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
                case TestData.BrowseNames.SimulationActive:
                {
                    if (createOrReplace)
                    {
                        if (SimulationActive == null)
                        {
                            if (replacement == null)
                            {
                                SimulationActive = new PropertyState<bool>(this);
                            }
                            else
                            {
                                SimulationActive = (PropertyState<bool>)replacement;
                            }
                        }
                    }

                    instance = SimulationActive;
                    break;
                }

                case TestData.BrowseNames.GenerateValues:
                {
                    if (createOrReplace)
                    {
                        if (GenerateValues == null)
                        {
                            if (replacement == null)
                            {
                                GenerateValues = new GenerateValuesMethodState(this);
                            }
                            else
                            {
                                GenerateValues = (GenerateValuesMethodState)replacement;
                            }
                        }
                    }

                    instance = GenerateValues;
                    break;
                }

                case TestData.BrowseNames.CycleComplete:
                {
                    if (createOrReplace)
                    {
                        if (CycleComplete == null)
                        {
                            if (replacement == null)
                            {
                                CycleComplete = new AcknowledgeableConditionState(this);
                            }
                            else
                            {
                                CycleComplete = (AcknowledgeableConditionState)replacement;
                            }
                        }
                    }

                    instance = CycleComplete;
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
        private PropertyState<bool> m_simulationActive;
        private GenerateValuesMethodState m_generateValuesMethod;
        private AcknowledgeableConditionState m_cycleComplete;
        #endregion
    }
    #endif
    #endregion

    #region ScalarValue1MethodState Class
    #if (!OPCUA_EXCLUDE_ScalarValue1MethodState)
    /// <summary>
    /// Stores an instance of the ScalarValue1MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValue1MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ScalarValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new ScalarValue1MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABYAAABTY2FsYXJWYWx1" +
           "ZTFNZXRob2RUeXBlAQHhJAAv4SQAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRz" +
           "AQHiJAAuAETiJAAAlgsAAAABACoBARgAAAAJAAAAQm9vbGVhbkluAAH/////AAAAAAABACoBARYAAAAH" +
           "AAAAU0J5dGVJbgAC/////wAAAAAAAQAqAQEVAAAABgAAAEJ5dGVJbgAD/////wAAAAAAAQAqAQEWAAAA" +
           "BwAAAEludDE2SW4ABP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MTZJbgAF/////wAAAAAAAQAqAQEW" +
           "AAAABwAAAEludDMySW4ABv////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MzJJbgAH/////wAAAAAAAQAq" +
           "AQEWAAAABwAAAEludDY0SW4ACP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50NjRJbgAJ/////wAAAAAA" +
           "AQAqAQEWAAAABwAAAEZsb2F0SW4ACv////8AAAAAAAEAKgEBFwAAAAgAAABEb3VibGVJbgAL/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEB" +
           "4yQALgBE4yQAAJYLAAAAAQAqAQEZAAAACgAAAEJvb2xlYW5PdXQAAf////8AAAAAAAEAKgEBFwAAAAgA" +
           "AABTQnl0ZU91dAAC/////wAAAAAAAQAqAQEWAAAABwAAAEJ5dGVPdXQAA/////8AAAAAAAEAKgEBFwAA" +
           "AAgAAABJbnQxNk91dAAE/////wAAAAAAAQAqAQEYAAAACQAAAFVJbnQxNk91dAAF/////wAAAAAAAQAq" +
           "AQEXAAAACAAAAEludDMyT3V0AAb/////AAAAAAABACoBARgAAAAJAAAAVUludDMyT3V0AAf/////AAAA" +
           "AAABACoBARcAAAAIAAAASW50NjRPdXQACP////8AAAAAAAEAKgEBGAAAAAkAAABVSW50NjRPdXQACf//" +
           "//8AAAAAAAEAKgEBFwAAAAgAAABGbG9hdE91dAAK/////wAAAAAAAQAqAQEYAAAACQAAAERvdWJsZU91" +
           "dAAL/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public ScalarValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            bool booleanIn = (bool)_inputArguments[0];
            sbyte sByteIn = (sbyte)_inputArguments[1];
            byte byteIn = (byte)_inputArguments[2];
            short int16In = (short)_inputArguments[3];
            ushort uInt16In = (ushort)_inputArguments[4];
            int int32In = (int)_inputArguments[5];
            uint uInt32In = (uint)_inputArguments[6];
            long int64In = (long)_inputArguments[7];
            ulong uInt64In = (ulong)_inputArguments[8];
            float floatIn = (float)_inputArguments[9];
            double doubleIn = (double)_inputArguments[10];

            bool booleanOut = (bool)_outputArguments[0];
            sbyte sByteOut = (sbyte)_outputArguments[1];
            byte byteOut = (byte)_outputArguments[2];
            short int16Out = (short)_outputArguments[3];
            ushort uInt16Out = (ushort)_outputArguments[4];
            int int32Out = (int)_outputArguments[5];
            uint uInt32Out = (uint)_outputArguments[6];
            long int64Out = (long)_outputArguments[7];
            ulong uInt64Out = (ulong)_outputArguments[8];
            float floatOut = (float)_outputArguments[9];
            double doubleOut = (double)_outputArguments[10];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    booleanIn,
                    sByteIn,
                    byteIn,
                    int16In,
                    uInt16In,
                    int32In,
                    uInt32In,
                    int64In,
                    uInt64In,
                    floatIn,
                    doubleIn,
                    ref booleanOut,
                    ref sByteOut,
                    ref byteOut,
                    ref int16Out,
                    ref uInt16Out,
                    ref int32Out,
                    ref uInt32Out,
                    ref int64Out,
                    ref uInt64Out,
                    ref floatOut,
                    ref doubleOut);
            }

            _outputArguments[0] = booleanOut;
            _outputArguments[1] = sByteOut;
            _outputArguments[2] = byteOut;
            _outputArguments[3] = int16Out;
            _outputArguments[4] = uInt16Out;
            _outputArguments[5] = int32Out;
            _outputArguments[6] = uInt32Out;
            _outputArguments[7] = int64Out;
            _outputArguments[8] = uInt64Out;
            _outputArguments[9] = floatOut;
            _outputArguments[10] = doubleOut;

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
    public delegate ServiceResult ScalarValue1MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        bool booleanIn,
        sbyte sByteIn,
        byte byteIn,
        short int16In,
        ushort uInt16In,
        int int32In,
        uint uInt32In,
        long int64In,
        ulong uInt64In,
        float floatIn,
        double doubleIn,
        ref bool booleanOut,
        ref sbyte sByteOut,
        ref byte byteOut,
        ref short int16Out,
        ref ushort uInt16Out,
        ref int int32Out,
        ref uint uInt32Out,
        ref long int64Out,
        ref ulong uInt64Out,
        ref float floatOut,
        ref double doubleOut);
    #endif
    #endregion

    #region ScalarValue2MethodState Class
    #if (!OPCUA_EXCLUDE_ScalarValue2MethodState)
    /// <summary>
    /// Stores an instance of the ScalarValue2MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValue2MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ScalarValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new ScalarValue2MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABYAAABTY2FsYXJWYWx1" +
           "ZTJNZXRob2RUeXBlAQHkJAAv5CQAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRz" +
           "AQHlJAAuAETlJAAAlgoAAAABACoBARcAAAAIAAAAU3RyaW5nSW4ADP////8AAAAAAAEAKgEBGQAAAAoA" +
           "AABEYXRlVGltZUluAA3/////AAAAAAABACoBARUAAAAGAAAAR3VpZEluAA7/////AAAAAAABACoBARsA" +
           "AAAMAAAAQnl0ZVN0cmluZ0luAA//////AAAAAAABACoBARsAAAAMAAAAWG1sRWxlbWVudEluABD/////" +
           "AAAAAAABACoBARcAAAAIAAAATm9kZUlkSW4AEf////8AAAAAAAEAKgEBHwAAABAAAABFeHBhbmRlZE5v" +
           "ZGVJZEluABL/////AAAAAAABACoBAR4AAAAPAAAAUXVhbGlmaWVkTmFtZUluABT/////AAAAAAABACoB" +
           "AR4AAAAPAAAATG9jYWxpemVkVGV4dEluABX/////AAAAAAABACoBARsAAAAMAAAAU3RhdHVzQ29kZUlu" +
           "ABP/////AAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJn" +
           "dW1lbnRzAQHmJAAuAETmJAAAlgoAAAABACoBARgAAAAJAAAAU3RyaW5nT3V0AAz/////AAAAAAABACoB" +
           "ARoAAAALAAAARGF0ZVRpbWVPdXQADf////8AAAAAAAEAKgEBFgAAAAcAAABHdWlkT3V0AA7/////AAAA" +
           "AAABACoBARwAAAANAAAAQnl0ZVN0cmluZ091dAAP/////wAAAAAAAQAqAQEcAAAADQAAAFhtbEVsZW1l" +
           "bnRPdXQAEP////8AAAAAAAEAKgEBGAAAAAkAAABOb2RlSWRPdXQAEf////8AAAAAAAEAKgEBIAAAABEA" +
           "AABFeHBhbmRlZE5vZGVJZE91dAAS/////wAAAAAAAQAqAQEfAAAAEAAAAFF1YWxpZmllZE5hbWVPdXQA" +
           "FP////8AAAAAAAEAKgEBHwAAABAAAABMb2NhbGl6ZWRUZXh0T3V0ABX/////AAAAAAABACoBARwAAAAN" +
           "AAAAU3RhdHVzQ29kZU91dAAT/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public ScalarValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            string stringIn = (string)_inputArguments[0];
            DateTime dateTimeIn = (DateTime)_inputArguments[1];
            Uuid guidIn = (Uuid)_inputArguments[2];
            byte[] byteStringIn = (byte[])_inputArguments[3];
            XmlElement xmlElementIn = (XmlElement)_inputArguments[4];
            NodeId nodeIdIn = (NodeId)_inputArguments[5];
            ExpandedNodeId expandedNodeIdIn = (ExpandedNodeId)_inputArguments[6];
            QualifiedName qualifiedNameIn = (QualifiedName)_inputArguments[7];
            LocalizedText localizedTextIn = (LocalizedText)_inputArguments[8];
            StatusCode statusCodeIn = (StatusCode)_inputArguments[9];

            string stringOut = (string)_outputArguments[0];
            DateTime dateTimeOut = (DateTime)_outputArguments[1];
            Uuid guidOut = (Uuid)_outputArguments[2];
            byte[] byteStringOut = (byte[])_outputArguments[3];
            XmlElement xmlElementOut = (XmlElement)_outputArguments[4];
            NodeId nodeIdOut = (NodeId)_outputArguments[5];
            ExpandedNodeId expandedNodeIdOut = (ExpandedNodeId)_outputArguments[6];
            QualifiedName qualifiedNameOut = (QualifiedName)_outputArguments[7];
            LocalizedText localizedTextOut = (LocalizedText)_outputArguments[8];
            StatusCode statusCodeOut = (StatusCode)_outputArguments[9];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    stringIn,
                    dateTimeIn,
                    guidIn,
                    byteStringIn,
                    xmlElementIn,
                    nodeIdIn,
                    expandedNodeIdIn,
                    qualifiedNameIn,
                    localizedTextIn,
                    statusCodeIn,
                    ref stringOut,
                    ref dateTimeOut,
                    ref guidOut,
                    ref byteStringOut,
                    ref xmlElementOut,
                    ref nodeIdOut,
                    ref expandedNodeIdOut,
                    ref qualifiedNameOut,
                    ref localizedTextOut,
                    ref statusCodeOut);
            }

            _outputArguments[0] = stringOut;
            _outputArguments[1] = dateTimeOut;
            _outputArguments[2] = guidOut;
            _outputArguments[3] = byteStringOut;
            _outputArguments[4] = xmlElementOut;
            _outputArguments[5] = nodeIdOut;
            _outputArguments[6] = expandedNodeIdOut;
            _outputArguments[7] = qualifiedNameOut;
            _outputArguments[8] = localizedTextOut;
            _outputArguments[9] = statusCodeOut;

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
    public delegate ServiceResult ScalarValue2MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string stringIn,
        DateTime dateTimeIn,
        Uuid guidIn,
        byte[] byteStringIn,
        XmlElement xmlElementIn,
        NodeId nodeIdIn,
        ExpandedNodeId expandedNodeIdIn,
        QualifiedName qualifiedNameIn,
        LocalizedText localizedTextIn,
        StatusCode statusCodeIn,
        ref string stringOut,
        ref DateTime dateTimeOut,
        ref Uuid guidOut,
        ref byte[] byteStringOut,
        ref XmlElement xmlElementOut,
        ref NodeId nodeIdOut,
        ref ExpandedNodeId expandedNodeIdOut,
        ref QualifiedName qualifiedNameOut,
        ref LocalizedText localizedTextOut,
        ref StatusCode statusCodeOut);
    #endif
    #endregion

    #region ScalarValue3MethodState Class
    #if (!OPCUA_EXCLUDE_ScalarValue3MethodState)
    /// <summary>
    /// Stores an instance of the ScalarValue3MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValue3MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ScalarValue3MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new ScalarValue3MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABYAAABTY2FsYXJWYWx1" +
           "ZTNNZXRob2RUeXBlAQHnJAAv5yQAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRz" +
           "AQHoJAAuAEToJAAAlgMAAAABACoBARgAAAAJAAAAVmFyaWFudEluABj/////AAAAAAABACoBARwAAAAN" +
           "AAAARW51bWVyYXRpb25JbgAd/////wAAAAAAAQAqAQEaAAAACwAAAFN0cnVjdHVyZUluABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQHp" +
           "JAAuAETpJAAAlgMAAAABACoBARkAAAAKAAAAVmFyaWFudE91dAAY/////wAAAAAAAQAqAQEdAAAADgAA" +
           "AEVudW1lcmF0aW9uT3V0AB3/////AAAAAAABACoBARsAAAAMAAAAU3RydWN0dXJlT3V0ABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public ScalarValue3MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            object variantIn = (object)_inputArguments[0];
            int enumerationIn = (int)_inputArguments[1];
            ExtensionObject structureIn = (ExtensionObject)_inputArguments[2];

            object variantOut = (object)_outputArguments[0];
            int enumerationOut = (int)_outputArguments[1];
            ExtensionObject structureOut = (ExtensionObject)_outputArguments[2];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    variantIn,
                    enumerationIn,
                    structureIn,
                    ref variantOut,
                    ref enumerationOut,
                    ref structureOut);
            }

            _outputArguments[0] = variantOut;
            _outputArguments[1] = enumerationOut;
            _outputArguments[2] = structureOut;

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
    public delegate ServiceResult ScalarValue3MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        object variantIn,
        int enumerationIn,
        ExtensionObject structureIn,
        ref object variantOut,
        ref int enumerationOut,
        ref ExtensionObject structureOut);
    #endif
    #endregion

    #region ScalarValueObjectState Class
    #if (!OPCUA_EXCLUDE_ScalarValueObjectState)
    /// <summary>
    /// Stores an instance of the ScalarValueObjectType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ScalarValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.ScalarValueObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABAB0AAABTY2FsYXJWYWx1" +
           "ZU9iamVjdFR5cGVJbnN0YW5jZQEB6iTqJAAAAQAAAAAkAAEB7iQeAAAANWCJCgIAAAABABAAAABTaW11" +
           "bGF0aW9uQWN0aXZlAQHrJAMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHByb2R1Y2UgbmV3" +
           "IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4AROskAAAAAf////8BAf////8AAAAA" +
           "BGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEB7CQALwEBqSTsJAAAAQH/////AQAAABdgqQoCAAAA" +
           "AAAOAAAASW5wdXRBcmd1bWVudHMBAe0kAC4ARO0kAACWAQAAAAEAKgEBRgAAAAoAAABJdGVyYXRpb25z" +
           "AAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5lcmF0ZS4BACgB" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEB7iQALwEAQQvu" +
           "JAAAAQAAAAAkAQEB6iQXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQHvJAAuAETvJAAAAA//////AQH/" +
           "////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQHwJAAuAETwJAAAABH/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAKAAAAU291cmNlTm9kZQEB8SQALgBE8SQAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "CgAAAFNvdXJjZU5hbWUBAfIkAC4ARPIkAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAQAAABUaW1l" +
           "AQHzJAAuAETzJAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZlVGltZQEB9CQA" +
           "LgBE9CQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEB9iQALgBE9iQAAAAV" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQH3JAAuAET3JAAAAAX/////AQH/////" +
           "AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBPC0ALgBEPC0AAAAR/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBPS0ALgBEPS0AAAAV/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBASYtAC4ARCYtAAAADP////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAgAAABCcmFuY2hJZAEB+CQALgBE+CQAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAABgAA" +
           "AFJldGFpbgEB+SQALgBE+SQAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVuYWJsZWRTdGF0" +
           "ZQEB+iQALwEAIyP6JAAAABX/////AQECAAAAAQAsIwABAQ8lAQAsIwABARclAQAAABVgiQoCAAAAAAAC" +
           "AAAASWQBAfskAC4ARPskAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFsaXR5AQEAJQAv" +
           "AQAqIwAlAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAQElAC4A" +
           "RAElAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEBBCUALwEAKiME" +
           "JQAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEFJQAuAEQFJQAA" +
           "AQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQEGJQAvAQAqIwYlAAAAFf////8B" +
           "Af////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAQclAC4ARAclAAABACYB/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEBCCUALgBECCUAAAAM/////wEB/////wAA" +
           "AAAEYYIKBAAAAAAABwAAAERpc2FibGUBAQolAC8BAEQjCiUAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIK" +
           "BAAAAAAABgAAAEVuYWJsZQEBCSUALwEAQyMJJQAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoEAAAAAAAK" +
           "AAAAQWRkQ29tbWVudAEBCyUALwEARSMLJQAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoCAAAAAAAOAAAA" +
           "SW5wdXRBcmd1bWVudHMBAQwlAC4ARAwlAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAA" +
           "AAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAH" +
           "AAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRp" +
           "dGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRTdGF0ZQEBDyUA" +
           "LwEAIyMPJQAAABX/////AQEBAAAAAQAsIwEBAfokAQAAABVgiQoCAAAAAAACAAAASWQBARAlAC4ARBAl" +
           "AAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBHyUALwEAlyMfJQAAAQEB" +
           "AAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBASAlAC4ARCAlAACWAgAA" +
           "AAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0" +
           "aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABU" +
           "aGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABVgiQoCAAAAAQAMAAAAQm9vbGVhblZhbHVlAQEjJQAvAD8jJQAAAAH/////AQH/////AAAAABVgiQoC" +
           "AAAAAQAKAAAAU0J5dGVWYWx1ZQEBJCUALwA/JCUAAAAC/////wEB/////wAAAAAVYIkKAgAAAAEACQAA" +
           "AEJ5dGVWYWx1ZQEBJSUALwA/JSUAAAAD/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDE2VmFs" +
           "dWUBASYlAC8APyYlAAAABP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50MTZWYWx1ZQEBJyUA" +
           "LwA/JyUAAAAF/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMyVmFsdWUBASglAC8APyglAAAA" +
           "Bv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50MzJWYWx1ZQEBKSUALwA/KSUAAAAH/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEACgAAAEludDY0VmFsdWUBASolAC8APyolAAAACP////8BAf////8AAAAA" +
           "FWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBKyUALwA/KyUAAAAJ/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEACgAAAEZsb2F0VmFsdWUBASwlAC8APywlAAAACv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABE" +
           "b3VibGVWYWx1ZQEBLSUALwA/LSUAAAAL/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFN0cmluZ1Zh" +
           "bHVlAQEuJQAvAD8uJQAAAAz/////AQH/////AAAAABVgiQoCAAAAAQANAAAARGF0ZVRpbWVWYWx1ZQEB" +
           "LyUALwA/LyUAAAAN/////wEB/////wAAAAAVYIkKAgAAAAEACQAAAEd1aWRWYWx1ZQEBMCUALwA/MCUA" +
           "AAAO/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAEJ5dGVTdHJpbmdWYWx1ZQEBMSUALwA/MSUAAAAP" +
           "/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAFhtbEVsZW1lbnRWYWx1ZQEBMiUALwA/MiUAAAAQ////" +
           "/wEB/////wAAAAAVYIkKAgAAAAEACwAAAE5vZGVJZFZhbHVlAQEzJQAvAD8zJQAAABH/////AQH/////" +
           "AAAAABVgiQoCAAAAAQATAAAARXhwYW5kZWROb2RlSWRWYWx1ZQEBNCUALwA/NCUAAAAS/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEAEgAAAFF1YWxpZmllZE5hbWVWYWx1ZQEBNSUALwA/NSUAAAAU/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEAEgAAAExvY2FsaXplZFRleHRWYWx1ZQEBNiUALwA/NiUAAAAV/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEADwAAAFN0YXR1c0NvZGVWYWx1ZQEBNyUALwA/NyUAAAAT/////wEB/////wAA" +
           "AAAVYIkKAgAAAAEADAAAAFZhcmlhbnRWYWx1ZQEBOCUALwA/OCUAAAAY/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEAEAAAAEVudW1lcmF0aW9uVmFsdWUBATklAC8APzklAAAAHf////8BAf////8AAAAAFWCJCgIA" +
           "AAABAA4AAABTdHJ1Y3R1cmVWYWx1ZQEBOiUALwA/OiUAAAAW/////wEB/////wAAAAAVYIkKAgAAAAEA" +
           "CwAAAE51bWJlclZhbHVlAQE7JQAvAD87JQAAABr/////AQH/////AAAAABVgiQoCAAAAAQAMAAAASW50" +
           "ZWdlclZhbHVlAQE8JQAvAD88JQAAABv/////AQH/////AAAAABVgiQoCAAAAAQANAAAAVUludGVnZXJW" +
           "YWx1ZQEBPSUALwA/PSUAAAAc/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public BaseDataVariableState<bool> BooleanValue
        {
            get
            {
                return m_booleanValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_booleanValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_booleanValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<sbyte> SByteValue
        {
            get
            {
                return m_sByteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_sByteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_sByteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte> ByteValue
        {
            get
            {
                return m_byteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<short> Int16Value
        {
            get
            {
                return m_int16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ushort> UInt16Value
        {
            get
            {
                return m_uInt16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<int> Int32Value
        {
            get
            {
                return m_int32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<uint> UInt32Value
        {
            get
            {
                return m_uInt32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<long> Int64Value
        {
            get
            {
                return m_int64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ulong> UInt64Value
        {
            get
            {
                return m_uInt64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<float> FloatValue
        {
            get
            {
                return m_floatValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_floatValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_floatValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<double> DoubleValue
        {
            get
            {
                return m_doubleValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_doubleValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_doubleValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<string> StringValue
        {
            get
            {
                return m_stringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_stringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_stringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<DateTime> DateTimeValue
        {
            get
            {
                return m_dateTimeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_dateTimeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_dateTimeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<Guid> GuidValue
        {
            get
            {
                return m_guidValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_guidValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_guidValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte[]> ByteStringValue
        {
            get
            {
                return m_byteStringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteStringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteStringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<XmlElement> XmlElementValue
        {
            get
            {
                return m_xmlElementValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_xmlElementValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_xmlElementValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<NodeId> NodeIdValue
        {
            get
            {
                return m_nodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_nodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_nodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ExpandedNodeId> ExpandedNodeIdValue
        {
            get
            {
                return m_expandedNodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_expandedNodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_expandedNodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<QualifiedName> QualifiedNameValue
        {
            get
            {
                return m_qualifiedNameValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_qualifiedNameValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_qualifiedNameValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<LocalizedText> LocalizedTextValue
        {
            get
            {
                return m_localizedTextValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_localizedTextValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_localizedTextValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<StatusCode> StatusCodeValue
        {
            get
            {
                return m_statusCodeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_statusCodeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_statusCodeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState VariantValue
        {
            get
            {
                return m_variantValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_variantValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_variantValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<int> EnumerationValue
        {
            get
            {
                return m_enumerationValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_enumerationValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_enumerationValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ExtensionObject> StructureValue
        {
            get
            {
                return m_structureValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_structureValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_structureValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState NumberValue
        {
            get
            {
                return m_numberValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_numberValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_numberValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState IntegerValue
        {
            get
            {
                return m_integerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_integerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_integerValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState UIntegerValue
        {
            get
            {
                return m_uIntegerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uIntegerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uIntegerValue = value;
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
            if (m_booleanValue != null)
            {
                children.Add(m_booleanValue);
            }

            if (m_sByteValue != null)
            {
                children.Add(m_sByteValue);
            }

            if (m_byteValue != null)
            {
                children.Add(m_byteValue);
            }

            if (m_int16Value != null)
            {
                children.Add(m_int16Value);
            }

            if (m_uInt16Value != null)
            {
                children.Add(m_uInt16Value);
            }

            if (m_int32Value != null)
            {
                children.Add(m_int32Value);
            }

            if (m_uInt32Value != null)
            {
                children.Add(m_uInt32Value);
            }

            if (m_int64Value != null)
            {
                children.Add(m_int64Value);
            }

            if (m_uInt64Value != null)
            {
                children.Add(m_uInt64Value);
            }

            if (m_floatValue != null)
            {
                children.Add(m_floatValue);
            }

            if (m_doubleValue != null)
            {
                children.Add(m_doubleValue);
            }

            if (m_stringValue != null)
            {
                children.Add(m_stringValue);
            }

            if (m_dateTimeValue != null)
            {
                children.Add(m_dateTimeValue);
            }

            if (m_guidValue != null)
            {
                children.Add(m_guidValue);
            }

            if (m_byteStringValue != null)
            {
                children.Add(m_byteStringValue);
            }

            if (m_xmlElementValue != null)
            {
                children.Add(m_xmlElementValue);
            }

            if (m_nodeIdValue != null)
            {
                children.Add(m_nodeIdValue);
            }

            if (m_expandedNodeIdValue != null)
            {
                children.Add(m_expandedNodeIdValue);
            }

            if (m_qualifiedNameValue != null)
            {
                children.Add(m_qualifiedNameValue);
            }

            if (m_localizedTextValue != null)
            {
                children.Add(m_localizedTextValue);
            }

            if (m_statusCodeValue != null)
            {
                children.Add(m_statusCodeValue);
            }

            if (m_variantValue != null)
            {
                children.Add(m_variantValue);
            }

            if (m_enumerationValue != null)
            {
                children.Add(m_enumerationValue);
            }

            if (m_structureValue != null)
            {
                children.Add(m_structureValue);
            }

            if (m_numberValue != null)
            {
                children.Add(m_numberValue);
            }

            if (m_integerValue != null)
            {
                children.Add(m_integerValue);
            }

            if (m_uIntegerValue != null)
            {
                children.Add(m_uIntegerValue);
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
                case TestData.BrowseNames.BooleanValue:
                {
                    if (createOrReplace)
                    {
                        if (BooleanValue == null)
                        {
                            if (replacement == null)
                            {
                                BooleanValue = new BaseDataVariableState<bool>(this);
                            }
                            else
                            {
                                BooleanValue = (BaseDataVariableState<bool>)replacement;
                            }
                        }
                    }

                    instance = BooleanValue;
                    break;
                }

                case TestData.BrowseNames.SByteValue:
                {
                    if (createOrReplace)
                    {
                        if (SByteValue == null)
                        {
                            if (replacement == null)
                            {
                                SByteValue = new BaseDataVariableState<sbyte>(this);
                            }
                            else
                            {
                                SByteValue = (BaseDataVariableState<sbyte>)replacement;
                            }
                        }
                    }

                    instance = SByteValue;
                    break;
                }

                case TestData.BrowseNames.ByteValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteValue = new BaseDataVariableState<byte>(this);
                            }
                            else
                            {
                                ByteValue = (BaseDataVariableState<byte>)replacement;
                            }
                        }
                    }

                    instance = ByteValue;
                    break;
                }

                case TestData.BrowseNames.Int16Value:
                {
                    if (createOrReplace)
                    {
                        if (Int16Value == null)
                        {
                            if (replacement == null)
                            {
                                Int16Value = new BaseDataVariableState<short>(this);
                            }
                            else
                            {
                                Int16Value = (BaseDataVariableState<short>)replacement;
                            }
                        }
                    }

                    instance = Int16Value;
                    break;
                }

                case TestData.BrowseNames.UInt16Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt16Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt16Value = new BaseDataVariableState<ushort>(this);
                            }
                            else
                            {
                                UInt16Value = (BaseDataVariableState<ushort>)replacement;
                            }
                        }
                    }

                    instance = UInt16Value;
                    break;
                }

                case TestData.BrowseNames.Int32Value:
                {
                    if (createOrReplace)
                    {
                        if (Int32Value == null)
                        {
                            if (replacement == null)
                            {
                                Int32Value = new BaseDataVariableState<int>(this);
                            }
                            else
                            {
                                Int32Value = (BaseDataVariableState<int>)replacement;
                            }
                        }
                    }

                    instance = Int32Value;
                    break;
                }

                case TestData.BrowseNames.UInt32Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt32Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt32Value = new BaseDataVariableState<uint>(this);
                            }
                            else
                            {
                                UInt32Value = (BaseDataVariableState<uint>)replacement;
                            }
                        }
                    }

                    instance = UInt32Value;
                    break;
                }

                case TestData.BrowseNames.Int64Value:
                {
                    if (createOrReplace)
                    {
                        if (Int64Value == null)
                        {
                            if (replacement == null)
                            {
                                Int64Value = new BaseDataVariableState<long>(this);
                            }
                            else
                            {
                                Int64Value = (BaseDataVariableState<long>)replacement;
                            }
                        }
                    }

                    instance = Int64Value;
                    break;
                }

                case TestData.BrowseNames.UInt64Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt64Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt64Value = new BaseDataVariableState<ulong>(this);
                            }
                            else
                            {
                                UInt64Value = (BaseDataVariableState<ulong>)replacement;
                            }
                        }
                    }

                    instance = UInt64Value;
                    break;
                }

                case TestData.BrowseNames.FloatValue:
                {
                    if (createOrReplace)
                    {
                        if (FloatValue == null)
                        {
                            if (replacement == null)
                            {
                                FloatValue = new BaseDataVariableState<float>(this);
                            }
                            else
                            {
                                FloatValue = (BaseDataVariableState<float>)replacement;
                            }
                        }
                    }

                    instance = FloatValue;
                    break;
                }

                case TestData.BrowseNames.DoubleValue:
                {
                    if (createOrReplace)
                    {
                        if (DoubleValue == null)
                        {
                            if (replacement == null)
                            {
                                DoubleValue = new BaseDataVariableState<double>(this);
                            }
                            else
                            {
                                DoubleValue = (BaseDataVariableState<double>)replacement;
                            }
                        }
                    }

                    instance = DoubleValue;
                    break;
                }

                case TestData.BrowseNames.StringValue:
                {
                    if (createOrReplace)
                    {
                        if (StringValue == null)
                        {
                            if (replacement == null)
                            {
                                StringValue = new BaseDataVariableState<string>(this);
                            }
                            else
                            {
                                StringValue = (BaseDataVariableState<string>)replacement;
                            }
                        }
                    }

                    instance = StringValue;
                    break;
                }

                case TestData.BrowseNames.DateTimeValue:
                {
                    if (createOrReplace)
                    {
                        if (DateTimeValue == null)
                        {
                            if (replacement == null)
                            {
                                DateTimeValue = new BaseDataVariableState<DateTime>(this);
                            }
                            else
                            {
                                DateTimeValue = (BaseDataVariableState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = DateTimeValue;
                    break;
                }

                case TestData.BrowseNames.GuidValue:
                {
                    if (createOrReplace)
                    {
                        if (GuidValue == null)
                        {
                            if (replacement == null)
                            {
                                GuidValue = new BaseDataVariableState<Guid>(this);
                            }
                            else
                            {
                                GuidValue = (BaseDataVariableState<Guid>)replacement;
                            }
                        }
                    }

                    instance = GuidValue;
                    break;
                }

                case TestData.BrowseNames.ByteStringValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteStringValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteStringValue = new BaseDataVariableState<byte[]>(this);
                            }
                            else
                            {
                                ByteStringValue = (BaseDataVariableState<byte[]>)replacement;
                            }
                        }
                    }

                    instance = ByteStringValue;
                    break;
                }

                case TestData.BrowseNames.XmlElementValue:
                {
                    if (createOrReplace)
                    {
                        if (XmlElementValue == null)
                        {
                            if (replacement == null)
                            {
                                XmlElementValue = new BaseDataVariableState<XmlElement>(this);
                            }
                            else
                            {
                                XmlElementValue = (BaseDataVariableState<XmlElement>)replacement;
                            }
                        }
                    }

                    instance = XmlElementValue;
                    break;
                }

                case TestData.BrowseNames.NodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (NodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                NodeIdValue = new BaseDataVariableState<NodeId>(this);
                            }
                            else
                            {
                                NodeIdValue = (BaseDataVariableState<NodeId>)replacement;
                            }
                        }
                    }

                    instance = NodeIdValue;
                    break;
                }

                case TestData.BrowseNames.ExpandedNodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (ExpandedNodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                ExpandedNodeIdValue = new BaseDataVariableState<ExpandedNodeId>(this);
                            }
                            else
                            {
                                ExpandedNodeIdValue = (BaseDataVariableState<ExpandedNodeId>)replacement;
                            }
                        }
                    }

                    instance = ExpandedNodeIdValue;
                    break;
                }

                case TestData.BrowseNames.QualifiedNameValue:
                {
                    if (createOrReplace)
                    {
                        if (QualifiedNameValue == null)
                        {
                            if (replacement == null)
                            {
                                QualifiedNameValue = new BaseDataVariableState<QualifiedName>(this);
                            }
                            else
                            {
                                QualifiedNameValue = (BaseDataVariableState<QualifiedName>)replacement;
                            }
                        }
                    }

                    instance = QualifiedNameValue;
                    break;
                }

                case TestData.BrowseNames.LocalizedTextValue:
                {
                    if (createOrReplace)
                    {
                        if (LocalizedTextValue == null)
                        {
                            if (replacement == null)
                            {
                                LocalizedTextValue = new BaseDataVariableState<LocalizedText>(this);
                            }
                            else
                            {
                                LocalizedTextValue = (BaseDataVariableState<LocalizedText>)replacement;
                            }
                        }
                    }

                    instance = LocalizedTextValue;
                    break;
                }

                case TestData.BrowseNames.StatusCodeValue:
                {
                    if (createOrReplace)
                    {
                        if (StatusCodeValue == null)
                        {
                            if (replacement == null)
                            {
                                StatusCodeValue = new BaseDataVariableState<StatusCode>(this);
                            }
                            else
                            {
                                StatusCodeValue = (BaseDataVariableState<StatusCode>)replacement;
                            }
                        }
                    }

                    instance = StatusCodeValue;
                    break;
                }

                case TestData.BrowseNames.VariantValue:
                {
                    if (createOrReplace)
                    {
                        if (VariantValue == null)
                        {
                            if (replacement == null)
                            {
                                VariantValue = new BaseDataVariableState(this);
                            }
                            else
                            {
                                VariantValue = (BaseDataVariableState)replacement;
                            }
                        }
                    }

                    instance = VariantValue;
                    break;
                }

                case TestData.BrowseNames.EnumerationValue:
                {
                    if (createOrReplace)
                    {
                        if (EnumerationValue == null)
                        {
                            if (replacement == null)
                            {
                                EnumerationValue = new BaseDataVariableState<int>(this);
                            }
                            else
                            {
                                EnumerationValue = (BaseDataVariableState<int>)replacement;
                            }
                        }
                    }

                    instance = EnumerationValue;
                    break;
                }

                case TestData.BrowseNames.StructureValue:
                {
                    if (createOrReplace)
                    {
                        if (StructureValue == null)
                        {
                            if (replacement == null)
                            {
                                StructureValue = new BaseDataVariableState<ExtensionObject>(this);
                            }
                            else
                            {
                                StructureValue = (BaseDataVariableState<ExtensionObject>)replacement;
                            }
                        }
                    }

                    instance = StructureValue;
                    break;
                }

                case TestData.BrowseNames.NumberValue:
                {
                    if (createOrReplace)
                    {
                        if (NumberValue == null)
                        {
                            if (replacement == null)
                            {
                                NumberValue = new BaseDataVariableState(this);
                            }
                            else
                            {
                                NumberValue = (BaseDataVariableState)replacement;
                            }
                        }
                    }

                    instance = NumberValue;
                    break;
                }

                case TestData.BrowseNames.IntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (IntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                IntegerValue = new BaseDataVariableState(this);
                            }
                            else
                            {
                                IntegerValue = (BaseDataVariableState)replacement;
                            }
                        }
                    }

                    instance = IntegerValue;
                    break;
                }

                case TestData.BrowseNames.UIntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (UIntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                UIntegerValue = new BaseDataVariableState(this);
                            }
                            else
                            {
                                UIntegerValue = (BaseDataVariableState)replacement;
                            }
                        }
                    }

                    instance = UIntegerValue;
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
        private BaseDataVariableState<bool> m_booleanValue;
        private BaseDataVariableState<sbyte> m_sByteValue;
        private BaseDataVariableState<byte> m_byteValue;
        private BaseDataVariableState<short> m_int16Value;
        private BaseDataVariableState<ushort> m_uInt16Value;
        private BaseDataVariableState<int> m_int32Value;
        private BaseDataVariableState<uint> m_uInt32Value;
        private BaseDataVariableState<long> m_int64Value;
        private BaseDataVariableState<ulong> m_uInt64Value;
        private BaseDataVariableState<float> m_floatValue;
        private BaseDataVariableState<double> m_doubleValue;
        private BaseDataVariableState<string> m_stringValue;
        private BaseDataVariableState<DateTime> m_dateTimeValue;
        private BaseDataVariableState<Guid> m_guidValue;
        private BaseDataVariableState<byte[]> m_byteStringValue;
        private BaseDataVariableState<XmlElement> m_xmlElementValue;
        private BaseDataVariableState<NodeId> m_nodeIdValue;
        private BaseDataVariableState<ExpandedNodeId> m_expandedNodeIdValue;
        private BaseDataVariableState<QualifiedName> m_qualifiedNameValue;
        private BaseDataVariableState<LocalizedText> m_localizedTextValue;
        private BaseDataVariableState<StatusCode> m_statusCodeValue;
        private BaseDataVariableState m_variantValue;
        private BaseDataVariableState<int> m_enumerationValue;
        private BaseDataVariableState<ExtensionObject> m_structureValue;
        private BaseDataVariableState m_numberValue;
        private BaseDataVariableState m_integerValue;
        private BaseDataVariableState m_uIntegerValue;
        #endregion
    }
    #endif
    #endregion

    #region AnalogScalarValueObjectState Class
    #if (!OPCUA_EXCLUDE_AnalogScalarValueObjectState)
    /// <summary>
    /// Stores an instance of the AnalogScalarValueObjectType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class AnalogScalarValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public AnalogScalarValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.AnalogScalarValueObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABACMAAABBbmFsb2dTY2Fs" +
           "YXJWYWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBPiU+JQAAAQAAAAAkAAEBQiUQAAAANWCJCgIAAAABABAA" +
           "AABTaW11bGF0aW9uQWN0aXZlAQE/JQMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHByb2R1" +
           "Y2UgbmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARD8lAAAAAf////8BAf//" +
           "//8AAAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBQCUALwEBqSRAJQAAAQH/////AQAAABdg" +
           "qQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAUElAC4AREElAACWAQAAAAEAKgEBRgAAAAoAAABJdGVy" +
           "YXRpb25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5lcmF0" +
           "ZS4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEBQiUA" +
           "LwEAQQtCJQAAAQAAAAAkAQEBPiUXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQFDJQAuAERDJQAAAA//" +
           "////AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQFEJQAuAEREJQAAABH/////AQH/////" +
           "AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBRSUALgBERSUAAAAR/////wEB/////wAAAAAVYIkK" +
           "AgAAAAAACgAAAFNvdXJjZU5hbWUBAUYlAC4AREYlAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAQA" +
           "AABUaW1lAQFHJQAuAERHJQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZlVGlt" +
           "ZQEBSCUALgBESCUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEBSiUALgBE" +
           "SiUAAAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQFLJQAuAERLJQAAAAX/////" +
           "AQH/////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBPi0ALgBEPi0AAAAR/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBPy0ALgBEPy0AAAAV/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBASctAC4ARCctAAAADP////8BAf////8A" +
           "AAAAFWCJCgIAAAAAAAgAAABCcmFuY2hJZAEBTCUALgBETCUAAAAR/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAABgAAAFJldGFpbgEBTSUALgBETSUAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVuYWJs" +
           "ZWRTdGF0ZQEBTiUALwEAIyNOJQAAABX/////AQECAAAAAQAsIwABAWMlAQAsIwABAWslAQAAABVgiQoC" +
           "AAAAAAACAAAASWQBAU8lAC4ARE8lAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFsaXR5" +
           "AQFUJQAvAQAqI1QlAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXAB" +
           "AVUlAC4ARFUlAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEBWCUA" +
           "LwEAKiNYJQAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQFZJQAu" +
           "AERZJQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQFaJQAvAQAqI1olAAAA" +
           "Ff////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAVslAC4ARFslAAABACYB" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEBXCUALgBEXCUAAAAM/////wEB" +
           "/////wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBAV4lAC8BAEQjXiUAAAEBAQAAAAEA+QsAAQDzCgAA" +
           "AAAEYYIKBAAAAAAABgAAAEVuYWJsZQEBXSUALwEAQyNdJQAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoE" +
           "AAAAAAAKAAAAQWRkQ29tbWVudAEBXyUALwEARSNfJQAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoCAAAA" +
           "AAAOAAAASW5wdXRBcmd1bWVudHMBAWAlAC4ARGAlAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//" +
           "////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoB" +
           "AUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhl" +
           "IGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRTdGF0" +
           "ZQEBYyUALwEAIyNjJQAAABX/////AQEBAAAAAQAsIwEBAU4lAQAAABVgiQoCAAAAAAACAAAASWQBAWQl" +
           "AC4ARGQlAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBcyUALwEAlyNz" +
           "JQAAAQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAXQlAC4ARHQl" +
           "AACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVy" +
           "IGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAA" +
           "ACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAABVgiQoCAAAAAQAKAAAAU0J5dGVWYWx1ZQEBdyUALwEAQAl3JQAAAAL/////AQH/////AQAA" +
           "ABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBeiUALgBEeiUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAJAAAAQnl0ZVZhbHVlAQF9JQAvAQBACX0lAAAAA/////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABF" +
           "VVJhbmdlAQGAJQAuAESAJQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQxNlZhbHVl" +
           "AQGDJQAvAQBACYMlAAAABP////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGGJQAuAESG" +
           "JQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50MTZWYWx1ZQEBiSUALwEAQAmJJQAA" +
           "AAX/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBjCUALgBEjCUAAAEAdAP/////AQH/" +
           "////AAAAABVgiQoCAAAAAQAKAAAASW50MzJWYWx1ZQEBjyUALwEAQAmPJQAAAAb/////AQH/////AQAA" +
           "ABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBkiUALgBEkiUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAA" +
           "AQALAAAAVUludDMyVmFsdWUBAZUlAC8BAEAJlSUAAAAH/////wEB/////wEAAAAVYIkKAgAAAAAABwAA" +
           "AEVVUmFuZ2UBAZglAC4ARJglAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDY0VmFs" +
           "dWUBAZslAC8BAEAJmyUAAAAI/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAZ4lAC4A" +
           "RJ4lAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQ2NFZhbHVlAQGhJQAvAQBACaEl" +
           "AAAACf////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGkJQAuAESkJQAAAQB0A/////8B" +
           "Af////8AAAAAFWCJCgIAAAABAAoAAABGbG9hdFZhbHVlAQGnJQAvAQBACaclAAAACv////8BAf////8B" +
           "AAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGqJQAuAESqJQAAAQB0A/////8BAf////8AAAAAFWCJCgIA" +
           "AAABAAsAAABEb3VibGVWYWx1ZQEBrSUALwEAQAmtJQAAAAv/////AQH/////AQAAABVgiQoCAAAAAAAH" +
           "AAAARVVSYW5nZQEBsCUALgBEsCUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAAAQALAAAATnVtYmVy" +
           "VmFsdWUBAbMlAC8BAEAJsyUAAAAa/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAbYl" +
           "AC4ARLYlAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAEADAAAAEludGVnZXJWYWx1ZQEBuSUALwEA" +
           "QAm5JQAAABv/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBvCUALgBEvCUAAAEAdAP/" +
           "////AQH/////AAAAABVgiQoCAAAAAQANAAAAVUludGVnZXJWYWx1ZQEBvyUALwEAQAm/JQAAABz/////" +
           "AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBwiUALgBEwiUAAAEAdAP/////AQH/////AAAA" +
           "AA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public AnalogItemState<sbyte> SByteValue
        {
            get
            {
                return m_sByteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_sByteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_sByteValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<byte> ByteValue
        {
            get
            {
                return m_byteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<short> Int16Value
        {
            get
            {
                return m_int16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int16Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<ushort> UInt16Value
        {
            get
            {
                return m_uInt16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt16Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<int> Int32Value
        {
            get
            {
                return m_int32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int32Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<uint> UInt32Value
        {
            get
            {
                return m_uInt32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt32Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<long> Int64Value
        {
            get
            {
                return m_int64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int64Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<ulong> UInt64Value
        {
            get
            {
                return m_uInt64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt64Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<float> FloatValue
        {
            get
            {
                return m_floatValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_floatValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_floatValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<double> DoubleValue
        {
            get
            {
                return m_doubleValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_doubleValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_doubleValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState NumberValue
        {
            get
            {
                return m_numberValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_numberValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_numberValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState IntegerValue
        {
            get
            {
                return m_integerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_integerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_integerValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState UIntegerValue
        {
            get
            {
                return m_uIntegerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uIntegerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uIntegerValue = value;
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
            if (m_sByteValue != null)
            {
                children.Add(m_sByteValue);
            }

            if (m_byteValue != null)
            {
                children.Add(m_byteValue);
            }

            if (m_int16Value != null)
            {
                children.Add(m_int16Value);
            }

            if (m_uInt16Value != null)
            {
                children.Add(m_uInt16Value);
            }

            if (m_int32Value != null)
            {
                children.Add(m_int32Value);
            }

            if (m_uInt32Value != null)
            {
                children.Add(m_uInt32Value);
            }

            if (m_int64Value != null)
            {
                children.Add(m_int64Value);
            }

            if (m_uInt64Value != null)
            {
                children.Add(m_uInt64Value);
            }

            if (m_floatValue != null)
            {
                children.Add(m_floatValue);
            }

            if (m_doubleValue != null)
            {
                children.Add(m_doubleValue);
            }

            if (m_numberValue != null)
            {
                children.Add(m_numberValue);
            }

            if (m_integerValue != null)
            {
                children.Add(m_integerValue);
            }

            if (m_uIntegerValue != null)
            {
                children.Add(m_uIntegerValue);
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
                case TestData.BrowseNames.SByteValue:
                {
                    if (createOrReplace)
                    {
                        if (SByteValue == null)
                        {
                            if (replacement == null)
                            {
                                SByteValue = new AnalogItemState<sbyte>(this);
                            }
                            else
                            {
                                SByteValue = (AnalogItemState<sbyte>)replacement;
                            }
                        }
                    }

                    instance = SByteValue;
                    break;
                }

                case TestData.BrowseNames.ByteValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteValue = new AnalogItemState<byte>(this);
                            }
                            else
                            {
                                ByteValue = (AnalogItemState<byte>)replacement;
                            }
                        }
                    }

                    instance = ByteValue;
                    break;
                }

                case TestData.BrowseNames.Int16Value:
                {
                    if (createOrReplace)
                    {
                        if (Int16Value == null)
                        {
                            if (replacement == null)
                            {
                                Int16Value = new AnalogItemState<short>(this);
                            }
                            else
                            {
                                Int16Value = (AnalogItemState<short>)replacement;
                            }
                        }
                    }

                    instance = Int16Value;
                    break;
                }

                case TestData.BrowseNames.UInt16Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt16Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt16Value = new AnalogItemState<ushort>(this);
                            }
                            else
                            {
                                UInt16Value = (AnalogItemState<ushort>)replacement;
                            }
                        }
                    }

                    instance = UInt16Value;
                    break;
                }

                case TestData.BrowseNames.Int32Value:
                {
                    if (createOrReplace)
                    {
                        if (Int32Value == null)
                        {
                            if (replacement == null)
                            {
                                Int32Value = new AnalogItemState<int>(this);
                            }
                            else
                            {
                                Int32Value = (AnalogItemState<int>)replacement;
                            }
                        }
                    }

                    instance = Int32Value;
                    break;
                }

                case TestData.BrowseNames.UInt32Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt32Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt32Value = new AnalogItemState<uint>(this);
                            }
                            else
                            {
                                UInt32Value = (AnalogItemState<uint>)replacement;
                            }
                        }
                    }

                    instance = UInt32Value;
                    break;
                }

                case TestData.BrowseNames.Int64Value:
                {
                    if (createOrReplace)
                    {
                        if (Int64Value == null)
                        {
                            if (replacement == null)
                            {
                                Int64Value = new AnalogItemState<long>(this);
                            }
                            else
                            {
                                Int64Value = (AnalogItemState<long>)replacement;
                            }
                        }
                    }

                    instance = Int64Value;
                    break;
                }

                case TestData.BrowseNames.UInt64Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt64Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt64Value = new AnalogItemState<ulong>(this);
                            }
                            else
                            {
                                UInt64Value = (AnalogItemState<ulong>)replacement;
                            }
                        }
                    }

                    instance = UInt64Value;
                    break;
                }

                case TestData.BrowseNames.FloatValue:
                {
                    if (createOrReplace)
                    {
                        if (FloatValue == null)
                        {
                            if (replacement == null)
                            {
                                FloatValue = new AnalogItemState<float>(this);
                            }
                            else
                            {
                                FloatValue = (AnalogItemState<float>)replacement;
                            }
                        }
                    }

                    instance = FloatValue;
                    break;
                }

                case TestData.BrowseNames.DoubleValue:
                {
                    if (createOrReplace)
                    {
                        if (DoubleValue == null)
                        {
                            if (replacement == null)
                            {
                                DoubleValue = new AnalogItemState<double>(this);
                            }
                            else
                            {
                                DoubleValue = (AnalogItemState<double>)replacement;
                            }
                        }
                    }

                    instance = DoubleValue;
                    break;
                }

                case TestData.BrowseNames.NumberValue:
                {
                    if (createOrReplace)
                    {
                        if (NumberValue == null)
                        {
                            if (replacement == null)
                            {
                                NumberValue = new AnalogItemState(this);
                            }
                            else
                            {
                                NumberValue = (AnalogItemState)replacement;
                            }
                        }
                    }

                    instance = NumberValue;
                    break;
                }

                case TestData.BrowseNames.IntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (IntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                IntegerValue = new AnalogItemState(this);
                            }
                            else
                            {
                                IntegerValue = (AnalogItemState)replacement;
                            }
                        }
                    }

                    instance = IntegerValue;
                    break;
                }

                case TestData.BrowseNames.UIntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (UIntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                UIntegerValue = new AnalogItemState(this);
                            }
                            else
                            {
                                UIntegerValue = (AnalogItemState)replacement;
                            }
                        }
                    }

                    instance = UIntegerValue;
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
        private AnalogItemState<sbyte> m_sByteValue;
        private AnalogItemState<byte> m_byteValue;
        private AnalogItemState<short> m_int16Value;
        private AnalogItemState<ushort> m_uInt16Value;
        private AnalogItemState<int> m_int32Value;
        private AnalogItemState<uint> m_uInt32Value;
        private AnalogItemState<long> m_int64Value;
        private AnalogItemState<ulong> m_uInt64Value;
        private AnalogItemState<float> m_floatValue;
        private AnalogItemState<double> m_doubleValue;
        private AnalogItemState m_numberValue;
        private AnalogItemState m_integerValue;
        private AnalogItemState m_uIntegerValue;
        #endregion
    }
    #endif
    #endregion

    #region ArrayValue1MethodState Class
    #if (!OPCUA_EXCLUDE_ArrayValue1MethodState)
    /// <summary>
    /// Stores an instance of the ArrayValue1MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValue1MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ArrayValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new ArrayValue1MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABUAAABBcnJheVZhbHVl" +
           "MU1ldGhvZFR5cGUBAcYlAC/GJQAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMB" +
           "AcclAC4ARMclAACWCwAAAAEAKgEBHAAAAAkAAABCb29sZWFuSW4AAQEAAAABAAAAAAAAAAABACoBARoA" +
           "AAAHAAAAU0J5dGVJbgACAQAAAAEAAAAAAAAAAAEAKgEBGQAAAAYAAABCeXRlSW4AAwEAAAABAAAAAAAA" +
           "AAABACoBARoAAAAHAAAASW50MTZJbgAEAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABVSW50MTZJbgAF" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABJbnQzMkluAAYBAAAAAQAAAAAAAAAAAQAqAQEbAAAACAAA" +
           "AFVJbnQzMkluAAcBAAAAAQAAAAAAAAAAAQAqAQEaAAAABwAAAEludDY0SW4ACAEAAAABAAAAAAAAAAAB" +
           "ACoBARsAAAAIAAAAVUludDY0SW4ACQEAAAABAAAAAAAAAAABACoBARoAAAAHAAAARmxvYXRJbgAKAQAA" +
           "AAEAAAAAAAAAAAEAKgEBGwAAAAgAAABEb3VibGVJbgALAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAcglAC4ARMglAACWCwAAAAEA" +
           "KgEBHQAAAAoAAABCb29sZWFuT3V0AAEBAAAAAQAAAAAAAAAAAQAqAQEbAAAACAAAAFNCeXRlT3V0AAIB" +
           "AAAAAQAAAAAAAAAAAQAqAQEaAAAABwAAAEJ5dGVPdXQAAwEAAAABAAAAAAAAAAABACoBARsAAAAIAAAA" +
           "SW50MTZPdXQABAEAAAABAAAAAAAAAAABACoBARwAAAAJAAAAVUludDE2T3V0AAUBAAAAAQAAAAAAAAAA" +
           "AQAqAQEbAAAACAAAAEludDMyT3V0AAYBAAAAAQAAAAAAAAAAAQAqAQEcAAAACQAAAFVJbnQzMk91dAAH" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABJbnQ2NE91dAAIAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAkA" +
           "AABVSW50NjRPdXQACQEAAAABAAAAAAAAAAABACoBARsAAAAIAAAARmxvYXRPdXQACgEAAAABAAAAAAAA" +
           "AAABACoBARwAAAAJAAAARG91YmxlT3V0AAsBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB////" +
           "/wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public ArrayValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            bool[] booleanIn = (bool[])_inputArguments[0];
            sbyte[] sByteIn = (sbyte[])_inputArguments[1];
            byte[] byteIn = (byte[])_inputArguments[2];
            short[] int16In = (short[])_inputArguments[3];
            ushort[] uInt16In = (ushort[])_inputArguments[4];
            int[] int32In = (int[])_inputArguments[5];
            uint[] uInt32In = (uint[])_inputArguments[6];
            long[] int64In = (long[])_inputArguments[7];
            ulong[] uInt64In = (ulong[])_inputArguments[8];
            float[] floatIn = (float[])_inputArguments[9];
            double[] doubleIn = (double[])_inputArguments[10];

            bool[] booleanOut = (bool[])_outputArguments[0];
            sbyte[] sByteOut = (sbyte[])_outputArguments[1];
            byte[] byteOut = (byte[])_outputArguments[2];
            short[] int16Out = (short[])_outputArguments[3];
            ushort[] uInt16Out = (ushort[])_outputArguments[4];
            int[] int32Out = (int[])_outputArguments[5];
            uint[] uInt32Out = (uint[])_outputArguments[6];
            long[] int64Out = (long[])_outputArguments[7];
            ulong[] uInt64Out = (ulong[])_outputArguments[8];
            float[] floatOut = (float[])_outputArguments[9];
            double[] doubleOut = (double[])_outputArguments[10];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    booleanIn,
                    sByteIn,
                    byteIn,
                    int16In,
                    uInt16In,
                    int32In,
                    uInt32In,
                    int64In,
                    uInt64In,
                    floatIn,
                    doubleIn,
                    ref booleanOut,
                    ref sByteOut,
                    ref byteOut,
                    ref int16Out,
                    ref uInt16Out,
                    ref int32Out,
                    ref uInt32Out,
                    ref int64Out,
                    ref uInt64Out,
                    ref floatOut,
                    ref doubleOut);
            }

            _outputArguments[0] = booleanOut;
            _outputArguments[1] = sByteOut;
            _outputArguments[2] = byteOut;
            _outputArguments[3] = int16Out;
            _outputArguments[4] = uInt16Out;
            _outputArguments[5] = int32Out;
            _outputArguments[6] = uInt32Out;
            _outputArguments[7] = int64Out;
            _outputArguments[8] = uInt64Out;
            _outputArguments[9] = floatOut;
            _outputArguments[10] = doubleOut;

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
    public delegate ServiceResult ArrayValue1MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        bool[] booleanIn,
        sbyte[] sByteIn,
        byte[] byteIn,
        short[] int16In,
        ushort[] uInt16In,
        int[] int32In,
        uint[] uInt32In,
        long[] int64In,
        ulong[] uInt64In,
        float[] floatIn,
        double[] doubleIn,
        ref bool[] booleanOut,
        ref sbyte[] sByteOut,
        ref byte[] byteOut,
        ref short[] int16Out,
        ref ushort[] uInt16Out,
        ref int[] int32Out,
        ref uint[] uInt32Out,
        ref long[] int64Out,
        ref ulong[] uInt64Out,
        ref float[] floatOut,
        ref double[] doubleOut);
    #endif
    #endregion

    #region ArrayValue2MethodState Class
    #if (!OPCUA_EXCLUDE_ArrayValue2MethodState)
    /// <summary>
    /// Stores an instance of the ArrayValue2MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValue2MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ArrayValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new ArrayValue2MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABUAAABBcnJheVZhbHVl" +
           "Mk1ldGhvZFR5cGUBAcklAC/JJQAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMB" +
           "AcolAC4ARMolAACWCgAAAAEAKgEBGwAAAAgAAABTdHJpbmdJbgAMAQAAAAEAAAAAAAAAAAEAKgEBHQAA" +
           "AAoAAABEYXRlVGltZUluAA0BAAAAAQAAAAAAAAAAAQAqAQEZAAAABgAAAEd1aWRJbgAOAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHwAAAAwAAABCeXRlU3RyaW5nSW4ADwEAAAABAAAAAAAAAAABACoBAR8AAAAMAAAAWG1s" +
           "RWxlbWVudEluABABAAAAAQAAAAAAAAAAAQAqAQEbAAAACAAAAE5vZGVJZEluABEBAAAAAQAAAAAAAAAA" +
           "AQAqAQEjAAAAEAAAAEV4cGFuZGVkTm9kZUlkSW4AEgEAAAABAAAAAAAAAAABACoBASIAAAAPAAAAUXVh" +
           "bGlmaWVkTmFtZUluABQBAAAAAQAAAAAAAAAAAQAqAQEiAAAADwAAAExvY2FsaXplZFRleHRJbgAVAQAA" +
           "AAEAAAAAAAAAAAEAKgEBHwAAAAwAAABTdGF0dXNDb2RlSW4AEwEAAAABAAAAAAAAAAABACgBAQAAAAEA" +
           "AAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQHLJQAuAETLJQAAlgoA" +
           "AAABACoBARwAAAAJAAAAU3RyaW5nT3V0AAwBAAAAAQAAAAAAAAAAAQAqAQEeAAAACwAAAERhdGVUaW1l" +
           "T3V0AA0BAAAAAQAAAAAAAAAAAQAqAQEaAAAABwAAAEd1aWRPdXQADgEAAAABAAAAAAAAAAABACoBASAA" +
           "AAANAAAAQnl0ZVN0cmluZ091dAAPAQAAAAEAAAAAAAAAAAEAKgEBIAAAAA0AAABYbWxFbGVtZW50T3V0" +
           "ABABAAAAAQAAAAAAAAAAAQAqAQEcAAAACQAAAE5vZGVJZE91dAARAQAAAAEAAAAAAAAAAAEAKgEBJAAA" +
           "ABEAAABFeHBhbmRlZE5vZGVJZE91dAASAQAAAAEAAAAAAAAAAAEAKgEBIwAAABAAAABRdWFsaWZpZWRO" +
           "YW1lT3V0ABQBAAAAAQAAAAAAAAAAAQAqAQEjAAAAEAAAAExvY2FsaXplZFRleHRPdXQAFQEAAAABAAAA" +
           "AAAAAAABACoBASAAAAANAAAAU3RhdHVzQ29kZU91dAATAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAA" +
           "AAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public ArrayValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            string[] stringIn = (string[])_inputArguments[0];
            DateTime[] dateTimeIn = (DateTime[])_inputArguments[1];
            Uuid[] guidIn = (Uuid[])_inputArguments[2];
            byte[][] byteStringIn = (byte[][])_inputArguments[3];
            XmlElement[] xmlElementIn = (XmlElement[])_inputArguments[4];
            NodeId[] nodeIdIn = (NodeId[])_inputArguments[5];
            ExpandedNodeId[] expandedNodeIdIn = (ExpandedNodeId[])_inputArguments[6];
            QualifiedName[] qualifiedNameIn = (QualifiedName[])_inputArguments[7];
            LocalizedText[] localizedTextIn = (LocalizedText[])_inputArguments[8];
            StatusCode[] statusCodeIn = (StatusCode[])_inputArguments[9];

            string[] stringOut = (string[])_outputArguments[0];
            DateTime[] dateTimeOut = (DateTime[])_outputArguments[1];
            Uuid[] guidOut = (Uuid[])_outputArguments[2];
            byte[][] byteStringOut = (byte[][])_outputArguments[3];
            XmlElement[] xmlElementOut = (XmlElement[])_outputArguments[4];
            NodeId[] nodeIdOut = (NodeId[])_outputArguments[5];
            ExpandedNodeId[] expandedNodeIdOut = (ExpandedNodeId[])_outputArguments[6];
            QualifiedName[] qualifiedNameOut = (QualifiedName[])_outputArguments[7];
            LocalizedText[] localizedTextOut = (LocalizedText[])_outputArguments[8];
            StatusCode[] statusCodeOut = (StatusCode[])_outputArguments[9];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    stringIn,
                    dateTimeIn,
                    guidIn,
                    byteStringIn,
                    xmlElementIn,
                    nodeIdIn,
                    expandedNodeIdIn,
                    qualifiedNameIn,
                    localizedTextIn,
                    statusCodeIn,
                    ref stringOut,
                    ref dateTimeOut,
                    ref guidOut,
                    ref byteStringOut,
                    ref xmlElementOut,
                    ref nodeIdOut,
                    ref expandedNodeIdOut,
                    ref qualifiedNameOut,
                    ref localizedTextOut,
                    ref statusCodeOut);
            }

            _outputArguments[0] = stringOut;
            _outputArguments[1] = dateTimeOut;
            _outputArguments[2] = guidOut;
            _outputArguments[3] = byteStringOut;
            _outputArguments[4] = xmlElementOut;
            _outputArguments[5] = nodeIdOut;
            _outputArguments[6] = expandedNodeIdOut;
            _outputArguments[7] = qualifiedNameOut;
            _outputArguments[8] = localizedTextOut;
            _outputArguments[9] = statusCodeOut;

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
    public delegate ServiceResult ArrayValue2MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        string[] stringIn,
        DateTime[] dateTimeIn,
        Uuid[] guidIn,
        byte[][] byteStringIn,
        XmlElement[] xmlElementIn,
        NodeId[] nodeIdIn,
        ExpandedNodeId[] expandedNodeIdIn,
        QualifiedName[] qualifiedNameIn,
        LocalizedText[] localizedTextIn,
        StatusCode[] statusCodeIn,
        ref string[] stringOut,
        ref DateTime[] dateTimeOut,
        ref Uuid[] guidOut,
        ref byte[][] byteStringOut,
        ref XmlElement[] xmlElementOut,
        ref NodeId[] nodeIdOut,
        ref ExpandedNodeId[] expandedNodeIdOut,
        ref QualifiedName[] qualifiedNameOut,
        ref LocalizedText[] localizedTextOut,
        ref StatusCode[] statusCodeOut);
    #endif
    #endregion

    #region ArrayValue3MethodState Class
    #if (!OPCUA_EXCLUDE_ArrayValue3MethodState)
    /// <summary>
    /// Stores an instance of the ArrayValue3MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValue3MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ArrayValue3MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new ArrayValue3MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABUAAABBcnJheVZhbHVl" +
           "M01ldGhvZFR5cGUBAcwlAC/MJQAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMB" +
           "Ac0lAC4ARM0lAACWAwAAAAEAKgEBHAAAAAkAAABWYXJpYW50SW4AGAEAAAABAAAAAAAAAAABACoBASAA" +
           "AAANAAAARW51bWVyYXRpb25JbgAdAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAsAAABTdHJ1Y3R1cmVJbgAW" +
           "AQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRB" +
           "cmd1bWVudHMBAc4lAC4ARM4lAACWAwAAAAEAKgEBHQAAAAoAAABWYXJpYW50T3V0ABgBAAAAAQAAAAAA" +
           "AAAAAQAqAQEhAAAADgAAAEVudW1lcmF0aW9uT3V0AB0BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAFN0" +
           "cnVjdHVyZU91dAAWAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public ArrayValue3MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            Variant[] variantIn = (Variant[])_inputArguments[0];
            int[] enumerationIn = (int[])_inputArguments[1];
            ExtensionObject[] structureIn = (ExtensionObject[])_inputArguments[2];

            Variant[] variantOut = (Variant[])_outputArguments[0];
            int[] enumerationOut = (int[])_outputArguments[1];
            ExtensionObject[] structureOut = (ExtensionObject[])_outputArguments[2];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    variantIn,
                    enumerationIn,
                    structureIn,
                    ref variantOut,
                    ref enumerationOut,
                    ref structureOut);
            }

            _outputArguments[0] = variantOut;
            _outputArguments[1] = enumerationOut;
            _outputArguments[2] = structureOut;

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
    public delegate ServiceResult ArrayValue3MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        Variant[] variantIn,
        int[] enumerationIn,
        ExtensionObject[] structureIn,
        ref Variant[] variantOut,
        ref int[] enumerationOut,
        ref ExtensionObject[] structureOut);
    #endif
    #endregion

    #region ArrayValueObjectState Class
    #if (!OPCUA_EXCLUDE_ArrayValueObjectState)
    /// <summary>
    /// Stores an instance of the ArrayValueObjectType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ArrayValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.ArrayValueObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABABwAAABBcnJheVZhbHVl" +
           "T2JqZWN0VHlwZUluc3RhbmNlAQHPJc8lAAABAAAAACQAAQHTJR4AAAA1YIkKAgAAAAEAEAAAAFNpbXVs" +
           "YXRpb25BY3RpdmUBAdAlAwAAAABHAAAASWYgdHJ1ZSB0aGUgc2VydmVyIHdpbGwgcHJvZHVjZSBuZXcg" +
           "dmFsdWVzIGZvciBlYWNoIG1vbml0b3JlZCB2YXJpYWJsZS4ALgBE0CUAAAAB/////wEB/////wAAAAAE" +
           "YYIKBAAAAAEADgAAAEdlbmVyYXRlVmFsdWVzAQHRJQAvAQGpJNElAAABAf////8BAAAAF2CpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEB0iUALgBE0iUAAJYBAAAAAQAqAQFGAAAACgAAAEl0ZXJhdGlvbnMA" +
           "B/////8AAAAAAwAAAAAlAAAAVGhlIG51bWJlciBvZiBuZXcgdmFsdWVzIHRvIGdlbmVyYXRlLgEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAABGCACgEAAAABAA0AAABDeWNsZUNvbXBsZXRlAQHTJQAvAQBBC9Ml" +
           "AAABAAAAACQBAQHPJRcAAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBAdQlAC4ARNQlAAAAD/////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAdUlAC4ARNUlAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAoAAABTb3VyY2VOb2RlAQHWJQAuAETWJQAAABH/////AQH/////AAAAABVgiQoCAAAAAAAK" +
           "AAAAU291cmNlTmFtZQEB1yUALgBE1yUAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAABAAAAFRpbWUB" +
           "AdglAC4ARNglAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVUaW1lAQHZJQAu" +
           "AETZJQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQHbJQAuAETbJQAAABX/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAdwlAC4ARNwlAAAABf////8BAf////8A" +
           "AAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQFALQAuAERALQAAABH/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQFBLQAuAERBLQAAABX/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBKC0ALgBEKC0AAAAM/////wEB/////wAAAAAVYIkK" +
           "AgAAAAAACAAAAEJyYW5jaElkAQHdJQAuAETdJQAAABH/////AQH/////AAAAABVgiQoCAAAAAAAGAAAA" +
           "UmV0YWluAQHeJQAuAETeJQAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAARW5hYmxlZFN0YXRl" +
           "AQHfJQAvAQAjI98lAAAAFf////8BAQIAAAABACwjAAEB9CUBACwjAAEB/CUBAAAAFWCJCgIAAAAAAAIA" +
           "AABJZAEB4CUALgBE4CUAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxpdHkBAeUlAC8B" +
           "ACoj5SUAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB5iUALgBE" +
           "5iUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQHpJQAvAQAqI+kl" +
           "AAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAeolAC4AROolAAAB" +
           "ACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAeslAC8BACoj6yUAAAAV/////wEB" +
           "/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB7CUALgBE7CUAAAEAJgH/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQHtJQAuAETtJQAAAAz/////AQH/////AAAA" +
           "AARhggoEAAAAAAAHAAAARGlzYWJsZQEB7yUALwEARCPvJQAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoE" +
           "AAAAAAAGAAAARW5hYmxlAQHuJQAvAQBDI+4lAAABAQEAAAABAPkLAAEA8woAAAAABGGCCgQAAAAAAAoA" +
           "AABBZGRDb21tZW50AQHwJQAvAQBFI/AlAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIAAAAAAA4AAABJ" +
           "bnB1dEFyZ3VtZW50cwEB8SUALgBE8SUAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD/////8AAAAA" +
           "AwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEBQgAAAAcA" +
           "AABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUgY29uZGl0" +
           "aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAAAAAoAAABBY2tlZFN0YXRlAQH0JQAv" +
           "AQAjI/QlAAAAFf////8BAQEAAAABACwjAQEB3yUBAAAAFWCJCgIAAAAAAAIAAABJZAEB9SUALgBE9SUA" +
           "AAAB/////wEB/////wAAAAAEYYIKBAAAAAAACwAAAEFja25vd2xlZGdlAQEEJgAvAQCXIwQmAAABAQEA" +
           "AAABAPkLAAEA8CIBAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBBSYALgBEBSYAAJYCAAAA" +
           "AQAqAQFGAAAABwAAAEV2ZW50SWQAD/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRo" +
           "ZSBldmVudCB0byBjb21tZW50LgEAKgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRo" +
           "ZSBjb21tZW50IHRvIGFkZCB0byB0aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA" +
           "F2CJCgIAAAABAAwAAABCb29sZWFuVmFsdWUBAQgmAC8APwgmAAAAAQEAAAABAAAAAAAAAAEB/////wAA" +
           "AAAXYIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAQkmAC8APwkmAAAAAgEAAAABAAAAAAAAAAEB/////wAA" +
           "AAAXYIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEBCiYALwA/CiYAAAADAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQAKAAAASW50MTZWYWx1ZQEBCyYALwA/CyYAAAAEAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQALAAAAVUludDE2VmFsdWUBAQwmAC8APwwmAAAABQEAAAABAAAAAAAAAAEB/////wAA" +
           "AAAXYIkKAgAAAAEACgAAAEludDMyVmFsdWUBAQ0mAC8APw0mAAAABgEAAAABAAAAAAAAAAEB/////wAA" +
           "AAAXYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVlAQEOJgAvAD8OJgAAAAcBAAAAAQAAAAAAAAABAf////8A" +
           "AAAAF2CJCgIAAAABAAoAAABJbnQ2NFZhbHVlAQEPJgAvAD8PJgAAAAgBAAAAAQAAAAAAAAABAf////8A" +
           "AAAAF2CJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBECYALwA/ECYAAAAJAQAAAAEAAAAAAAAAAQH/////" +
           "AAAAABdgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBESYALwA/ESYAAAAKAQAAAAEAAAAAAAAAAQH/////" +
           "AAAAABdgiQoCAAAAAQALAAAARG91YmxlVmFsdWUBARImAC8APxImAAAACwEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAFN0cmluZ1ZhbHVlAQETJgAvAD8TJgAAAAwBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAA0AAABEYXRlVGltZVZhbHVlAQEUJgAvAD8UJgAAAA0BAAAAAQAAAAAAAAAB" +
           "Af////8AAAAAF2CJCgIAAAABAAkAAABHdWlkVmFsdWUBARUmAC8APxUmAAAADgEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEADwAAAEJ5dGVTdHJpbmdWYWx1ZQEBFiYALwA/FiYAAAAPAQAAAAEAAAAA" +
           "AAAAAQH/////AAAAABdgiQoCAAAAAQAPAAAAWG1sRWxlbWVudFZhbHVlAQEXJgAvAD8XJgAAABABAAAA" +
           "AQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAsAAABOb2RlSWRWYWx1ZQEBGCYALwA/GCYAAAARAQAA" +
           "AAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQATAAAARXhwYW5kZWROb2RlSWRWYWx1ZQEBGSYALwA/" +
           "GSYAAAASAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQASAAAAUXVhbGlmaWVkTmFtZVZhbHVl" +
           "AQEaJgAvAD8aJgAAABQBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABABIAAABMb2NhbGl6ZWRU" +
           "ZXh0VmFsdWUBARsmAC8APxsmAAAAFQEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAFN0" +
           "YXR1c0NvZGVWYWx1ZQEBHCYALwA/HCYAAAATAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAM" +
           "AAAAVmFyaWFudFZhbHVlAQEdJgAvAD8dJgAAABgBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAAB" +
           "ABAAAABFbnVtZXJhdGlvblZhbHVlAQEeJgAvAD8eJgAAAB0BAAAAAQAAAAAAAAABAf////8AAAAAF2CJ" +
           "CgIAAAABAA4AAABTdHJ1Y3R1cmVWYWx1ZQEBHyYALwA/HyYAAAAWAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQALAAAATnVtYmVyVmFsdWUBASAmAC8APyAmAAAAGgEAAAABAAAAAAAAAAEB/////wAA" +
           "AAAXYIkKAgAAAAEADAAAAEludGVnZXJWYWx1ZQEBISYALwA/ISYAAAAbAQAAAAEAAAAAAAAAAQH/////" +
           "AAAAABdgiQoCAAAAAQANAAAAVUludGVnZXJWYWx1ZQEBIiYALwA/IiYAAAAcAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public BaseDataVariableState<bool[]> BooleanValue
        {
            get
            {
                return m_booleanValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_booleanValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_booleanValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<sbyte[]> SByteValue
        {
            get
            {
                return m_sByteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_sByteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_sByteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte[]> ByteValue
        {
            get
            {
                return m_byteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<short[]> Int16Value
        {
            get
            {
                return m_int16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ushort[]> UInt16Value
        {
            get
            {
                return m_uInt16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<int[]> Int32Value
        {
            get
            {
                return m_int32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<uint[]> UInt32Value
        {
            get
            {
                return m_uInt32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<long[]> Int64Value
        {
            get
            {
                return m_int64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ulong[]> UInt64Value
        {
            get
            {
                return m_uInt64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<float[]> FloatValue
        {
            get
            {
                return m_floatValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_floatValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_floatValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<double[]> DoubleValue
        {
            get
            {
                return m_doubleValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_doubleValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_doubleValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<string[]> StringValue
        {
            get
            {
                return m_stringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_stringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_stringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<DateTime[]> DateTimeValue
        {
            get
            {
                return m_dateTimeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_dateTimeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_dateTimeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<Guid[]> GuidValue
        {
            get
            {
                return m_guidValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_guidValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_guidValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte[][]> ByteStringValue
        {
            get
            {
                return m_byteStringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteStringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteStringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<XmlElement[]> XmlElementValue
        {
            get
            {
                return m_xmlElementValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_xmlElementValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_xmlElementValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<NodeId[]> NodeIdValue
        {
            get
            {
                return m_nodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_nodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_nodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ExpandedNodeId[]> ExpandedNodeIdValue
        {
            get
            {
                return m_expandedNodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_expandedNodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_expandedNodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<QualifiedName[]> QualifiedNameValue
        {
            get
            {
                return m_qualifiedNameValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_qualifiedNameValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_qualifiedNameValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<LocalizedText[]> LocalizedTextValue
        {
            get
            {
                return m_localizedTextValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_localizedTextValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_localizedTextValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<StatusCode[]> StatusCodeValue
        {
            get
            {
                return m_statusCodeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_statusCodeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_statusCodeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<object[]> VariantValue
        {
            get
            {
                return m_variantValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_variantValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_variantValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<int[]> EnumerationValue
        {
            get
            {
                return m_enumerationValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_enumerationValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_enumerationValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ExtensionObject[]> StructureValue
        {
            get
            {
                return m_structureValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_structureValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_structureValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<object[]> NumberValue
        {
            get
            {
                return m_numberValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_numberValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_numberValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<object[]> IntegerValue
        {
            get
            {
                return m_integerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_integerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_integerValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<object[]> UIntegerValue
        {
            get
            {
                return m_uIntegerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uIntegerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uIntegerValue = value;
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
            if (m_booleanValue != null)
            {
                children.Add(m_booleanValue);
            }

            if (m_sByteValue != null)
            {
                children.Add(m_sByteValue);
            }

            if (m_byteValue != null)
            {
                children.Add(m_byteValue);
            }

            if (m_int16Value != null)
            {
                children.Add(m_int16Value);
            }

            if (m_uInt16Value != null)
            {
                children.Add(m_uInt16Value);
            }

            if (m_int32Value != null)
            {
                children.Add(m_int32Value);
            }

            if (m_uInt32Value != null)
            {
                children.Add(m_uInt32Value);
            }

            if (m_int64Value != null)
            {
                children.Add(m_int64Value);
            }

            if (m_uInt64Value != null)
            {
                children.Add(m_uInt64Value);
            }

            if (m_floatValue != null)
            {
                children.Add(m_floatValue);
            }

            if (m_doubleValue != null)
            {
                children.Add(m_doubleValue);
            }

            if (m_stringValue != null)
            {
                children.Add(m_stringValue);
            }

            if (m_dateTimeValue != null)
            {
                children.Add(m_dateTimeValue);
            }

            if (m_guidValue != null)
            {
                children.Add(m_guidValue);
            }

            if (m_byteStringValue != null)
            {
                children.Add(m_byteStringValue);
            }

            if (m_xmlElementValue != null)
            {
                children.Add(m_xmlElementValue);
            }

            if (m_nodeIdValue != null)
            {
                children.Add(m_nodeIdValue);
            }

            if (m_expandedNodeIdValue != null)
            {
                children.Add(m_expandedNodeIdValue);
            }

            if (m_qualifiedNameValue != null)
            {
                children.Add(m_qualifiedNameValue);
            }

            if (m_localizedTextValue != null)
            {
                children.Add(m_localizedTextValue);
            }

            if (m_statusCodeValue != null)
            {
                children.Add(m_statusCodeValue);
            }

            if (m_variantValue != null)
            {
                children.Add(m_variantValue);
            }

            if (m_enumerationValue != null)
            {
                children.Add(m_enumerationValue);
            }

            if (m_structureValue != null)
            {
                children.Add(m_structureValue);
            }

            if (m_numberValue != null)
            {
                children.Add(m_numberValue);
            }

            if (m_integerValue != null)
            {
                children.Add(m_integerValue);
            }

            if (m_uIntegerValue != null)
            {
                children.Add(m_uIntegerValue);
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
                case TestData.BrowseNames.BooleanValue:
                {
                    if (createOrReplace)
                    {
                        if (BooleanValue == null)
                        {
                            if (replacement == null)
                            {
                                BooleanValue = new BaseDataVariableState<bool[]>(this);
                            }
                            else
                            {
                                BooleanValue = (BaseDataVariableState<bool[]>)replacement;
                            }
                        }
                    }

                    instance = BooleanValue;
                    break;
                }

                case TestData.BrowseNames.SByteValue:
                {
                    if (createOrReplace)
                    {
                        if (SByteValue == null)
                        {
                            if (replacement == null)
                            {
                                SByteValue = new BaseDataVariableState<sbyte[]>(this);
                            }
                            else
                            {
                                SByteValue = (BaseDataVariableState<sbyte[]>)replacement;
                            }
                        }
                    }

                    instance = SByteValue;
                    break;
                }

                case TestData.BrowseNames.ByteValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteValue = new BaseDataVariableState<byte[]>(this);
                            }
                            else
                            {
                                ByteValue = (BaseDataVariableState<byte[]>)replacement;
                            }
                        }
                    }

                    instance = ByteValue;
                    break;
                }

                case TestData.BrowseNames.Int16Value:
                {
                    if (createOrReplace)
                    {
                        if (Int16Value == null)
                        {
                            if (replacement == null)
                            {
                                Int16Value = new BaseDataVariableState<short[]>(this);
                            }
                            else
                            {
                                Int16Value = (BaseDataVariableState<short[]>)replacement;
                            }
                        }
                    }

                    instance = Int16Value;
                    break;
                }

                case TestData.BrowseNames.UInt16Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt16Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt16Value = new BaseDataVariableState<ushort[]>(this);
                            }
                            else
                            {
                                UInt16Value = (BaseDataVariableState<ushort[]>)replacement;
                            }
                        }
                    }

                    instance = UInt16Value;
                    break;
                }

                case TestData.BrowseNames.Int32Value:
                {
                    if (createOrReplace)
                    {
                        if (Int32Value == null)
                        {
                            if (replacement == null)
                            {
                                Int32Value = new BaseDataVariableState<int[]>(this);
                            }
                            else
                            {
                                Int32Value = (BaseDataVariableState<int[]>)replacement;
                            }
                        }
                    }

                    instance = Int32Value;
                    break;
                }

                case TestData.BrowseNames.UInt32Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt32Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt32Value = new BaseDataVariableState<uint[]>(this);
                            }
                            else
                            {
                                UInt32Value = (BaseDataVariableState<uint[]>)replacement;
                            }
                        }
                    }

                    instance = UInt32Value;
                    break;
                }

                case TestData.BrowseNames.Int64Value:
                {
                    if (createOrReplace)
                    {
                        if (Int64Value == null)
                        {
                            if (replacement == null)
                            {
                                Int64Value = new BaseDataVariableState<long[]>(this);
                            }
                            else
                            {
                                Int64Value = (BaseDataVariableState<long[]>)replacement;
                            }
                        }
                    }

                    instance = Int64Value;
                    break;
                }

                case TestData.BrowseNames.UInt64Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt64Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt64Value = new BaseDataVariableState<ulong[]>(this);
                            }
                            else
                            {
                                UInt64Value = (BaseDataVariableState<ulong[]>)replacement;
                            }
                        }
                    }

                    instance = UInt64Value;
                    break;
                }

                case TestData.BrowseNames.FloatValue:
                {
                    if (createOrReplace)
                    {
                        if (FloatValue == null)
                        {
                            if (replacement == null)
                            {
                                FloatValue = new BaseDataVariableState<float[]>(this);
                            }
                            else
                            {
                                FloatValue = (BaseDataVariableState<float[]>)replacement;
                            }
                        }
                    }

                    instance = FloatValue;
                    break;
                }

                case TestData.BrowseNames.DoubleValue:
                {
                    if (createOrReplace)
                    {
                        if (DoubleValue == null)
                        {
                            if (replacement == null)
                            {
                                DoubleValue = new BaseDataVariableState<double[]>(this);
                            }
                            else
                            {
                                DoubleValue = (BaseDataVariableState<double[]>)replacement;
                            }
                        }
                    }

                    instance = DoubleValue;
                    break;
                }

                case TestData.BrowseNames.StringValue:
                {
                    if (createOrReplace)
                    {
                        if (StringValue == null)
                        {
                            if (replacement == null)
                            {
                                StringValue = new BaseDataVariableState<string[]>(this);
                            }
                            else
                            {
                                StringValue = (BaseDataVariableState<string[]>)replacement;
                            }
                        }
                    }

                    instance = StringValue;
                    break;
                }

                case TestData.BrowseNames.DateTimeValue:
                {
                    if (createOrReplace)
                    {
                        if (DateTimeValue == null)
                        {
                            if (replacement == null)
                            {
                                DateTimeValue = new BaseDataVariableState<DateTime[]>(this);
                            }
                            else
                            {
                                DateTimeValue = (BaseDataVariableState<DateTime[]>)replacement;
                            }
                        }
                    }

                    instance = DateTimeValue;
                    break;
                }

                case TestData.BrowseNames.GuidValue:
                {
                    if (createOrReplace)
                    {
                        if (GuidValue == null)
                        {
                            if (replacement == null)
                            {
                                GuidValue = new BaseDataVariableState<Guid[]>(this);
                            }
                            else
                            {
                                GuidValue = (BaseDataVariableState<Guid[]>)replacement;
                            }
                        }
                    }

                    instance = GuidValue;
                    break;
                }

                case TestData.BrowseNames.ByteStringValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteStringValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteStringValue = new BaseDataVariableState<byte[][]>(this);
                            }
                            else
                            {
                                ByteStringValue = (BaseDataVariableState<byte[][]>)replacement;
                            }
                        }
                    }

                    instance = ByteStringValue;
                    break;
                }

                case TestData.BrowseNames.XmlElementValue:
                {
                    if (createOrReplace)
                    {
                        if (XmlElementValue == null)
                        {
                            if (replacement == null)
                            {
                                XmlElementValue = new BaseDataVariableState<XmlElement[]>(this);
                            }
                            else
                            {
                                XmlElementValue = (BaseDataVariableState<XmlElement[]>)replacement;
                            }
                        }
                    }

                    instance = XmlElementValue;
                    break;
                }

                case TestData.BrowseNames.NodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (NodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                NodeIdValue = new BaseDataVariableState<NodeId[]>(this);
                            }
                            else
                            {
                                NodeIdValue = (BaseDataVariableState<NodeId[]>)replacement;
                            }
                        }
                    }

                    instance = NodeIdValue;
                    break;
                }

                case TestData.BrowseNames.ExpandedNodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (ExpandedNodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                ExpandedNodeIdValue = new BaseDataVariableState<ExpandedNodeId[]>(this);
                            }
                            else
                            {
                                ExpandedNodeIdValue = (BaseDataVariableState<ExpandedNodeId[]>)replacement;
                            }
                        }
                    }

                    instance = ExpandedNodeIdValue;
                    break;
                }

                case TestData.BrowseNames.QualifiedNameValue:
                {
                    if (createOrReplace)
                    {
                        if (QualifiedNameValue == null)
                        {
                            if (replacement == null)
                            {
                                QualifiedNameValue = new BaseDataVariableState<QualifiedName[]>(this);
                            }
                            else
                            {
                                QualifiedNameValue = (BaseDataVariableState<QualifiedName[]>)replacement;
                            }
                        }
                    }

                    instance = QualifiedNameValue;
                    break;
                }

                case TestData.BrowseNames.LocalizedTextValue:
                {
                    if (createOrReplace)
                    {
                        if (LocalizedTextValue == null)
                        {
                            if (replacement == null)
                            {
                                LocalizedTextValue = new BaseDataVariableState<LocalizedText[]>(this);
                            }
                            else
                            {
                                LocalizedTextValue = (BaseDataVariableState<LocalizedText[]>)replacement;
                            }
                        }
                    }

                    instance = LocalizedTextValue;
                    break;
                }

                case TestData.BrowseNames.StatusCodeValue:
                {
                    if (createOrReplace)
                    {
                        if (StatusCodeValue == null)
                        {
                            if (replacement == null)
                            {
                                StatusCodeValue = new BaseDataVariableState<StatusCode[]>(this);
                            }
                            else
                            {
                                StatusCodeValue = (BaseDataVariableState<StatusCode[]>)replacement;
                            }
                        }
                    }

                    instance = StatusCodeValue;
                    break;
                }

                case TestData.BrowseNames.VariantValue:
                {
                    if (createOrReplace)
                    {
                        if (VariantValue == null)
                        {
                            if (replacement == null)
                            {
                                VariantValue = new BaseDataVariableState<object[]>(this);
                            }
                            else
                            {
                                VariantValue = (BaseDataVariableState<object[]>)replacement;
                            }
                        }
                    }

                    instance = VariantValue;
                    break;
                }

                case TestData.BrowseNames.EnumerationValue:
                {
                    if (createOrReplace)
                    {
                        if (EnumerationValue == null)
                        {
                            if (replacement == null)
                            {
                                EnumerationValue = new BaseDataVariableState<int[]>(this);
                            }
                            else
                            {
                                EnumerationValue = (BaseDataVariableState<int[]>)replacement;
                            }
                        }
                    }

                    instance = EnumerationValue;
                    break;
                }

                case TestData.BrowseNames.StructureValue:
                {
                    if (createOrReplace)
                    {
                        if (StructureValue == null)
                        {
                            if (replacement == null)
                            {
                                StructureValue = new BaseDataVariableState<ExtensionObject[]>(this);
                            }
                            else
                            {
                                StructureValue = (BaseDataVariableState<ExtensionObject[]>)replacement;
                            }
                        }
                    }

                    instance = StructureValue;
                    break;
                }

                case TestData.BrowseNames.NumberValue:
                {
                    if (createOrReplace)
                    {
                        if (NumberValue == null)
                        {
                            if (replacement == null)
                            {
                                NumberValue = new BaseDataVariableState<object[]>(this);
                            }
                            else
                            {
                                NumberValue = (BaseDataVariableState<object[]>)replacement;
                            }
                        }
                    }

                    instance = NumberValue;
                    break;
                }

                case TestData.BrowseNames.IntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (IntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                IntegerValue = new BaseDataVariableState<object[]>(this);
                            }
                            else
                            {
                                IntegerValue = (BaseDataVariableState<object[]>)replacement;
                            }
                        }
                    }

                    instance = IntegerValue;
                    break;
                }

                case TestData.BrowseNames.UIntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (UIntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                UIntegerValue = new BaseDataVariableState<object[]>(this);
                            }
                            else
                            {
                                UIntegerValue = (BaseDataVariableState<object[]>)replacement;
                            }
                        }
                    }

                    instance = UIntegerValue;
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
        private BaseDataVariableState<bool[]> m_booleanValue;
        private BaseDataVariableState<sbyte[]> m_sByteValue;
        private BaseDataVariableState<byte[]> m_byteValue;
        private BaseDataVariableState<short[]> m_int16Value;
        private BaseDataVariableState<ushort[]> m_uInt16Value;
        private BaseDataVariableState<int[]> m_int32Value;
        private BaseDataVariableState<uint[]> m_uInt32Value;
        private BaseDataVariableState<long[]> m_int64Value;
        private BaseDataVariableState<ulong[]> m_uInt64Value;
        private BaseDataVariableState<float[]> m_floatValue;
        private BaseDataVariableState<double[]> m_doubleValue;
        private BaseDataVariableState<string[]> m_stringValue;
        private BaseDataVariableState<DateTime[]> m_dateTimeValue;
        private BaseDataVariableState<Guid[]> m_guidValue;
        private BaseDataVariableState<byte[][]> m_byteStringValue;
        private BaseDataVariableState<XmlElement[]> m_xmlElementValue;
        private BaseDataVariableState<NodeId[]> m_nodeIdValue;
        private BaseDataVariableState<ExpandedNodeId[]> m_expandedNodeIdValue;
        private BaseDataVariableState<QualifiedName[]> m_qualifiedNameValue;
        private BaseDataVariableState<LocalizedText[]> m_localizedTextValue;
        private BaseDataVariableState<StatusCode[]> m_statusCodeValue;
        private BaseDataVariableState<object[]> m_variantValue;
        private BaseDataVariableState<int[]> m_enumerationValue;
        private BaseDataVariableState<ExtensionObject[]> m_structureValue;
        private BaseDataVariableState<object[]> m_numberValue;
        private BaseDataVariableState<object[]> m_integerValue;
        private BaseDataVariableState<object[]> m_uIntegerValue;
        #endregion
    }
    #endif
    #endregion

    #region AnalogArrayValueObjectState Class
    #if (!OPCUA_EXCLUDE_AnalogArrayValueObjectState)
    /// <summary>
    /// Stores an instance of the AnalogArrayValueObjectType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class AnalogArrayValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public AnalogArrayValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.AnalogArrayValueObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABACIAAABBbmFsb2dBcnJh" +
           "eVZhbHVlT2JqZWN0VHlwZUluc3RhbmNlAQEjJiMmAAABAAAAACQAAQEnJhAAAAA1YIkKAgAAAAEAEAAA" +
           "AFNpbXVsYXRpb25BY3RpdmUBASQmAwAAAABHAAAASWYgdHJ1ZSB0aGUgc2VydmVyIHdpbGwgcHJvZHVj" +
           "ZSBuZXcgdmFsdWVzIGZvciBlYWNoIG1vbml0b3JlZCB2YXJpYWJsZS4ALgBEJCYAAAAB/////wEB////" +
           "/wAAAAAEYYIKBAAAAAEADgAAAEdlbmVyYXRlVmFsdWVzAQElJgAvAQGpJCUmAAABAf////8BAAAAF2Cp" +
           "CgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBJiYALgBEJiYAAJYBAAAAAQAqAQFGAAAACgAAAEl0ZXJh" +
           "dGlvbnMAB/////8AAAAAAwAAAAAlAAAAVGhlIG51bWJlciBvZiBuZXcgdmFsdWVzIHRvIGdlbmVyYXRl" +
           "LgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGCACgEAAAABAA0AAABDeWNsZUNvbXBsZXRlAQEnJgAv" +
           "AQBBCycmAAABAAAAACQBAQEjJhcAAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBASgmAC4ARCgmAAAAD///" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBASkmAC4ARCkmAAAAEf////8BAf////8A" +
           "AAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQEqJgAuAEQqJgAAABH/////AQH/////AAAAABVgiQoC" +
           "AAAAAAAKAAAAU291cmNlTmFtZQEBKyYALgBEKyYAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAABAAA" +
           "AFRpbWUBASwmAC4ARCwmAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVUaW1l" +
           "AQEtJgAuAEQtJgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQEvJgAuAEQv" +
           "JgAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBATAmAC4ARDAmAAAABf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQFCLQAuAERCLQAAABH/////AQH/" +
           "////AAAAABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQFDLQAuAERDLQAAABX/////AQH/" +
           "////AAAAABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBKS0ALgBEKS0AAAAM/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAACAAAAEJyYW5jaElkAQExJgAuAEQxJgAAABH/////AQH/////AAAAABVgiQoCAAAA" +
           "AAAGAAAAUmV0YWluAQEyJgAuAEQyJgAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAARW5hYmxl" +
           "ZFN0YXRlAQEzJgAvAQAjIzMmAAAAFf////8BAQIAAAABACwjAAEBSCYBACwjAAEBUCYBAAAAFWCJCgIA" +
           "AAAAAAIAAABJZAEBNCYALgBENCYAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxpdHkB" +
           "ATkmAC8BACojOSYAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB" +
           "OiYALgBEOiYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQE9JgAv" +
           "AQAqIz0mAAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAT4mAC4A" +
           "RD4mAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAT8mAC8BACojPyYAAAAV" +
           "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" +
           "LgBEfSYAAAEAdAP/////AQH/////AAAAABdgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBgCYALwEAQAmA" +
           "JgAAAAgBAAAAAQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGDJgAuAESDJgAA" +
           "AQB0A/////8BAf////8AAAAAF2CJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBhiYALwEAQAmGJgAAAAkB" +
           "AAAAAQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGJJgAuAESJJgAAAQB0A///" +
           "//8BAf////8AAAAAF2CJCgIAAAABAAoAAABGbG9hdFZhbHVlAQGMJgAvAQBACYwmAAAACgEAAAABAAAA" +
           "AAAAAAEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAY8mAC4ARI8mAAABAHQD/////wEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAERvdWJsZVZhbHVlAQGSJgAvAQBACZImAAAACwEAAAABAAAAAAAAAAEB" +
           "/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAZUmAC4ARJUmAAABAHQD/////wEB/////wAAAAAX" +
           "YIkKAgAAAAEACwAAAE51bWJlclZhbHVlAQGYJgAvAQBACZgmAAAAGgEAAAABAAAAAAAAAAEB/////wEA" +
           "AAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAZsmAC4ARJsmAAABAHQD/////wEB/////wAAAAAXYIkKAgAA" +
           "AAEADAAAAEludGVnZXJWYWx1ZQEBniYALwEAQAmeJgAAABsBAAAAAQAAAAAAAAABAf////8BAAAAFWCJ" +
           "CgIAAAAAAAcAAABFVVJhbmdlAQGhJgAuAEShJgAAAQB0A/////8BAf////8AAAAAF2CJCgIAAAABAA0A" +
           "AABVSW50ZWdlclZhbHVlAQGkJgAvAQBACaQmAAAAHAEAAAABAAAAAAAAAAEB/////wEAAAAVYIkKAgAA" +
           "AAAABwAAAEVVUmFuZ2UBAacmAC4ARKcmAAABAHQD/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public AnalogItemState<sbyte[]> SByteValue
        {
            get
            {
                return m_sByteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_sByteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_sByteValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<byte[]> ByteValue
        {
            get
            {
                return m_byteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<short[]> Int16Value
        {
            get
            {
                return m_int16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int16Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<ushort[]> UInt16Value
        {
            get
            {
                return m_uInt16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt16Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<int[]> Int32Value
        {
            get
            {
                return m_int32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int32Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<uint[]> UInt32Value
        {
            get
            {
                return m_uInt32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt32Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<long[]> Int64Value
        {
            get
            {
                return m_int64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int64Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<ulong[]> UInt64Value
        {
            get
            {
                return m_uInt64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt64Value = value;
            }
        }

        /// <remarks />
        public AnalogItemState<float[]> FloatValue
        {
            get
            {
                return m_floatValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_floatValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_floatValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<double[]> DoubleValue
        {
            get
            {
                return m_doubleValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_doubleValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_doubleValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<object[]> NumberValue
        {
            get
            {
                return m_numberValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_numberValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_numberValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<object[]> IntegerValue
        {
            get
            {
                return m_integerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_integerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_integerValue = value;
            }
        }

        /// <remarks />
        public AnalogItemState<object[]> UIntegerValue
        {
            get
            {
                return m_uIntegerValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uIntegerValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uIntegerValue = value;
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
            if (m_sByteValue != null)
            {
                children.Add(m_sByteValue);
            }

            if (m_byteValue != null)
            {
                children.Add(m_byteValue);
            }

            if (m_int16Value != null)
            {
                children.Add(m_int16Value);
            }

            if (m_uInt16Value != null)
            {
                children.Add(m_uInt16Value);
            }

            if (m_int32Value != null)
            {
                children.Add(m_int32Value);
            }

            if (m_uInt32Value != null)
            {
                children.Add(m_uInt32Value);
            }

            if (m_int64Value != null)
            {
                children.Add(m_int64Value);
            }

            if (m_uInt64Value != null)
            {
                children.Add(m_uInt64Value);
            }

            if (m_floatValue != null)
            {
                children.Add(m_floatValue);
            }

            if (m_doubleValue != null)
            {
                children.Add(m_doubleValue);
            }

            if (m_numberValue != null)
            {
                children.Add(m_numberValue);
            }

            if (m_integerValue != null)
            {
                children.Add(m_integerValue);
            }

            if (m_uIntegerValue != null)
            {
                children.Add(m_uIntegerValue);
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
                case TestData.BrowseNames.SByteValue:
                {
                    if (createOrReplace)
                    {
                        if (SByteValue == null)
                        {
                            if (replacement == null)
                            {
                                SByteValue = new AnalogItemState<sbyte[]>(this);
                            }
                            else
                            {
                                SByteValue = (AnalogItemState<sbyte[]>)replacement;
                            }
                        }
                    }

                    instance = SByteValue;
                    break;
                }

                case TestData.BrowseNames.ByteValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteValue = new AnalogItemState<byte[]>(this);
                            }
                            else
                            {
                                ByteValue = (AnalogItemState<byte[]>)replacement;
                            }
                        }
                    }

                    instance = ByteValue;
                    break;
                }

                case TestData.BrowseNames.Int16Value:
                {
                    if (createOrReplace)
                    {
                        if (Int16Value == null)
                        {
                            if (replacement == null)
                            {
                                Int16Value = new AnalogItemState<short[]>(this);
                            }
                            else
                            {
                                Int16Value = (AnalogItemState<short[]>)replacement;
                            }
                        }
                    }

                    instance = Int16Value;
                    break;
                }

                case TestData.BrowseNames.UInt16Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt16Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt16Value = new AnalogItemState<ushort[]>(this);
                            }
                            else
                            {
                                UInt16Value = (AnalogItemState<ushort[]>)replacement;
                            }
                        }
                    }

                    instance = UInt16Value;
                    break;
                }

                case TestData.BrowseNames.Int32Value:
                {
                    if (createOrReplace)
                    {
                        if (Int32Value == null)
                        {
                            if (replacement == null)
                            {
                                Int32Value = new AnalogItemState<int[]>(this);
                            }
                            else
                            {
                                Int32Value = (AnalogItemState<int[]>)replacement;
                            }
                        }
                    }

                    instance = Int32Value;
                    break;
                }

                case TestData.BrowseNames.UInt32Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt32Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt32Value = new AnalogItemState<uint[]>(this);
                            }
                            else
                            {
                                UInt32Value = (AnalogItemState<uint[]>)replacement;
                            }
                        }
                    }

                    instance = UInt32Value;
                    break;
                }

                case TestData.BrowseNames.Int64Value:
                {
                    if (createOrReplace)
                    {
                        if (Int64Value == null)
                        {
                            if (replacement == null)
                            {
                                Int64Value = new AnalogItemState<long[]>(this);
                            }
                            else
                            {
                                Int64Value = (AnalogItemState<long[]>)replacement;
                            }
                        }
                    }

                    instance = Int64Value;
                    break;
                }

                case TestData.BrowseNames.UInt64Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt64Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt64Value = new AnalogItemState<ulong[]>(this);
                            }
                            else
                            {
                                UInt64Value = (AnalogItemState<ulong[]>)replacement;
                            }
                        }
                    }

                    instance = UInt64Value;
                    break;
                }

                case TestData.BrowseNames.FloatValue:
                {
                    if (createOrReplace)
                    {
                        if (FloatValue == null)
                        {
                            if (replacement == null)
                            {
                                FloatValue = new AnalogItemState<float[]>(this);
                            }
                            else
                            {
                                FloatValue = (AnalogItemState<float[]>)replacement;
                            }
                        }
                    }

                    instance = FloatValue;
                    break;
                }

                case TestData.BrowseNames.DoubleValue:
                {
                    if (createOrReplace)
                    {
                        if (DoubleValue == null)
                        {
                            if (replacement == null)
                            {
                                DoubleValue = new AnalogItemState<double[]>(this);
                            }
                            else
                            {
                                DoubleValue = (AnalogItemState<double[]>)replacement;
                            }
                        }
                    }

                    instance = DoubleValue;
                    break;
                }

                case TestData.BrowseNames.NumberValue:
                {
                    if (createOrReplace)
                    {
                        if (NumberValue == null)
                        {
                            if (replacement == null)
                            {
                                NumberValue = new AnalogItemState<object[]>(this);
                            }
                            else
                            {
                                NumberValue = (AnalogItemState<object[]>)replacement;
                            }
                        }
                    }

                    instance = NumberValue;
                    break;
                }

                case TestData.BrowseNames.IntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (IntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                IntegerValue = new AnalogItemState<object[]>(this);
                            }
                            else
                            {
                                IntegerValue = (AnalogItemState<object[]>)replacement;
                            }
                        }
                    }

                    instance = IntegerValue;
                    break;
                }

                case TestData.BrowseNames.UIntegerValue:
                {
                    if (createOrReplace)
                    {
                        if (UIntegerValue == null)
                        {
                            if (replacement == null)
                            {
                                UIntegerValue = new AnalogItemState<object[]>(this);
                            }
                            else
                            {
                                UIntegerValue = (AnalogItemState<object[]>)replacement;
                            }
                        }
                    }

                    instance = UIntegerValue;
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
        private AnalogItemState<sbyte[]> m_sByteValue;
        private AnalogItemState<byte[]> m_byteValue;
        private AnalogItemState<short[]> m_int16Value;
        private AnalogItemState<ushort[]> m_uInt16Value;
        private AnalogItemState<int[]> m_int32Value;
        private AnalogItemState<uint[]> m_uInt32Value;
        private AnalogItemState<long[]> m_int64Value;
        private AnalogItemState<ulong[]> m_uInt64Value;
        private AnalogItemState<float[]> m_floatValue;
        private AnalogItemState<double[]> m_doubleValue;
        private AnalogItemState<object[]> m_numberValue;
        private AnalogItemState<object[]> m_integerValue;
        private AnalogItemState<object[]> m_uIntegerValue;
        #endregion
    }
    #endif
    #endregion

    #region UserScalarValueObjectState Class
    #if (!OPCUA_EXCLUDE_UserScalarValueObjectState)
    /// <summary>
    /// Stores an instance of the UserScalarValueObjectType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserScalarValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UserScalarValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.UserScalarValueObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABACEAAABVc2VyU2NhbGFy" +
           "VmFsdWVPYmplY3RUeXBlSW5zdGFuY2UBAcEmwSYAAAEAAAAAJAABAcUmGQAAADVgiQoCAAAAAQAQAAAA" +
           "U2ltdWxhdGlvbkFjdGl2ZQEBwiYDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9kdWNl" +
           "IG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAETCJgAAAAH/////AQH/////" +
           "AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAcMmAC8BAakkwyYAAAEB/////wEAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQHEJgAuAETEJgAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0" +
           "aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJhdGUu" +
           "AQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAcUmAC8B" +
           "AEELxSYAAAEAAAAAJAEBAcEmFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBxiYALgBExiYAAAAP////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBxyYALgBExyYAAAAR/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAcgmAC4ARMgmAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAoAAABTb3VyY2VOYW1lAQHJJgAuAETJJgAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAEAAAA" +
           "VGltZQEByiYALgBEyiYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUB" +
           "AcsmAC4ARMsmAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAc0mAC4ARM0m" +
           "AAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBziYALgBEziYAAAAF/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAUQtAC4AREQtAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAUUtAC4AREUtAAAAFf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQEqLQAuAEQqLQAAAAz/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAc8mAC4ARM8mAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAYAAABSZXRhaW4BAdAmAC4ARNAmAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFibGVk" +
           "U3RhdGUBAdEmAC8BACMj0SYAAAAV/////wEBAgAAAAEALCMAAQHmJgEALCMAAQHuJgEAAAAVYIkKAgAA" +
           "AAAAAgAAAElkAQHSJgAuAETSJgAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0eQEB" +
           "1yYALwEAKiPXJgAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQHY" +
           "JgAuAETYJgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAdsmAC8B" +
           "ACoj2yYAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB3CYALgBE" +
           "3CYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEB3SYALwEAKiPdJgAAABX/" +
           "////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQHeJgAuAETeJgAAAQAmAf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAd8mAC4ARN8mAAAADP////8BAf//" +
           "//8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQHhJgAvAQBEI+EmAAABAQEAAAABAPkLAAEA8woAAAAA" +
           "BGGCCgQAAAAAAAYAAABFbmFibGUBAeAmAC8BAEMj4CYAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIKBAAA" +
           "AAAACgAAAEFkZENvbW1lbnQBAeImAC8BAEUj4iYAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAAAAAA" +
           "DgAAAElucHV0QXJndW1lbnRzAQHjJgAuAETjJgAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP////" +
           "/wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFC" +
           "AAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBj" +
           "b25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3RhdGUB" +
           "AeYmAC8BACMj5iYAAAAV/////wEBAQAAAAEALCMBAQHRJgEAAAAVYIkKAgAAAAAAAgAAAElkAQHnJgAu" +
           "AETnJgAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAfYmAC8BAJcj9iYA" +
           "AAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQH3JgAuAET3JgAA" +
           "lgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBm" +
           "b3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAk" +
           "AAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAVYIkKAgAAAAEADAAAAEJvb2xlYW5WYWx1ZQEB+iYALwA/+iYAAAEBqib/////AQH/////AAAA" +
           "ABVgiQoCAAAAAQAKAAAAU0J5dGVWYWx1ZQEB+yYALwA/+yYAAAEBqyb/////AQH/////AAAAABVgiQoC" +
           "AAAAAQAJAAAAQnl0ZVZhbHVlAQH8JgAvAD/8JgAAAQGsJv////8BAf////8AAAAAFWCJCgIAAAABAAoA" +
           "AABJbnQxNlZhbHVlAQH9JgAvAD/9JgAAAQGtJv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50" +
           "MTZWYWx1ZQEB/iYALwA//iYAAAEBrib/////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50MzJWYWx1" +
           "ZQEB/yYALwA//yYAAAEBryb/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBAQAn" +
           "AC8APwAnAAABAbAm/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDY0VmFsdWUBAQEnAC8APwEn" +
           "AAABAbEm/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQ2NFZhbHVlAQECJwAvAD8CJwAAAQGy" +
           "Jv////8BAf////8AAAAAFWCJCgIAAAABAAoAAABGbG9hdFZhbHVlAQEDJwAvAD8DJwAAAQGzJv////8B" +
           "Af////8AAAAAFWCJCgIAAAABAAsAAABEb3VibGVWYWx1ZQEBBCcALwA/BCcAAAEBtCb/////AQH/////" +
           "AAAAABVgiQoCAAAAAQALAAAAU3RyaW5nVmFsdWUBAQUnAC8APwUnAAABAbUm/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEADQAAAERhdGVUaW1lVmFsdWUBAQYnAC8APwYnAAABAbYm/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEACQAAAEd1aWRWYWx1ZQEBBycALwA/BycAAAEBtyb/////AQH/////AAAAABVgiQoCAAAAAQAP" +
           "AAAAQnl0ZVN0cmluZ1ZhbHVlAQEIJwAvAD8IJwAAAQG4Jv////8BAf////8AAAAAFWCJCgIAAAABAA8A" +
           "AABYbWxFbGVtZW50VmFsdWUBAQknAC8APwknAAABAbkm/////wEB/////wAAAAAVYIkKAgAAAAEACwAA" +
           "AE5vZGVJZFZhbHVlAQEKJwAvAD8KJwAAAQG6Jv////8BAf////8AAAAAFWCJCgIAAAABABMAAABFeHBh" +
           "bmRlZE5vZGVJZFZhbHVlAQELJwAvAD8LJwAAAQG7Jv////8BAf////8AAAAAFWCJCgIAAAABABIAAABR" +
           "dWFsaWZpZWROYW1lVmFsdWUBAQwnAC8APwwnAAABAbwm/////wEB/////wAAAAAVYIkKAgAAAAEAEgAA" +
           "AExvY2FsaXplZFRleHRWYWx1ZQEBDScALwA/DScAAAEBvSb/////AQH/////AAAAABVgiQoCAAAAAQAP" +
           "AAAAU3RhdHVzQ29kZVZhbHVlAQEOJwAvAD8OJwAAAQG+Jv////8BAf////8AAAAAFWCJCgIAAAABAAwA" +
           "AABWYXJpYW50VmFsdWUBAQ8nAC8APw8nAAABAb8m/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public BaseDataVariableState<bool> BooleanValue
        {
            get
            {
                return m_booleanValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_booleanValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_booleanValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<sbyte> SByteValue
        {
            get
            {
                return m_sByteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_sByteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_sByteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte> ByteValue
        {
            get
            {
                return m_byteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<short> Int16Value
        {
            get
            {
                return m_int16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ushort> UInt16Value
        {
            get
            {
                return m_uInt16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<int> Int32Value
        {
            get
            {
                return m_int32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<uint> UInt32Value
        {
            get
            {
                return m_uInt32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<long> Int64Value
        {
            get
            {
                return m_int64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ulong> UInt64Value
        {
            get
            {
                return m_uInt64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<float> FloatValue
        {
            get
            {
                return m_floatValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_floatValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_floatValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<double> DoubleValue
        {
            get
            {
                return m_doubleValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_doubleValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_doubleValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<string> StringValue
        {
            get
            {
                return m_stringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_stringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_stringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<DateTime> DateTimeValue
        {
            get
            {
                return m_dateTimeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_dateTimeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_dateTimeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<Guid> GuidValue
        {
            get
            {
                return m_guidValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_guidValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_guidValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte[]> ByteStringValue
        {
            get
            {
                return m_byteStringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteStringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteStringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<XmlElement> XmlElementValue
        {
            get
            {
                return m_xmlElementValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_xmlElementValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_xmlElementValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<NodeId> NodeIdValue
        {
            get
            {
                return m_nodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_nodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_nodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ExpandedNodeId> ExpandedNodeIdValue
        {
            get
            {
                return m_expandedNodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_expandedNodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_expandedNodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<QualifiedName> QualifiedNameValue
        {
            get
            {
                return m_qualifiedNameValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_qualifiedNameValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_qualifiedNameValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<LocalizedText> LocalizedTextValue
        {
            get
            {
                return m_localizedTextValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_localizedTextValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_localizedTextValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<StatusCode> StatusCodeValue
        {
            get
            {
                return m_statusCodeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_statusCodeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_statusCodeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState VariantValue
        {
            get
            {
                return m_variantValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_variantValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_variantValue = value;
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
            if (m_booleanValue != null)
            {
                children.Add(m_booleanValue);
            }

            if (m_sByteValue != null)
            {
                children.Add(m_sByteValue);
            }

            if (m_byteValue != null)
            {
                children.Add(m_byteValue);
            }

            if (m_int16Value != null)
            {
                children.Add(m_int16Value);
            }

            if (m_uInt16Value != null)
            {
                children.Add(m_uInt16Value);
            }

            if (m_int32Value != null)
            {
                children.Add(m_int32Value);
            }

            if (m_uInt32Value != null)
            {
                children.Add(m_uInt32Value);
            }

            if (m_int64Value != null)
            {
                children.Add(m_int64Value);
            }

            if (m_uInt64Value != null)
            {
                children.Add(m_uInt64Value);
            }

            if (m_floatValue != null)
            {
                children.Add(m_floatValue);
            }

            if (m_doubleValue != null)
            {
                children.Add(m_doubleValue);
            }

            if (m_stringValue != null)
            {
                children.Add(m_stringValue);
            }

            if (m_dateTimeValue != null)
            {
                children.Add(m_dateTimeValue);
            }

            if (m_guidValue != null)
            {
                children.Add(m_guidValue);
            }

            if (m_byteStringValue != null)
            {
                children.Add(m_byteStringValue);
            }

            if (m_xmlElementValue != null)
            {
                children.Add(m_xmlElementValue);
            }

            if (m_nodeIdValue != null)
            {
                children.Add(m_nodeIdValue);
            }

            if (m_expandedNodeIdValue != null)
            {
                children.Add(m_expandedNodeIdValue);
            }

            if (m_qualifiedNameValue != null)
            {
                children.Add(m_qualifiedNameValue);
            }

            if (m_localizedTextValue != null)
            {
                children.Add(m_localizedTextValue);
            }

            if (m_statusCodeValue != null)
            {
                children.Add(m_statusCodeValue);
            }

            if (m_variantValue != null)
            {
                children.Add(m_variantValue);
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
                case TestData.BrowseNames.BooleanValue:
                {
                    if (createOrReplace)
                    {
                        if (BooleanValue == null)
                        {
                            if (replacement == null)
                            {
                                BooleanValue = new BaseDataVariableState<bool>(this);
                            }
                            else
                            {
                                BooleanValue = (BaseDataVariableState<bool>)replacement;
                            }
                        }
                    }

                    instance = BooleanValue;
                    break;
                }

                case TestData.BrowseNames.SByteValue:
                {
                    if (createOrReplace)
                    {
                        if (SByteValue == null)
                        {
                            if (replacement == null)
                            {
                                SByteValue = new BaseDataVariableState<sbyte>(this);
                            }
                            else
                            {
                                SByteValue = (BaseDataVariableState<sbyte>)replacement;
                            }
                        }
                    }

                    instance = SByteValue;
                    break;
                }

                case TestData.BrowseNames.ByteValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteValue = new BaseDataVariableState<byte>(this);
                            }
                            else
                            {
                                ByteValue = (BaseDataVariableState<byte>)replacement;
                            }
                        }
                    }

                    instance = ByteValue;
                    break;
                }

                case TestData.BrowseNames.Int16Value:
                {
                    if (createOrReplace)
                    {
                        if (Int16Value == null)
                        {
                            if (replacement == null)
                            {
                                Int16Value = new BaseDataVariableState<short>(this);
                            }
                            else
                            {
                                Int16Value = (BaseDataVariableState<short>)replacement;
                            }
                        }
                    }

                    instance = Int16Value;
                    break;
                }

                case TestData.BrowseNames.UInt16Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt16Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt16Value = new BaseDataVariableState<ushort>(this);
                            }
                            else
                            {
                                UInt16Value = (BaseDataVariableState<ushort>)replacement;
                            }
                        }
                    }

                    instance = UInt16Value;
                    break;
                }

                case TestData.BrowseNames.Int32Value:
                {
                    if (createOrReplace)
                    {
                        if (Int32Value == null)
                        {
                            if (replacement == null)
                            {
                                Int32Value = new BaseDataVariableState<int>(this);
                            }
                            else
                            {
                                Int32Value = (BaseDataVariableState<int>)replacement;
                            }
                        }
                    }

                    instance = Int32Value;
                    break;
                }

                case TestData.BrowseNames.UInt32Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt32Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt32Value = new BaseDataVariableState<uint>(this);
                            }
                            else
                            {
                                UInt32Value = (BaseDataVariableState<uint>)replacement;
                            }
                        }
                    }

                    instance = UInt32Value;
                    break;
                }

                case TestData.BrowseNames.Int64Value:
                {
                    if (createOrReplace)
                    {
                        if (Int64Value == null)
                        {
                            if (replacement == null)
                            {
                                Int64Value = new BaseDataVariableState<long>(this);
                            }
                            else
                            {
                                Int64Value = (BaseDataVariableState<long>)replacement;
                            }
                        }
                    }

                    instance = Int64Value;
                    break;
                }

                case TestData.BrowseNames.UInt64Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt64Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt64Value = new BaseDataVariableState<ulong>(this);
                            }
                            else
                            {
                                UInt64Value = (BaseDataVariableState<ulong>)replacement;
                            }
                        }
                    }

                    instance = UInt64Value;
                    break;
                }

                case TestData.BrowseNames.FloatValue:
                {
                    if (createOrReplace)
                    {
                        if (FloatValue == null)
                        {
                            if (replacement == null)
                            {
                                FloatValue = new BaseDataVariableState<float>(this);
                            }
                            else
                            {
                                FloatValue = (BaseDataVariableState<float>)replacement;
                            }
                        }
                    }

                    instance = FloatValue;
                    break;
                }

                case TestData.BrowseNames.DoubleValue:
                {
                    if (createOrReplace)
                    {
                        if (DoubleValue == null)
                        {
                            if (replacement == null)
                            {
                                DoubleValue = new BaseDataVariableState<double>(this);
                            }
                            else
                            {
                                DoubleValue = (BaseDataVariableState<double>)replacement;
                            }
                        }
                    }

                    instance = DoubleValue;
                    break;
                }

                case TestData.BrowseNames.StringValue:
                {
                    if (createOrReplace)
                    {
                        if (StringValue == null)
                        {
                            if (replacement == null)
                            {
                                StringValue = new BaseDataVariableState<string>(this);
                            }
                            else
                            {
                                StringValue = (BaseDataVariableState<string>)replacement;
                            }
                        }
                    }

                    instance = StringValue;
                    break;
                }

                case TestData.BrowseNames.DateTimeValue:
                {
                    if (createOrReplace)
                    {
                        if (DateTimeValue == null)
                        {
                            if (replacement == null)
                            {
                                DateTimeValue = new BaseDataVariableState<DateTime>(this);
                            }
                            else
                            {
                                DateTimeValue = (BaseDataVariableState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = DateTimeValue;
                    break;
                }

                case TestData.BrowseNames.GuidValue:
                {
                    if (createOrReplace)
                    {
                        if (GuidValue == null)
                        {
                            if (replacement == null)
                            {
                                GuidValue = new BaseDataVariableState<Guid>(this);
                            }
                            else
                            {
                                GuidValue = (BaseDataVariableState<Guid>)replacement;
                            }
                        }
                    }

                    instance = GuidValue;
                    break;
                }

                case TestData.BrowseNames.ByteStringValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteStringValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteStringValue = new BaseDataVariableState<byte[]>(this);
                            }
                            else
                            {
                                ByteStringValue = (BaseDataVariableState<byte[]>)replacement;
                            }
                        }
                    }

                    instance = ByteStringValue;
                    break;
                }

                case TestData.BrowseNames.XmlElementValue:
                {
                    if (createOrReplace)
                    {
                        if (XmlElementValue == null)
                        {
                            if (replacement == null)
                            {
                                XmlElementValue = new BaseDataVariableState<XmlElement>(this);
                            }
                            else
                            {
                                XmlElementValue = (BaseDataVariableState<XmlElement>)replacement;
                            }
                        }
                    }

                    instance = XmlElementValue;
                    break;
                }

                case TestData.BrowseNames.NodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (NodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                NodeIdValue = new BaseDataVariableState<NodeId>(this);
                            }
                            else
                            {
                                NodeIdValue = (BaseDataVariableState<NodeId>)replacement;
                            }
                        }
                    }

                    instance = NodeIdValue;
                    break;
                }

                case TestData.BrowseNames.ExpandedNodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (ExpandedNodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                ExpandedNodeIdValue = new BaseDataVariableState<ExpandedNodeId>(this);
                            }
                            else
                            {
                                ExpandedNodeIdValue = (BaseDataVariableState<ExpandedNodeId>)replacement;
                            }
                        }
                    }

                    instance = ExpandedNodeIdValue;
                    break;
                }

                case TestData.BrowseNames.QualifiedNameValue:
                {
                    if (createOrReplace)
                    {
                        if (QualifiedNameValue == null)
                        {
                            if (replacement == null)
                            {
                                QualifiedNameValue = new BaseDataVariableState<QualifiedName>(this);
                            }
                            else
                            {
                                QualifiedNameValue = (BaseDataVariableState<QualifiedName>)replacement;
                            }
                        }
                    }

                    instance = QualifiedNameValue;
                    break;
                }

                case TestData.BrowseNames.LocalizedTextValue:
                {
                    if (createOrReplace)
                    {
                        if (LocalizedTextValue == null)
                        {
                            if (replacement == null)
                            {
                                LocalizedTextValue = new BaseDataVariableState<LocalizedText>(this);
                            }
                            else
                            {
                                LocalizedTextValue = (BaseDataVariableState<LocalizedText>)replacement;
                            }
                        }
                    }

                    instance = LocalizedTextValue;
                    break;
                }

                case TestData.BrowseNames.StatusCodeValue:
                {
                    if (createOrReplace)
                    {
                        if (StatusCodeValue == null)
                        {
                            if (replacement == null)
                            {
                                StatusCodeValue = new BaseDataVariableState<StatusCode>(this);
                            }
                            else
                            {
                                StatusCodeValue = (BaseDataVariableState<StatusCode>)replacement;
                            }
                        }
                    }

                    instance = StatusCodeValue;
                    break;
                }

                case TestData.BrowseNames.VariantValue:
                {
                    if (createOrReplace)
                    {
                        if (VariantValue == null)
                        {
                            if (replacement == null)
                            {
                                VariantValue = new BaseDataVariableState(this);
                            }
                            else
                            {
                                VariantValue = (BaseDataVariableState)replacement;
                            }
                        }
                    }

                    instance = VariantValue;
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
        private BaseDataVariableState<bool> m_booleanValue;
        private BaseDataVariableState<sbyte> m_sByteValue;
        private BaseDataVariableState<byte> m_byteValue;
        private BaseDataVariableState<short> m_int16Value;
        private BaseDataVariableState<ushort> m_uInt16Value;
        private BaseDataVariableState<int> m_int32Value;
        private BaseDataVariableState<uint> m_uInt32Value;
        private BaseDataVariableState<long> m_int64Value;
        private BaseDataVariableState<ulong> m_uInt64Value;
        private BaseDataVariableState<float> m_floatValue;
        private BaseDataVariableState<double> m_doubleValue;
        private BaseDataVariableState<string> m_stringValue;
        private BaseDataVariableState<DateTime> m_dateTimeValue;
        private BaseDataVariableState<Guid> m_guidValue;
        private BaseDataVariableState<byte[]> m_byteStringValue;
        private BaseDataVariableState<XmlElement> m_xmlElementValue;
        private BaseDataVariableState<NodeId> m_nodeIdValue;
        private BaseDataVariableState<ExpandedNodeId> m_expandedNodeIdValue;
        private BaseDataVariableState<QualifiedName> m_qualifiedNameValue;
        private BaseDataVariableState<LocalizedText> m_localizedTextValue;
        private BaseDataVariableState<StatusCode> m_statusCodeValue;
        private BaseDataVariableState m_variantValue;
        #endregion
    }
    #endif
    #endregion

    #region UserScalarValue1MethodState Class
    #if (!OPCUA_EXCLUDE_UserScalarValue1MethodState)
    /// <summary>
    /// Stores an instance of the UserScalarValue1MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserScalarValue1MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UserScalarValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new UserScalarValue1MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABoAAABVc2VyU2NhbGFy" +
           "VmFsdWUxTWV0aG9kVHlwZQEBECcALxAnAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEBEScALgBEEScAAJYMAAAAAQAqAQEaAAAACQAAAEJvb2xlYW5JbgEBqib/////AAAAAAABACoB" +
           "ARgAAAAHAAAAU0J5dGVJbgEBqyb/////AAAAAAABACoBARcAAAAGAAAAQnl0ZUluAQGsJv////8AAAAA" +
           "AAEAKgEBGAAAAAcAAABJbnQxNkluAQGtJv////8AAAAAAAEAKgEBGQAAAAgAAABVSW50MTZJbgEBrib/" +
           "////AAAAAAABACoBARgAAAAHAAAASW50MzJJbgEBryb/////AAAAAAABACoBARkAAAAIAAAAVUludDMy" +
           "SW4BAbAm/////wAAAAAAAQAqAQEYAAAABwAAAEludDY0SW4BAbEm/////wAAAAAAAQAqAQEZAAAACAAA" +
           "AFVJbnQ2NEluAQGyJv////8AAAAAAAEAKgEBGAAAAAcAAABGbG9hdEluAQGzJv////8AAAAAAAEAKgEB" +
           "GQAAAAgAAABEb3VibGVJbgEBtCb/////AAAAAAABACoBARkAAAAIAAAAU3RyaW5nSW4BAbUm/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEB" +
           "EicALgBEEicAAJYMAAAAAQAqAQEbAAAACgAAAEJvb2xlYW5PdXQBAaom/////wAAAAAAAQAqAQEZAAAA" +
           "CAAAAFNCeXRlT3V0AQGrJv////8AAAAAAAEAKgEBGAAAAAcAAABCeXRlT3V0AQGsJv////8AAAAAAAEA" +
           "KgEBGQAAAAgAAABJbnQxNk91dAEBrSb/////AAAAAAABACoBARoAAAAJAAAAVUludDE2T3V0AQGuJv//" +
           "//8AAAAAAAEAKgEBGQAAAAgAAABJbnQzMk91dAEBryb/////AAAAAAABACoBARoAAAAJAAAAVUludDMy" +
           "T3V0AQGwJv////8AAAAAAAEAKgEBGQAAAAgAAABJbnQ2NE91dAEBsSb/////AAAAAAABACoBARoAAAAJ" +
           "AAAAVUludDY0T3V0AQGyJv////8AAAAAAAEAKgEBGQAAAAgAAABGbG9hdE91dAEBsyb/////AAAAAAAB" +
           "ACoBARoAAAAJAAAARG91YmxlT3V0AQG0Jv////8AAAAAAAEAKgEBGgAAAAkAAABTdHJpbmdPdXQBAbUm" +
           "/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public UserScalarValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            bool booleanIn = (bool)_inputArguments[0];
            sbyte sByteIn = (sbyte)_inputArguments[1];
            byte byteIn = (byte)_inputArguments[2];
            short int16In = (short)_inputArguments[3];
            ushort uInt16In = (ushort)_inputArguments[4];
            int int32In = (int)_inputArguments[5];
            uint uInt32In = (uint)_inputArguments[6];
            long int64In = (long)_inputArguments[7];
            ulong uInt64In = (ulong)_inputArguments[8];
            float floatIn = (float)_inputArguments[9];
            double doubleIn = (double)_inputArguments[10];
            string stringIn = (string)_inputArguments[11];

            bool booleanOut = (bool)_outputArguments[0];
            sbyte sByteOut = (sbyte)_outputArguments[1];
            byte byteOut = (byte)_outputArguments[2];
            short int16Out = (short)_outputArguments[3];
            ushort uInt16Out = (ushort)_outputArguments[4];
            int int32Out = (int)_outputArguments[5];
            uint uInt32Out = (uint)_outputArguments[6];
            long int64Out = (long)_outputArguments[7];
            ulong uInt64Out = (ulong)_outputArguments[8];
            float floatOut = (float)_outputArguments[9];
            double doubleOut = (double)_outputArguments[10];
            string stringOut = (string)_outputArguments[11];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    booleanIn,
                    sByteIn,
                    byteIn,
                    int16In,
                    uInt16In,
                    int32In,
                    uInt32In,
                    int64In,
                    uInt64In,
                    floatIn,
                    doubleIn,
                    stringIn,
                    ref booleanOut,
                    ref sByteOut,
                    ref byteOut,
                    ref int16Out,
                    ref uInt16Out,
                    ref int32Out,
                    ref uInt32Out,
                    ref int64Out,
                    ref uInt64Out,
                    ref floatOut,
                    ref doubleOut,
                    ref stringOut);
            }

            _outputArguments[0] = booleanOut;
            _outputArguments[1] = sByteOut;
            _outputArguments[2] = byteOut;
            _outputArguments[3] = int16Out;
            _outputArguments[4] = uInt16Out;
            _outputArguments[5] = int32Out;
            _outputArguments[6] = uInt32Out;
            _outputArguments[7] = int64Out;
            _outputArguments[8] = uInt64Out;
            _outputArguments[9] = floatOut;
            _outputArguments[10] = doubleOut;
            _outputArguments[11] = stringOut;

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
    public delegate ServiceResult UserScalarValue1MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        bool booleanIn,
        sbyte sByteIn,
        byte byteIn,
        short int16In,
        ushort uInt16In,
        int int32In,
        uint uInt32In,
        long int64In,
        ulong uInt64In,
        float floatIn,
        double doubleIn,
        string stringIn,
        ref bool booleanOut,
        ref sbyte sByteOut,
        ref byte byteOut,
        ref short int16Out,
        ref ushort uInt16Out,
        ref int int32Out,
        ref uint uInt32Out,
        ref long int64Out,
        ref ulong uInt64Out,
        ref float floatOut,
        ref double doubleOut,
        ref string stringOut);
    #endif
    #endregion

    #region UserScalarValue2MethodState Class
    #if (!OPCUA_EXCLUDE_UserScalarValue2MethodState)
    /// <summary>
    /// Stores an instance of the UserScalarValue2MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserScalarValue2MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UserScalarValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new UserScalarValue2MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABoAAABVc2VyU2NhbGFy" +
           "VmFsdWUyTWV0aG9kVHlwZQEBEycALxMnAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEBFCcALgBEFCcAAJYKAAAAAQAqAQEbAAAACgAAAERhdGVUaW1lSW4BAbYm/////wAAAAAAAQAq" +
           "AQEXAAAABgAAAEd1aWRJbgEBtyb/////AAAAAAABACoBAR0AAAAMAAAAQnl0ZVN0cmluZ0luAQG4Jv//" +
           "//8AAAAAAAEAKgEBHQAAAAwAAABYbWxFbGVtZW50SW4BAbkm/////wAAAAAAAQAqAQEZAAAACAAAAE5v" +
           "ZGVJZEluAQG6Jv////8AAAAAAAEAKgEBIQAAABAAAABFeHBhbmRlZE5vZGVJZEluAQG7Jv////8AAAAA" +
           "AAEAKgEBIAAAAA8AAABRdWFsaWZpZWROYW1lSW4BAbwm/////wAAAAAAAQAqAQEgAAAADwAAAExvY2Fs" +
           "aXplZFRleHRJbgEBvSb/////AAAAAAABACoBAR0AAAAMAAAAU3RhdHVzQ29kZUluAQG+Jv////8AAAAA" +
           "AAEAKgEBGgAAAAkAAABWYXJpYW50SW4BAb8m/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAA" +
           "AAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBFScALgBEFScAAJYKAAAAAQAqAQEcAAAACwAA" +
           "AERhdGVUaW1lT3V0AQG2Jv////8AAAAAAAEAKgEBGAAAAAcAAABHdWlkT3V0AQG3Jv////8AAAAAAAEA" +
           "KgEBHgAAAA0AAABCeXRlU3RyaW5nT3V0AQG4Jv////8AAAAAAAEAKgEBHgAAAA0AAABYbWxFbGVtZW50" +
           "T3V0AQG5Jv////8AAAAAAAEAKgEBGgAAAAkAAABOb2RlSWRPdXQBAbom/////wAAAAAAAQAqAQEiAAAA" +
           "EQAAAEV4cGFuZGVkTm9kZUlkT3V0AQG7Jv////8AAAAAAAEAKgEBIQAAABAAAABRdWFsaWZpZWROYW1l" +
           "T3V0AQG8Jv////8AAAAAAAEAKgEBIQAAABAAAABMb2NhbGl6ZWRUZXh0T3V0AQG9Jv////8AAAAAAAEA" +
           "KgEBHgAAAA0AAABTdGF0dXNDb2RlT3V0AQG+Jv////8AAAAAAAEAKgEBGwAAAAoAAABWYXJpYW50T3V0" +
           "AQG/Jv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public UserScalarValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            DateTime dateTimeIn = (DateTime)_inputArguments[0];
            Uuid guidIn = (Uuid)_inputArguments[1];
            byte[] byteStringIn = (byte[])_inputArguments[2];
            XmlElement xmlElementIn = (XmlElement)_inputArguments[3];
            NodeId nodeIdIn = (NodeId)_inputArguments[4];
            ExpandedNodeId expandedNodeIdIn = (ExpandedNodeId)_inputArguments[5];
            QualifiedName qualifiedNameIn = (QualifiedName)_inputArguments[6];
            LocalizedText localizedTextIn = (LocalizedText)_inputArguments[7];
            StatusCode statusCodeIn = (StatusCode)_inputArguments[8];
            object variantIn = (object)_inputArguments[9];

            DateTime dateTimeOut = (DateTime)_outputArguments[0];
            Uuid guidOut = (Uuid)_outputArguments[1];
            byte[] byteStringOut = (byte[])_outputArguments[2];
            XmlElement xmlElementOut = (XmlElement)_outputArguments[3];
            NodeId nodeIdOut = (NodeId)_outputArguments[4];
            ExpandedNodeId expandedNodeIdOut = (ExpandedNodeId)_outputArguments[5];
            QualifiedName qualifiedNameOut = (QualifiedName)_outputArguments[6];
            LocalizedText localizedTextOut = (LocalizedText)_outputArguments[7];
            StatusCode statusCodeOut = (StatusCode)_outputArguments[8];
            object variantOut = (object)_outputArguments[9];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    dateTimeIn,
                    guidIn,
                    byteStringIn,
                    xmlElementIn,
                    nodeIdIn,
                    expandedNodeIdIn,
                    qualifiedNameIn,
                    localizedTextIn,
                    statusCodeIn,
                    variantIn,
                    ref dateTimeOut,
                    ref guidOut,
                    ref byteStringOut,
                    ref xmlElementOut,
                    ref nodeIdOut,
                    ref expandedNodeIdOut,
                    ref qualifiedNameOut,
                    ref localizedTextOut,
                    ref statusCodeOut,
                    ref variantOut);
            }

            _outputArguments[0] = dateTimeOut;
            _outputArguments[1] = guidOut;
            _outputArguments[2] = byteStringOut;
            _outputArguments[3] = xmlElementOut;
            _outputArguments[4] = nodeIdOut;
            _outputArguments[5] = expandedNodeIdOut;
            _outputArguments[6] = qualifiedNameOut;
            _outputArguments[7] = localizedTextOut;
            _outputArguments[8] = statusCodeOut;
            _outputArguments[9] = variantOut;

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
    public delegate ServiceResult UserScalarValue2MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        DateTime dateTimeIn,
        Uuid guidIn,
        byte[] byteStringIn,
        XmlElement xmlElementIn,
        NodeId nodeIdIn,
        ExpandedNodeId expandedNodeIdIn,
        QualifiedName qualifiedNameIn,
        LocalizedText localizedTextIn,
        StatusCode statusCodeIn,
        object variantIn,
        ref DateTime dateTimeOut,
        ref Uuid guidOut,
        ref byte[] byteStringOut,
        ref XmlElement xmlElementOut,
        ref NodeId nodeIdOut,
        ref ExpandedNodeId expandedNodeIdOut,
        ref QualifiedName qualifiedNameOut,
        ref LocalizedText localizedTextOut,
        ref StatusCode statusCodeOut,
        ref object variantOut);
    #endif
    #endregion

    #region UserArrayValueObjectState Class
    #if (!OPCUA_EXCLUDE_UserArrayValueObjectState)
    /// <summary>
    /// Stores an instance of the UserArrayValueObjectType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserArrayValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UserArrayValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.UserArrayValueObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABACAAAABVc2VyQXJyYXlW" +
           "YWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBFycXJwAAAQAAAAAkAAEBGycZAAAANWCJCgIAAAABABAAAABT" +
           "aW11bGF0aW9uQWN0aXZlAQEYJwMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHByb2R1Y2Ug" +
           "bmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARBgnAAAAAf////8BAf////8A" +
           "AAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBGScALwEBqSQZJwAAAQH/////AQAAABdgqQoC" +
           "AAAAAAAOAAAASW5wdXRBcmd1bWVudHMBARonAC4ARBonAACWAQAAAAEAKgEBRgAAAAoAAABJdGVyYXRp" +
           "b25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5lcmF0ZS4B" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEBGycALwEA" +
           "QQsbJwAAAQAAAAAkAQEBFycXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQEcJwAuAEQcJwAAAA//////" +
           "AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQEdJwAuAEQdJwAAABH/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBHicALgBEHicAAAAR/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACgAAAFNvdXJjZU5hbWUBAR8nAC4ARB8nAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAQAAABU" +
           "aW1lAQEgJwAuAEQgJwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZlVGltZQEB" +
           "IScALgBEIScAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEBIycALgBEIycA" +
           "AAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQEkJwAuAEQkJwAAAAX/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBRi0ALgBERi0AAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBRy0ALgBERy0AAAAV/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBASstAC4ARCstAAAADP////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAgAAABCcmFuY2hJZAEBJScALgBEJScAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BgAAAFJldGFpbgEBJicALgBEJicAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVuYWJsZWRT" +
           "dGF0ZQEBJycALwEAIyMnJwAAABX/////AQECAAAAAQAsIwABATwnAQAsIwABAUQnAQAAABVgiQoCAAAA" +
           "AAACAAAASWQBASgnAC4ARCgnAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFsaXR5AQEt" +
           "JwAvAQAqIy0nAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAS4n" +
           "AC4ARC4nAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEBMScALwEA" +
           "KiMxJwAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEyJwAuAEQy" +
           "JwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQEzJwAvAQAqIzMnAAAAFf//" +
           "//8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABATQnAC4ARDQnAAABACYB////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEBNScALgBENScAAAAM/////wEB////" +
           "/wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBATcnAC8BAEQjNycAAAEBAQAAAAEA+QsAAQDzCgAAAAAE" +
           "YYIKBAAAAAAABgAAAEVuYWJsZQEBNicALwEAQyM2JwAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoEAAAA" +
           "AAAKAAAAQWRkQ29tbWVudAEBOCcALwEARSM4JwAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMBATknAC4ARDknAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////" +
           "AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIA" +
           "AAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNv" +
           "bmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRTdGF0ZQEB" +
           "PCcALwEAIyM8JwAAABX/////AQEBAAAAAQAsIwEBAScnAQAAABVgiQoCAAAAAAACAAAASWQBAT0nAC4A" +
           "RD0nAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBTCcALwEAlyNMJwAA" +
           "AQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAU0nAC4ARE0nAACW" +
           "AgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZv" +
           "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";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public BaseDataVariableState<bool[]> BooleanValue
        {
            get
            {
                return m_booleanValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_booleanValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_booleanValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<sbyte[]> SByteValue
        {
            get
            {
                return m_sByteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_sByteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_sByteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte[]> ByteValue
        {
            get
            {
                return m_byteValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<short[]> Int16Value
        {
            get
            {
                return m_int16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ushort[]> UInt16Value
        {
            get
            {
                return m_uInt16Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt16Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt16Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<int[]> Int32Value
        {
            get
            {
                return m_int32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<uint[]> UInt32Value
        {
            get
            {
                return m_uInt32Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt32Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt32Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<long[]> Int64Value
        {
            get
            {
                return m_int64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_int64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_int64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ulong[]> UInt64Value
        {
            get
            {
                return m_uInt64Value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_uInt64Value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_uInt64Value = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<float[]> FloatValue
        {
            get
            {
                return m_floatValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_floatValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_floatValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<double[]> DoubleValue
        {
            get
            {
                return m_doubleValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_doubleValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_doubleValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<string[]> StringValue
        {
            get
            {
                return m_stringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_stringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_stringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<DateTime[]> DateTimeValue
        {
            get
            {
                return m_dateTimeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_dateTimeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_dateTimeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<Guid[]> GuidValue
        {
            get
            {
                return m_guidValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_guidValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_guidValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<byte[][]> ByteStringValue
        {
            get
            {
                return m_byteStringValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_byteStringValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_byteStringValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<XmlElement[]> XmlElementValue
        {
            get
            {
                return m_xmlElementValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_xmlElementValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_xmlElementValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<NodeId[]> NodeIdValue
        {
            get
            {
                return m_nodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_nodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_nodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<ExpandedNodeId[]> ExpandedNodeIdValue
        {
            get
            {
                return m_expandedNodeIdValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_expandedNodeIdValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_expandedNodeIdValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<QualifiedName[]> QualifiedNameValue
        {
            get
            {
                return m_qualifiedNameValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_qualifiedNameValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_qualifiedNameValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<LocalizedText[]> LocalizedTextValue
        {
            get
            {
                return m_localizedTextValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_localizedTextValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_localizedTextValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<StatusCode[]> StatusCodeValue
        {
            get
            {
                return m_statusCodeValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_statusCodeValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_statusCodeValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<object[]> VariantValue
        {
            get
            {
                return m_variantValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_variantValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_variantValue = value;
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
            if (m_booleanValue != null)
            {
                children.Add(m_booleanValue);
            }

            if (m_sByteValue != null)
            {
                children.Add(m_sByteValue);
            }

            if (m_byteValue != null)
            {
                children.Add(m_byteValue);
            }

            if (m_int16Value != null)
            {
                children.Add(m_int16Value);
            }

            if (m_uInt16Value != null)
            {
                children.Add(m_uInt16Value);
            }

            if (m_int32Value != null)
            {
                children.Add(m_int32Value);
            }

            if (m_uInt32Value != null)
            {
                children.Add(m_uInt32Value);
            }

            if (m_int64Value != null)
            {
                children.Add(m_int64Value);
            }

            if (m_uInt64Value != null)
            {
                children.Add(m_uInt64Value);
            }

            if (m_floatValue != null)
            {
                children.Add(m_floatValue);
            }

            if (m_doubleValue != null)
            {
                children.Add(m_doubleValue);
            }

            if (m_stringValue != null)
            {
                children.Add(m_stringValue);
            }

            if (m_dateTimeValue != null)
            {
                children.Add(m_dateTimeValue);
            }

            if (m_guidValue != null)
            {
                children.Add(m_guidValue);
            }

            if (m_byteStringValue != null)
            {
                children.Add(m_byteStringValue);
            }

            if (m_xmlElementValue != null)
            {
                children.Add(m_xmlElementValue);
            }

            if (m_nodeIdValue != null)
            {
                children.Add(m_nodeIdValue);
            }

            if (m_expandedNodeIdValue != null)
            {
                children.Add(m_expandedNodeIdValue);
            }

            if (m_qualifiedNameValue != null)
            {
                children.Add(m_qualifiedNameValue);
            }

            if (m_localizedTextValue != null)
            {
                children.Add(m_localizedTextValue);
            }

            if (m_statusCodeValue != null)
            {
                children.Add(m_statusCodeValue);
            }

            if (m_variantValue != null)
            {
                children.Add(m_variantValue);
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
                case TestData.BrowseNames.BooleanValue:
                {
                    if (createOrReplace)
                    {
                        if (BooleanValue == null)
                        {
                            if (replacement == null)
                            {
                                BooleanValue = new BaseDataVariableState<bool[]>(this);
                            }
                            else
                            {
                                BooleanValue = (BaseDataVariableState<bool[]>)replacement;
                            }
                        }
                    }

                    instance = BooleanValue;
                    break;
                }

                case TestData.BrowseNames.SByteValue:
                {
                    if (createOrReplace)
                    {
                        if (SByteValue == null)
                        {
                            if (replacement == null)
                            {
                                SByteValue = new BaseDataVariableState<sbyte[]>(this);
                            }
                            else
                            {
                                SByteValue = (BaseDataVariableState<sbyte[]>)replacement;
                            }
                        }
                    }

                    instance = SByteValue;
                    break;
                }

                case TestData.BrowseNames.ByteValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteValue = new BaseDataVariableState<byte[]>(this);
                            }
                            else
                            {
                                ByteValue = (BaseDataVariableState<byte[]>)replacement;
                            }
                        }
                    }

                    instance = ByteValue;
                    break;
                }

                case TestData.BrowseNames.Int16Value:
                {
                    if (createOrReplace)
                    {
                        if (Int16Value == null)
                        {
                            if (replacement == null)
                            {
                                Int16Value = new BaseDataVariableState<short[]>(this);
                            }
                            else
                            {
                                Int16Value = (BaseDataVariableState<short[]>)replacement;
                            }
                        }
                    }

                    instance = Int16Value;
                    break;
                }

                case TestData.BrowseNames.UInt16Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt16Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt16Value = new BaseDataVariableState<ushort[]>(this);
                            }
                            else
                            {
                                UInt16Value = (BaseDataVariableState<ushort[]>)replacement;
                            }
                        }
                    }

                    instance = UInt16Value;
                    break;
                }

                case TestData.BrowseNames.Int32Value:
                {
                    if (createOrReplace)
                    {
                        if (Int32Value == null)
                        {
                            if (replacement == null)
                            {
                                Int32Value = new BaseDataVariableState<int[]>(this);
                            }
                            else
                            {
                                Int32Value = (BaseDataVariableState<int[]>)replacement;
                            }
                        }
                    }

                    instance = Int32Value;
                    break;
                }

                case TestData.BrowseNames.UInt32Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt32Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt32Value = new BaseDataVariableState<uint[]>(this);
                            }
                            else
                            {
                                UInt32Value = (BaseDataVariableState<uint[]>)replacement;
                            }
                        }
                    }

                    instance = UInt32Value;
                    break;
                }

                case TestData.BrowseNames.Int64Value:
                {
                    if (createOrReplace)
                    {
                        if (Int64Value == null)
                        {
                            if (replacement == null)
                            {
                                Int64Value = new BaseDataVariableState<long[]>(this);
                            }
                            else
                            {
                                Int64Value = (BaseDataVariableState<long[]>)replacement;
                            }
                        }
                    }

                    instance = Int64Value;
                    break;
                }

                case TestData.BrowseNames.UInt64Value:
                {
                    if (createOrReplace)
                    {
                        if (UInt64Value == null)
                        {
                            if (replacement == null)
                            {
                                UInt64Value = new BaseDataVariableState<ulong[]>(this);
                            }
                            else
                            {
                                UInt64Value = (BaseDataVariableState<ulong[]>)replacement;
                            }
                        }
                    }

                    instance = UInt64Value;
                    break;
                }

                case TestData.BrowseNames.FloatValue:
                {
                    if (createOrReplace)
                    {
                        if (FloatValue == null)
                        {
                            if (replacement == null)
                            {
                                FloatValue = new BaseDataVariableState<float[]>(this);
                            }
                            else
                            {
                                FloatValue = (BaseDataVariableState<float[]>)replacement;
                            }
                        }
                    }

                    instance = FloatValue;
                    break;
                }

                case TestData.BrowseNames.DoubleValue:
                {
                    if (createOrReplace)
                    {
                        if (DoubleValue == null)
                        {
                            if (replacement == null)
                            {
                                DoubleValue = new BaseDataVariableState<double[]>(this);
                            }
                            else
                            {
                                DoubleValue = (BaseDataVariableState<double[]>)replacement;
                            }
                        }
                    }

                    instance = DoubleValue;
                    break;
                }

                case TestData.BrowseNames.StringValue:
                {
                    if (createOrReplace)
                    {
                        if (StringValue == null)
                        {
                            if (replacement == null)
                            {
                                StringValue = new BaseDataVariableState<string[]>(this);
                            }
                            else
                            {
                                StringValue = (BaseDataVariableState<string[]>)replacement;
                            }
                        }
                    }

                    instance = StringValue;
                    break;
                }

                case TestData.BrowseNames.DateTimeValue:
                {
                    if (createOrReplace)
                    {
                        if (DateTimeValue == null)
                        {
                            if (replacement == null)
                            {
                                DateTimeValue = new BaseDataVariableState<DateTime[]>(this);
                            }
                            else
                            {
                                DateTimeValue = (BaseDataVariableState<DateTime[]>)replacement;
                            }
                        }
                    }

                    instance = DateTimeValue;
                    break;
                }

                case TestData.BrowseNames.GuidValue:
                {
                    if (createOrReplace)
                    {
                        if (GuidValue == null)
                        {
                            if (replacement == null)
                            {
                                GuidValue = new BaseDataVariableState<Guid[]>(this);
                            }
                            else
                            {
                                GuidValue = (BaseDataVariableState<Guid[]>)replacement;
                            }
                        }
                    }

                    instance = GuidValue;
                    break;
                }

                case TestData.BrowseNames.ByteStringValue:
                {
                    if (createOrReplace)
                    {
                        if (ByteStringValue == null)
                        {
                            if (replacement == null)
                            {
                                ByteStringValue = new BaseDataVariableState<byte[][]>(this);
                            }
                            else
                            {
                                ByteStringValue = (BaseDataVariableState<byte[][]>)replacement;
                            }
                        }
                    }

                    instance = ByteStringValue;
                    break;
                }

                case TestData.BrowseNames.XmlElementValue:
                {
                    if (createOrReplace)
                    {
                        if (XmlElementValue == null)
                        {
                            if (replacement == null)
                            {
                                XmlElementValue = new BaseDataVariableState<XmlElement[]>(this);
                            }
                            else
                            {
                                XmlElementValue = (BaseDataVariableState<XmlElement[]>)replacement;
                            }
                        }
                    }

                    instance = XmlElementValue;
                    break;
                }

                case TestData.BrowseNames.NodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (NodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                NodeIdValue = new BaseDataVariableState<NodeId[]>(this);
                            }
                            else
                            {
                                NodeIdValue = (BaseDataVariableState<NodeId[]>)replacement;
                            }
                        }
                    }

                    instance = NodeIdValue;
                    break;
                }

                case TestData.BrowseNames.ExpandedNodeIdValue:
                {
                    if (createOrReplace)
                    {
                        if (ExpandedNodeIdValue == null)
                        {
                            if (replacement == null)
                            {
                                ExpandedNodeIdValue = new BaseDataVariableState<ExpandedNodeId[]>(this);
                            }
                            else
                            {
                                ExpandedNodeIdValue = (BaseDataVariableState<ExpandedNodeId[]>)replacement;
                            }
                        }
                    }

                    instance = ExpandedNodeIdValue;
                    break;
                }

                case TestData.BrowseNames.QualifiedNameValue:
                {
                    if (createOrReplace)
                    {
                        if (QualifiedNameValue == null)
                        {
                            if (replacement == null)
                            {
                                QualifiedNameValue = new BaseDataVariableState<QualifiedName[]>(this);
                            }
                            else
                            {
                                QualifiedNameValue = (BaseDataVariableState<QualifiedName[]>)replacement;
                            }
                        }
                    }

                    instance = QualifiedNameValue;
                    break;
                }

                case TestData.BrowseNames.LocalizedTextValue:
                {
                    if (createOrReplace)
                    {
                        if (LocalizedTextValue == null)
                        {
                            if (replacement == null)
                            {
                                LocalizedTextValue = new BaseDataVariableState<LocalizedText[]>(this);
                            }
                            else
                            {
                                LocalizedTextValue = (BaseDataVariableState<LocalizedText[]>)replacement;
                            }
                        }
                    }

                    instance = LocalizedTextValue;
                    break;
                }

                case TestData.BrowseNames.StatusCodeValue:
                {
                    if (createOrReplace)
                    {
                        if (StatusCodeValue == null)
                        {
                            if (replacement == null)
                            {
                                StatusCodeValue = new BaseDataVariableState<StatusCode[]>(this);
                            }
                            else
                            {
                                StatusCodeValue = (BaseDataVariableState<StatusCode[]>)replacement;
                            }
                        }
                    }

                    instance = StatusCodeValue;
                    break;
                }

                case TestData.BrowseNames.VariantValue:
                {
                    if (createOrReplace)
                    {
                        if (VariantValue == null)
                        {
                            if (replacement == null)
                            {
                                VariantValue = new BaseDataVariableState<object[]>(this);
                            }
                            else
                            {
                                VariantValue = (BaseDataVariableState<object[]>)replacement;
                            }
                        }
                    }

                    instance = VariantValue;
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
        private BaseDataVariableState<bool[]> m_booleanValue;
        private BaseDataVariableState<sbyte[]> m_sByteValue;
        private BaseDataVariableState<byte[]> m_byteValue;
        private BaseDataVariableState<short[]> m_int16Value;
        private BaseDataVariableState<ushort[]> m_uInt16Value;
        private BaseDataVariableState<int[]> m_int32Value;
        private BaseDataVariableState<uint[]> m_uInt32Value;
        private BaseDataVariableState<long[]> m_int64Value;
        private BaseDataVariableState<ulong[]> m_uInt64Value;
        private BaseDataVariableState<float[]> m_floatValue;
        private BaseDataVariableState<double[]> m_doubleValue;
        private BaseDataVariableState<string[]> m_stringValue;
        private BaseDataVariableState<DateTime[]> m_dateTimeValue;
        private BaseDataVariableState<Guid[]> m_guidValue;
        private BaseDataVariableState<byte[][]> m_byteStringValue;
        private BaseDataVariableState<XmlElement[]> m_xmlElementValue;
        private BaseDataVariableState<NodeId[]> m_nodeIdValue;
        private BaseDataVariableState<ExpandedNodeId[]> m_expandedNodeIdValue;
        private BaseDataVariableState<QualifiedName[]> m_qualifiedNameValue;
        private BaseDataVariableState<LocalizedText[]> m_localizedTextValue;
        private BaseDataVariableState<StatusCode[]> m_statusCodeValue;
        private BaseDataVariableState<object[]> m_variantValue;
        #endregion
    }
    #endif
    #endregion

    #region UserArrayValue1MethodState Class
    #if (!OPCUA_EXCLUDE_UserArrayValue1MethodState)
    /// <summary>
    /// Stores an instance of the UserArrayValue1MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserArrayValue1MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UserArrayValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new UserArrayValue1MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABkAAABVc2VyQXJyYXlW" +
           "YWx1ZTFNZXRob2RUeXBlAQFmJwAvZicAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQFnJwAuAERnJwAAlgwAAAABACoBAR4AAAAJAAAAQm9vbGVhbkluAQGqJgEAAAABAAAAAAAAAAAB" +
           "ACoBARwAAAAHAAAAU0J5dGVJbgEBqyYBAAAAAQAAAAAAAAAAAQAqAQEbAAAABgAAAEJ5dGVJbgEBrCYB" +
           "AAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEludDE2SW4BAa0mAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgA" +
           "AABVSW50MTZJbgEBriYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEludDMySW4BAa8mAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHQAAAAgAAABVSW50MzJJbgEBsCYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEludDY0" +
           "SW4BAbEmAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABVSW50NjRJbgEBsiYBAAAAAQAAAAAAAAAAAQAq" +
           "AQEcAAAABwAAAEZsb2F0SW4BAbMmAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABEb3VibGVJbgEBtCYB" +
           "AAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAFN0cmluZ0luAQG1JgEAAAABAAAAAAAAAAABACgBAQAAAAEA" +
           "AAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFoJwAuAERoJwAAlgwA" +
           "AAABACoBAR8AAAAKAAAAQm9vbGVhbk91dAEBqiYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAFNCeXRl" +
           "T3V0AQGrJgEAAAABAAAAAAAAAAABACoBARwAAAAHAAAAQnl0ZU91dAEBrCYBAAAAAQAAAAAAAAAAAQAq" +
           "AQEdAAAACAAAAEludDE2T3V0AQGtJgEAAAABAAAAAAAAAAABACoBAR4AAAAJAAAAVUludDE2T3V0AQGu" +
           "JgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAASW50MzJPdXQBAa8mAQAAAAEAAAAAAAAAAAEAKgEBHgAA" +
           "AAkAAABVSW50MzJPdXQBAbAmAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABJbnQ2NE91dAEBsSYBAAAA" +
           "AQAAAAAAAAAAAQAqAQEeAAAACQAAAFVJbnQ2NE91dAEBsiYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAA" +
           "AEZsb2F0T3V0AQGzJgEAAAABAAAAAAAAAAABACoBAR4AAAAJAAAARG91YmxlT3V0AQG0JgEAAAABAAAA" +
           "AAAAAAABACoBAR4AAAAJAAAAU3RyaW5nT3V0AQG1JgEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAA" +
           "AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public UserArrayValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            bool[] booleanIn = (bool[])_inputArguments[0];
            sbyte[] sByteIn = (sbyte[])_inputArguments[1];
            byte[] byteIn = (byte[])_inputArguments[2];
            short[] int16In = (short[])_inputArguments[3];
            ushort[] uInt16In = (ushort[])_inputArguments[4];
            int[] int32In = (int[])_inputArguments[5];
            uint[] uInt32In = (uint[])_inputArguments[6];
            long[] int64In = (long[])_inputArguments[7];
            ulong[] uInt64In = (ulong[])_inputArguments[8];
            float[] floatIn = (float[])_inputArguments[9];
            double[] doubleIn = (double[])_inputArguments[10];
            string[] stringIn = (string[])_inputArguments[11];

            bool[] booleanOut = (bool[])_outputArguments[0];
            sbyte[] sByteOut = (sbyte[])_outputArguments[1];
            byte[] byteOut = (byte[])_outputArguments[2];
            short[] int16Out = (short[])_outputArguments[3];
            ushort[] uInt16Out = (ushort[])_outputArguments[4];
            int[] int32Out = (int[])_outputArguments[5];
            uint[] uInt32Out = (uint[])_outputArguments[6];
            long[] int64Out = (long[])_outputArguments[7];
            ulong[] uInt64Out = (ulong[])_outputArguments[8];
            float[] floatOut = (float[])_outputArguments[9];
            double[] doubleOut = (double[])_outputArguments[10];
            string[] stringOut = (string[])_outputArguments[11];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    booleanIn,
                    sByteIn,
                    byteIn,
                    int16In,
                    uInt16In,
                    int32In,
                    uInt32In,
                    int64In,
                    uInt64In,
                    floatIn,
                    doubleIn,
                    stringIn,
                    ref booleanOut,
                    ref sByteOut,
                    ref byteOut,
                    ref int16Out,
                    ref uInt16Out,
                    ref int32Out,
                    ref uInt32Out,
                    ref int64Out,
                    ref uInt64Out,
                    ref floatOut,
                    ref doubleOut,
                    ref stringOut);
            }

            _outputArguments[0] = booleanOut;
            _outputArguments[1] = sByteOut;
            _outputArguments[2] = byteOut;
            _outputArguments[3] = int16Out;
            _outputArguments[4] = uInt16Out;
            _outputArguments[5] = int32Out;
            _outputArguments[6] = uInt32Out;
            _outputArguments[7] = int64Out;
            _outputArguments[8] = uInt64Out;
            _outputArguments[9] = floatOut;
            _outputArguments[10] = doubleOut;
            _outputArguments[11] = stringOut;

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
    public delegate ServiceResult UserArrayValue1MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        bool[] booleanIn,
        sbyte[] sByteIn,
        byte[] byteIn,
        short[] int16In,
        ushort[] uInt16In,
        int[] int32In,
        uint[] uInt32In,
        long[] int64In,
        ulong[] uInt64In,
        float[] floatIn,
        double[] doubleIn,
        string[] stringIn,
        ref bool[] booleanOut,
        ref sbyte[] sByteOut,
        ref byte[] byteOut,
        ref short[] int16Out,
        ref ushort[] uInt16Out,
        ref int[] int32Out,
        ref uint[] uInt32Out,
        ref long[] int64Out,
        ref ulong[] uInt64Out,
        ref float[] floatOut,
        ref double[] doubleOut,
        ref string[] stringOut);
    #endif
    #endregion

    #region UserArrayValue2MethodState Class
    #if (!OPCUA_EXCLUDE_UserArrayValue2MethodState)
    /// <summary>
    /// Stores an instance of the UserArrayValue2MethodType Method.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserArrayValue2MethodState : MethodState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public UserArrayValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public new static NodeState Construct(NodeState parent)
        {
            return new UserArrayValue2MethodState(parent);
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
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGECCgQAAAABABkAAABVc2VyQXJyYXlW" +
           "YWx1ZTJNZXRob2RUeXBlAQFpJwAvaScAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQFqJwAuAERqJwAAlgoAAAABACoBAR8AAAAKAAAARGF0ZVRpbWVJbgEBtiYBAAAAAQAAAAAAAAAA" +
           "AQAqAQEbAAAABgAAAEd1aWRJbgEBtyYBAAAAAQAAAAAAAAAAAQAqAQEhAAAADAAAAEJ5dGVTdHJpbmdJ" +
           "bgEBuCYBAAAAAQAAAAAAAAAAAQAqAQEhAAAADAAAAFhtbEVsZW1lbnRJbgEBuSYBAAAAAQAAAAAAAAAA" +
           "AQAqAQEdAAAACAAAAE5vZGVJZEluAQG6JgEAAAABAAAAAAAAAAABACoBASUAAAAQAAAARXhwYW5kZWRO" +
           "b2RlSWRJbgEBuyYBAAAAAQAAAAAAAAAAAQAqAQEkAAAADwAAAFF1YWxpZmllZE5hbWVJbgEBvCYBAAAA" +
           "AQAAAAAAAAAAAQAqAQEkAAAADwAAAExvY2FsaXplZFRleHRJbgEBvSYBAAAAAQAAAAAAAAAAAQAqAQEh" +
           "AAAADAAAAFN0YXR1c0NvZGVJbgEBviYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAFZhcmlhbnRJbgEB" +
           "vyYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1" +
           "dEFyZ3VtZW50cwEBaycALgBEaycAAJYKAAAAAQAqAQEgAAAACwAAAERhdGVUaW1lT3V0AQG2JgEAAAAB" +
           "AAAAAAAAAAABACoBARwAAAAHAAAAR3VpZE91dAEBtyYBAAAAAQAAAAAAAAAAAQAqAQEiAAAADQAAAEJ5" +
           "dGVTdHJpbmdPdXQBAbgmAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0AAABYbWxFbGVtZW50T3V0AQG5JgEA" +
           "AAABAAAAAAAAAAABACoBAR4AAAAJAAAATm9kZUlkT3V0AQG6JgEAAAABAAAAAAAAAAABACoBASYAAAAR" +
           "AAAARXhwYW5kZWROb2RlSWRPdXQBAbsmAQAAAAEAAAAAAAAAAAEAKgEBJQAAABAAAABRdWFsaWZpZWRO" +
           "YW1lT3V0AQG8JgEAAAABAAAAAAAAAAABACoBASUAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBvSYBAAAA" +
           "AQAAAAAAAAAAAQAqAQEiAAAADQAAAFN0YXR1c0NvZGVPdXQBAb4mAQAAAAEAAAAAAAAAAAEAKgEBHwAA" +
           "AAoAAABWYXJpYW50T3V0AQG/JgEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <summary>
        /// Raised when the the method is called.
        /// </summary>
        public UserArrayValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Invokes the method, returns the result and output argument.
        /// </summary>
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

            ServiceResult result = null;

            DateTime[] dateTimeIn = (DateTime[])_inputArguments[0];
            Uuid[] guidIn = (Uuid[])_inputArguments[1];
            byte[][] byteStringIn = (byte[][])_inputArguments[2];
            XmlElement[] xmlElementIn = (XmlElement[])_inputArguments[3];
            NodeId[] nodeIdIn = (NodeId[])_inputArguments[4];
            ExpandedNodeId[] expandedNodeIdIn = (ExpandedNodeId[])_inputArguments[5];
            QualifiedName[] qualifiedNameIn = (QualifiedName[])_inputArguments[6];
            LocalizedText[] localizedTextIn = (LocalizedText[])_inputArguments[7];
            StatusCode[] statusCodeIn = (StatusCode[])_inputArguments[8];
            Variant[] variantIn = (Variant[])_inputArguments[9];

            DateTime[] dateTimeOut = (DateTime[])_outputArguments[0];
            Uuid[] guidOut = (Uuid[])_outputArguments[1];
            byte[][] byteStringOut = (byte[][])_outputArguments[2];
            XmlElement[] xmlElementOut = (XmlElement[])_outputArguments[3];
            NodeId[] nodeIdOut = (NodeId[])_outputArguments[4];
            ExpandedNodeId[] expandedNodeIdOut = (ExpandedNodeId[])_outputArguments[5];
            QualifiedName[] qualifiedNameOut = (QualifiedName[])_outputArguments[6];
            LocalizedText[] localizedTextOut = (LocalizedText[])_outputArguments[7];
            StatusCode[] statusCodeOut = (StatusCode[])_outputArguments[8];
            Variant[] variantOut = (Variant[])_outputArguments[9];

            if (OnCall != null)
            {
                result = OnCall(
                    _context,
                    this,
                    _objectId,
                    dateTimeIn,
                    guidIn,
                    byteStringIn,
                    xmlElementIn,
                    nodeIdIn,
                    expandedNodeIdIn,
                    qualifiedNameIn,
                    localizedTextIn,
                    statusCodeIn,
                    variantIn,
                    ref dateTimeOut,
                    ref guidOut,
                    ref byteStringOut,
                    ref xmlElementOut,
                    ref nodeIdOut,
                    ref expandedNodeIdOut,
                    ref qualifiedNameOut,
                    ref localizedTextOut,
                    ref statusCodeOut,
                    ref variantOut);
            }

            _outputArguments[0] = dateTimeOut;
            _outputArguments[1] = guidOut;
            _outputArguments[2] = byteStringOut;
            _outputArguments[3] = xmlElementOut;
            _outputArguments[4] = nodeIdOut;
            _outputArguments[5] = expandedNodeIdOut;
            _outputArguments[6] = qualifiedNameOut;
            _outputArguments[7] = localizedTextOut;
            _outputArguments[8] = statusCodeOut;
            _outputArguments[9] = variantOut;

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
    public delegate ServiceResult UserArrayValue2MethodStateMethodCallHandler(
        ISystemContext _context,
        MethodState _method,
        NodeId _objectId,
        DateTime[] dateTimeIn,
        Uuid[] guidIn,
        byte[][] byteStringIn,
        XmlElement[] xmlElementIn,
        NodeId[] nodeIdIn,
        ExpandedNodeId[] expandedNodeIdIn,
        QualifiedName[] qualifiedNameIn,
        LocalizedText[] localizedTextIn,
        StatusCode[] statusCodeIn,
        Variant[] variantIn,
        ref DateTime[] dateTimeOut,
        ref Uuid[] guidOut,
        ref byte[][] byteStringOut,
        ref XmlElement[] xmlElementOut,
        ref NodeId[] nodeIdOut,
        ref ExpandedNodeId[] expandedNodeIdOut,
        ref QualifiedName[] qualifiedNameOut,
        ref LocalizedText[] localizedTextOut,
        ref StatusCode[] statusCodeOut,
        ref Variant[] variantOut);
    #endif
    #endregion

    #region MethodTestState Class
    #if (!OPCUA_EXCLUDE_MethodTestState)
    /// <summary>
    /// Stores an instance of the MethodTestType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class MethodTestState : FolderState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public MethodTestState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.MethodTestType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABABYAAABNZXRob2RUZXN0" +
           "VHlwZUluc3RhbmNlAQFsJ2wnAAD/////CgAAAARhAgoEAAAAAQANAAAAU2NhbGFyTWV0aG9kMQEBbScA" +
           "L20nAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBbicALgBEbicAAJYLAAAA" +
           "AQAqAQEYAAAACQAAAEJvb2xlYW5JbgAB/////wAAAAAAAQAqAQEWAAAABwAAAFNCeXRlSW4AAv////8A" +
           "AAAAAAEAKgEBFQAAAAYAAABCeXRlSW4AA/////8AAAAAAAEAKgEBFgAAAAcAAABJbnQxNkluAAT/////" +
           "AAAAAAABACoBARcAAAAIAAAAVUludDE2SW4ABf////8AAAAAAAEAKgEBFgAAAAcAAABJbnQzMkluAAb/" +
           "////AAAAAAABACoBARcAAAAIAAAAVUludDMySW4AB/////8AAAAAAAEAKgEBFgAAAAcAAABJbnQ2NElu" +
           "AAj/////AAAAAAABACoBARcAAAAIAAAAVUludDY0SW4ACf////8AAAAAAAEAKgEBFgAAAAcAAABGbG9h" +
           "dEluAAr/////AAAAAAABACoBARcAAAAIAAAARG91YmxlSW4AC/////8AAAAAAAEAKAEBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAW8nAC4ARG8nAACWCwAAAAEA" +
           "KgEBGQAAAAoAAABCb29sZWFuT3V0AAH/////AAAAAAABACoBARcAAAAIAAAAU0J5dGVPdXQAAv////8A" +
           "AAAAAAEAKgEBFgAAAAcAAABCeXRlT3V0AAP/////AAAAAAABACoBARcAAAAIAAAASW50MTZPdXQABP//" +
           "//8AAAAAAAEAKgEBGAAAAAkAAABVSW50MTZPdXQABf////8AAAAAAAEAKgEBFwAAAAgAAABJbnQzMk91" +
           "dAAG/////wAAAAAAAQAqAQEYAAAACQAAAFVJbnQzMk91dAAH/////wAAAAAAAQAqAQEXAAAACAAAAElu" +
           "dDY0T3V0AAj/////AAAAAAABACoBARgAAAAJAAAAVUludDY0T3V0AAn/////AAAAAAABACoBARcAAAAI" +
           "AAAARmxvYXRPdXQACv////8AAAAAAAEAKgEBGAAAAAkAAABEb3VibGVPdXQAC/////8AAAAAAAEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAABGECCgQAAAABAA0AAABTY2FsYXJNZXRob2QyAQFwJwAvcCcAAAEB" +
           "/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFxJwAuAERxJwAAlgoAAAABACoBARcA" +
           "AAAIAAAAU3RyaW5nSW4ADP////8AAAAAAAEAKgEBGQAAAAoAAABEYXRlVGltZUluAA3/////AAAAAAAB" +
           "ACoBARUAAAAGAAAAR3VpZEluAA7/////AAAAAAABACoBARsAAAAMAAAAQnl0ZVN0cmluZ0luAA//////" +
           "AAAAAAABACoBARsAAAAMAAAAWG1sRWxlbWVudEluABD/////AAAAAAABACoBARcAAAAIAAAATm9kZUlk" +
           "SW4AEf////8AAAAAAAEAKgEBHwAAABAAAABFeHBhbmRlZE5vZGVJZEluABL/////AAAAAAABACoBAR4A" +
           "AAAPAAAAUXVhbGlmaWVkTmFtZUluABT/////AAAAAAABACoBAR4AAAAPAAAATG9jYWxpemVkVGV4dElu" +
           "ABX/////AAAAAAABACoBARsAAAAMAAAAU3RhdHVzQ29kZUluABP/////AAAAAAABACgBAQAAAAEAAAAA" +
           "AAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFyJwAuAERyJwAAlgoAAAAB" +
           "ACoBARgAAAAJAAAAU3RyaW5nT3V0AAz/////AAAAAAABACoBARoAAAALAAAARGF0ZVRpbWVPdXQADf//" +
           "//8AAAAAAAEAKgEBFgAAAAcAAABHdWlkT3V0AA7/////AAAAAAABACoBARwAAAANAAAAQnl0ZVN0cmlu" +
           "Z091dAAP/////wAAAAAAAQAqAQEcAAAADQAAAFhtbEVsZW1lbnRPdXQAEP////8AAAAAAAEAKgEBGAAA" +
           "AAkAAABOb2RlSWRPdXQAEf////8AAAAAAAEAKgEBIAAAABEAAABFeHBhbmRlZE5vZGVJZE91dAAS////" +
           "/wAAAAAAAQAqAQEfAAAAEAAAAFF1YWxpZmllZE5hbWVPdXQAFP////8AAAAAAAEAKgEBHwAAABAAAABM" +
           "b2NhbGl6ZWRUZXh0T3V0ABX/////AAAAAAABACoBARwAAAANAAAAU3RhdHVzQ29kZU91dAAT/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYQIKBAAAAAEADQAAAFNjYWxhck1ldGhvZDMBAXMn" +
           "AC9zJwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAXQnAC4ARHQnAACWAwAA" +
           "AAEAKgEBGAAAAAkAAABWYXJpYW50SW4AGP////8AAAAAAAEAKgEBHAAAAA0AAABFbnVtZXJhdGlvbklu" +
           "AB3/////AAAAAAABACoBARoAAAALAAAAU3RydWN0dXJlSW4AFv////8AAAAAAAEAKAEBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAXUnAC4ARHUnAACWAwAAAAEA" +
           "KgEBGQAAAAoAAABWYXJpYW50T3V0ABj/////AAAAAAABACoBAR0AAAAOAAAARW51bWVyYXRpb25PdXQA" +
           "Hf////8AAAAAAAEAKgEBGwAAAAwAAABTdHJ1Y3R1cmVPdXQAFv////8AAAAAAAEAKAEBAAAAAQAAAAAA" +
           "AAABAf////8AAAAABGECCgQAAAABAAwAAABBcnJheU1ldGhvZDEBAXYnAC92JwAAAQH/////AgAAABdg" +
           "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" +
           "/wAAAAAEYQIKBAAAAAEAEQAAAFVzZXJTY2FsYXJNZXRob2QxAQF/JwAvfycAAAEB/////wIAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQGAJwAuAESAJwAAlgwAAAABACoBARoAAAAJAAAAQm9vbGVh" +
           "bkluAQGqJv////8AAAAAAAEAKgEBGAAAAAcAAABTQnl0ZUluAQGrJv////8AAAAAAAEAKgEBFwAAAAYA" +
           "AABCeXRlSW4BAawm/////wAAAAAAAQAqAQEYAAAABwAAAEludDE2SW4BAa0m/////wAAAAAAAQAqAQEZ" +
           "AAAACAAAAFVJbnQxNkluAQGuJv////8AAAAAAAEAKgEBGAAAAAcAAABJbnQzMkluAQGvJv////8AAAAA" +
           "AAEAKgEBGQAAAAgAAABVSW50MzJJbgEBsCb/////AAAAAAABACoBARgAAAAHAAAASW50NjRJbgEBsSb/" +
           "////AAAAAAABACoBARkAAAAIAAAAVUludDY0SW4BAbIm/////wAAAAAAAQAqAQEYAAAABwAAAEZsb2F0" +
           "SW4BAbMm/////wAAAAAAAQAqAQEZAAAACAAAAERvdWJsZUluAQG0Jv////8AAAAAAAEAKgEBGQAAAAgA" +
           "AABTdHJpbmdJbgEBtSb/////AAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAP" +
           "AAAAT3V0cHV0QXJndW1lbnRzAQGBJwAuAESBJwAAlgwAAAABACoBARsAAAAKAAAAQm9vbGVhbk91dAEB" +
           "qib/////AAAAAAABACoBARkAAAAIAAAAU0J5dGVPdXQBAasm/////wAAAAAAAQAqAQEYAAAABwAAAEJ5" +
           "dGVPdXQBAawm/////wAAAAAAAQAqAQEZAAAACAAAAEludDE2T3V0AQGtJv////8AAAAAAAEAKgEBGgAA" +
           "AAkAAABVSW50MTZPdXQBAa4m/////wAAAAAAAQAqAQEZAAAACAAAAEludDMyT3V0AQGvJv////8AAAAA" +
           "AAEAKgEBGgAAAAkAAABVSW50MzJPdXQBAbAm/////wAAAAAAAQAqAQEZAAAACAAAAEludDY0T3V0AQGx" +
           "Jv////8AAAAAAAEAKgEBGgAAAAkAAABVSW50NjRPdXQBAbIm/////wAAAAAAAQAqAQEZAAAACAAAAEZs" +
           "b2F0T3V0AQGzJv////8AAAAAAAEAKgEBGgAAAAkAAABEb3VibGVPdXQBAbQm/////wAAAAAAAQAqAQEa" +
           "AAAACQAAAFN0cmluZ091dAEBtSb/////AAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhAgoE" +
           "AAAAAQARAAAAVXNlclNjYWxhck1ldGhvZDIBAYInAC+CJwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAA" +
           "SW5wdXRBcmd1bWVudHMBAYMnAC4ARIMnAACWCgAAAAEAKgEBGwAAAAoAAABEYXRlVGltZUluAQG2Jv//" +
           "//8AAAAAAAEAKgEBFwAAAAYAAABHdWlkSW4BAbcm/////wAAAAAAAQAqAQEdAAAADAAAAEJ5dGVTdHJp" +
           "bmdJbgEBuCb/////AAAAAAABACoBAR0AAAAMAAAAWG1sRWxlbWVudEluAQG5Jv////8AAAAAAAEAKgEB" +
           "GQAAAAgAAABOb2RlSWRJbgEBuib/////AAAAAAABACoBASEAAAAQAAAARXhwYW5kZWROb2RlSWRJbgEB" +
           "uyb/////AAAAAAABACoBASAAAAAPAAAAUXVhbGlmaWVkTmFtZUluAQG8Jv////8AAAAAAAEAKgEBIAAA" +
           "AA8AAABMb2NhbGl6ZWRUZXh0SW4BAb0m/////wAAAAAAAQAqAQEdAAAADAAAAFN0YXR1c0NvZGVJbgEB" +
           "vib/////AAAAAAABACoBARoAAAAJAAAAVmFyaWFudEluAQG/Jv////8AAAAAAAEAKAEBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAYQnAC4ARIQnAACWCgAAAAEA" +
           "KgEBHAAAAAsAAABEYXRlVGltZU91dAEBtib/////AAAAAAABACoBARgAAAAHAAAAR3VpZE91dAEBtyb/" +
           "////AAAAAAABACoBAR4AAAANAAAAQnl0ZVN0cmluZ091dAEBuCb/////AAAAAAABACoBAR4AAAANAAAA" +
           "WG1sRWxlbWVudE91dAEBuSb/////AAAAAAABACoBARoAAAAJAAAATm9kZUlkT3V0AQG6Jv////8AAAAA" +
           "AAEAKgEBIgAAABEAAABFeHBhbmRlZE5vZGVJZE91dAEBuyb/////AAAAAAABACoBASEAAAAQAAAAUXVh" +
           "bGlmaWVkTmFtZU91dAEBvCb/////AAAAAAABACoBASEAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBvSb/" +
           "////AAAAAAABACoBAR4AAAANAAAAU3RhdHVzQ29kZU91dAEBvib/////AAAAAAABACoBARsAAAAKAAAA" +
           "VmFyaWFudE91dAEBvyb/////AAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhAgoEAAAAAQAQ" +
           "AAAAVXNlckFycmF5TWV0aG9kMQEBhScAL4UnAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBhicALgBEhicAAJYMAAAAAQAqAQEeAAAACQAAAEJvb2xlYW5JbgEBqiYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEcAAAABwAAAFNCeXRlSW4BAasmAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAYAAABCeXRlSW4B" +
           "AawmAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQxNkluAQGtJgEAAAABAAAAAAAAAAABACoBAR0A" +
           "AAAIAAAAVUludDE2SW4BAa4mAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQzMkluAQGvJgEAAAAB" +
           "AAAAAAAAAAABACoBAR0AAAAIAAAAVUludDMySW4BAbAmAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJ" +
           "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" +
           "AAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public ScalarValue1MethodState ScalarMethod1
        {
            get
            {
                return m_scalarMethod1Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_scalarMethod1Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_scalarMethod1Method = value;
            }
        }

        /// <remarks />
        public ScalarValue2MethodState ScalarMethod2
        {
            get
            {
                return m_scalarMethod2Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_scalarMethod2Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_scalarMethod2Method = value;
            }
        }

        /// <remarks />
        public ScalarValue3MethodState ScalarMethod3
        {
            get
            {
                return m_scalarMethod3Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_scalarMethod3Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_scalarMethod3Method = value;
            }
        }

        /// <remarks />
        public ArrayValue1MethodState ArrayMethod1
        {
            get
            {
                return m_arrayMethod1Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_arrayMethod1Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_arrayMethod1Method = value;
            }
        }

        /// <remarks />
        public ArrayValue2MethodState ArrayMethod2
        {
            get
            {
                return m_arrayMethod2Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_arrayMethod2Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_arrayMethod2Method = value;
            }
        }

        /// <remarks />
        public ArrayValue3MethodState ArrayMethod3
        {
            get
            {
                return m_arrayMethod3Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_arrayMethod3Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_arrayMethod3Method = value;
            }
        }

        /// <remarks />
        public UserScalarValue1MethodState UserScalarMethod1
        {
            get
            {
                return m_userScalarMethod1Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_userScalarMethod1Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_userScalarMethod1Method = value;
            }
        }

        /// <remarks />
        public UserScalarValue2MethodState UserScalarMethod2
        {
            get
            {
                return m_userScalarMethod2Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_userScalarMethod2Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_userScalarMethod2Method = value;
            }
        }

        /// <remarks />
        public UserArrayValue1MethodState UserArrayMethod1
        {
            get
            {
                return m_userArrayMethod1Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_userArrayMethod1Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_userArrayMethod1Method = value;
            }
        }

        /// <remarks />
        public UserArrayValue2MethodState UserArrayMethod2
        {
            get
            {
                return m_userArrayMethod2Method;
            }

            set
            {
                if (!Object.ReferenceEquals(m_userArrayMethod2Method, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_userArrayMethod2Method = value;
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
            if (m_scalarMethod1Method != null)
            {
                children.Add(m_scalarMethod1Method);
            }

            if (m_scalarMethod2Method != null)
            {
                children.Add(m_scalarMethod2Method);
            }

            if (m_scalarMethod3Method != null)
            {
                children.Add(m_scalarMethod3Method);
            }

            if (m_arrayMethod1Method != null)
            {
                children.Add(m_arrayMethod1Method);
            }

            if (m_arrayMethod2Method != null)
            {
                children.Add(m_arrayMethod2Method);
            }

            if (m_arrayMethod3Method != null)
            {
                children.Add(m_arrayMethod3Method);
            }

            if (m_userScalarMethod1Method != null)
            {
                children.Add(m_userScalarMethod1Method);
            }

            if (m_userScalarMethod2Method != null)
            {
                children.Add(m_userScalarMethod2Method);
            }

            if (m_userArrayMethod1Method != null)
            {
                children.Add(m_userArrayMethod1Method);
            }

            if (m_userArrayMethod2Method != null)
            {
                children.Add(m_userArrayMethod2Method);
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
                case TestData.BrowseNames.ScalarMethod1:
                {
                    if (createOrReplace)
                    {
                        if (ScalarMethod1 == null)
                        {
                            if (replacement == null)
                            {
                                ScalarMethod1 = new ScalarValue1MethodState(this);
                            }
                            else
                            {
                                ScalarMethod1 = (ScalarValue1MethodState)replacement;
                            }
                        }
                    }

                    instance = ScalarMethod1;
                    break;
                }

                case TestData.BrowseNames.ScalarMethod2:
                {
                    if (createOrReplace)
                    {
                        if (ScalarMethod2 == null)
                        {
                            if (replacement == null)
                            {
                                ScalarMethod2 = new ScalarValue2MethodState(this);
                            }
                            else
                            {
                                ScalarMethod2 = (ScalarValue2MethodState)replacement;
                            }
                        }
                    }

                    instance = ScalarMethod2;
                    break;
                }

                case TestData.BrowseNames.ScalarMethod3:
                {
                    if (createOrReplace)
                    {
                        if (ScalarMethod3 == null)
                        {
                            if (replacement == null)
                            {
                                ScalarMethod3 = new ScalarValue3MethodState(this);
                            }
                            else
                            {
                                ScalarMethod3 = (ScalarValue3MethodState)replacement;
                            }
                        }
                    }

                    instance = ScalarMethod3;
                    break;
                }

                case TestData.BrowseNames.ArrayMethod1:
                {
                    if (createOrReplace)
                    {
                        if (ArrayMethod1 == null)
                        {
                            if (replacement == null)
                            {
                                ArrayMethod1 = new ArrayValue1MethodState(this);
                            }
                            else
                            {
                                ArrayMethod1 = (ArrayValue1MethodState)replacement;
                            }
                        }
                    }

                    instance = ArrayMethod1;
                    break;
                }

                case TestData.BrowseNames.ArrayMethod2:
                {
                    if (createOrReplace)
                    {
                        if (ArrayMethod2 == null)
                        {
                            if (replacement == null)
                            {
                                ArrayMethod2 = new ArrayValue2MethodState(this);
                            }
                            else
                            {
                                ArrayMethod2 = (ArrayValue2MethodState)replacement;
                            }
                        }
                    }

                    instance = ArrayMethod2;
                    break;
                }

                case TestData.BrowseNames.ArrayMethod3:
                {
                    if (createOrReplace)
                    {
                        if (ArrayMethod3 == null)
                        {
                            if (replacement == null)
                            {
                                ArrayMethod3 = new ArrayValue3MethodState(this);
                            }
                            else
                            {
                                ArrayMethod3 = (ArrayValue3MethodState)replacement;
                            }
                        }
                    }

                    instance = ArrayMethod3;
                    break;
                }

                case TestData.BrowseNames.UserScalarMethod1:
                {
                    if (createOrReplace)
                    {
                        if (UserScalarMethod1 == null)
                        {
                            if (replacement == null)
                            {
                                UserScalarMethod1 = new UserScalarValue1MethodState(this);
                            }
                            else
                            {
                                UserScalarMethod1 = (UserScalarValue1MethodState)replacement;
                            }
                        }
                    }

                    instance = UserScalarMethod1;
                    break;
                }

                case TestData.BrowseNames.UserScalarMethod2:
                {
                    if (createOrReplace)
                    {
                        if (UserScalarMethod2 == null)
                        {
                            if (replacement == null)
                            {
                                UserScalarMethod2 = new UserScalarValue2MethodState(this);
                            }
                            else
                            {
                                UserScalarMethod2 = (UserScalarValue2MethodState)replacement;
                            }
                        }
                    }

                    instance = UserScalarMethod2;
                    break;
                }

                case TestData.BrowseNames.UserArrayMethod1:
                {
                    if (createOrReplace)
                    {
                        if (UserArrayMethod1 == null)
                        {
                            if (replacement == null)
                            {
                                UserArrayMethod1 = new UserArrayValue1MethodState(this);
                            }
                            else
                            {
                                UserArrayMethod1 = (UserArrayValue1MethodState)replacement;
                            }
                        }
                    }

                    instance = UserArrayMethod1;
                    break;
                }

                case TestData.BrowseNames.UserArrayMethod2:
                {
                    if (createOrReplace)
                    {
                        if (UserArrayMethod2 == null)
                        {
                            if (replacement == null)
                            {
                                UserArrayMethod2 = new UserArrayValue2MethodState(this);
                            }
                            else
                            {
                                UserArrayMethod2 = (UserArrayValue2MethodState)replacement;
                            }
                        }
                    }

                    instance = UserArrayMethod2;
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
        private ScalarValue1MethodState m_scalarMethod1Method;
        private ScalarValue2MethodState m_scalarMethod2Method;
        private ScalarValue3MethodState m_scalarMethod3Method;
        private ArrayValue1MethodState m_arrayMethod1Method;
        private ArrayValue2MethodState m_arrayMethod2Method;
        private ArrayValue3MethodState m_arrayMethod3Method;
        private UserScalarValue1MethodState m_userScalarMethod1Method;
        private UserScalarValue2MethodState m_userScalarMethod2Method;
        private UserArrayValue1MethodState m_userArrayMethod1Method;
        private UserArrayValue2MethodState m_userArrayMethod2Method;
        #endregion
    }
    #endif
    #endregion

    #region TestSystemConditionState Class
    #if (!OPCUA_EXCLUDE_TestSystemConditionState)
    /// <summary>
    /// Stores an instance of the TestSystemConditionType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class TestSystemConditionState : ConditionState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public TestSystemConditionState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.TestSystemConditionType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGAAAgEAAAABAB8AAABUZXN0U3lzdGVt" +
           "Q29uZGl0aW9uVHlwZUluc3RhbmNlAQGLJ4snAAD/////FgAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEB" +
           "jCcALgBEjCcAAAAP/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBjScALgBEjScA" +
           "AAAR/////wEB/////wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAY4nAC4ARI4nAAAAEf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOYW1lAQGPJwAuAESPJwAAAAz/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAEAAAAVGltZQEBkCcALgBEkCcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAL" +
           "AAAAUmVjZWl2ZVRpbWUBAZEnAC4ARJEnAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1l" +
           "c3NhZ2UBAZMnAC4ARJMnAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBlCcA" +
           "LgBElCcAAAAF/////wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAUgtAC4A" +
           "REgtAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAUktAC4A" +
           "REktAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQEsLQAuAEQsLQAA" +
           "AAz/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAZUnAC4ARJUnAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAYAAABSZXRhaW4BAZYnAC4ARJYnAAAAAf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAwAAABFbmFibGVkU3RhdGUBAZcnAC8BACMjlycAAAAV/////wEB/////wEAAAAVYIkKAgAAAAAA" +
           "AgAAAElkAQGYJwAuAESYJwAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0eQEBnScA" +
           "LwEAKiOdJwAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQGeJwAu" +
           "AESeJwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAaEnAC8BACoj" +
           "oScAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBoicALgBEoicA" +
           "AAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBoycALwEAKiOjJwAAABX/////" +
           "AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQGkJwAuAESkJwAAAQAmAf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAaUnAC4ARKUnAAAADP////8BAf////8A" +
           "AAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQGnJwAvAQBEI6cnAAABAQEAAAABAPkLAAEA8woAAAAABGGC" +
           "CgQAAAAAAAYAAABFbmFibGUBAaYnAC8BAEMjpicAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIKBAAAAAAA" +
           "CgAAAEFkZENvbW1lbnQBAagnAC8BAEUjqCcAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAAAAAADgAA" +
           "AElucHV0QXJndW1lbnRzAQGpJwAuAESpJwAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAA" +
           "AAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAA" +
           "BwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25k" +
           "aXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAEAEgAAAE1vbml0b3JlZE5vZGVD" +
           "b3VudAEBrCcALgBErCcAAAAG/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<int> MonitoredNodeCount
        {
            get
            {
                return m_monitoredNodeCount;
            }

            set
            {
                if (!Object.ReferenceEquals(m_monitoredNodeCount, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_monitoredNodeCount = value;
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
            if (m_monitoredNodeCount != null)
            {
                children.Add(m_monitoredNodeCount);
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
                case TestData.BrowseNames.MonitoredNodeCount:
                {
                    if (createOrReplace)
                    {
                        if (MonitoredNodeCount == null)
                        {
                            if (replacement == null)
                            {
                                MonitoredNodeCount = new PropertyState<int>(this);
                            }
                            else
                            {
                                MonitoredNodeCount = (PropertyState<int>)replacement;
                            }
                        }
                    }

                    instance = MonitoredNodeCount;
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
        private PropertyState<int> m_monitoredNodeCount;
        #endregion
    }
    #endif
    #endregion
}