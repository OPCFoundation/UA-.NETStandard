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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GenerateValuesMethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public GenerateValuesMethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new GenerateValuesMethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABgAAABHZW5lcmF0ZVZh" +
           "bHVlc01ldGhvZFR5cGUBAZkkAC8BAZkkmSQAAAEB/////wEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJn" +
           "dW1lbnRzAQGaJAAuAESaJAAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0aW9ucwAH/////wAAAAADAAAA" +
           "ACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJhdGUuAQAoAQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public GenerateValuesMethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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

            uint iterations = (uint)_inputArguments[0];

            if (OnCall != null)
            {
                _result = OnCall(
                    _context,
                    this,
                    _objectId,
                    iterations);
            }

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class GenerateValuesEventState : BaseEventState
    {
        #region Constructors
        /// <remarks />
        public GenerateValuesEventState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.GenerateValuesEventType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABAB8AAABHZW5lcmF0ZVZh" +
           "bHVlc0V2ZW50VHlwZUluc3RhbmNlAQGbJAEBmySbJAAA/////woAAAAVYIkKAgAAAAAABwAAAEV2ZW50" +
           "SWQBAZwkAC4ARJwkAAAAD/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAZ0kAC4A" +
           "RJ0kAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQGeJAAuAESeJAAAABH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTmFtZQEBnyQALgBEnyQAAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAaAkAC4ARKAkAAABACYB/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACwAAAFJlY2VpdmVUaW1lAQGhJAAuAEShJAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcA" +
           "AABNZXNzYWdlAQGjJAAuAESjJAAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkB" +
           "AaQkAC4ARKQkAAAABf////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJdGVyYXRpb25zAQGlJAAuAESl" +
           "JAAAAAf/////AQH/////AAAAABVgiQoCAAAAAQANAAAATmV3VmFsdWVDb3VudAEBpiQALgBEpiQAAAAH" +
           "/////wEB/////wAAAAA=";
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
        /// <remarks />
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
            
        /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class TestDataObjectState : BaseObjectState
    {
        #region Constructors
        /// <remarks />
        public TestDataObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.TestDataObjectType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABABoAAABUZXN0RGF0YU9i" +
           "amVjdFR5cGVJbnN0YW5jZQEBpyQBAackpyQAAAEAAAAAJAABAaskAwAAADVgiQoCAAAAAQAQAAAAU2lt" +
           "dWxhdGlvbkFjdGl2ZQEBqCQDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9kdWNlIG5l" +
           "dyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAESoJAAAAAH/////AQH/////AAAA" +
           "AARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAakkAC8BAakkqSQAAAEB/////wEAAAAXYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQGqJAAuAESqJAAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0aW9u" +
           "cwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJhdGUuAQAo" +
           "AQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAaskAC8BAEEL" +
           "qyQAAAEAAAAAJAEBAackFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBrCQALgBErCQAAAAP/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBrSQALgBErSQAAAAR/////wEB/////wAAAAAV" +
           "YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAa4kAC4ARK4kAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAoAAABTb3VyY2VOYW1lAQGvJAAuAESvJAAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAEAAAAVGlt" +
           "ZQEBsCQALgBEsCQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUBAbEk" +
           "AC4ARLEkAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAbMkAC4ARLMkAAAA" +
           "Ff////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBtCQALgBEtCQAAAAF/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBATotAC4ARDotAAAAEf////8BAf////8A" +
           "AAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBATstAC4ARDstAAAAFf////8BAf////8A" +
           "AAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQElLQAuAEQlLQAAAAz/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAIAAAAQnJhbmNoSWQBAbUkAC4ARLUkAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAYA" +
           "AABSZXRhaW4BAbYkAC4ARLYkAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFibGVkU3Rh" +
           "dGUBAbckAC8BACMjtyQAAAAV/////wEBAgAAAAEALCMAAQHMJAEALCMAAQHUJAEAAAAVYIkKAgAAAAAA" +
           "AgAAAElkAQG4JAAuAES4JAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0eQEBvSQA" +
           "LwEAKiO9JAAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQG+JAAu" +
           "AES+JAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAcEkAC8BACoj" +
           "wSQAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBwiQALgBEwiQA" +
           "AAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBwyQALwEAKiPDJAAAABX/////" +
           "AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQHEJAAuAETEJAAAAQAmAf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAcUkAC4ARMUkAAAADP////8BAf////8A" +
           "AAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQHHJAAvAQBEI8ckAAABAQEAAAABAPkLAAEA8woAAAAABGGC" +
           "CgQAAAAAAAYAAABFbmFibGUBAcYkAC8BAEMjxiQAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIKBAAAAAAA" +
           "CgAAAEFkZENvbW1lbnQBAcgkAC8BAEUjyCQAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAAAAAADgAA" +
           "AElucHV0QXJndW1lbnRzAQHJJAAuAETJJAAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAA" +
           "AAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAA" +
           "BwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25k" +
           "aXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3RhdGUBAcwk" +
           "AC8BACMjzCQAAAAV/////wEBAQAAAAEALCMBAQG3JAEAAAAVYIkKAgAAAAAAAgAAAElkAQHNJAAuAETN" +
           "JAAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAdwkAC8BAJcj3CQAAAEB" +
           "AQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQHdJAAuAETdJAAAlgIA" +
           "AAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3Ig" +
           "dGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAA" +
           "VGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAA" +
           "AAA=";
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
        /// <remarks />
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
            
        /// <remarks />
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

    #region TestDataVariableState Class
    #if (!OPCUA_EXCLUDE_TestDataVariableState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class TestDataVariableState : BaseDataVariableState
    {
        #region Constructors
        /// <remarks />
        public TestDataVariableState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.VariableTypes.TestDataVariableType, TestData.Namespaces.TestData, namespaceUris);
        }

        /// <remarks />
        protected override NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.DataTypes.BaseDataType, Opc.Ua.Namespaces.OpcUa, namespaceUris);
        }

        /// <remarks />
        protected override int GetDefaultValueRank()
        {
            return ValueRanks.Scalar;
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////FWCBAgIAAAABABwAAABUZXN0RGF0YVZh" +
           "cmlhYmxlVHlwZUluc3RhbmNlAQHpAwEB6QPpAwAAABgBAf////8AAAAA";
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

    #region TestDataVariableState<T> Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public class TestDataVariableState<T> : TestDataVariableState
    {
        #region Constructors
        /// <remarks />
        public TestDataVariableState(NodeState parent) : base(parent)
        {
            Value = default(T);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Value = default(T);
            DataType = TypeInfo.GetDataTypeId(typeof(T));
            ValueRank = TypeInfo.GetValueRank(typeof(T));
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }
        #endregion

        #region Public Members
        /// <remarks />
        public new T Value
        {
            get
            {
                return CheckTypeBeforeCast<T>(((BaseVariableState)this).Value, true);
            }

            set
            {
                ((BaseVariableState)this).Value = value;
            }
        }
        #endregion
    }
    #endregion
    #endif
    #endregion

    #region ScalarValueVariableState Class
    #if (!OPCUA_EXCLUDE_ScalarValueVariableState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValueVariableState : TestDataVariableState<ScalarValueDataType>
    {
        #region Constructors
        /// <remarks />
        public ScalarValueVariableState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.VariableTypes.ScalarValueVariableType, TestData.Namespaces.TestData, namespaceUris);
        }

        /// <remarks />
        protected override NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.DataTypes.ScalarValueDataType, TestData.Namespaces.TestData, namespaceUris);
        }

        /// <remarks />
        protected override int GetDefaultValueRank()
        {
            return ValueRanks.Scalar;
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////FWCBAgIAAAABAB8AAABTY2FsYXJWYWx1" +
           "ZVZhcmlhYmxlVHlwZUluc3RhbmNlAQHqAwEB6gPqAwAAAQHgJAEB/////xsAAAAVYIkKAgAAAAEADAAA" +
           "AEJvb2xlYW5WYWx1ZQEB6wMALgBE6wMAAAAB/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAFNCeXRl" +
           "VmFsdWUBAewDAC4AROwDAAAAAv////8BAf////8AAAAAFWCJCgIAAAABAAkAAABCeXRlVmFsdWUBAe0D" +
           "AC4ARO0DAAAAA/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQxNlZhbHVlAQHuAwAuAETuAwAA" +
           "AAT/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDE2VmFsdWUBAe8DAC4ARO8DAAAABf////8B" +
           "Af////8AAAAAFWCJCgIAAAABAAoAAABJbnQzMlZhbHVlAQHwAwAuAETwAwAAAAb/////AQH/////AAAA" +
           "ABVgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBAfEDAC4ARPEDAAAAB/////8BAf////8AAAAAFWCJCgIA" +
           "AAABAAoAAABJbnQ2NFZhbHVlAQHyAwAuAETyAwAAAAj/////AQH/////AAAAABVgiQoCAAAAAQALAAAA" +
           "VUludDY0VmFsdWUBAfMDAC4ARPMDAAAACf////8BAf////8AAAAAFWCJCgIAAAABAAoAAABGbG9hdFZh" +
           "bHVlAQH0AwAuAET0AwAAAAr/////AQH/////AAAAABVgiQoCAAAAAQALAAAARG91YmxlVmFsdWUBAfUD" +
           "AC4ARPUDAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABTdHJpbmdWYWx1ZQEB9gMALgBE9gMA" +
           "AAAM/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAERhdGVUaW1lVmFsdWUBAfcDAC4ARPcDAAAADf//" +
           "//8BAf////8AAAAAFWCJCgIAAAABAAkAAABHdWlkVmFsdWUBAfgDAC4ARPgDAAAADv////8BAf////8A" +
           "AAAAFWCJCgIAAAABAA8AAABCeXRlU3RyaW5nVmFsdWUBAfkDAC4ARPkDAAAAD/////8BAf////8AAAAA" +
           "FWCJCgIAAAABAA8AAABYbWxFbGVtZW50VmFsdWUBAfoDAC4ARPoDAAAAEP////8BAf////8AAAAAFWCJ" +
           "CgIAAAABAAsAAABOb2RlSWRWYWx1ZQEB+wMALgBE+wMAAAAR/////wEB/////wAAAAAVYIkKAgAAAAEA" +
           "EwAAAEV4cGFuZGVkTm9kZUlkVmFsdWUBAfwDAC4ARPwDAAAAEv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "ABIAAABRdWFsaWZpZWROYW1lVmFsdWUBAf0DAC4ARP0DAAAAFP////8BAf////8AAAAAFWCJCgIAAAAB" +
           "ABIAAABMb2NhbGl6ZWRUZXh0VmFsdWUBAf4DAC4ARP4DAAAAFf////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AA8AAABTdGF0dXNDb2RlVmFsdWUBAf8DAC4ARP8DAAAAE/////8BAf////8AAAAAFWCJCgIAAAABAAwA" +
           "AABWYXJpYW50VmFsdWUBAQYEAC4ARAYEAAAAGP////8BAf////8AAAAAFWCJCgIAAAABABAAAABFbnVt" +
           "ZXJhdGlvblZhbHVlAQEHBAAuAEQHBAAAAB3/////AQH/////AAAAABVgiQoCAAAAAQAOAAAAU3RydWN0" +
           "dXJlVmFsdWUBAQgEAC4ARAgEAAAAFv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABOdW1iZXJWYWx1" +
           "ZQEBCQQALgBECQQAAAAa/////wEB/////wAAAAAVYIkKAgAAAAEADAAAAEludGVnZXJWYWx1ZQEBCgQA" +
           "LgBECgQAAAAb/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAFVJbnRlZ2VyVmFsdWUBAQsEAC4ARAsE" +
           "AAAAHP////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<bool> BooleanValue
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
        public PropertyState<sbyte> SByteValue
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
        public PropertyState<byte> ByteValue
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
        public PropertyState<short> Int16Value
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
        public PropertyState<ushort> UInt16Value
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
        public PropertyState<int> Int32Value
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
        public PropertyState<uint> UInt32Value
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
        public PropertyState<long> Int64Value
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
        public PropertyState<ulong> UInt64Value
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
        public PropertyState<float> FloatValue
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
        public PropertyState<double> DoubleValue
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
        public PropertyState<string> StringValue
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
        public PropertyState<DateTime> DateTimeValue
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
        public PropertyState<Guid> GuidValue
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
        public PropertyState<byte[]> ByteStringValue
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
        public PropertyState<XmlElement> XmlElementValue
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
        public PropertyState<NodeId> NodeIdValue
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
        public PropertyState<ExpandedNodeId> ExpandedNodeIdValue
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
        public PropertyState<QualifiedName> QualifiedNameValue
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
        public PropertyState<LocalizedText> LocalizedTextValue
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
        public PropertyState<StatusCode> StatusCodeValue
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
        public PropertyState VariantValue
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
        public PropertyState EnumerationValue
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
        public PropertyState<ExtensionObject> StructureValue
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
        public PropertyState NumberValue
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
        public PropertyState IntegerValue
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
        public PropertyState UIntegerValue
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
        /// <remarks />
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
            
        /// <remarks />
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
                                BooleanValue = new PropertyState<bool>(this);
                            }
                            else
                            {
                                BooleanValue = (PropertyState<bool>)replacement;
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
                                SByteValue = new PropertyState<sbyte>(this);
                            }
                            else
                            {
                                SByteValue = (PropertyState<sbyte>)replacement;
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
                                ByteValue = new PropertyState<byte>(this);
                            }
                            else
                            {
                                ByteValue = (PropertyState<byte>)replacement;
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
                                Int16Value = new PropertyState<short>(this);
                            }
                            else
                            {
                                Int16Value = (PropertyState<short>)replacement;
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
                                UInt16Value = new PropertyState<ushort>(this);
                            }
                            else
                            {
                                UInt16Value = (PropertyState<ushort>)replacement;
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
                                Int32Value = new PropertyState<int>(this);
                            }
                            else
                            {
                                Int32Value = (PropertyState<int>)replacement;
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
                                UInt32Value = new PropertyState<uint>(this);
                            }
                            else
                            {
                                UInt32Value = (PropertyState<uint>)replacement;
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
                                Int64Value = new PropertyState<long>(this);
                            }
                            else
                            {
                                Int64Value = (PropertyState<long>)replacement;
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
                                UInt64Value = new PropertyState<ulong>(this);
                            }
                            else
                            {
                                UInt64Value = (PropertyState<ulong>)replacement;
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
                                FloatValue = new PropertyState<float>(this);
                            }
                            else
                            {
                                FloatValue = (PropertyState<float>)replacement;
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
                                DoubleValue = new PropertyState<double>(this);
                            }
                            else
                            {
                                DoubleValue = (PropertyState<double>)replacement;
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
                                StringValue = new PropertyState<string>(this);
                            }
                            else
                            {
                                StringValue = (PropertyState<string>)replacement;
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
                                DateTimeValue = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                DateTimeValue = (PropertyState<DateTime>)replacement;
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
                                GuidValue = new PropertyState<Guid>(this);
                            }
                            else
                            {
                                GuidValue = (PropertyState<Guid>)replacement;
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
                                ByteStringValue = new PropertyState<byte[]>(this);
                            }
                            else
                            {
                                ByteStringValue = (PropertyState<byte[]>)replacement;
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
                                XmlElementValue = new PropertyState<XmlElement>(this);
                            }
                            else
                            {
                                XmlElementValue = (PropertyState<XmlElement>)replacement;
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
                                NodeIdValue = new PropertyState<NodeId>(this);
                            }
                            else
                            {
                                NodeIdValue = (PropertyState<NodeId>)replacement;
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
                                ExpandedNodeIdValue = new PropertyState<ExpandedNodeId>(this);
                            }
                            else
                            {
                                ExpandedNodeIdValue = (PropertyState<ExpandedNodeId>)replacement;
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
                                QualifiedNameValue = new PropertyState<QualifiedName>(this);
                            }
                            else
                            {
                                QualifiedNameValue = (PropertyState<QualifiedName>)replacement;
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
                                LocalizedTextValue = new PropertyState<LocalizedText>(this);
                            }
                            else
                            {
                                LocalizedTextValue = (PropertyState<LocalizedText>)replacement;
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
                                StatusCodeValue = new PropertyState<StatusCode>(this);
                            }
                            else
                            {
                                StatusCodeValue = (PropertyState<StatusCode>)replacement;
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
                                VariantValue = new PropertyState(this);
                            }
                            else
                            {
                                VariantValue = (PropertyState)replacement;
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
                                EnumerationValue = new PropertyState(this);
                            }
                            else
                            {
                                EnumerationValue = (PropertyState)replacement;
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
                                StructureValue = new PropertyState<ExtensionObject>(this);
                            }
                            else
                            {
                                StructureValue = (PropertyState<ExtensionObject>)replacement;
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
                                NumberValue = new PropertyState(this);
                            }
                            else
                            {
                                NumberValue = (PropertyState)replacement;
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
                                IntegerValue = new PropertyState(this);
                            }
                            else
                            {
                                IntegerValue = (PropertyState)replacement;
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
                                UIntegerValue = new PropertyState(this);
                            }
                            else
                            {
                                UIntegerValue = (PropertyState)replacement;
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
        private PropertyState<bool> m_booleanValue;
        private PropertyState<sbyte> m_sByteValue;
        private PropertyState<byte> m_byteValue;
        private PropertyState<short> m_int16Value;
        private PropertyState<ushort> m_uInt16Value;
        private PropertyState<int> m_int32Value;
        private PropertyState<uint> m_uInt32Value;
        private PropertyState<long> m_int64Value;
        private PropertyState<ulong> m_uInt64Value;
        private PropertyState<float> m_floatValue;
        private PropertyState<double> m_doubleValue;
        private PropertyState<string> m_stringValue;
        private PropertyState<DateTime> m_dateTimeValue;
        private PropertyState<Guid> m_guidValue;
        private PropertyState<byte[]> m_byteStringValue;
        private PropertyState<XmlElement> m_xmlElementValue;
        private PropertyState<NodeId> m_nodeIdValue;
        private PropertyState<ExpandedNodeId> m_expandedNodeIdValue;
        private PropertyState<QualifiedName> m_qualifiedNameValue;
        private PropertyState<LocalizedText> m_localizedTextValue;
        private PropertyState<StatusCode> m_statusCodeValue;
        private PropertyState m_variantValue;
        private PropertyState m_enumerationValue;
        private PropertyState<ExtensionObject> m_structureValue;
        private PropertyState m_numberValue;
        private PropertyState m_integerValue;
        private PropertyState m_uIntegerValue;
        #endregion
    }

    #region ScalarValueVariableValue Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public class ScalarValueVariableValue : BaseVariableValue
    {
        #region Constructors
        /// <remarks />
        public ScalarValueVariableValue(ScalarValueVariableState variable, ScalarValueDataType value, object dataLock) : base(dataLock)
        {
            m_value = value;

            if (m_value == null)
            {
                m_value = new ScalarValueDataType();
            }

            Initialize(variable);
        }
        #endregion

        #region Public Members
        /// <remarks />
        public ScalarValueVariableState Variable
        {
            get { return m_variable; }
        }

        /// <remarks />
        public ScalarValueDataType Value
        {
            get { return m_value;  }
            set { m_value = value; }
        }
        #endregion

        #region Private Methods
        private void Initialize(ScalarValueVariableState variable)
        {
            lock (Lock)
            {
                m_variable = variable;

                variable.Value = m_value;

                variable.OnReadValue = OnReadValue;
                variable.OnSimpleWriteValue = OnWriteValue;

                BaseVariableState instance = null;
                List<BaseInstanceState> updateList = new List<BaseInstanceState>();
                updateList.Add(variable);

                instance = m_variable.BooleanValue;
                instance.OnReadValue = OnRead_BooleanValue;
                instance.OnSimpleWriteValue = OnWrite_BooleanValue;
                updateList.Add(instance);
                instance = m_variable.SByteValue;
                instance.OnReadValue = OnRead_SByteValue;
                instance.OnSimpleWriteValue = OnWrite_SByteValue;
                updateList.Add(instance);
                instance = m_variable.ByteValue;
                instance.OnReadValue = OnRead_ByteValue;
                instance.OnSimpleWriteValue = OnWrite_ByteValue;
                updateList.Add(instance);
                instance = m_variable.Int16Value;
                instance.OnReadValue = OnRead_Int16Value;
                instance.OnSimpleWriteValue = OnWrite_Int16Value;
                updateList.Add(instance);
                instance = m_variable.UInt16Value;
                instance.OnReadValue = OnRead_UInt16Value;
                instance.OnSimpleWriteValue = OnWrite_UInt16Value;
                updateList.Add(instance);
                instance = m_variable.Int32Value;
                instance.OnReadValue = OnRead_Int32Value;
                instance.OnSimpleWriteValue = OnWrite_Int32Value;
                updateList.Add(instance);
                instance = m_variable.UInt32Value;
                instance.OnReadValue = OnRead_UInt32Value;
                instance.OnSimpleWriteValue = OnWrite_UInt32Value;
                updateList.Add(instance);
                instance = m_variable.Int64Value;
                instance.OnReadValue = OnRead_Int64Value;
                instance.OnSimpleWriteValue = OnWrite_Int64Value;
                updateList.Add(instance);
                instance = m_variable.UInt64Value;
                instance.OnReadValue = OnRead_UInt64Value;
                instance.OnSimpleWriteValue = OnWrite_UInt64Value;
                updateList.Add(instance);
                instance = m_variable.FloatValue;
                instance.OnReadValue = OnRead_FloatValue;
                instance.OnSimpleWriteValue = OnWrite_FloatValue;
                updateList.Add(instance);
                instance = m_variable.DoubleValue;
                instance.OnReadValue = OnRead_DoubleValue;
                instance.OnSimpleWriteValue = OnWrite_DoubleValue;
                updateList.Add(instance);
                instance = m_variable.StringValue;
                instance.OnReadValue = OnRead_StringValue;
                instance.OnSimpleWriteValue = OnWrite_StringValue;
                updateList.Add(instance);
                instance = m_variable.DateTimeValue;
                instance.OnReadValue = OnRead_DateTimeValue;
                instance.OnSimpleWriteValue = OnWrite_DateTimeValue;
                updateList.Add(instance);
                instance = m_variable.GuidValue;
                instance.OnReadValue = OnRead_GuidValue;
                instance.OnSimpleWriteValue = OnWrite_GuidValue;
                updateList.Add(instance);
                instance = m_variable.ByteStringValue;
                instance.OnReadValue = OnRead_ByteStringValue;
                instance.OnSimpleWriteValue = OnWrite_ByteStringValue;
                updateList.Add(instance);
                instance = m_variable.XmlElementValue;
                instance.OnReadValue = OnRead_XmlElementValue;
                instance.OnSimpleWriteValue = OnWrite_XmlElementValue;
                updateList.Add(instance);
                instance = m_variable.NodeIdValue;
                instance.OnReadValue = OnRead_NodeIdValue;
                instance.OnSimpleWriteValue = OnWrite_NodeIdValue;
                updateList.Add(instance);
                instance = m_variable.ExpandedNodeIdValue;
                instance.OnReadValue = OnRead_ExpandedNodeIdValue;
                instance.OnSimpleWriteValue = OnWrite_ExpandedNodeIdValue;
                updateList.Add(instance);
                instance = m_variable.QualifiedNameValue;
                instance.OnReadValue = OnRead_QualifiedNameValue;
                instance.OnSimpleWriteValue = OnWrite_QualifiedNameValue;
                updateList.Add(instance);
                instance = m_variable.LocalizedTextValue;
                instance.OnReadValue = OnRead_LocalizedTextValue;
                instance.OnSimpleWriteValue = OnWrite_LocalizedTextValue;
                updateList.Add(instance);
                instance = m_variable.StatusCodeValue;
                instance.OnReadValue = OnRead_StatusCodeValue;
                instance.OnSimpleWriteValue = OnWrite_StatusCodeValue;
                updateList.Add(instance);
                instance = m_variable.VariantValue;
                instance.OnReadValue = OnRead_VariantValue;
                instance.OnSimpleWriteValue = OnWrite_VariantValue;
                updateList.Add(instance);
                instance = m_variable.EnumerationValue;
                instance.OnReadValue = OnRead_EnumerationValue;
                instance.OnSimpleWriteValue = OnWrite_EnumerationValue;
                updateList.Add(instance);
                instance = m_variable.StructureValue;
                instance.OnReadValue = OnRead_StructureValue;
                instance.OnSimpleWriteValue = OnWrite_StructureValue;
                updateList.Add(instance);
                instance = m_variable.NumberValue;
                instance.OnReadValue = OnRead_NumberValue;
                instance.OnSimpleWriteValue = OnWrite_NumberValue;
                updateList.Add(instance);
                instance = m_variable.IntegerValue;
                instance.OnReadValue = OnRead_IntegerValue;
                instance.OnSimpleWriteValue = OnWrite_IntegerValue;
                updateList.Add(instance);
                instance = m_variable.UIntegerValue;
                instance.OnReadValue = OnRead_UIntegerValue;
                instance.OnSimpleWriteValue = OnWrite_UIntegerValue;
                updateList.Add(instance);

                SetUpdateList(updateList);
            }
        }

        /// <remarks />
        protected ServiceResult OnReadValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        private ServiceResult OnWriteValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value = (ScalarValueDataType)Write(value);
            }

            return ServiceResult.Good;
        }

        #region BooleanValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_BooleanValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.BooleanValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_BooleanValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.BooleanValue = (bool)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region SByteValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_SByteValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.SByteValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_SByteValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.SByteValue = (sbyte)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region ByteValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_ByteValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.ByteValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_ByteValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.ByteValue = (byte)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Int16Value Access Methods
        /// <remarks />
        private ServiceResult OnRead_Int16Value(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.Int16Value;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Int16Value(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.Int16Value = (short)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region UInt16Value Access Methods
        /// <remarks />
        private ServiceResult OnRead_UInt16Value(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.UInt16Value;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UInt16Value(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.UInt16Value = (ushort)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Int32Value Access Methods
        /// <remarks />
        private ServiceResult OnRead_Int32Value(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.Int32Value;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Int32Value(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.Int32Value = (int)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region UInt32Value Access Methods
        /// <remarks />
        private ServiceResult OnRead_UInt32Value(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.UInt32Value;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UInt32Value(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.UInt32Value = (uint)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Int64Value Access Methods
        /// <remarks />
        private ServiceResult OnRead_Int64Value(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.Int64Value;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Int64Value(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.Int64Value = (long)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region UInt64Value Access Methods
        /// <remarks />
        private ServiceResult OnRead_UInt64Value(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.UInt64Value;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UInt64Value(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.UInt64Value = (ulong)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region FloatValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_FloatValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.FloatValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_FloatValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.FloatValue = (float)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region DoubleValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_DoubleValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.DoubleValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_DoubleValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.DoubleValue = (double)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region StringValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_StringValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.StringValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_StringValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.StringValue = (string)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region DateTimeValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_DateTimeValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.DateTimeValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_DateTimeValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.DateTimeValue = (DateTime)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region GuidValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_GuidValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.GuidValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_GuidValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.GuidValue = (Uuid)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region ByteStringValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_ByteStringValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.ByteStringValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_ByteStringValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.ByteStringValue = (byte[])Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region XmlElementValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_XmlElementValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.XmlElementValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_XmlElementValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.XmlElementValue = (XmlElement)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region NodeIdValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_NodeIdValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.NodeIdValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_NodeIdValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.NodeIdValue = (NodeId)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region ExpandedNodeIdValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_ExpandedNodeIdValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.ExpandedNodeIdValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_ExpandedNodeIdValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.ExpandedNodeIdValue = (ExpandedNodeId)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region QualifiedNameValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_QualifiedNameValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.QualifiedNameValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_QualifiedNameValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.QualifiedNameValue = (QualifiedName)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region LocalizedTextValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_LocalizedTextValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.LocalizedTextValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_LocalizedTextValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.LocalizedTextValue = (LocalizedText)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region StatusCodeValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_StatusCodeValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.StatusCodeValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_StatusCodeValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.StatusCodeValue = (StatusCode)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region VariantValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_VariantValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.VariantValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_VariantValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.VariantValue = (Variant)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region EnumerationValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_EnumerationValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.EnumerationValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_EnumerationValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.EnumerationValue = (int)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region StructureValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_StructureValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.StructureValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_StructureValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.StructureValue = (ExtensionObject)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region NumberValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_NumberValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.NumberValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_NumberValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.NumberValue = (Variant)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region IntegerValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_IntegerValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.IntegerValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_IntegerValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.IntegerValue = (Variant)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region UIntegerValue Access Methods
        /// <remarks />
        private ServiceResult OnRead_UIntegerValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                DoBeforeReadProcessing(context, node);

                if (m_value != null)
                {
                    value = m_value.UIntegerValue;
                }

                return Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UIntegerValue(ISystemContext context, NodeState node, ref object value)
        {
            lock (Lock)
            {
                m_value.UIntegerValue = (Variant)Write(value);
            }

            return ServiceResult.Good;
        }
        #endregion
        #endregion

        #region Private Fields
        private ScalarValueDataType m_value;
        private ScalarValueVariableState m_variable;
        #endregion
    }
    #endregion
    #endif
    #endregion

    #region ScalarValue1MethodState Class
    #if (!OPCUA_EXCLUDE_ScalarValue1MethodState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValue1MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ScalarValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ScalarValue1MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABYAAABTY2FsYXJWYWx1" +
           "ZTFNZXRob2RUeXBlAQHhJAAvAQHhJOEkAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEB4iQALgBE4iQAAJYLAAAAAQAqAQEYAAAACQAAAEJvb2xlYW5JbgAB/////wAAAAAAAQAqAQEW" +
           "AAAABwAAAFNCeXRlSW4AAv////8AAAAAAAEAKgEBFQAAAAYAAABCeXRlSW4AA/////8AAAAAAAEAKgEB" +
           "FgAAAAcAAABJbnQxNkluAAT/////AAAAAAABACoBARcAAAAIAAAAVUludDE2SW4ABf////8AAAAAAAEA" +
           "KgEBFgAAAAcAAABJbnQzMkluAAb/////AAAAAAABACoBARcAAAAIAAAAVUludDMySW4AB/////8AAAAA" +
           "AAEAKgEBFgAAAAcAAABJbnQ2NEluAAj/////AAAAAAABACoBARcAAAAIAAAAVUludDY0SW4ACf////8A" +
           "AAAAAAEAKgEBFgAAAAcAAABGbG9hdEluAAr/////AAAAAAABACoBARcAAAAIAAAARG91YmxlSW4AC///" +
           "//8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVu" +
           "dHMBAeMkAC4AROMkAACWCwAAAAEAKgEBGQAAAAoAAABCb29sZWFuT3V0AAH/////AAAAAAABACoBARcA" +
           "AAAIAAAAU0J5dGVPdXQAAv////8AAAAAAAEAKgEBFgAAAAcAAABCeXRlT3V0AAP/////AAAAAAABACoB" +
           "ARcAAAAIAAAASW50MTZPdXQABP////8AAAAAAAEAKgEBGAAAAAkAAABVSW50MTZPdXQABf////8AAAAA" +
           "AAEAKgEBFwAAAAgAAABJbnQzMk91dAAG/////wAAAAAAAQAqAQEYAAAACQAAAFVJbnQzMk91dAAH////" +
           "/wAAAAAAAQAqAQEXAAAACAAAAEludDY0T3V0AAj/////AAAAAAABACoBARgAAAAJAAAAVUludDY0T3V0" +
           "AAn/////AAAAAAABACoBARcAAAAIAAAARmxvYXRPdXQACv////8AAAAAAAEAKgEBGAAAAAkAAABEb3Vi" +
           "bGVPdXQAC/////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ScalarValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValue2MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ScalarValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ScalarValue2MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABYAAABTY2FsYXJWYWx1" +
           "ZTJNZXRob2RUeXBlAQHkJAAvAQHkJOQkAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEB5SQALgBE5SQAAJYKAAAAAQAqAQEXAAAACAAAAFN0cmluZ0luAAz/////AAAAAAABACoBARkA" +
           "AAAKAAAARGF0ZVRpbWVJbgAN/////wAAAAAAAQAqAQEVAAAABgAAAEd1aWRJbgAO/////wAAAAAAAQAq" +
           "AQEbAAAADAAAAEJ5dGVTdHJpbmdJbgAP/////wAAAAAAAQAqAQEbAAAADAAAAFhtbEVsZW1lbnRJbgAQ" +
           "/////wAAAAAAAQAqAQEXAAAACAAAAE5vZGVJZEluABH/////AAAAAAABACoBAR8AAAAQAAAARXhwYW5k" +
           "ZWROb2RlSWRJbgAS/////wAAAAAAAQAqAQEeAAAADwAAAFF1YWxpZmllZE5hbWVJbgAU/////wAAAAAA" +
           "AQAqAQEeAAAADwAAAExvY2FsaXplZFRleHRJbgAV/////wAAAAAAAQAqAQEbAAAADAAAAFN0YXR1c0Nv" +
           "ZGVJbgAT/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1" +
           "dEFyZ3VtZW50cwEB5iQALgBE5iQAAJYKAAAAAQAqAQEYAAAACQAAAFN0cmluZ091dAAM/////wAAAAAA" +
           "AQAqAQEaAAAACwAAAERhdGVUaW1lT3V0AA3/////AAAAAAABACoBARYAAAAHAAAAR3VpZE91dAAO////" +
           "/wAAAAAAAQAqAQEcAAAADQAAAEJ5dGVTdHJpbmdPdXQAD/////8AAAAAAAEAKgEBHAAAAA0AAABYbWxF" +
           "bGVtZW50T3V0ABD/////AAAAAAABACoBARgAAAAJAAAATm9kZUlkT3V0ABH/////AAAAAAABACoBASAA" +
           "AAARAAAARXhwYW5kZWROb2RlSWRPdXQAEv////8AAAAAAAEAKgEBHwAAABAAAABRdWFsaWZpZWROYW1l" +
           "T3V0ABT/////AAAAAAABACoBAR8AAAAQAAAATG9jYWxpemVkVGV4dE91dAAV/////wAAAAAAAQAqAQEc" +
           "AAAADQAAAFN0YXR1c0NvZGVPdXQAE/////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ScalarValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValue3MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ScalarValue3MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ScalarValue3MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABYAAABTY2FsYXJWYWx1" +
           "ZTNNZXRob2RUeXBlAQHnJAAvAQHnJOckAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEB6CQALgBE6CQAAJYDAAAAAQAqAQEYAAAACQAAAFZhcmlhbnRJbgAY/////wAAAAAAAQAqAQEc" +
           "AAAADQAAAEVudW1lcmF0aW9uSW4AHf////8AAAAAAAEAKgEBGgAAAAsAAABTdHJ1Y3R1cmVJbgAW////" +
           "/wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50" +
           "cwEB6SQALgBE6SQAAJYDAAAAAQAqAQEZAAAACgAAAFZhcmlhbnRPdXQAGP////8AAAAAAAEAKgEBHQAA" +
           "AA4AAABFbnVtZXJhdGlvbk91dAAd/////wAAAAAAAQAqAQEbAAAADAAAAFN0cnVjdHVyZU91dAAW////" +
           "/wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ScalarValue3MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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

            object variantIn = (object)_inputArguments[0];
            int enumerationIn = (int)_inputArguments[1];
            ExtensionObject structureIn = (ExtensionObject)_inputArguments[2];

            object variantOut = (object)_outputArguments[0];
            int enumerationOut = (int)_outputArguments[1];
            ExtensionObject structureOut = (ExtensionObject)_outputArguments[2];

            if (OnCall != null)
            {
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <remarks />
        public ScalarValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.ScalarValueObjectType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABAB0AAABTY2FsYXJWYWx1" +
           "ZU9iamVjdFR5cGVJbnN0YW5jZQEB6iQBAeok6iQAAAEAAAAAJAABAe4kHgAAADVgiQoCAAAAAQAQAAAA" +
           "U2ltdWxhdGlvbkFjdGl2ZQEB6yQDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9kdWNl" +
           "IG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAETrJAAAAAH/////AQH/////" +
           "AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAewkAC8BAakk7CQAAAEB/////wEAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQHtJAAuAETtJAAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0" +
           "aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJhdGUu" +
           "AQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAe4kAC8B" +
           "AEEL7iQAAAEAAAAAJAEBAeokFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEB7yQALgBE7yQAAAAP////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEB8CQALgBE8CQAAAAR/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAfEkAC4ARPEkAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAoAAABTb3VyY2VOYW1lAQHyJAAuAETyJAAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAEAAAA" +
           "VGltZQEB8yQALgBE8yQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUB" +
           "AfQkAC4ARPQkAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAfYkAC4ARPYk" +
           "AAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEB9yQALgBE9yQAAAAF/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBATwtAC4ARDwtAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAT0tAC4ARD0tAAAAFf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQEmLQAuAEQmLQAAAAz/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAfgkAC4ARPgkAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAYAAABSZXRhaW4BAfkkAC4ARPkkAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFibGVk" +
           "U3RhdGUBAfokAC8BACMj+iQAAAAV/////wEBAgAAAAEALCMAAQEPJQEALCMAAQEXJQEAAAAVYIkKAgAA" +
           "AAAAAgAAAElkAQH7JAAuAET7JAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0eQEB" +
           "ACUALwEAKiMAJQAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEB" +
           "JQAuAEQBJQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAQQlAC8B" +
           "ACojBCUAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBBSUALgBE" +
           "BSUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBBiUALwEAKiMGJQAAABX/" +
           "////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEHJQAuAEQHJQAAAQAmAf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAQglAC4ARAglAAAADP////8BAf//" +
           "//8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQEKJQAvAQBEIwolAAABAQEAAAABAPkLAAEA8woAAAAA" +
           "BGGCCgQAAAAAAAYAAABFbmFibGUBAQklAC8BAEMjCSUAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIKBAAA" +
           "AAAACgAAAEFkZENvbW1lbnQBAQslAC8BAEUjCyUAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAAAAAA" +
           "DgAAAElucHV0QXJndW1lbnRzAQEMJQAuAEQMJQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP////" +
           "/wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFC" +
           "AAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBj" +
           "b25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3RhdGUB" +
           "AQ8lAC8BACMjDyUAAAAV/////wEBAQAAAAEALCMBAQH6JAEAAAAVYIkKAgAAAAAAAgAAAElkAQEQJQAu" +
           "AEQQJQAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAR8lAC8BAJcjHyUA" +
           "AAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEgJQAuAEQgJQAA" +
           "lgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBm" +
           "b3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAk" +
           "AAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAVYIkKAgAAAAEADAAAAEJvb2xlYW5WYWx1ZQEBIyUALwA/IyUAAAAB/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBASQlAC8APyQlAAAAAv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AAkAAABCeXRlVmFsdWUBASUlAC8APyUlAAAAA/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQx" +
           "NlZhbHVlAQEmJQAvAD8mJQAAAAT/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDE2VmFsdWUB" +
           "ASclAC8APyclAAAABf////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQzMlZhbHVlAQEoJQAvAD8o" +
           "JQAAAAb/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBASklAC8APyklAAAAB///" +
           "//8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2NFZhbHVlAQEqJQAvAD8qJQAAAAj/////AQH/////" +
           "AAAAABVgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBASslAC8APyslAAAACf////8BAf////8AAAAAFWCJ" +
           "CgIAAAABAAoAAABGbG9hdFZhbHVlAQEsJQAvAD8sJQAAAAr/////AQH/////AAAAABVgiQoCAAAAAQAL" +
           "AAAARG91YmxlVmFsdWUBAS0lAC8APy0lAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABTdHJp" +
           "bmdWYWx1ZQEBLiUALwA/LiUAAAAM/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAERhdGVUaW1lVmFs" +
           "dWUBAS8lAC8APy8lAAAADf////8BAf////8AAAAAFWCJCgIAAAABAAkAAABHdWlkVmFsdWUBATAlAC8A" +
           "PzAlAAAADv////8BAf////8AAAAAFWCJCgIAAAABAA8AAABCeXRlU3RyaW5nVmFsdWUBATElAC8APzEl" +
           "AAAAD/////8BAf////8AAAAAFWCJCgIAAAABAA8AAABYbWxFbGVtZW50VmFsdWUBATIlAC8APzIlAAAA" +
           "EP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABOb2RlSWRWYWx1ZQEBMyUALwA/MyUAAAAR/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEAEwAAAEV4cGFuZGVkTm9kZUlkVmFsdWUBATQlAC8APzQlAAAAEv////8B" +
           "Af////8AAAAAFWCJCgIAAAABABIAAABRdWFsaWZpZWROYW1lVmFsdWUBATUlAC8APzUlAAAAFP////8B" +
           "Af////8AAAAAFWCJCgIAAAABABIAAABMb2NhbGl6ZWRUZXh0VmFsdWUBATYlAC8APzYlAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAABAA8AAABTdGF0dXNDb2RlVmFsdWUBATclAC8APzclAAAAE/////8BAf//" +
           "//8AAAAAFWCJCgIAAAABAAwAAABWYXJpYW50VmFsdWUBATglAC8APzglAAAAGP////8BAf////8AAAAA" +
           "FWCJCgIAAAABABAAAABFbnVtZXJhdGlvblZhbHVlAQE5JQAvAD85JQAAAB3/////AQH/////AAAAABVg" +
           "iQoCAAAAAQAOAAAAU3RydWN0dXJlVmFsdWUBATolAC8APzolAAAAFv////8BAf////8AAAAAFWCJCgIA" +
           "AAABAAsAAABOdW1iZXJWYWx1ZQEBOyUALwA/OyUAAAAa/////wEB/////wAAAAAVYIkKAgAAAAEADAAA" +
           "AEludGVnZXJWYWx1ZQEBPCUALwA/PCUAAAAb/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAFVJbnRl" +
           "Z2VyVmFsdWUBAT0lAC8APz0lAAAAHP////8BAf////8AAAAA";
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
        public BaseDataVariableState EnumerationValue
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
        /// <remarks />
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
            
        /// <remarks />
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
                                EnumerationValue = new BaseDataVariableState(this);
                            }
                            else
                            {
                                EnumerationValue = (BaseDataVariableState)replacement;
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
        private BaseDataVariableState m_enumerationValue;
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class AnalogScalarValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <remarks />
        public AnalogScalarValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.AnalogScalarValueObjectType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABACMAAABBbmFsb2dTY2Fs" +
           "YXJWYWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBPiUBAT4lPiUAAAEAAAAAJAABAUIlEAAAADVgiQoCAAAA" +
           "AQAQAAAAU2ltdWxhdGlvbkFjdGl2ZQEBPyUDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBw" +
           "cm9kdWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAEQ/JQAAAAH/////" +
           "AQH/////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAUAlAC8BAakkQCUAAAEB/////wEA" +
           "AAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFBJQAuAERBJQAAlgEAAAABACoBAUYAAAAKAAAA" +
           "SXRlcmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2Vu" +
           "ZXJhdGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUB" +
           "AUIlAC8BAEELQiUAAAEAAAAAJAEBAT4lFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBQyUALgBEQyUA" +
           "AAAP/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBRCUALgBERCUAAAAR/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAUUlAC4AREUlAAAAEf////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAoAAABTb3VyY2VOYW1lAQFGJQAuAERGJQAAAAz/////AQH/////AAAAABVgiQoCAAAA" +
           "AAAEAAAAVGltZQEBRyUALgBERyUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2" +
           "ZVRpbWUBAUglAC4AREglAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAUol" +
           "AC4AREolAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBSyUALgBESyUAAAAF" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAT4tAC4ARD4tAAAAEf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAT8tAC4ARD8tAAAAFf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQEnLQAuAEQnLQAAAAz/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAUwlAC4AREwlAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAYAAABSZXRhaW4BAU0lAC4ARE0lAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABF" +
           "bmFibGVkU3RhdGUBAU4lAC8BACMjTiUAAAAV/////wEBAgAAAAEALCMAAQFjJQEALCMAAQFrJQEAAAAV" +
           "YIkKAgAAAAAAAgAAAElkAQFPJQAuAERPJQAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVh" +
           "bGl0eQEBVCUALwEAKiNUJQAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0" +
           "YW1wAQFVJQAuAERVJQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkB" +
           "AVglAC8BACojWCUAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB" +
           "WSUALgBEWSUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBWiUALwEAKiNa" +
           "JQAAABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQFbJQAuAERbJQAA" +
           "AQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAVwlAC4ARFwlAAAADP//" +
           "//8BAf////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQFeJQAvAQBEI14lAAABAQEAAAABAPkLAAEA" +
           "8woAAAAABGGCCgQAAAAAAAYAAABFbmFibGUBAV0lAC8BAEMjXSUAAAEBAQAAAAEA+QsAAQDzCgAAAAAE" +
           "YYIKBAAAAAAACgAAAEFkZENvbW1lbnQBAV8lAC8BAEUjXyUAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFgJQAuAERgJQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJ" +
           "ZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQu" +
           "AQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRv" +
           "IHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2Vk" +
           "U3RhdGUBAWMlAC8BACMjYyUAAAAV/////wEBAQAAAAEALCMBAQFOJQEAAAAVYIkKAgAAAAAAAgAAAElk" +
           "AQFkJQAuAERkJQAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAXMlAC8B" +
           "AJcjcyUAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQF0JQAu" +
           "AER0JQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRp" +
           "ZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAA" +
           "AwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAA" +
           "AAEB/////wAAAAAVYIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAXclAC8BAEAJdyUAAAAC/////wEB////" +
           "/wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAXolAC4ARHolAAABAHQD/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEACQAAAEJ5dGVWYWx1ZQEBfSUALwEAQAl9JQAAAAP/////AQH/////AQAAABVgiQoCAAAAAAAH" +
           "AAAARVVSYW5nZQEBgCUALgBEgCUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50MTZW" +
           "YWx1ZQEBgyUALwEAQAmDJQAAAAT/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBhiUA" +
           "LgBEhiUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDE2VmFsdWUBAYklAC8BAEAJ" +
           "iSUAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAYwlAC4ARIwlAAABAHQD////" +
           "/wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMyVmFsdWUBAY8lAC8BAEAJjyUAAAAG/////wEB////" +
           "/wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAZIlAC4ARJIlAAABAHQD/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEACwAAAFVJbnQzMlZhbHVlAQGVJQAvAQBACZUlAAAAB/////8BAf////8BAAAAFWCJCgIAAAAA" +
           "AAcAAABFVVJhbmdlAQGYJQAuAESYJQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2" +
           "NFZhbHVlAQGbJQAvAQBACZslAAAACP////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGe" +
           "JQAuAESeJQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBoSUALwEA" +
           "QAmhJQAAAAn/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBpCUALgBEpCUAAAEAdAP/" +
           "////AQH/////AAAAABVgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBpyUALwEAQAmnJQAAAAr/////AQH/" +
           "////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBqiUALgBEqiUAAAEAdAP/////AQH/////AAAAABVg" +
           "iQoCAAAAAQALAAAARG91YmxlVmFsdWUBAa0lAC8BAEAJrSUAAAAL/////wEB/////wEAAAAVYIkKAgAA" +
           "AAAABwAAAEVVUmFuZ2UBAbAlAC4ARLAlAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAE51" +
           "bWJlclZhbHVlAQGzJQAvAQBACbMlAAAAGv////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdl" +
           "AQG2JQAuAES2JQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBAbkl" +
           "AC8BAEAJuSUAAAAb/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAbwlAC4ARLwlAAAB" +
           "AHQD/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAFVJbnRlZ2VyVmFsdWUBAb8lAC8BAEAJvyUAAAAc" +
           "/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAcIlAC4ARMIlAAABAHQD/////wEB////" +
           "/wAAAAA=";
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
        /// <remarks />
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
            
        /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValue1MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ArrayValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ArrayValue1MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABUAAABBcnJheVZhbHVl" +
           "MU1ldGhvZFR5cGUBAcYlAC8BAcYlxiUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQHHJQAuAETHJQAAlgsAAAABACoBARwAAAAJAAAAQm9vbGVhbkluAAEBAAAAAQAAAAAAAAAAAQAq" +
           "AQEaAAAABwAAAFNCeXRlSW4AAgEAAAABAAAAAAAAAAABACoBARkAAAAGAAAAQnl0ZUluAAMBAAAAAQAA" +
           "AAAAAAAAAQAqAQEaAAAABwAAAEludDE2SW4ABAEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAVUludDE2" +
           "SW4ABQEAAAABAAAAAAAAAAABACoBARoAAAAHAAAASW50MzJJbgAGAQAAAAEAAAAAAAAAAAEAKgEBGwAA" +
           "AAgAAABVSW50MzJJbgAHAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABJbnQ2NEluAAgBAAAAAQAAAAAA" +
           "AAAAAQAqAQEbAAAACAAAAFVJbnQ2NEluAAkBAAAAAQAAAAAAAAAAAQAqAQEaAAAABwAAAEZsb2F0SW4A" +
           "CgEAAAABAAAAAAAAAAABACoBARsAAAAIAAAARG91YmxlSW4ACwEAAAABAAAAAAAAAAABACgBAQAAAAEA" +
           "AAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQHIJQAuAETIJQAAlgsA" +
           "AAABACoBAR0AAAAKAAAAQm9vbGVhbk91dAABAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABTQnl0ZU91" +
           "dAACAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABCeXRlT3V0AAMBAAAAAQAAAAAAAAAAAQAqAQEbAAAA" +
           "CAAAAEludDE2T3V0AAQBAAAAAQAAAAAAAAAAAQAqAQEcAAAACQAAAFVJbnQxNk91dAAFAQAAAAEAAAAA" +
           "AAAAAAEAKgEBGwAAAAgAAABJbnQzMk91dAAGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAkAAABVSW50MzJP" +
           "dXQABwEAAAABAAAAAAAAAAABACoBARsAAAAIAAAASW50NjRPdXQACAEAAAABAAAAAAAAAAABACoBARwA" +
           "AAAJAAAAVUludDY0T3V0AAkBAAAAAQAAAAAAAAAAAQAqAQEbAAAACAAAAEZsb2F0T3V0AAoBAAAAAQAA" +
           "AAAAAAAAAQAqAQEcAAAACQAAAERvdWJsZU91dAALAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAAB" +
           "Af////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ArrayValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValue2MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ArrayValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ArrayValue2MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABUAAABBcnJheVZhbHVl" +
           "Mk1ldGhvZFR5cGUBAcklAC8BAcklySUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQHKJQAuAETKJQAAlgoAAAABACoBARsAAAAIAAAAU3RyaW5nSW4ADAEAAAABAAAAAAAAAAABACoB" +
           "AR0AAAAKAAAARGF0ZVRpbWVJbgANAQAAAAEAAAAAAAAAAAEAKgEBGQAAAAYAAABHdWlkSW4ADgEAAAAB" +
           "AAAAAAAAAAABACoBAR8AAAAMAAAAQnl0ZVN0cmluZ0luAA8BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAA" +
           "AFhtbEVsZW1lbnRJbgAQAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABOb2RlSWRJbgARAQAAAAEAAAAA" +
           "AAAAAAEAKgEBIwAAABAAAABFeHBhbmRlZE5vZGVJZEluABIBAAAAAQAAAAAAAAAAAQAqAQEiAAAADwAA" +
           "AFF1YWxpZmllZE5hbWVJbgAUAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA8AAABMb2NhbGl6ZWRUZXh0SW4A" +
           "FQEAAAABAAAAAAAAAAABACoBAR8AAAAMAAAAU3RhdHVzQ29kZUluABMBAAAAAQAAAAAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEByyUALgBEyyUA" +
           "AJYKAAAAAQAqAQEcAAAACQAAAFN0cmluZ091dAAMAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAsAAABEYXRl" +
           "VGltZU91dAANAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABHdWlkT3V0AA4BAAAAAQAAAAAAAAAAAQAq" +
           "AQEgAAAADQAAAEJ5dGVTdHJpbmdPdXQADwEAAAABAAAAAAAAAAABACoBASAAAAANAAAAWG1sRWxlbWVu" +
           "dE91dAAQAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAkAAABOb2RlSWRPdXQAEQEAAAABAAAAAAAAAAABACoB" +
           "ASQAAAARAAAARXhwYW5kZWROb2RlSWRPdXQAEgEAAAABAAAAAAAAAAABACoBASMAAAAQAAAAUXVhbGlm" +
           "aWVkTmFtZU91dAAUAQAAAAEAAAAAAAAAAAEAKgEBIwAAABAAAABMb2NhbGl6ZWRUZXh0T3V0ABUBAAAA" +
           "AQAAAAAAAAAAAQAqAQEgAAAADQAAAFN0YXR1c0NvZGVPdXQAEwEAAAABAAAAAAAAAAABACgBAQAAAAEA" +
           "AAAAAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ArrayValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValue3MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public ArrayValue3MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new ArrayValue3MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABUAAABBcnJheVZhbHVl" +
           "M01ldGhvZFR5cGUBAcwlAC8BAcwlzCUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQHNJQAuAETNJQAAlgMAAAABACoBARwAAAAJAAAAVmFyaWFudEluABgBAAAAAQAAAAAAAAAAAQAq" +
           "AQEgAAAADQAAAEVudW1lcmF0aW9uSW4AHQEAAAABAAAAAAAAAAABACoBAR4AAAALAAAAU3RydWN0dXJl" +
           "SW4AFgEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0" +
           "cHV0QXJndW1lbnRzAQHOJQAuAETOJQAAlgMAAAABACoBAR0AAAAKAAAAVmFyaWFudE91dAAYAQAAAAEA" +
           "AAAAAAAAAAEAKgEBIQAAAA4AAABFbnVtZXJhdGlvbk91dAAdAQAAAAEAAAAAAAAAAAEAKgEBHwAAAAwA" +
           "AABTdHJ1Y3R1cmVPdXQAFgEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public ArrayValue3MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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

            Variant[] variantIn = (Variant[])_inputArguments[0];
            int[] enumerationIn = (int[])_inputArguments[1];
            ExtensionObject[] structureIn = (ExtensionObject[])_inputArguments[2];

            Variant[] variantOut = (Variant[])_outputArguments[0];
            int[] enumerationOut = (int[])_outputArguments[1];
            ExtensionObject[] structureOut = (ExtensionObject[])_outputArguments[2];

            if (OnCall != null)
            {
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ArrayValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <remarks />
        public ArrayValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.ArrayValueObjectType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABABwAAABBcnJheVZhbHVl" +
           "T2JqZWN0VHlwZUluc3RhbmNlAQHPJQEBzyXPJQAAAQAAAAAkAAEB0yUeAAAANWCJCgIAAAABABAAAABT" +
           "aW11bGF0aW9uQWN0aXZlAQHQJQMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHByb2R1Y2Ug" +
           "bmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARNAlAAAAAf////8BAf////8A" +
           "AAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEB0SUALwEBqSTRJQAAAQH/////AQAAABdgqQoC" +
           "AAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAdIlAC4ARNIlAACWAQAAAAEAKgEBRgAAAAoAAABJdGVyYXRp" +
           "b25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5lcmF0ZS4B" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEB0yUALwEA" +
           "QQvTJQAAAQAAAAAkAQEBzyUXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQHUJQAuAETUJQAAAA//////" +
           "AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQHVJQAuAETVJQAAABH/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEB1iUALgBE1iUAAAAR/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACgAAAFNvdXJjZU5hbWUBAdclAC4ARNclAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAQAAABU" +
           "aW1lAQHYJQAuAETYJQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZlVGltZQEB" +
           "2SUALgBE2SUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEB2yUALgBE2yUA" +
           "AAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQHcJQAuAETcJQAAAAX/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBQC0ALgBEQC0AAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBQS0ALgBEQS0AAAAV/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBASgtAC4ARCgtAAAADP////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAgAAABCcmFuY2hJZAEB3SUALgBE3SUAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BgAAAFJldGFpbgEB3iUALgBE3iUAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVuYWJsZWRT" +
           "dGF0ZQEB3yUALwEAIyPfJQAAABX/////AQECAAAAAQAsIwABAfQlAQAsIwABAfwlAQAAABVgiQoCAAAA" +
           "AAACAAAASWQBAeAlAC4AROAlAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFsaXR5AQHl" +
           "JQAvAQAqI+UlAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAeYl" +
           "AC4AROYlAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEB6SUALwEA" +
           "KiPpJQAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQHqJQAuAETq" +
           "JQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQHrJQAvAQAqI+slAAAAFf//" +
           "//8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAewlAC4AROwlAAABACYB////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEB7SUALgBE7SUAAAAM/////wEB////" +
           "/wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBAe8lAC8BAEQj7yUAAAEBAQAAAAEA+QsAAQDzCgAAAAAE" +
           "YYIKBAAAAAAABgAAAEVuYWJsZQEB7iUALwEAQyPuJQAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoEAAAA" +
           "AAAKAAAAQWRkQ29tbWVudAEB8CUALwEARSPwJQAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMBAfElAC4ARPElAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////" +
           "AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIA" +
           "AAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNv" +
           "bmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRTdGF0ZQEB" +
           "9CUALwEAIyP0JQAAABX/////AQEBAAAAAQAsIwEBAd8lAQAAABVgiQoCAAAAAAACAAAASWQBAfUlAC4A" +
           "RPUlAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBBCYALwEAlyMEJgAA" +
           "AQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAQUmAC4ARAUmAACW" +
           "AgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZv" +
           "ciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQA" +
           "AABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////" +
           "AAAAABdgiQoCAAAAAQAMAAAAQm9vbGVhblZhbHVlAQEIJgAvAD8IJgAAAAEBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAoAAABTQnl0ZVZhbHVlAQEJJgAvAD8JJgAAAAIBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAkAAABCeXRlVmFsdWUBAQomAC8APwomAAAAAwEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACgAAAEludDE2VmFsdWUBAQsmAC8APwsmAAAABAEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAFVJbnQxNlZhbHVlAQEMJgAvAD8MJgAAAAUBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAoAAABJbnQzMlZhbHVlAQENJgAvAD8NJgAAAAYBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAsAAABVSW50MzJWYWx1ZQEBDiYALwA/DiYAAAAHAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAABdgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBDyYALwA/DyYAAAAIAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAABdgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBARAmAC8APxAmAAAACQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEACgAAAEZsb2F0VmFsdWUBAREmAC8APxEmAAAACgEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEACwAAAERvdWJsZVZhbHVlAQESJgAvAD8SJgAAAAsBAAAAAQAAAAAAAAAB" +
           "Af////8AAAAAF2CJCgIAAAABAAsAAABTdHJpbmdWYWx1ZQEBEyYALwA/EyYAAAAMAQAAAAEAAAAAAAAA" +
           "AQH/////AAAAABdgiQoCAAAAAQANAAAARGF0ZVRpbWVWYWx1ZQEBFCYALwA/FCYAAAANAQAAAAEAAAAA" +
           "AAAAAQH/////AAAAABdgiQoCAAAAAQAJAAAAR3VpZFZhbHVlAQEVJgAvAD8VJgAAAA4BAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABAA8AAABCeXRlU3RyaW5nVmFsdWUBARYmAC8APxYmAAAADwEAAAAB" +
           "AAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAFhtbEVsZW1lbnRWYWx1ZQEBFyYALwA/FyYAAAAQ" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQALAAAATm9kZUlkVmFsdWUBARgmAC8APxgmAAAA" +
           "EQEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEAEwAAAEV4cGFuZGVkTm9kZUlkVmFsdWUBARkm" +
           "AC8APxkmAAAAEgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEAEgAAAFF1YWxpZmllZE5hbWVW" +
           "YWx1ZQEBGiYALwA/GiYAAAAUAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQASAAAATG9jYWxp" +
           "emVkVGV4dFZhbHVlAQEbJgAvAD8bJgAAABUBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAA8A" +
           "AABTdGF0dXNDb2RlVmFsdWUBARwmAC8APxwmAAAAEwEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAA" +
           "AAEADAAAAFZhcmlhbnRWYWx1ZQEBHSYALwA/HSYAAAAYAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoC" +
           "AAAAAQAQAAAARW51bWVyYXRpb25WYWx1ZQEBHiYALwA/HiYAAAAdAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQAOAAAAU3RydWN0dXJlVmFsdWUBAR8mAC8APx8mAAAAFgEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAE51bWJlclZhbHVlAQEgJgAvAD8gJgAAABoBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBASEmAC8APyEmAAAAGwEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEADQAAAFVJbnRlZ2VyVmFsdWUBASImAC8APyImAAAAHAEAAAABAAAAAAAA" +
           "AAEB/////wAAAAA=";
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
        /// <remarks />
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
            
        /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class AnalogArrayValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <remarks />
        public AnalogArrayValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.AnalogArrayValueObjectType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABACIAAABBbmFsb2dBcnJh" +
           "eVZhbHVlT2JqZWN0VHlwZUluc3RhbmNlAQEjJgEBIyYjJgAAAQAAAAAkAAEBJyYQAAAANWCJCgIAAAAB" +
           "ABAAAABTaW11bGF0aW9uQWN0aXZlAQEkJgMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHBy" +
           "b2R1Y2UgbmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARCQmAAAAAf////8B" +
           "Af////8AAAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBJSYALwEBqSQlJgAAAQH/////AQAA" +
           "ABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBASYmAC4ARCYmAACWAQAAAAEAKgEBRgAAAAoAAABJ" +
           "dGVyYXRpb25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5l" +
           "cmF0ZS4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEB" +
           "JyYALwEAQQsnJgAAAQAAAAAkAQEBIyYXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQEoJgAuAEQoJgAA" +
           "AA//////AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQEpJgAuAEQpJgAAABH/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBKiYALgBEKiYAAAAR/////wEB/////wAAAAAV" +
           "YIkKAgAAAAAACgAAAFNvdXJjZU5hbWUBASsmAC4ARCsmAAAADP////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAQAAABUaW1lAQEsJgAuAEQsJgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZl" +
           "VGltZQEBLSYALgBELSYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEBLyYA" +
           "LgBELyYAAAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQEwJgAuAEQwJgAAAAX/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBQi0ALgBEQi0AAAAR////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBQy0ALgBEQy0AAAAV////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBASktAC4ARCktAAAADP////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAgAAABCcmFuY2hJZAEBMSYALgBEMSYAAAAR/////wEB/////wAAAAAVYIkK" +
           "AgAAAAAABgAAAFJldGFpbgEBMiYALgBEMiYAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVu" +
           "YWJsZWRTdGF0ZQEBMyYALwEAIyMzJgAAABX/////AQECAAAAAQAsIwABAUgmAQAsIwABAVAmAQAAABVg" +
           "iQoCAAAAAAACAAAASWQBATQmAC4ARDQmAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFs" +
           "aXR5AQE5JgAvAQAqIzkmAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3Rh" +
           "bXABATomAC4ARDomAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEB" +
           "PSYALwEAKiM9JgAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQE+" +
           "JgAuAEQ+JgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQE/JgAvAQAqIz8m" +
           "AAAAFf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAUAmAC4AREAmAAAB" +
           "ACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEBQSYALgBEQSYAAAAM////" +
           "/wEB/////wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBAUMmAC8BAEQjQyYAAAEBAQAAAAEA+QsAAQDz" +
           "CgAAAAAEYYIKBAAAAAAABgAAAEVuYWJsZQEBQiYALwEAQyNCJgAAAQEBAAAAAQD5CwABAPMKAAAAAARh" +
           "ggoEAAAAAAAKAAAAQWRkQ29tbWVudAEBRCYALwEARSNEJgAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoC" +
           "AAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAUUmAC4AREUmAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElk" +
           "AA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4B" +
           "ACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8g" +
           "dGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRT" +
           "dGF0ZQEBSCYALwEAIyNIJgAAABX/////AQEBAAAAAQAsIwEBATMmAQAAABVgiQoCAAAAAAACAAAASWQB" +
           "AUkmAC4AREkmAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBWCYALwEA" +
           "lyNYJgAAAQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAVkmAC4A" +
           "RFkmAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlm" +
           "aWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAAD" +
           "AAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAA" +
           "AQH/////AAAAABdgiQoCAAAAAQAKAAAAU0J5dGVWYWx1ZQEBXCYALwEAQAlcJgAAAAIBAAAAAQAAAAAA" +
           "AAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQFfJgAuAERfJgAAAQB0A/////8BAf////8A" +
           "AAAAF2CJCgIAAAABAAkAAABCeXRlVmFsdWUBAWImAC8BAEAJYiYAAAADAQAAAAEAAAAAAAAAAQH/////" +
           "AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBZSYALgBEZSYAAAEAdAP/////AQH/////AAAAABdgiQoC" +
           "AAAAAQAKAAAASW50MTZWYWx1ZQEBaCYALwEAQAloJgAAAAQBAAAAAQAAAAAAAAABAf////8BAAAAFWCJ" +
           "CgIAAAAAAAcAAABFVVJhbmdlAQFrJgAuAERrJgAAAQB0A/////8BAf////8AAAAAF2CJCgIAAAABAAsA" +
           "AABVSW50MTZWYWx1ZQEBbiYALwEAQAluJgAAAAUBAAAAAQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAA" +
           "AAcAAABFVVJhbmdlAQFxJgAuAERxJgAAAQB0A/////8BAf////8AAAAAF2CJCgIAAAABAAoAAABJbnQz" +
           "MlZhbHVlAQF0JgAvAQBACXQmAAAABgEAAAABAAAAAAAAAAEB/////wEAAAAVYIkKAgAAAAAABwAAAEVV" +
           "UmFuZ2UBAXcmAC4ARHcmAAABAHQD/////wEB/////wAAAAAXYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVl" +
           "AQF6JgAvAQBACXomAAAABwEAAAABAAAAAAAAAAEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UB" +
           "AX0mAC4ARH0mAAABAHQD/////wEB/////wAAAAAXYIkKAgAAAAEACgAAAEludDY0VmFsdWUBAYAmAC8B" +
           "AEAJgCYAAAAIAQAAAAEAAAAAAAAAAQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBgyYALgBE" +
           "gyYAAAEAdAP/////AQH/////AAAAABdgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBAYYmAC8BAEAJhiYA" +
           "AAAJAQAAAAEAAAAAAAAAAQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBiSYALgBEiSYAAAEA" +
           "dAP/////AQH/////AAAAABdgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBjCYALwEAQAmMJgAAAAoBAAAA" +
           "AQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGPJgAuAESPJgAAAQB0A/////8B" +
           "Af////8AAAAAF2CJCgIAAAABAAsAAABEb3VibGVWYWx1ZQEBkiYALwEAQAmSJgAAAAsBAAAAAQAAAAAA" +
           "AAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGVJgAuAESVJgAAAQB0A/////8BAf////8A" +
           "AAAAF2CJCgIAAAABAAsAAABOdW1iZXJWYWx1ZQEBmCYALwEAQAmYJgAAABoBAAAAAQAAAAAAAAABAf//" +
           "//8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGbJgAuAESbJgAAAQB0A/////8BAf////8AAAAAF2CJ" +
           "CgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBAZ4mAC8BAEAJniYAAAAbAQAAAAEAAAAAAAAAAQH/////AQAA" +
           "ABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBoSYALgBEoSYAAAEAdAP/////AQH/////AAAAABdgiQoCAAAA" +
           "AQANAAAAVUludGVnZXJWYWx1ZQEBpCYALwEAQAmkJgAAABwBAAAAAQAAAAAAAAABAf////8BAAAAFWCJ" +
           "CgIAAAAAAAcAAABFVVJhbmdlAQGnJgAuAESnJgAAAQB0A/////8BAf////8AAAAA";
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
        /// <remarks />
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
            
        /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserScalarValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <remarks />
        public UserScalarValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.UserScalarValueObjectType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABACEAAABVc2VyU2NhbGFy" +
           "VmFsdWVPYmplY3RUeXBlSW5zdGFuY2UBAcEmAQHBJsEmAAABAAAAACQAAQHFJhkAAAA1YIkKAgAAAAEA" +
           "EAAAAFNpbXVsYXRpb25BY3RpdmUBAcImAwAAAABHAAAASWYgdHJ1ZSB0aGUgc2VydmVyIHdpbGwgcHJv" +
           "ZHVjZSBuZXcgdmFsdWVzIGZvciBlYWNoIG1vbml0b3JlZCB2YXJpYWJsZS4ALgBEwiYAAAAB/////wEB" +
           "/////wAAAAAEYYIKBAAAAAEADgAAAEdlbmVyYXRlVmFsdWVzAQHDJgAvAQGpJMMmAAABAf////8BAAAA" +
           "F2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBxCYALgBExCYAAJYBAAAAAQAqAQFGAAAACgAAAEl0" +
           "ZXJhdGlvbnMAB/////8AAAAAAwAAAAAlAAAAVGhlIG51bWJlciBvZiBuZXcgdmFsdWVzIHRvIGdlbmVy" +
           "YXRlLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGCACgEAAAABAA0AAABDeWNsZUNvbXBsZXRlAQHF" +
           "JgAvAQBBC8UmAAABAAAAACQBAQHBJhcAAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBAcYmAC4ARMYmAAAA" +
           "D/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAccmAC4ARMcmAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQHIJgAuAETIJgAAABH/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAKAAAAU291cmNlTmFtZQEBySYALgBEySYAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BAAAAFRpbWUBAcomAC4ARMomAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVU" +
           "aW1lAQHLJgAuAETLJgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQHNJgAu" +
           "AETNJgAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAc4mAC4ARM4mAAAABf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQFELQAuAERELQAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQFFLQAuAERFLQAAABX/////" +
           "AQH/////AAAAABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBKi0ALgBEKi0AAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACAAAAEJyYW5jaElkAQHPJgAuAETPJgAAABH/////AQH/////AAAAABVgiQoC" +
           "AAAAAAAGAAAAUmV0YWluAQHQJgAuAETQJgAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAARW5h" +
           "YmxlZFN0YXRlAQHRJgAvAQAjI9EmAAAAFf////8BAQIAAAABACwjAAEB5iYBACwjAAEB7iYBAAAAFWCJ" +
           "CgIAAAAAAAIAAABJZAEB0iYALgBE0iYAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxp" +
           "dHkBAdcmAC8BACoj1yYAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFt" +
           "cAEB2CYALgBE2CYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQHb" +
           "JgAvAQAqI9smAAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAdwm" +
           "AC4ARNwmAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAd0mAC8BACoj3SYA" +
           "AAAV/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB3iYALgBE3iYAAAEA" +
           "JgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQHfJgAuAETfJgAAAAz/////" +
           "AQH/////AAAAAARhggoEAAAAAAAHAAAARGlzYWJsZQEB4SYALwEARCPhJgAAAQEBAAAAAQD5CwABAPMK" +
           "AAAAAARhggoEAAAAAAAGAAAARW5hYmxlAQHgJgAvAQBDI+AmAAABAQEAAAABAPkLAAEA8woAAAAABGGC" +
           "CgQAAAAAAAoAAABBZGRDb21tZW50AQHiJgAvAQBFI+ImAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIA" +
           "AAAAAA4AAABJbnB1dEFyZ3VtZW50cwEB4yYALgBE4yYAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQA" +
           "D/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEA" +
           "KgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0" +
           "aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAAAAAoAAABBY2tlZFN0" +
           "YXRlAQHmJgAvAQAjI+YmAAAAFf////8BAQEAAAABACwjAQEB0SYBAAAAFWCJCgIAAAAAAAIAAABJZAEB" +
           "5yYALgBE5yYAAAAB/////wEB/////wAAAAAEYYIKBAAAAAAACwAAAEFja25vd2xlZGdlAQH2JgAvAQCX" +
           "I/YmAAABAQEAAAABAPkLAAEA8CIBAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEB9yYALgBE" +
           "9yYAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZp" +
           "ZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMA" +
           "AAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAAB" +
           "Af////8AAAAAFWCJCgIAAAABAAwAAABCb29sZWFuVmFsdWUBAfomAC8AP/omAAABAaom/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAfsmAC8AP/smAAABAasm/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEB/CYALwA//CYAAAEBrCb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAKAAAASW50MTZWYWx1ZQEB/SYALwA//SYAAAEBrSb/////AQH/////AAAAABVgiQoCAAAAAQALAAAA" +
           "VUludDE2VmFsdWUBAf4mAC8AP/4mAAABAa4m/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMy" +
           "VmFsdWUBAf8mAC8AP/8mAAABAa8m/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVl" +
           "AQEAJwAvAD8AJwAAAQGwJv////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2NFZhbHVlAQEBJwAv" +
           "AD8BJwAAAQGxJv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBAicALwA/AicA" +
           "AAEBsib/////AQH/////AAAAABVgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBAycALwA/AycAAAEBsyb/" +
           "////AQH/////AAAAABVgiQoCAAAAAQALAAAARG91YmxlVmFsdWUBAQQnAC8APwQnAAABAbQm/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEACwAAAFN0cmluZ1ZhbHVlAQEFJwAvAD8FJwAAAQG1Jv////8BAf////8A" +
           "AAAAFWCJCgIAAAABAA0AAABEYXRlVGltZVZhbHVlAQEGJwAvAD8GJwAAAQG2Jv////8BAf////8AAAAA" +
           "FWCJCgIAAAABAAkAAABHdWlkVmFsdWUBAQcnAC8APwcnAAABAbcm/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEADwAAAEJ5dGVTdHJpbmdWYWx1ZQEBCCcALwA/CCcAAAEBuCb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAPAAAAWG1sRWxlbWVudFZhbHVlAQEJJwAvAD8JJwAAAQG5Jv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AAsAAABOb2RlSWRWYWx1ZQEBCicALwA/CicAAAEBuib/////AQH/////AAAAABVgiQoCAAAAAQATAAAA" +
           "RXhwYW5kZWROb2RlSWRWYWx1ZQEBCycALwA/CycAAAEBuyb/////AQH/////AAAAABVgiQoCAAAAAQAS" +
           "AAAAUXVhbGlmaWVkTmFtZVZhbHVlAQEMJwAvAD8MJwAAAQG8Jv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "ABIAAABMb2NhbGl6ZWRUZXh0VmFsdWUBAQ0nAC8APw0nAAABAb0m/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEADwAAAFN0YXR1c0NvZGVWYWx1ZQEBDicALwA/DicAAAEBvib/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAMAAAAVmFyaWFudFZhbHVlAQEPJwAvAD8PJwAAAQG/Jv////8BAf////8AAAAA";
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
        /// <remarks />
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
            
        /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserScalarValue1MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public UserScalarValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new UserScalarValue1MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABoAAABVc2VyU2NhbGFy" +
           "VmFsdWUxTWV0aG9kVHlwZQEBECcALwEBECcQJwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBAREnAC4ARBEnAACWDAAAAAEAKgEBGgAAAAkAAABCb29sZWFuSW4BAaom/////wAAAAAA" +
           "AQAqAQEYAAAABwAAAFNCeXRlSW4BAasm/////wAAAAAAAQAqAQEXAAAABgAAAEJ5dGVJbgEBrCb/////" +
           "AAAAAAABACoBARgAAAAHAAAASW50MTZJbgEBrSb/////AAAAAAABACoBARkAAAAIAAAAVUludDE2SW4B" +
           "Aa4m/////wAAAAAAAQAqAQEYAAAABwAAAEludDMySW4BAa8m/////wAAAAAAAQAqAQEZAAAACAAAAFVJ" +
           "bnQzMkluAQGwJv////8AAAAAAAEAKgEBGAAAAAcAAABJbnQ2NEluAQGxJv////8AAAAAAAEAKgEBGQAA" +
           "AAgAAABVSW50NjRJbgEBsib/////AAAAAAABACoBARgAAAAHAAAARmxvYXRJbgEBsyb/////AAAAAAAB" +
           "ACoBARkAAAAIAAAARG91YmxlSW4BAbQm/////wAAAAAAAQAqAQEZAAAACAAAAFN0cmluZ0luAQG1Jv//" +
           "//8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVu" +
           "dHMBARInAC4ARBInAACWDAAAAAEAKgEBGwAAAAoAAABCb29sZWFuT3V0AQGqJv////8AAAAAAAEAKgEB" +
           "GQAAAAgAAABTQnl0ZU91dAEBqyb/////AAAAAAABACoBARgAAAAHAAAAQnl0ZU91dAEBrCb/////AAAA" +
           "AAABACoBARkAAAAIAAAASW50MTZPdXQBAa0m/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQxNk91dAEB" +
           "rib/////AAAAAAABACoBARkAAAAIAAAASW50MzJPdXQBAa8m/////wAAAAAAAQAqAQEaAAAACQAAAFVJ" +
           "bnQzMk91dAEBsCb/////AAAAAAABACoBARkAAAAIAAAASW50NjRPdXQBAbEm/////wAAAAAAAQAqAQEa" +
           "AAAACQAAAFVJbnQ2NE91dAEBsib/////AAAAAAABACoBARkAAAAIAAAARmxvYXRPdXQBAbMm/////wAA" +
           "AAAAAQAqAQEaAAAACQAAAERvdWJsZU91dAEBtCb/////AAAAAAABACoBARoAAAAJAAAAU3RyaW5nT3V0" +
           "AQG1Jv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public UserScalarValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserScalarValue2MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public UserScalarValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new UserScalarValue2MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABoAAABVc2VyU2NhbGFy" +
           "VmFsdWUyTWV0aG9kVHlwZQEBEycALwEBEycTJwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBARQnAC4ARBQnAACWCgAAAAEAKgEBGwAAAAoAAABEYXRlVGltZUluAQG2Jv////8AAAAA" +
           "AAEAKgEBFwAAAAYAAABHdWlkSW4BAbcm/////wAAAAAAAQAqAQEdAAAADAAAAEJ5dGVTdHJpbmdJbgEB" +
           "uCb/////AAAAAAABACoBAR0AAAAMAAAAWG1sRWxlbWVudEluAQG5Jv////8AAAAAAAEAKgEBGQAAAAgA" +
           "AABOb2RlSWRJbgEBuib/////AAAAAAABACoBASEAAAAQAAAARXhwYW5kZWROb2RlSWRJbgEBuyb/////" +
           "AAAAAAABACoBASAAAAAPAAAAUXVhbGlmaWVkTmFtZUluAQG8Jv////8AAAAAAAEAKgEBIAAAAA8AAABM" +
           "b2NhbGl6ZWRUZXh0SW4BAb0m/////wAAAAAAAQAqAQEdAAAADAAAAFN0YXR1c0NvZGVJbgEBvib/////" +
           "AAAAAAABACoBARoAAAAJAAAAVmFyaWFudEluAQG/Jv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBARUnAC4ARBUnAACWCgAAAAEAKgEBHAAA" +
           "AAsAAABEYXRlVGltZU91dAEBtib/////AAAAAAABACoBARgAAAAHAAAAR3VpZE91dAEBtyb/////AAAA" +
           "AAABACoBAR4AAAANAAAAQnl0ZVN0cmluZ091dAEBuCb/////AAAAAAABACoBAR4AAAANAAAAWG1sRWxl" +
           "bWVudE91dAEBuSb/////AAAAAAABACoBARoAAAAJAAAATm9kZUlkT3V0AQG6Jv////8AAAAAAAEAKgEB" +
           "IgAAABEAAABFeHBhbmRlZE5vZGVJZE91dAEBuyb/////AAAAAAABACoBASEAAAAQAAAAUXVhbGlmaWVk" +
           "TmFtZU91dAEBvCb/////AAAAAAABACoBASEAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBvSb/////AAAA" +
           "AAABACoBAR4AAAANAAAAU3RhdHVzQ29kZU91dAEBvib/////AAAAAAABACoBARsAAAAKAAAAVmFyaWFu" +
           "dE91dAEBvyb/////AAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public UserScalarValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserArrayValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <remarks />
        public UserArrayValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.UserArrayValueObjectType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABACAAAABVc2VyQXJyYXlW" +
           "YWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBFycBARcnFycAAAEAAAAAJAABARsnGQAAADVgiQoCAAAAAQAQ" +
           "AAAAU2ltdWxhdGlvbkFjdGl2ZQEBGCcDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9k" +
           "dWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAEQYJwAAAAH/////AQH/" +
           "////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBARknAC8BAakkGScAAAEB/////wEAAAAX" +
           "YKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEaJwAuAEQaJwAAlgEAAAABACoBAUYAAAAKAAAASXRl" +
           "cmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJh" +
           "dGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBARsn" +
           "AC8BAEELGycAAAEAAAAAJAEBARcnFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBHCcALgBEHCcAAAAP" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBHScALgBEHScAAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAR4nAC4ARB4nAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAoAAABTb3VyY2VOYW1lAQEfJwAuAEQfJwAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAE" +
           "AAAAVGltZQEBICcALgBEICcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRp" +
           "bWUBASEnAC4ARCEnAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBASMnAC4A" +
           "RCMnAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBJCcALgBEJCcAAAAF////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAUYtAC4AREYtAAAAEf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAUctAC4AREctAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQErLQAuAEQrLQAAAAz/////AQH/////" +
           "AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBASUnAC4ARCUnAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAYAAABSZXRhaW4BASYnAC4ARCYnAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFi" +
           "bGVkU3RhdGUBAScnAC8BACMjJycAAAAV/////wEBAgAAAAEALCMAAQE8JwEALCMAAQFEJwEAAAAVYIkK" +
           "AgAAAAAAAgAAAElkAQEoJwAuAEQoJwAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0" +
           "eQEBLScALwEAKiMtJwAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1w" +
           "AQEuJwAuAEQuJwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBATEn" +
           "AC8BACojMScAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBMicA" +
           "LgBEMicAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBMycALwEAKiMzJwAA" +
           "ABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQE0JwAuAEQ0JwAAAQAm" +
           "Af////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBATUnAC4ARDUnAAAADP////8B" +
           "Af////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQE3JwAvAQBEIzcnAAABAQEAAAABAPkLAAEA8woA" +
           "AAAABGGCCgQAAAAAAAYAAABFbmFibGUBATYnAC8BAEMjNicAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIK" +
           "BAAAAAAACgAAAEFkZENvbW1lbnQBATgnAC8BAEUjOCcAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQE5JwAuAEQ5JwAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP" +
           "/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAq" +
           "AQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRo" +
           "ZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3Rh" +
           "dGUBATwnAC8BACMjPCcAAAAV/////wEBAQAAAAEALCMBAQEnJwEAAAAVYIkKAgAAAAAAAgAAAElkAQE9" +
           "JwAuAEQ9JwAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAUwnAC8BAJcj" +
           "TCcAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFNJwAuAERN" +
           "JwAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmll" +
           "ciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAA" +
           "AAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEADAAAAEJvb2xlYW5WYWx1ZQEBUCcALwA/UCcAAAEBqiYBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABAAoAAABTQnl0ZVZhbHVlAQFRJwAvAD9RJwAAAQGrJgEAAAABAAAA" +
           "AAAAAAEB/////wAAAAAXYIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEBUicALwA/UicAAAEBrCYBAAAAAQAA" +
           "AAAAAAABAf////8AAAAAF2CJCgIAAAABAAoAAABJbnQxNlZhbHVlAQFTJwAvAD9TJwAAAQGtJgEAAAAB" +
           "AAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAFVJbnQxNlZhbHVlAQFUJwAvAD9UJwAAAQGuJgEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACgAAAEludDMyVmFsdWUBAVUnAC8AP1UnAAABAa8m" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBAVYnAC8AP1YnAAAB" +
           "AbAmAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBVycALwA/VycA" +
           "AAEBsSYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBWCcALwA/" +
           "WCcAAAEBsiYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAoAAABGbG9hdFZhbHVlAQFZJwAv" +
           "AD9ZJwAAAQGzJgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAERvdWJsZVZhbHVlAQFa" +
           "JwAvAD9aJwAAAQG0JgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAFN0cmluZ1ZhbHVl" +
           "AQFbJwAvAD9bJwAAAQG1JgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADQAAAERhdGVUaW1l" +
           "VmFsdWUBAVwnAC8AP1wnAAABAbYmAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAJAAAAR3Vp" +
           "ZFZhbHVlAQFdJwAvAD9dJwAAAQG3JgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAEJ5" +
           "dGVTdHJpbmdWYWx1ZQEBXicALwA/XicAAAEBuCYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAAB" +
           "AA8AAABYbWxFbGVtZW50VmFsdWUBAV8nAC8AP18nAAABAbkmAQAAAAEAAAAAAAAAAQH/////AAAAABdg" +
           "iQoCAAAAAQALAAAATm9kZUlkVmFsdWUBAWAnAC8AP2AnAAABAbomAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQATAAAARXhwYW5kZWROb2RlSWRWYWx1ZQEBYScALwA/YScAAAEBuyYBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABABIAAABRdWFsaWZpZWROYW1lVmFsdWUBAWInAC8AP2InAAABAbwm" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQASAAAATG9jYWxpemVkVGV4dFZhbHVlAQFjJwAv" +
           "AD9jJwAAAQG9JgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAFN0YXR1c0NvZGVWYWx1" +
           "ZQEBZCcALwA/ZCcAAAEBviYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAwAAABWYXJpYW50" +
           "VmFsdWUBAWUnAC8AP2UnAAABAb8mAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
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
        /// <remarks />
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
            
        /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserArrayValue1MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public UserArrayValue1MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new UserArrayValue1MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABkAAABVc2VyQXJyYXlW" +
           "YWx1ZTFNZXRob2RUeXBlAQFmJwAvAQFmJ2YnAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBZycALgBEZycAAJYMAAAAAQAqAQEeAAAACQAAAEJvb2xlYW5JbgEBqiYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEcAAAABwAAAFNCeXRlSW4BAasmAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAYAAABCeXRlSW4B" +
           "AawmAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQxNkluAQGtJgEAAAABAAAAAAAAAAABACoBAR0A" +
           "AAAIAAAAVUludDE2SW4BAa4mAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQzMkluAQGvJgEAAAAB" +
           "AAAAAAAAAAABACoBAR0AAAAIAAAAVUludDMySW4BAbAmAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJ" +
           "bnQ2NEluAQGxJgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAAVUludDY0SW4BAbImAQAAAAEAAAAAAAAA" +
           "AAEAKgEBHAAAAAcAAABGbG9hdEluAQGzJgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAARG91YmxlSW4B" +
           "AbQmAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABTdHJpbmdJbgEBtSYBAAAAAQAAAAAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBaCcALgBEaCcA" +
           "AJYMAAAAAQAqAQEfAAAACgAAAEJvb2xlYW5PdXQBAaomAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABT" +
           "Qnl0ZU91dAEBqyYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEJ5dGVPdXQBAawmAQAAAAEAAAAAAAAA" +
           "AAEAKgEBHQAAAAgAAABJbnQxNk91dAEBrSYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAFVJbnQxNk91" +
           "dAEBriYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAEludDMyT3V0AQGvJgEAAAABAAAAAAAAAAABACoB" +
           "AR4AAAAJAAAAVUludDMyT3V0AQGwJgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAASW50NjRPdXQBAbEm" +
           "AQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABVSW50NjRPdXQBAbImAQAAAAEAAAAAAAAAAAEAKgEBHQAA" +
           "AAgAAABGbG9hdE91dAEBsyYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAERvdWJsZU91dAEBtCYBAAAA" +
           "AQAAAAAAAAAAAQAqAQEeAAAACQAAAFN0cmluZ091dAEBtSYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAA" +
           "AAAAAAEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public UserArrayValue1MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class UserArrayValue2MethodState : MethodState
    {
        #region Constructors
        /// <remarks />
        public UserArrayValue2MethodState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        public new static NodeState Construct(NodeState parent)
        {
            return new UserArrayValue2MethodState(parent);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGGCCgQAAAABABkAAABVc2VyQXJyYXlW" +
           "YWx1ZTJNZXRob2RUeXBlAQFpJwAvAQFpJ2knAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBaicALgBEaicAAJYKAAAAAQAqAQEfAAAACgAAAERhdGVUaW1lSW4BAbYmAQAAAAEAAAAA" +
           "AAAAAAEAKgEBGwAAAAYAAABHdWlkSW4BAbcmAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABCeXRlU3Ry" +
           "aW5nSW4BAbgmAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABYbWxFbGVtZW50SW4BAbkmAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHQAAAAgAAABOb2RlSWRJbgEBuiYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAEV4cGFu" +
           "ZGVkTm9kZUlkSW4BAbsmAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABRdWFsaWZpZWROYW1lSW4BAbwm" +
           "AQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAb0mAQAAAAEAAAAAAAAAAAEA" +
           "KgEBIQAAAAwAAABTdGF0dXNDb2RlSW4BAb4mAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABWYXJpYW50" +
           "SW4BAb8mAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABP" +
           "dXRwdXRBcmd1bWVudHMBAWsnAC4ARGsnAACWCgAAAAEAKgEBIAAAAAsAAABEYXRlVGltZU91dAEBtiYB" +
           "AAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEd1aWRPdXQBAbcmAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0A" +
           "AABCeXRlU3RyaW5nT3V0AQG4JgEAAAABAAAAAAAAAAABACoBASIAAAANAAAAWG1sRWxlbWVudE91dAEB" +
           "uSYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAE5vZGVJZE91dAEBuiYBAAAAAQAAAAAAAAAAAQAqAQEm" +
           "AAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0AQG7JgEAAAABAAAAAAAAAAABACoBASUAAAAQAAAAUXVhbGlm" +
           "aWVkTmFtZU91dAEBvCYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAExvY2FsaXplZFRleHRPdXQBAb0m" +
           "AQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0AAABTdGF0dXNDb2RlT3V0AQG+JgEAAAABAAAAAAAAAAABACoB" +
           "AR8AAAAKAAAAVmFyaWFudE91dAEBvyYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAA" +
           "AAA=";
        #endregion
        #endif
        #endregion

        #region Event Callbacks
        /// <remarks />
        public UserArrayValue2MethodStateMethodCallHandler OnCall;
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        /// <remarks />
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
                _result = OnCall(
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

            return _result;
        }
        #endregion

        #region Private Fields
        #endregion
    }

    /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class MethodTestState : FolderState
    {
        #region Constructors
        /// <remarks />
        public MethodTestState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.MethodTestType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABABYAAABNZXRob2RUZXN0" +
           "VHlwZUluc3RhbmNlAQFsJwEBbCdsJwAA/////woAAAAEYYIKBAAAAAEADQAAAFNjYWxhck1ldGhvZDEB" +
           "AW0nAC8BAW0nbScAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFuJwAuAERu" +
           "JwAAlgsAAAABACoBARgAAAAJAAAAQm9vbGVhbkluAAH/////AAAAAAABACoBARYAAAAHAAAAU0J5dGVJ" +
           "bgAC/////wAAAAAAAQAqAQEVAAAABgAAAEJ5dGVJbgAD/////wAAAAAAAQAqAQEWAAAABwAAAEludDE2" +
           "SW4ABP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MTZJbgAF/////wAAAAAAAQAqAQEWAAAABwAAAElu" +
           "dDMySW4ABv////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MzJJbgAH/////wAAAAAAAQAqAQEWAAAABwAA" +
           "AEludDY0SW4ACP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50NjRJbgAJ/////wAAAAAAAQAqAQEWAAAA" +
           "BwAAAEZsb2F0SW4ACv////8AAAAAAAEAKgEBFwAAAAgAAABEb3VibGVJbgAL/////wAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBbycALgBEbycA" +
           "AJYLAAAAAQAqAQEZAAAACgAAAEJvb2xlYW5PdXQAAf////8AAAAAAAEAKgEBFwAAAAgAAABTQnl0ZU91" +
           "dAAC/////wAAAAAAAQAqAQEWAAAABwAAAEJ5dGVPdXQAA/////8AAAAAAAEAKgEBFwAAAAgAAABJbnQx" +
           "Nk91dAAE/////wAAAAAAAQAqAQEYAAAACQAAAFVJbnQxNk91dAAF/////wAAAAAAAQAqAQEXAAAACAAA" +
           "AEludDMyT3V0AAb/////AAAAAAABACoBARgAAAAJAAAAVUludDMyT3V0AAf/////AAAAAAABACoBARcA" +
           "AAAIAAAASW50NjRPdXQACP////8AAAAAAAEAKgEBGAAAAAkAAABVSW50NjRPdXQACf////8AAAAAAAEA" +
           "KgEBFwAAAAgAAABGbG9hdE91dAAK/////wAAAAAAAQAqAQEYAAAACQAAAERvdWJsZU91dAAL/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADQAAAFNjYWxhck1ldGhvZDIBAXAn" +
           "AC8BAXAncCcAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFxJwAuAERxJwAA" +
           "lgoAAAABACoBARcAAAAIAAAAU3RyaW5nSW4ADP////8AAAAAAAEAKgEBGQAAAAoAAABEYXRlVGltZUlu" +
           "AA3/////AAAAAAABACoBARUAAAAGAAAAR3VpZEluAA7/////AAAAAAABACoBARsAAAAMAAAAQnl0ZVN0" +
           "cmluZ0luAA//////AAAAAAABACoBARsAAAAMAAAAWG1sRWxlbWVudEluABD/////AAAAAAABACoBARcA" +
           "AAAIAAAATm9kZUlkSW4AEf////8AAAAAAAEAKgEBHwAAABAAAABFeHBhbmRlZE5vZGVJZEluABL/////" +
           "AAAAAAABACoBAR4AAAAPAAAAUXVhbGlmaWVkTmFtZUluABT/////AAAAAAABACoBAR4AAAAPAAAATG9j" +
           "YWxpemVkVGV4dEluABX/////AAAAAAABACoBARsAAAAMAAAAU3RhdHVzQ29kZUluABP/////AAAAAAAB" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFyJwAu" +
           "AERyJwAAlgoAAAABACoBARgAAAAJAAAAU3RyaW5nT3V0AAz/////AAAAAAABACoBARoAAAALAAAARGF0" +
           "ZVRpbWVPdXQADf////8AAAAAAAEAKgEBFgAAAAcAAABHdWlkT3V0AA7/////AAAAAAABACoBARwAAAAN" +
           "AAAAQnl0ZVN0cmluZ091dAAP/////wAAAAAAAQAqAQEcAAAADQAAAFhtbEVsZW1lbnRPdXQAEP////8A" +
           "AAAAAAEAKgEBGAAAAAkAAABOb2RlSWRPdXQAEf////8AAAAAAAEAKgEBIAAAABEAAABFeHBhbmRlZE5v" +
           "ZGVJZE91dAAS/////wAAAAAAAQAqAQEfAAAAEAAAAFF1YWxpZmllZE5hbWVPdXQAFP////8AAAAAAAEA" +
           "KgEBHwAAABAAAABMb2NhbGl6ZWRUZXh0T3V0ABX/////AAAAAAABACoBARwAAAANAAAAU3RhdHVzQ29k" +
           "ZU91dAAT/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADQAAAFNjYWxh" +
           "ck1ldGhvZDMBAXMnAC8BAXMncycAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRz" +
           "AQF0JwAuAER0JwAAlgMAAAABACoBARgAAAAJAAAAVmFyaWFudEluABj/////AAAAAAABACoBARwAAAAN" +
           "AAAARW51bWVyYXRpb25JbgAd/////wAAAAAAAQAqAQEaAAAACwAAAFN0cnVjdHVyZUluABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQF1" +
           "JwAuAER1JwAAlgMAAAABACoBARkAAAAKAAAAVmFyaWFudE91dAAY/////wAAAAAAAQAqAQEdAAAADgAA" +
           "AEVudW1lcmF0aW9uT3V0AB3/////AAAAAAABACoBARsAAAAMAAAAU3RydWN0dXJlT3V0ABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAAAQAMAAAAQXJyYXlNZXRob2QxAQF2JwAv" +
           "AQF2J3YnAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBdycALgBEdycAAJYL" +
           "AAAAAQAqAQEcAAAACQAAAEJvb2xlYW5JbgABAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABTQnl0ZUlu" +
           "AAIBAAAAAQAAAAAAAAAAAQAqAQEZAAAABgAAAEJ5dGVJbgADAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcA" +
           "AABJbnQxNkluAAQBAAAAAQAAAAAAAAAAAQAqAQEbAAAACAAAAFVJbnQxNkluAAUBAAAAAQAAAAAAAAAA" +
           "AQAqAQEaAAAABwAAAEludDMySW4ABgEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAVUludDMySW4ABwEA" +
           "AAABAAAAAAAAAAABACoBARoAAAAHAAAASW50NjRJbgAIAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABV" +
           "SW50NjRJbgAJAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABGbG9hdEluAAoBAAAAAQAAAAAAAAAAAQAq" +
           "AQEbAAAACAAAAERvdWJsZUluAAsBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAX" +
           "YKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBeCcALgBEeCcAAJYLAAAAAQAqAQEdAAAACgAAAEJv" +
           "b2xlYW5PdXQAAQEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAU0J5dGVPdXQAAgEAAAABAAAAAAAAAAAB" +
           "ACoBARoAAAAHAAAAQnl0ZU91dAADAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABJbnQxNk91dAAEAQAA" +
           "AAEAAAAAAAAAAAEAKgEBHAAAAAkAAABVSW50MTZPdXQABQEAAAABAAAAAAAAAAABACoBARsAAAAIAAAA" +
           "SW50MzJPdXQABgEAAAABAAAAAAAAAAABACoBARwAAAAJAAAAVUludDMyT3V0AAcBAAAAAQAAAAAAAAAA" +
           "AQAqAQEbAAAACAAAAEludDY0T3V0AAgBAAAAAQAAAAAAAAAAAQAqAQEcAAAACQAAAFVJbnQ2NE91dAAJ" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABGbG9hdE91dAAKAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAkA" +
           "AABEb3VibGVPdXQACwEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAA" +
           "AQAMAAAAQXJyYXlNZXRob2QyAQF5JwAvAQF5J3knAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1" +
           "dEFyZ3VtZW50cwEBeicALgBEeicAAJYKAAAAAQAqAQEbAAAACAAAAFN0cmluZ0luAAwBAAAAAQAAAAAA" +
           "AAAAAQAqAQEdAAAACgAAAERhdGVUaW1lSW4ADQEAAAABAAAAAAAAAAABACoBARkAAAAGAAAAR3VpZElu" +
           "AA4BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAEJ5dGVTdHJpbmdJbgAPAQAAAAEAAAAAAAAAAAEAKgEB" +
           "HwAAAAwAAABYbWxFbGVtZW50SW4AEAEAAAABAAAAAAAAAAABACoBARsAAAAIAAAATm9kZUlkSW4AEQEA" +
           "AAABAAAAAAAAAAABACoBASMAAAAQAAAARXhwYW5kZWROb2RlSWRJbgASAQAAAAEAAAAAAAAAAAEAKgEB" +
           "IgAAAA8AAABRdWFsaWZpZWROYW1lSW4AFAEAAAABAAAAAAAAAAABACoBASIAAAAPAAAATG9jYWxpemVk" +
           "VGV4dEluABUBAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAFN0YXR1c0NvZGVJbgATAQAAAAEAAAAAAAAA" +
           "AAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAXsn" +
           "AC4ARHsnAACWCgAAAAEAKgEBHAAAAAkAAABTdHJpbmdPdXQADAEAAAABAAAAAAAAAAABACoBAR4AAAAL" +
           "AAAARGF0ZVRpbWVPdXQADQEAAAABAAAAAAAAAAABACoBARoAAAAHAAAAR3VpZE91dAAOAQAAAAEAAAAA" +
           "AAAAAAEAKgEBIAAAAA0AAABCeXRlU3RyaW5nT3V0AA8BAAAAAQAAAAAAAAAAAQAqAQEgAAAADQAAAFht" +
           "bEVsZW1lbnRPdXQAEAEAAAABAAAAAAAAAAABACoBARwAAAAJAAAATm9kZUlkT3V0ABEBAAAAAQAAAAAA" +
           "AAAAAQAqAQEkAAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0ABIBAAAAAQAAAAAAAAAAAQAqAQEjAAAAEAAA" +
           "AFF1YWxpZmllZE5hbWVPdXQAFAEAAAABAAAAAAAAAAABACoBASMAAAAQAAAATG9jYWxpemVkVGV4dE91" +
           "dAAVAQAAAAEAAAAAAAAAAAEAKgEBIAAAAA0AAABTdGF0dXNDb2RlT3V0ABMBAAAAAQAAAAAAAAAAAQAo" +
           "AQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADAAAAEFycmF5TWV0aG9kMwEBfCcALwEBfCd8" +
           "JwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAX0nAC4ARH0nAACWAwAAAAEA" +
           "KgEBHAAAAAkAAABWYXJpYW50SW4AGAEAAAABAAAAAAAAAAABACoBASAAAAANAAAARW51bWVyYXRpb25J" +
           "bgAdAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAsAAABTdHJ1Y3R1cmVJbgAWAQAAAAEAAAAAAAAAAAEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAX4nAC4ARH4n" +
           "AACWAwAAAAEAKgEBHQAAAAoAAABWYXJpYW50T3V0ABgBAAAAAQAAAAAAAAAAAQAqAQEhAAAADgAAAEVu" +
           "dW1lcmF0aW9uT3V0AB0BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAFN0cnVjdHVyZU91dAAWAQAAAAEA" +
           "AAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQAAAABABEAAABVc2VyU2NhbGFyTWV0" +
           "aG9kMQEBfycALwEBfyd/JwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAYAn" +
           "AC4ARIAnAACWDAAAAAEAKgEBGgAAAAkAAABCb29sZWFuSW4BAaom/////wAAAAAAAQAqAQEYAAAABwAA" +
           "AFNCeXRlSW4BAasm/////wAAAAAAAQAqAQEXAAAABgAAAEJ5dGVJbgEBrCb/////AAAAAAABACoBARgA" +
           "AAAHAAAASW50MTZJbgEBrSb/////AAAAAAABACoBARkAAAAIAAAAVUludDE2SW4BAa4m/////wAAAAAA" +
           "AQAqAQEYAAAABwAAAEludDMySW4BAa8m/////wAAAAAAAQAqAQEZAAAACAAAAFVJbnQzMkluAQGwJv//" +
           "//8AAAAAAAEAKgEBGAAAAAcAAABJbnQ2NEluAQGxJv////8AAAAAAAEAKgEBGQAAAAgAAABVSW50NjRJ" +
           "bgEBsib/////AAAAAAABACoBARgAAAAHAAAARmxvYXRJbgEBsyb/////AAAAAAABACoBARkAAAAIAAAA" +
           "RG91YmxlSW4BAbQm/////wAAAAAAAQAqAQEZAAAACAAAAFN0cmluZ0luAQG1Jv////8AAAAAAAEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAYEnAC4ARIEn" +
           "AACWDAAAAAEAKgEBGwAAAAoAAABCb29sZWFuT3V0AQGqJv////8AAAAAAAEAKgEBGQAAAAgAAABTQnl0" +
           "ZU91dAEBqyb/////AAAAAAABACoBARgAAAAHAAAAQnl0ZU91dAEBrCb/////AAAAAAABACoBARkAAAAI" +
           "AAAASW50MTZPdXQBAa0m/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQxNk91dAEBrib/////AAAAAAAB" +
           "ACoBARkAAAAIAAAASW50MzJPdXQBAa8m/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQzMk91dAEBsCb/" +
           "////AAAAAAABACoBARkAAAAIAAAASW50NjRPdXQBAbEm/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQ2" +
           "NE91dAEBsib/////AAAAAAABACoBARkAAAAIAAAARmxvYXRPdXQBAbMm/////wAAAAAAAQAqAQEaAAAA" +
           "CQAAAERvdWJsZU91dAEBtCb/////AAAAAAABACoBARoAAAAJAAAAU3RyaW5nT3V0AQG1Jv////8AAAAA" +
           "AAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQAAAABABEAAABVc2VyU2NhbGFyTWV0aG9kMgEB" +
           "gicALwEBgieCJwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAYMnAC4ARIMn" +
           "AACWCgAAAAEAKgEBGwAAAAoAAABEYXRlVGltZUluAQG2Jv////8AAAAAAAEAKgEBFwAAAAYAAABHdWlk" +
           "SW4BAbcm/////wAAAAAAAQAqAQEdAAAADAAAAEJ5dGVTdHJpbmdJbgEBuCb/////AAAAAAABACoBAR0A" +
           "AAAMAAAAWG1sRWxlbWVudEluAQG5Jv////8AAAAAAAEAKgEBGQAAAAgAAABOb2RlSWRJbgEBuib/////" +
           "AAAAAAABACoBASEAAAAQAAAARXhwYW5kZWROb2RlSWRJbgEBuyb/////AAAAAAABACoBASAAAAAPAAAA" +
           "UXVhbGlmaWVkTmFtZUluAQG8Jv////8AAAAAAAEAKgEBIAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAb0m" +
           "/////wAAAAAAAQAqAQEdAAAADAAAAFN0YXR1c0NvZGVJbgEBvib/////AAAAAAABACoBARoAAAAJAAAA" +
           "VmFyaWFudEluAQG/Jv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8A" +
           "AABPdXRwdXRBcmd1bWVudHMBAYQnAC4ARIQnAACWCgAAAAEAKgEBHAAAAAsAAABEYXRlVGltZU91dAEB" +
           "tib/////AAAAAAABACoBARgAAAAHAAAAR3VpZE91dAEBtyb/////AAAAAAABACoBAR4AAAANAAAAQnl0" +
           "ZVN0cmluZ091dAEBuCb/////AAAAAAABACoBAR4AAAANAAAAWG1sRWxlbWVudE91dAEBuSb/////AAAA" +
           "AAABACoBARoAAAAJAAAATm9kZUlkT3V0AQG6Jv////8AAAAAAAEAKgEBIgAAABEAAABFeHBhbmRlZE5v" +
           "ZGVJZE91dAEBuyb/////AAAAAAABACoBASEAAAAQAAAAUXVhbGlmaWVkTmFtZU91dAEBvCb/////AAAA" +
           "AAABACoBASEAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBvSb/////AAAAAAABACoBAR4AAAANAAAAU3Rh" +
           "dHVzQ29kZU91dAEBvib/////AAAAAAABACoBARsAAAAKAAAAVmFyaWFudE91dAEBvyb/////AAAAAAAB" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAAAQAQAAAAVXNlckFycmF5TWV0aG9kMQEBhScA" +
           "LwEBhSeFJwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAYYnAC4ARIYnAACW" +
           "DAAAAAEAKgEBHgAAAAkAAABCb29sZWFuSW4BAaomAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABTQnl0" +
           "ZUluAQGrJgEAAAABAAAAAAAAAAABACoBARsAAAAGAAAAQnl0ZUluAQGsJgEAAAABAAAAAAAAAAABACoB" +
           "ARwAAAAHAAAASW50MTZJbgEBrSYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAFVJbnQxNkluAQGuJgEA" +
           "AAABAAAAAAAAAAABACoBARwAAAAHAAAASW50MzJJbgEBryYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAA" +
           "AFVJbnQzMkluAQGwJgEAAAABAAAAAAAAAAABACoBARwAAAAHAAAASW50NjRJbgEBsSYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEdAAAACAAAAFVJbnQ2NEluAQGyJgEAAAABAAAAAAAAAAABACoBARwAAAAHAAAARmxvYXRJ" +
           "bgEBsyYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAERvdWJsZUluAQG0JgEAAAABAAAAAAAAAAABACoB" +
           "AR0AAAAIAAAAU3RyaW5nSW4BAbUmAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA" +
           "F2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAYcnAC4ARIcnAACWDAAAAAEAKgEBHwAAAAoAAABC" +
           "b29sZWFuT3V0AQGqJgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAAU0J5dGVPdXQBAasmAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHAAAAAcAAABCeXRlT3V0AQGsJgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAASW50MTZP" +
           "dXQBAa0mAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABVSW50MTZPdXQBAa4mAQAAAAEAAAAAAAAAAAEA" +
           "KgEBHQAAAAgAAABJbnQzMk91dAEBryYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAFVJbnQzMk91dAEB" +
           "sCYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAEludDY0T3V0AQGxJgEAAAABAAAAAAAAAAABACoBAR4A" +
           "AAAJAAAAVUludDY0T3V0AQGyJgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAARmxvYXRPdXQBAbMmAQAA" +
           "AAEAAAAAAAAAAAEAKgEBHgAAAAkAAABEb3VibGVPdXQBAbQmAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkA" +
           "AABTdHJpbmdPdXQBAbUmAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQA" +
           "AAABABAAAABVc2VyQXJyYXlNZXRob2QyAQGIJwAvAQGIJ4gnAAABAf////8CAAAAF2CpCgIAAAAAAA4A" +
           "AABJbnB1dEFyZ3VtZW50cwEBiScALgBEiScAAJYKAAAAAQAqAQEfAAAACgAAAERhdGVUaW1lSW4BAbYm" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGwAAAAYAAABHdWlkSW4BAbcmAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwA" +
           "AABCeXRlU3RyaW5nSW4BAbgmAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABYbWxFbGVtZW50SW4BAbkm" +
           "AQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABOb2RlSWRJbgEBuiYBAAAAAQAAAAAAAAAAAQAqAQElAAAA" +
           "EAAAAEV4cGFuZGVkTm9kZUlkSW4BAbsmAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABRdWFsaWZpZWRO" +
           "YW1lSW4BAbwmAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAb0mAQAAAAEA" +
           "AAAAAAAAAAEAKgEBIQAAAAwAAABTdGF0dXNDb2RlSW4BAb4mAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkA" +
           "AABWYXJpYW50SW4BAb8mAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIA" +
           "AAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAYonAC4ARIonAACWCgAAAAEAKgEBIAAAAAsAAABEYXRlVGlt" +
           "ZU91dAEBtiYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEd1aWRPdXQBAbcmAQAAAAEAAAAAAAAAAAEA" +
           "KgEBIgAAAA0AAABCeXRlU3RyaW5nT3V0AQG4JgEAAAABAAAAAAAAAAABACoBASIAAAANAAAAWG1sRWxl" +
           "bWVudE91dAEBuSYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAE5vZGVJZE91dAEBuiYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEmAAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0AQG7JgEAAAABAAAAAAAAAAABACoBASUAAAAQ" +
           "AAAAUXVhbGlmaWVkTmFtZU91dAEBvCYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAExvY2FsaXplZFRl" +
           "eHRPdXQBAb0mAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0AAABTdGF0dXNDb2RlT3V0AQG+JgEAAAABAAAA" +
           "AAAAAAABACoBAR8AAAAKAAAAVmFyaWFudE91dAEBvyYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAA" +
           "AAEB/////wAAAAA=";
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
        /// <remarks />
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
            
        /// <remarks />
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
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class TestSystemConditionState : ConditionState
    {
        #region Constructors
        /// <remarks />
        public TestSystemConditionState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.TestSystemConditionType, TestData.Namespaces.TestData, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABAB8AAABUZXN0U3lzdGVt" +
           "Q29uZGl0aW9uVHlwZUluc3RhbmNlAQGLJwEBiyeLJwAA/////xYAAAAVYIkKAgAAAAAABwAAAEV2ZW50" +
           "SWQBAYwnAC4ARIwnAAAAD/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAY0nAC4A" +
           "RI0nAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQGOJwAuAESOJwAAABH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTmFtZQEBjycALgBEjycAAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAZAnAC4ARJAnAAABACYB/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACwAAAFJlY2VpdmVUaW1lAQGRJwAuAESRJwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcA" +
           "AABNZXNzYWdlAQGTJwAuAESTJwAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkB" +
           "AZQnAC4ARJQnAAAABf////8BAf////8AAAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQFI" +
           "LQAuAERILQAAABH/////AQH/////AAAAABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQFJ" +
           "LQAuAERJLQAAABX/////AQH/////AAAAABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBLC0ALgBE" +
           "LC0AAAAM/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAEJyYW5jaElkAQGVJwAuAESVJwAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAAGAAAAUmV0YWluAQGWJwAuAESWJwAAAAH/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAMAAAARW5hYmxlZFN0YXRlAQGXJwAvAQAjI5cnAAAAFf////8BAf////8BAAAAFWCJCgIA" +
           "AAAAAAIAAABJZAEBmCcALgBEmCcAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxpdHkB" +
           "AZ0nAC8BACojnScAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB" +
           "nicALgBEnicAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQGhJwAv" +
           "AQAqI6EnAAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAaInAC4A" +
           "RKInAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAaMnAC8BACojoycAAAAV" +
           "/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBpCcALgBEpCcAAAEAJgH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQGlJwAuAESlJwAAAAz/////AQH/" +
           "////AAAAAARhggoEAAAAAAAHAAAARGlzYWJsZQEBpycALwEARCOnJwAAAQEBAAAAAQD5CwABAPMKAAAA" +
           "AARhggoEAAAAAAAGAAAARW5hYmxlAQGmJwAvAQBDI6YnAAABAQEAAAABAPkLAAEA8woAAAAABGGCCgQA" +
           "AAAAAAoAAABBZGRDb21tZW50AQGoJwAvAQBFI6gnAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBqScALgBEqScAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD///" +
           "//8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEB" +
           "QgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUg" +
           "Y29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAABABIAAABNb25pdG9yZWRO" +
           "b2RlQ291bnQBAawnAC4ARKwnAAAABv////8BAf////8AAAAA";
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
        /// <remarks />
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
            
        /// <remarks />
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