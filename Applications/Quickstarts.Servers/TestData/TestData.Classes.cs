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
           "bHVlc01ldGhvZFR5cGUBAekDAC8BAekD6QMAAAEB/////wEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJn" +
           "dW1lbnRzAQHqAwAuAETqAwAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0aW9ucwAH/////wAAAAADAAAA" +
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
           "bHVlc0V2ZW50VHlwZUluc3RhbmNlAQHrAwEB6wPrAwAA/////woAAAAVYIkKAgAAAAAABwAAAEV2ZW50" +
           "SWQBAewDAC4AROwDAAAAD/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAe0DAC4A" +
           "RO0DAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQHuAwAuAETuAwAAABH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTmFtZQEB7wMALgBE7wMAAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAfADAC4ARPADAAABACYB/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACwAAAFJlY2VpdmVUaW1lAQHxAwAuAETxAwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcA" +
           "AABNZXNzYWdlAQHzAwAuAETzAwAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkB" +
           "AfQDAC4ARPQDAAAABf////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJdGVyYXRpb25zAQH1AwAuAET1" +
           "AwAAAAf/////AQH/////AAAAABVgiQoCAAAAAQANAAAATmV3VmFsdWVDb3VudAEB9gMALgBE9gMAAAAH" +
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
           "amVjdFR5cGVJbnN0YW5jZQEB9wMBAfcD9wMAAAEAAAAAJAABAfsDAwAAADVgiQoCAAAAAQAQAAAAU2lt" +
           "dWxhdGlvbkFjdGl2ZQEB+AMDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9kdWNlIG5l" +
           "dyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAET4AwAAAAH/////AQH/////AAAA" +
           "AARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAfkDAC8BAfkD+QMAAAEB/////wEAAAAXYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQH6AwAuAET6AwAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0aW9u" +
           "cwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJhdGUuAQAo" +
           "AQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAfsDAC8BAEEL" +
           "+wMAAAEAAAAAJAEBAfcDFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEB/AMALgBE/AMAAAAP/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEB/QMALgBE/QMAAAAR/////wEB/////wAAAAAV" +
           "YIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAf4DAC4ARP4DAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAoAAABTb3VyY2VOYW1lAQH/AwAuAET/AwAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAEAAAAVGlt" +
           "ZQEBAAQALgBEAAQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUBAQEE" +
           "AC4ARAEEAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAQMEAC4ARAMEAAAA" +
           "Ff////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBBAQALgBEBAQAAAAF/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAQUEAC4ARAUEAAAAEf////8BAf////8A" +
           "AAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAQYEAC4ARAYEAAAAFf////8BAf////8A" +
           "AAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQEJBAAuAEQJBAAAAAz/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAIAAAAQnJhbmNoSWQBAQoEAC4ARAoEAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAYA" +
           "AABSZXRhaW4BAQsEAC4ARAsEAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFibGVkU3Rh" +
           "dGUBAQwEAC8BACMjDAQAAAAV/////wEBAgAAAAEALCMAAQEgBAEALCMAAQEpBAEAAAAVYIkKAgAAAAAA" +
           "AgAAAElkAQENBAAuAEQNBAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0eQEBFQQA" +
           "LwEAKiMVBAAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEWBAAu" +
           "AEQWBAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBARcEAC8BACoj" +
           "FwQAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBGAQALgBEGAQA" +
           "AAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBGQQALwEAKiMZBAAAABX/////" +
           "AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEaBAAuAEQaBAAAAQAmAf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBARsEAC4ARBsEAAAADP////8BAf////8A" +
           "AAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQEcBAAvAQBEIxwEAAABAQEAAAABAPkLAAEA8woAAAAABGGC" +
           "CgQAAAAAAAYAAABFbmFibGUBAR0EAC8BAEMjHQQAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIKBAAAAAAA" +
           "CgAAAEFkZENvbW1lbnQBAR4EAC8BAEUjHgQAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAAAAAADgAA" +
           "AElucHV0QXJndW1lbnRzAQEfBAAuAEQfBAAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAA" +
           "AAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAA" +
           "BwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25k" +
           "aXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3RhdGUBASAE" +
           "AC8BACMjIAQAAAAV/////wEBAQAAAAEALCMBAQEMBAEAAAAVYIkKAgAAAAAAAgAAAElkAQEhBAAuAEQh" +
           "BAAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBATIEAC8BAJcjMgQAAAEB" +
           "AQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEzBAAuAEQzBAAAlgIA" +
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
           "cmlhYmxlVHlwZUluc3RhbmNlAQE3BAEBNwQ3BAAAABgBAQEAAAAAJAABATsEAwAAADVgiQoCAAAAAQAQ" +
           "AAAAU2ltdWxhdGlvbkFjdGl2ZQEBOAQDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9k" +
           "dWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAEQ4BAAAAAH/////AQH/" +
           "////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBATkEAC8BATkEOQQAAAEB/////wEAAAAX" +
           "YKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQE6BAAuAEQ6BAAAlgEAAAABACoBAUYAAAAKAAAASXRl" +
           "cmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJh" +
           "dGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBATsE" +
           "AC8BAEELOwQAAAEAAAAAJAEBATcEFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBPAQALgBEPAQAAAAP" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBPQQALgBEPQQAAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAT4EAC4ARD4EAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAoAAABTb3VyY2VOYW1lAQE/BAAuAEQ/BAAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAE" +
           "AAAAVGltZQEBQAQALgBEQAQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRp" +
           "bWUBAUEEAC4AREEEAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAUMEAC4A" +
           "REMEAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBRAQALgBERAQAAAAF////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAUUEAC4AREUEAAAAEf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAUYEAC4AREYEAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQFJBAAuAERJBAAAAAz/////AQH/////" +
           "AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAUoEAC4AREoEAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAYAAABSZXRhaW4BAUsEAC4AREsEAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFi" +
           "bGVkU3RhdGUBAUwEAC8BACMjTAQAAAAV/////wEBAgAAAAEALCMAAQFgBAEALCMAAQFpBAEAAAAVYIkK" +
           "AgAAAAAAAgAAAElkAQFNBAAuAERNBAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0" +
           "eQEBVQQALwEAKiNVBAAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1w" +
           "AQFWBAAuAERWBAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAVcE" +
           "AC8BACojVwQAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBWAQA" +
           "LgBEWAQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBWQQALwEAKiNZBAAA" +
           "ABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQFaBAAuAERaBAAAAQAm" +
           "Af////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAVsEAC4ARFsEAAAADP////8B" +
           "Af////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQFcBAAvAQBEI1wEAAABAQEAAAABAPkLAAEA8woA" +
           "AAAABGGCCgQAAAAAAAYAAABFbmFibGUBAV0EAC8BAEMjXQQAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIK" +
           "BAAAAAAACgAAAEFkZENvbW1lbnQBAV4EAC8BAEUjXgQAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQFfBAAuAERfBAAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP" +
           "/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAq" +
           "AQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRo" +
           "ZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3Rh" +
           "dGUBAWAEAC8BACMjYAQAAAAV/////wEBAQAAAAEALCMBAQFMBAEAAAAVYIkKAgAAAAAAAgAAAElkAQFh" +
           "BAAuAERhBAAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAXIEAC8BAJcj" +
           "cgQAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFzBAAuAERz" +
           "BAAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmll" +
           "ciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAA" +
           "AAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAA=";
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
    public partial class ScalarValueVariableState : TestDataVariableState<TestData.ScalarValueDataType>
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
           "ZVZhcmlhYmxlVHlwZUluc3RhbmNlAQF2BAEBdgR2BAAAAQE2BAEBAQAAAAAkAAEBegQeAAAANWCJCgIA" +
           "AAABABAAAABTaW11bGF0aW9uQWN0aXZlAQF3BAMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxs" +
           "IHByb2R1Y2UgbmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARHcEAAAAAf//" +
           "//8BAf////8AAAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBeAQALwEBOQR4BAAAAQH/////" +
           "AQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAXkEAC4ARHkEAACWAQAAAAEAKgEBRgAAAAoA" +
           "AABJdGVyYXRpb25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBn" +
           "ZW5lcmF0ZS4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0" +
           "ZQEBegQALwEAQQt6BAAAAQAAAAAkAQEBdgQXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQF7BAAuAER7" +
           "BAAAAA//////AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQF8BAAuAER8BAAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBfQQALgBEfQQAAAAR/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAACgAAAFNvdXJjZU5hbWUBAX4EAC4ARH4EAAAADP////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAQAAABUaW1lAQF/BAAuAER/BAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNl" +
           "aXZlVGltZQEBgAQALgBEgAQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEB" +
           "ggQALgBEggQAAAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQGDBAAuAESDBAAA" +
           "AAX/////AQH/////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBhAQALgBEhAQAAAAR" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBhQQALgBEhQQAAAAV" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBAYgEAC4ARIgEAAAADP////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAAgAAABCcmFuY2hJZAEBiQQALgBEiQQAAAAR/////wEB/////wAAAAAV" +
           "YIkKAgAAAAAABgAAAFJldGFpbgEBigQALgBEigQAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAA" +
           "AEVuYWJsZWRTdGF0ZQEBiwQALwEAIyOLBAAAABX/////AQECAAAAAQAsIwABAZ8EAQAsIwABAagEAQAA" +
           "ABVgiQoCAAAAAAACAAAASWQBAYwEAC4ARIwEAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABR" +
           "dWFsaXR5AQGUBAAvAQAqI5QEAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1l" +
           "c3RhbXABAZUEAC4ARJUEAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0" +
           "eQEBlgQALwEAKiOWBAAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1w" +
           "AQGXBAAuAESXBAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQGYBAAvAQAq" +
           "I5gEAAAAFf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAZkEAC4ARJkE" +
           "AAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEBmgQALgBEmgQAAAAM" +
           "/////wEB/////wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBAZsEAC8BAEQjmwQAAAEBAQAAAAEA+QsA" +
           "AQDzCgAAAAAEYYIKBAAAAAAABgAAAEVuYWJsZQEBnAQALwEAQyOcBAAAAQEBAAAAAQD5CwABAPMKAAAA" +
           "AARhggoEAAAAAAAKAAAAQWRkQ29tbWVudAEBnQQALwEARSOdBAAAAQEBAAAAAQD5CwABAA0LAQAAABdg" +
           "qQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAZ4EAC4ARJ4EAACWAgAAAAEAKgEBRgAAAAcAAABFdmVu" +
           "dElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVu" +
           "dC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQg" +
           "dG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNr" +
           "ZWRTdGF0ZQEBnwQALwEAIyOfBAAAABX/////AQEBAAAAAQAsIwEBAYsEAQAAABVgiQoCAAAAAAACAAAA" +
           "SWQBAaAEAC4ARKAEAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBsQQA" +
           "LwEAlyOxBAAAAQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAbIE" +
           "AC4ARLIEAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVu" +
           "dGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAA" +
           "AAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAA" +
           "AAAAAQH/////AAAAABVgiQoCAAAAAQAMAAAAQm9vbGVhblZhbHVlAQG1BAAvAD+1BAAAAAH/////AQH/" +
           "////AAAAABVgiQoCAAAAAQAKAAAAU0J5dGVWYWx1ZQEBtgQALwA/tgQAAAAC/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEBtwQALwA/twQAAAAD/////wEB/////wAAAAAVYIkKAgAAAAEA" +
           "CgAAAEludDE2VmFsdWUBAbgEAC8AP7gEAAAABP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50" +
           "MTZWYWx1ZQEBuQQALwA/uQQAAAAF/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMyVmFsdWUB" +
           "AboEAC8AP7oEAAAABv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50MzJWYWx1ZQEBuwQALwA/" +
           "uwQAAAAH/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDY0VmFsdWUBAbwEAC8AP7wEAAAACP//" +
           "//8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBvQQALwA/vQQAAAAJ/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEACgAAAEZsb2F0VmFsdWUBAb4EAC8AP74EAAAACv////8BAf////8AAAAAFWCJ" +
           "CgIAAAABAAsAAABEb3VibGVWYWx1ZQEBvwQALwA/vwQAAAAL/////wEB/////wAAAAAVYIkKAgAAAAEA" +
           "CwAAAFN0cmluZ1ZhbHVlAQHABAAvAD/ABAAAAAz/////AQH/////AAAAABVgiQoCAAAAAQANAAAARGF0" +
           "ZVRpbWVWYWx1ZQEBwQQALwA/wQQAAAAN/////wEB/////wAAAAAVYIkKAgAAAAEACQAAAEd1aWRWYWx1" +
           "ZQEBwgQALwA/wgQAAAAO/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAEJ5dGVTdHJpbmdWYWx1ZQEB" +
           "wwQALwA/wwQAAAAP/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAFhtbEVsZW1lbnRWYWx1ZQEBxAQA" +
           "LwA/xAQAAAAQ/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAE5vZGVJZFZhbHVlAQHFBAAvAD/FBAAA" +
           "ABH/////AQH/////AAAAABVgiQoCAAAAAQATAAAARXhwYW5kZWROb2RlSWRWYWx1ZQEBxgQALwA/xgQA" +
           "AAAS/////wEB/////wAAAAAVYIkKAgAAAAEAEgAAAFF1YWxpZmllZE5hbWVWYWx1ZQEBxwQALwA/xwQA" +
           "AAAU/////wEB/////wAAAAAVYIkKAgAAAAEAEgAAAExvY2FsaXplZFRleHRWYWx1ZQEByAQALwA/yAQA" +
           "AAAV/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAFN0YXR1c0NvZGVWYWx1ZQEByQQALwA/yQQAAAAT" +
           "/////wEB/////wAAAAAVYIkKAgAAAAEADAAAAFZhcmlhbnRWYWx1ZQEBygQALwA/ygQAAAAY/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEAEAAAAEVudW1lcmF0aW9uVmFsdWUBAcsEAC8AP8sEAAAAHf////8BAf//" +
           "//8AAAAAFWCJCgIAAAABAA4AAABTdHJ1Y3R1cmVWYWx1ZQEBzAQALwA/zAQAAAAW/////wEB/////wAA" +
           "AAAVYIkKAgAAAAEACwAAAE51bWJlclZhbHVlAQHNBAAvAD/NBAAAABr/////AQH/////AAAAABVgiQoC" +
           "AAAAAQAMAAAASW50ZWdlclZhbHVlAQHOBAAvAD/OBAAAABv/////AQH/////AAAAABVgiQoCAAAAAQAN" +
           "AAAAVUludGVnZXJWYWx1ZQEBzwQALwA/zwQAAAAc/////wEB/////wAAAAA=";
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
            get { return m_value; }
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
                variable.OnWriteValue = OnWriteValue;

                BaseVariableState instance = null;
                List<BaseInstanceState> updateList = new List<BaseInstanceState>();
                updateList.Add(variable);

                instance = m_variable.BooleanValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_BooleanValue;
                    instance.OnWriteValue = OnWrite_BooleanValue;
                    updateList.Add(instance);
                }
                instance = m_variable.SByteValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_SByteValue;
                    instance.OnWriteValue = OnWrite_SByteValue;
                    updateList.Add(instance);
                }
                instance = m_variable.ByteValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_ByteValue;
                    instance.OnWriteValue = OnWrite_ByteValue;
                    updateList.Add(instance);
                }
                instance = m_variable.Int16Value;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_Int16Value;
                    instance.OnWriteValue = OnWrite_Int16Value;
                    updateList.Add(instance);
                }
                instance = m_variable.UInt16Value;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_UInt16Value;
                    instance.OnWriteValue = OnWrite_UInt16Value;
                    updateList.Add(instance);
                }
                instance = m_variable.Int32Value;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_Int32Value;
                    instance.OnWriteValue = OnWrite_Int32Value;
                    updateList.Add(instance);
                }
                instance = m_variable.UInt32Value;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_UInt32Value;
                    instance.OnWriteValue = OnWrite_UInt32Value;
                    updateList.Add(instance);
                }
                instance = m_variable.Int64Value;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_Int64Value;
                    instance.OnWriteValue = OnWrite_Int64Value;
                    updateList.Add(instance);
                }
                instance = m_variable.UInt64Value;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_UInt64Value;
                    instance.OnWriteValue = OnWrite_UInt64Value;
                    updateList.Add(instance);
                }
                instance = m_variable.FloatValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_FloatValue;
                    instance.OnWriteValue = OnWrite_FloatValue;
                    updateList.Add(instance);
                }
                instance = m_variable.DoubleValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_DoubleValue;
                    instance.OnWriteValue = OnWrite_DoubleValue;
                    updateList.Add(instance);
                }
                instance = m_variable.StringValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_StringValue;
                    instance.OnWriteValue = OnWrite_StringValue;
                    updateList.Add(instance);
                }
                instance = m_variable.DateTimeValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_DateTimeValue;
                    instance.OnWriteValue = OnWrite_DateTimeValue;
                    updateList.Add(instance);
                }
                instance = m_variable.GuidValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_GuidValue;
                    instance.OnWriteValue = OnWrite_GuidValue;
                    updateList.Add(instance);
                }
                instance = m_variable.ByteStringValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_ByteStringValue;
                    instance.OnWriteValue = OnWrite_ByteStringValue;
                    updateList.Add(instance);
                }
                instance = m_variable.XmlElementValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_XmlElementValue;
                    instance.OnWriteValue = OnWrite_XmlElementValue;
                    updateList.Add(instance);
                }
                instance = m_variable.NodeIdValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_NodeIdValue;
                    instance.OnWriteValue = OnWrite_NodeIdValue;
                    updateList.Add(instance);
                }
                instance = m_variable.ExpandedNodeIdValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_ExpandedNodeIdValue;
                    instance.OnWriteValue = OnWrite_ExpandedNodeIdValue;
                    updateList.Add(instance);
                }
                instance = m_variable.QualifiedNameValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_QualifiedNameValue;
                    instance.OnWriteValue = OnWrite_QualifiedNameValue;
                    updateList.Add(instance);
                }
                instance = m_variable.LocalizedTextValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_LocalizedTextValue;
                    instance.OnWriteValue = OnWrite_LocalizedTextValue;
                    updateList.Add(instance);
                }
                instance = m_variable.StatusCodeValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_StatusCodeValue;
                    instance.OnWriteValue = OnWrite_StatusCodeValue;
                    updateList.Add(instance);
                }
                instance = m_variable.VariantValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_VariantValue;
                    instance.OnWriteValue = OnWrite_VariantValue;
                    updateList.Add(instance);
                }
                instance = m_variable.EnumerationValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_EnumerationValue;
                    instance.OnWriteValue = OnWrite_EnumerationValue;
                    updateList.Add(instance);
                }
                instance = m_variable.StructureValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_StructureValue;
                    instance.OnWriteValue = OnWrite_StructureValue;
                    updateList.Add(instance);
                }
                instance = m_variable.NumberValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_NumberValue;
                    instance.OnWriteValue = OnWrite_NumberValue;
                    updateList.Add(instance);
                }
                instance = m_variable.IntegerValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_IntegerValue;
                    instance.OnWriteValue = OnWrite_IntegerValue;
                    updateList.Add(instance);
                }
                instance = m_variable.UIntegerValue;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_UIntegerValue;
                    instance.OnWriteValue = OnWrite_UIntegerValue;
                    updateList.Add(instance);
                }

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

        private ServiceResult OnWriteValue(
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
                ScalarValueDataType newValue;
                if (value is ExtensionObject extensionObject)
                {
                    newValue = (ScalarValueDataType)extensionObject.Body;
                }
                else
                {
                    newValue = (ScalarValueDataType)value;
                }

                if (!Utils.IsEqual(m_value, newValue))
                {
                    UpdateChildrenChangeMasks(context, ref newValue, ref statusCode, ref timestamp);
                    Timestamp = timestamp;
                    m_value = (ScalarValueDataType)Write(newValue);
                    m_variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
                }
            }

            return ServiceResult.Good;
        }

        private void UpdateChildrenChangeMasks(ISystemContext context, ref ScalarValueDataType newValue, ref StatusCode statusCode, ref DateTime timestamp)
        {
            if (!Utils.IsEqual(m_value.BooleanValue, newValue.BooleanValue)) UpdateChildVariableStatus(m_variable.BooleanValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.SByteValue, newValue.SByteValue)) UpdateChildVariableStatus(m_variable.SByteValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.ByteValue, newValue.ByteValue)) UpdateChildVariableStatus(m_variable.ByteValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.Int16Value, newValue.Int16Value)) UpdateChildVariableStatus(m_variable.Int16Value, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.UInt16Value, newValue.UInt16Value)) UpdateChildVariableStatus(m_variable.UInt16Value, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.Int32Value, newValue.Int32Value)) UpdateChildVariableStatus(m_variable.Int32Value, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.UInt32Value, newValue.UInt32Value)) UpdateChildVariableStatus(m_variable.UInt32Value, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.Int64Value, newValue.Int64Value)) UpdateChildVariableStatus(m_variable.Int64Value, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.UInt64Value, newValue.UInt64Value)) UpdateChildVariableStatus(m_variable.UInt64Value, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.FloatValue, newValue.FloatValue)) UpdateChildVariableStatus(m_variable.FloatValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.DoubleValue, newValue.DoubleValue)) UpdateChildVariableStatus(m_variable.DoubleValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.StringValue, newValue.StringValue)) UpdateChildVariableStatus(m_variable.StringValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.DateTimeValue, newValue.DateTimeValue)) UpdateChildVariableStatus(m_variable.DateTimeValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.GuidValue, newValue.GuidValue)) UpdateChildVariableStatus(m_variable.GuidValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.ByteStringValue, newValue.ByteStringValue)) UpdateChildVariableStatus(m_variable.ByteStringValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.XmlElementValue, newValue.XmlElementValue)) UpdateChildVariableStatus(m_variable.XmlElementValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.NodeIdValue, newValue.NodeIdValue)) UpdateChildVariableStatus(m_variable.NodeIdValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.ExpandedNodeIdValue, newValue.ExpandedNodeIdValue)) UpdateChildVariableStatus(m_variable.ExpandedNodeIdValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.QualifiedNameValue, newValue.QualifiedNameValue)) UpdateChildVariableStatus(m_variable.QualifiedNameValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.LocalizedTextValue, newValue.LocalizedTextValue)) UpdateChildVariableStatus(m_variable.LocalizedTextValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.StatusCodeValue, newValue.StatusCodeValue)) UpdateChildVariableStatus(m_variable.StatusCodeValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.VariantValue, newValue.VariantValue)) UpdateChildVariableStatus(m_variable.VariantValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.EnumerationValue, newValue.EnumerationValue)) UpdateChildVariableStatus(m_variable.EnumerationValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.StructureValue, newValue.StructureValue)) UpdateChildVariableStatus(m_variable.StructureValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.NumberValue, newValue.NumberValue)) UpdateChildVariableStatus(m_variable.NumberValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.IntegerValue, newValue.IntegerValue)) UpdateChildVariableStatus(m_variable.IntegerValue, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.UIntegerValue, newValue.UIntegerValue)) UpdateChildVariableStatus(m_variable.UIntegerValue, ref statusCode, ref timestamp);
        }

        private void UpdateParent(ISystemContext context, ref StatusCode statusCode, ref DateTime timestamp)
        {
            Timestamp = timestamp;
            m_variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
            m_variable.ClearChangeMasks(context, false);
        }

        private void UpdateChildVariableStatus(BaseVariableState child, ref StatusCode statusCode, ref DateTime timestamp)
        {
            if (child == null) return;
            child.StatusCode = statusCode;
            if (timestamp == DateTime.MinValue)
            {
                timestamp = DateTime.UtcNow;
            }
            child.Timestamp = timestamp;
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

                var childVariable = m_variable?.BooleanValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.BooleanValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_BooleanValue(
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
                UpdateChildVariableStatus(m_variable.BooleanValue, ref statusCode, ref timestamp);
                m_value.BooleanValue = (bool)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.SByteValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.SByteValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_SByteValue(
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
                UpdateChildVariableStatus(m_variable.SByteValue, ref statusCode, ref timestamp);
                m_value.SByteValue = (sbyte)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.ByteValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.ByteValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_ByteValue(
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
                UpdateChildVariableStatus(m_variable.ByteValue, ref statusCode, ref timestamp);
                m_value.ByteValue = (byte)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.Int16Value;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.Int16Value;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Int16Value(
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
                UpdateChildVariableStatus(m_variable.Int16Value, ref statusCode, ref timestamp);
                m_value.Int16Value = (short)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.UInt16Value;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.UInt16Value;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UInt16Value(
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
                UpdateChildVariableStatus(m_variable.UInt16Value, ref statusCode, ref timestamp);
                m_value.UInt16Value = (ushort)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.Int32Value;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.Int32Value;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Int32Value(
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
                UpdateChildVariableStatus(m_variable.Int32Value, ref statusCode, ref timestamp);
                m_value.Int32Value = (int)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.UInt32Value;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.UInt32Value;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UInt32Value(
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
                UpdateChildVariableStatus(m_variable.UInt32Value, ref statusCode, ref timestamp);
                m_value.UInt32Value = (uint)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.Int64Value;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.Int64Value;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Int64Value(
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
                UpdateChildVariableStatus(m_variable.Int64Value, ref statusCode, ref timestamp);
                m_value.Int64Value = (long)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.UInt64Value;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.UInt64Value;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UInt64Value(
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
                UpdateChildVariableStatus(m_variable.UInt64Value, ref statusCode, ref timestamp);
                m_value.UInt64Value = (ulong)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.FloatValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.FloatValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_FloatValue(
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
                UpdateChildVariableStatus(m_variable.FloatValue, ref statusCode, ref timestamp);
                m_value.FloatValue = (float)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.DoubleValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.DoubleValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_DoubleValue(
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
                UpdateChildVariableStatus(m_variable.DoubleValue, ref statusCode, ref timestamp);
                m_value.DoubleValue = (double)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.StringValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.StringValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_StringValue(
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
                UpdateChildVariableStatus(m_variable.StringValue, ref statusCode, ref timestamp);
                m_value.StringValue = (string)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.DateTimeValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.DateTimeValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_DateTimeValue(
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
                UpdateChildVariableStatus(m_variable.DateTimeValue, ref statusCode, ref timestamp);
                m_value.DateTimeValue = (DateTime)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.GuidValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.GuidValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_GuidValue(
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
                UpdateChildVariableStatus(m_variable.GuidValue, ref statusCode, ref timestamp);
                m_value.GuidValue = (Uuid)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.ByteStringValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.ByteStringValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_ByteStringValue(
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
                UpdateChildVariableStatus(m_variable.ByteStringValue, ref statusCode, ref timestamp);
                m_value.ByteStringValue = (byte[])Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.XmlElementValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.XmlElementValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_XmlElementValue(
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
                UpdateChildVariableStatus(m_variable.XmlElementValue, ref statusCode, ref timestamp);
                m_value.XmlElementValue = (XmlElement)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.NodeIdValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.NodeIdValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_NodeIdValue(
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
                UpdateChildVariableStatus(m_variable.NodeIdValue, ref statusCode, ref timestamp);
                m_value.NodeIdValue = (NodeId)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.ExpandedNodeIdValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.ExpandedNodeIdValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_ExpandedNodeIdValue(
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
                UpdateChildVariableStatus(m_variable.ExpandedNodeIdValue, ref statusCode, ref timestamp);
                m_value.ExpandedNodeIdValue = (ExpandedNodeId)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.QualifiedNameValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.QualifiedNameValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_QualifiedNameValue(
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
                UpdateChildVariableStatus(m_variable.QualifiedNameValue, ref statusCode, ref timestamp);
                m_value.QualifiedNameValue = (QualifiedName)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.LocalizedTextValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.LocalizedTextValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_LocalizedTextValue(
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
                UpdateChildVariableStatus(m_variable.LocalizedTextValue, ref statusCode, ref timestamp);
                m_value.LocalizedTextValue = (LocalizedText)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.StatusCodeValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.StatusCodeValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_StatusCodeValue(
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
                UpdateChildVariableStatus(m_variable.StatusCodeValue, ref statusCode, ref timestamp);
                m_value.StatusCodeValue = (StatusCode)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.VariantValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.VariantValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_VariantValue(
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
                UpdateChildVariableStatus(m_variable.VariantValue, ref statusCode, ref timestamp);
                m_value.VariantValue = (Variant)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.EnumerationValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.EnumerationValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_EnumerationValue(
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
                UpdateChildVariableStatus(m_variable.EnumerationValue, ref statusCode, ref timestamp);
                m_value.EnumerationValue = (int)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.StructureValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.StructureValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_StructureValue(
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
                UpdateChildVariableStatus(m_variable.StructureValue, ref statusCode, ref timestamp);
                m_value.StructureValue = (ExtensionObject)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.NumberValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.NumberValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_NumberValue(
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
                UpdateChildVariableStatus(m_variable.NumberValue, ref statusCode, ref timestamp);
                m_value.NumberValue = (Variant)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.IntegerValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.IntegerValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_IntegerValue(
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
                UpdateChildVariableStatus(m_variable.IntegerValue, ref statusCode, ref timestamp);
                m_value.IntegerValue = (Variant)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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

                var childVariable = m_variable?.UIntegerValue;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.UIntegerValue;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_UIntegerValue(
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
                UpdateChildVariableStatus(m_variable.UIntegerValue, ref statusCode, ref timestamp);
                m_value.UIntegerValue = (Variant)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
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
           "ZTFNZXRob2RUeXBlAQHQBAAvAQHQBNAEAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEB0QQALgBE0QQAAJYLAAAAAQAqAQEYAAAACQAAAEJvb2xlYW5JbgAB/////wAAAAAAAQAqAQEW" +
           "AAAABwAAAFNCeXRlSW4AAv////8AAAAAAAEAKgEBFQAAAAYAAABCeXRlSW4AA/////8AAAAAAAEAKgEB" +
           "FgAAAAcAAABJbnQxNkluAAT/////AAAAAAABACoBARcAAAAIAAAAVUludDE2SW4ABf////8AAAAAAAEA" +
           "KgEBFgAAAAcAAABJbnQzMkluAAb/////AAAAAAABACoBARcAAAAIAAAAVUludDMySW4AB/////8AAAAA" +
           "AAEAKgEBFgAAAAcAAABJbnQ2NEluAAj/////AAAAAAABACoBARcAAAAIAAAAVUludDY0SW4ACf////8A" +
           "AAAAAAEAKgEBFgAAAAcAAABGbG9hdEluAAr/////AAAAAAABACoBARcAAAAIAAAARG91YmxlSW4AC///" +
           "//8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVu" +
           "dHMBAdIEAC4ARNIEAACWCwAAAAEAKgEBGQAAAAoAAABCb29sZWFuT3V0AAH/////AAAAAAABACoBARcA" +
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
           "ZTJNZXRob2RUeXBlAQHTBAAvAQHTBNMEAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEB1AQALgBE1AQAAJYKAAAAAQAqAQEXAAAACAAAAFN0cmluZ0luAAz/////AAAAAAABACoBARkA" +
           "AAAKAAAARGF0ZVRpbWVJbgAN/////wAAAAAAAQAqAQEVAAAABgAAAEd1aWRJbgAO/////wAAAAAAAQAq" +
           "AQEbAAAADAAAAEJ5dGVTdHJpbmdJbgAP/////wAAAAAAAQAqAQEbAAAADAAAAFhtbEVsZW1lbnRJbgAQ" +
           "/////wAAAAAAAQAqAQEXAAAACAAAAE5vZGVJZEluABH/////AAAAAAABACoBAR8AAAAQAAAARXhwYW5k" +
           "ZWROb2RlSWRJbgAS/////wAAAAAAAQAqAQEeAAAADwAAAFF1YWxpZmllZE5hbWVJbgAU/////wAAAAAA" +
           "AQAqAQEeAAAADwAAAExvY2FsaXplZFRleHRJbgAV/////wAAAAAAAQAqAQEbAAAADAAAAFN0YXR1c0Nv" +
           "ZGVJbgAT/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1" +
           "dEFyZ3VtZW50cwEB1QQALgBE1QQAAJYKAAAAAQAqAQEYAAAACQAAAFN0cmluZ091dAAM/////wAAAAAA" +
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
           "ZTNNZXRob2RUeXBlAQHWBAAvAQHWBNYEAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEB1wQALgBE1wQAAJYDAAAAAQAqAQEYAAAACQAAAFZhcmlhbnRJbgAY/////wAAAAAAAQAqAQEc" +
           "AAAADQAAAEVudW1lcmF0aW9uSW4AHf////8AAAAAAAEAKgEBGgAAAAsAAABTdHJ1Y3R1cmVJbgAW////" +
           "/wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50" +
           "cwEB2AQALgBE2AQAAJYDAAAAAQAqAQEZAAAACgAAAFZhcmlhbnRPdXQAGP////8AAAAAAAEAKgEBHQAA" +
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
           "ZU9iamVjdFR5cGVJbnN0YW5jZQEB2QQBAdkE2QQAAAEAAAAAJAABAd0EHwAAADVgiQoCAAAAAQAQAAAA" +
           "U2ltdWxhdGlvbkFjdGl2ZQEB2gQDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9kdWNl" +
           "IG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAETaBAAAAAH/////AQH/////" +
           "AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAdsEAC8BAfkD2wQAAAEB/////wEAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQHcBAAuAETcBAAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0" +
           "aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJhdGUu" +
           "AQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAd0EAC8B" +
           "AEEL3QQAAAEAAAAAJAEBAdkEFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEB3gQALgBE3gQAAAAP////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEB3wQALgBE3wQAAAAR/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAeAEAC4AROAEAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAoAAABTb3VyY2VOYW1lAQHhBAAuAEThBAAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAEAAAA" +
           "VGltZQEB4gQALgBE4gQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUB" +
           "AeMEAC4AROMEAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAeUEAC4AROUE" +
           "AAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEB5gQALgBE5gQAAAAF/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAecEAC4AROcEAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAegEAC4AROgEAAAAFf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQHrBAAuAETrBAAAAAz/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAewEAC4AROwEAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAYAAABSZXRhaW4BAe0EAC4ARO0EAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFibGVk" +
           "U3RhdGUBAe4EAC8BACMj7gQAAAAV/////wEBAgAAAAEALCMAAQECBQEALCMAAQELBQEAAAAVYIkKAgAA" +
           "AAAAAgAAAElkAQHvBAAuAETvBAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0eQEB" +
           "9wQALwEAKiP3BAAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQH4" +
           "BAAuAET4BAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAfkEAC8B" +
           "ACoj+QQAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB+gQALgBE" +
           "+gQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEB+wQALwEAKiP7BAAAABX/" +
           "////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQH8BAAuAET8BAAAAQAmAf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAf0EAC4ARP0EAAAADP////8BAf//" +
           "//8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQH+BAAvAQBEI/4EAAABAQEAAAABAPkLAAEA8woAAAAA" +
           "BGGCCgQAAAAAAAYAAABFbmFibGUBAf8EAC8BAEMj/wQAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIKBAAA" +
           "AAAACgAAAEFkZENvbW1lbnQBAQAFAC8BAEUjAAUAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAAAAAA" +
           "DgAAAElucHV0QXJndW1lbnRzAQEBBQAuAEQBBQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP////" +
           "/wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFC" +
           "AAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBj" +
           "b25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3RhdGUB" +
           "AQIFAC8BACMjAgUAAAAV/////wEBAQAAAAEALCMBAQHuBAEAAAAVYIkKAgAAAAAAAgAAAElkAQEDBQAu" +
           "AEQDBQAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBARQFAC8BAJcjFAUA" +
           "AAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEVBQAuAEQVBQAA" +
           "lgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBm" +
           "b3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAk" +
           "AAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAVYIkKAgAAAAEADAAAAEJvb2xlYW5WYWx1ZQEBGAUALwA/GAUAAAAB/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBARkFAC8APxkFAAAAAv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AAkAAABCeXRlVmFsdWUBARoFAC8APxoFAAAAA/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQx" +
           "NlZhbHVlAQEbBQAvAD8bBQAAAAT/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDE2VmFsdWUB" +
           "ARwFAC8APxwFAAAABf////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQzMlZhbHVlAQEdBQAvAD8d" +
           "BQAAAAb/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBAR4FAC8APx4FAAAAB///" +
           "//8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2NFZhbHVlAQEfBQAvAD8fBQAAAAj/////AQH/////" +
           "AAAAABVgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBASAFAC8APyAFAAAACf////8BAf////8AAAAAFWCJ" +
           "CgIAAAABAAoAAABGbG9hdFZhbHVlAQEhBQAvAD8hBQAAAAr/////AQH/////AAAAABVgiQoCAAAAAQAL" +
           "AAAARG91YmxlVmFsdWUBASIFAC8APyIFAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABTdHJp" +
           "bmdWYWx1ZQEBIwUALwA/IwUAAAAM/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAERhdGVUaW1lVmFs" +
           "dWUBASQFAC8APyQFAAAADf////8BAf////8AAAAAFWCJCgIAAAABAAkAAABHdWlkVmFsdWUBASUFAC8A" +
           "PyUFAAAADv////8BAf////8AAAAAFWCJCgIAAAABAA8AAABCeXRlU3RyaW5nVmFsdWUBASYFAC8APyYF" +
           "AAAAD/////8BAf////8AAAAAFWCJCgIAAAABAA8AAABYbWxFbGVtZW50VmFsdWUBAScFAC8APycFAAAA" +
           "EP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABOb2RlSWRWYWx1ZQEBKAUALwA/KAUAAAAR/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEAEwAAAEV4cGFuZGVkTm9kZUlkVmFsdWUBASkFAC8APykFAAAAEv////8B" +
           "Af////8AAAAAFWCJCgIAAAABABIAAABRdWFsaWZpZWROYW1lVmFsdWUBASoFAC8APyoFAAAAFP////8B" +
           "Af////8AAAAAFWCJCgIAAAABABIAAABMb2NhbGl6ZWRUZXh0VmFsdWUBASsFAC8APysFAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAABAA8AAABTdGF0dXNDb2RlVmFsdWUBASwFAC8APywFAAAAE/////8BAf//" +
           "//8AAAAAFWCJCgIAAAABAAwAAABWYXJpYW50VmFsdWUBAS0FAC8APy0FAAAAGP////8BAf////8AAAAA" +
           "FWCJCgIAAAABABAAAABFbnVtZXJhdGlvblZhbHVlAQEuBQAvAD8uBQAAAB3/////AQH/////AAAAABVg" +
           "iQoCAAAAAQAOAAAAU3RydWN0dXJlVmFsdWUBAS8FAC8APy8FAAAAFv////8BAf////8AAAAAFWCJCgIA" +
           "AAABAAsAAABOdW1iZXJWYWx1ZQEBMAUALwA/MAUAAAAa/////wEB/////wAAAAAVYIkKAgAAAAEADAAA" +
           "AEludGVnZXJWYWx1ZQEBMQUALwA/MQUAAAAb/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAFVJbnRl" +
           "Z2VyVmFsdWUBATIFAC8APzIFAAAAHP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABWZWN0b3JWYWx1" +
           "ZQEBMwUALwEBfAczBQAAAQF7B/////8BAf////8DAAAAFWCJCgIAAAABAAEAAABYAQEJDgAuAEQJDgAA" +
           "AAv/////AQH/////AAAAABVgiQoCAAAAAQABAAAAWQEBCg4ALgBECg4AAAAL/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEAAQAAAFoBAQsOAC4ARAsOAAAAC/////8BAf////8AAAAA";
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

        /// <remarks />
        public VectorVariableState VectorValue
        {
            get
            {
                return m_vectorValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vectorValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vectorValue = value;
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

            if (m_vectorValue != null)
            {
                children.Add(m_vectorValue);
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

                case TestData.BrowseNames.VectorValue:
                {
                    if (createOrReplace)
                    {
                        if (VectorValue == null)
                        {
                            if (replacement == null)
                            {
                                VectorValue = new VectorVariableState(this);
                            }
                            else
                            {
                                VectorValue = (VectorVariableState)replacement;
                            }
                        }
                    }

                    instance = VectorValue;
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
        private VectorVariableState m_vectorValue;
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
           "YXJWYWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBNAUBATQFNAUAAAEAAAAAJAABATgFEAAAADVgiQoCAAAA" +
           "AQAQAAAAU2ltdWxhdGlvbkFjdGl2ZQEBNQUDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBw" +
           "cm9kdWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAEQ1BQAAAAH/////" +
           "AQH/////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBATYFAC8BAfkDNgUAAAEB/////wEA" +
           "AAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQE3BQAuAEQ3BQAAlgEAAAABACoBAUYAAAAKAAAA" +
           "SXRlcmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2Vu" +
           "ZXJhdGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUB" +
           "ATgFAC8BAEELOAUAAAEAAAAAJAEBATQFFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBOQUALgBEOQUA" +
           "AAAP/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBOgUALgBEOgUAAAAR/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBATsFAC4ARDsFAAAAEf////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAoAAABTb3VyY2VOYW1lAQE8BQAuAEQ8BQAAAAz/////AQH/////AAAAABVgiQoCAAAA" +
           "AAAEAAAAVGltZQEBPQUALgBEPQUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2" +
           "ZVRpbWUBAT4FAC4ARD4FAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAUAF" +
           "AC4AREAFAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBQQUALgBEQQUAAAAF" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAUIFAC4AREIFAAAAEf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAUMFAC4AREMFAAAAFf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQFGBQAuAERGBQAAAAz/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAUcFAC4AREcFAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAYAAABSZXRhaW4BAUgFAC4AREgFAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABF" +
           "bmFibGVkU3RhdGUBAUkFAC8BACMjSQUAAAAV/////wEBAgAAAAEALCMAAQFdBQEALCMAAQFmBQEAAAAV" +
           "YIkKAgAAAAAAAgAAAElkAQFKBQAuAERKBQAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVh" +
           "bGl0eQEBUgUALwEAKiNSBQAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0" +
           "YW1wAQFTBQAuAERTBQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkB" +
           "AVQFAC8BACojVAUAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB" +
           "VQUALgBEVQUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBVgUALwEAKiNW" +
           "BQAAABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQFXBQAuAERXBQAA" +
           "AQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAVgFAC4ARFgFAAAADP//" +
           "//8BAf////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQFZBQAvAQBEI1kFAAABAQEAAAABAPkLAAEA" +
           "8woAAAAABGGCCgQAAAAAAAYAAABFbmFibGUBAVoFAC8BAEMjWgUAAAEBAQAAAAEA+QsAAQDzCgAAAAAE" +
           "YYIKBAAAAAAACgAAAEFkZENvbW1lbnQBAVsFAC8BAEUjWwUAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFcBQAuAERcBQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJ" +
           "ZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQu" +
           "AQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRv" +
           "IHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2Vk" +
           "U3RhdGUBAV0FAC8BACMjXQUAAAAV/////wEBAQAAAAEALCMBAQFJBQEAAAAVYIkKAgAAAAAAAgAAAElk" +
           "AQFeBQAuAEReBQAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAW8FAC8B" +
           "AJcjbwUAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFwBQAu" +
           "AERwBQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRp" +
           "ZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAA" +
           "AwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAA" +
           "AAEB/////wAAAAAVYIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAXMFAC8BAEAJcwUAAAAC/////wEB////" +
           "/wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAXcFAC4ARHcFAAABAHQD/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEACQAAAEJ5dGVWYWx1ZQEBeQUALwEAQAl5BQAAAAP/////AQH/////AQAAABVgiQoCAAAAAAAH" +
           "AAAARVVSYW5nZQEBfQUALgBEfQUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50MTZW" +
           "YWx1ZQEBfwUALwEAQAl/BQAAAAT/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBgwUA" +
           "LgBEgwUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDE2VmFsdWUBAYUFAC8BAEAJ" +
           "hQUAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAYkFAC4ARIkFAAABAHQD////" +
           "/wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMyVmFsdWUBAYsFAC8BAEAJiwUAAAAG/////wEB////" +
           "/wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAY8FAC4ARI8FAAABAHQD/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEACwAAAFVJbnQzMlZhbHVlAQGRBQAvAQBACZEFAAAAB/////8BAf////8BAAAAFWCJCgIAAAAA" +
           "AAcAAABFVVJhbmdlAQGVBQAuAESVBQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2" +
           "NFZhbHVlAQGXBQAvAQBACZcFAAAACP////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGb" +
           "BQAuAESbBQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBnQUALwEA" +
           "QAmdBQAAAAn/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBoQUALgBEoQUAAAEAdAP/" +
           "////AQH/////AAAAABVgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBowUALwEAQAmjBQAAAAr/////AQH/" +
           "////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBpwUALgBEpwUAAAEAdAP/////AQH/////AAAAABVg" +
           "iQoCAAAAAQALAAAARG91YmxlVmFsdWUBAakFAC8BAEAJqQUAAAAL/////wEB/////wEAAAAVYIkKAgAA" +
           "AAAABwAAAEVVUmFuZ2UBAa0FAC4ARK0FAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAE51" +
           "bWJlclZhbHVlAQGvBQAvAQBACa8FAAAAGv////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdl" +
           "AQGzBQAuAESzBQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBAbUF" +
           "AC8BAEAJtQUAAAAb/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAbkFAC4ARLkFAAAB" +
           "AHQD/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAFVJbnRlZ2VyVmFsdWUBAbsFAC8BAEAJuwUAAAAc" +
           "/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAb8FAC4ARL8FAAABAHQD/////wEB////" +
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
           "MU1ldGhvZFR5cGUBAcIFAC8BAcIFwgUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQHDBQAuAETDBQAAlgsAAAABACoBARwAAAAJAAAAQm9vbGVhbkluAAEBAAAAAQAAAAAAAAAAAQAq" +
           "AQEaAAAABwAAAFNCeXRlSW4AAgEAAAABAAAAAAAAAAABACoBARkAAAAGAAAAQnl0ZUluAAMBAAAAAQAA" +
           "AAAAAAAAAQAqAQEaAAAABwAAAEludDE2SW4ABAEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAVUludDE2" +
           "SW4ABQEAAAABAAAAAAAAAAABACoBARoAAAAHAAAASW50MzJJbgAGAQAAAAEAAAAAAAAAAAEAKgEBGwAA" +
           "AAgAAABVSW50MzJJbgAHAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABJbnQ2NEluAAgBAAAAAQAAAAAA" +
           "AAAAAQAqAQEbAAAACAAAAFVJbnQ2NEluAAkBAAAAAQAAAAAAAAAAAQAqAQEaAAAABwAAAEZsb2F0SW4A" +
           "CgEAAAABAAAAAAAAAAABACoBARsAAAAIAAAARG91YmxlSW4ACwEAAAABAAAAAAAAAAABACgBAQAAAAEA" +
           "AAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQHEBQAuAETEBQAAlgsA" +
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
           "Mk1ldGhvZFR5cGUBAcUFAC8BAcUFxQUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQHGBQAuAETGBQAAlgoAAAABACoBARsAAAAIAAAAU3RyaW5nSW4ADAEAAAABAAAAAAAAAAABACoB" +
           "AR0AAAAKAAAARGF0ZVRpbWVJbgANAQAAAAEAAAAAAAAAAAEAKgEBGQAAAAYAAABHdWlkSW4ADgEAAAAB" +
           "AAAAAAAAAAABACoBAR8AAAAMAAAAQnl0ZVN0cmluZ0luAA8BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAA" +
           "AFhtbEVsZW1lbnRJbgAQAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABOb2RlSWRJbgARAQAAAAEAAAAA" +
           "AAAAAAEAKgEBIwAAABAAAABFeHBhbmRlZE5vZGVJZEluABIBAAAAAQAAAAAAAAAAAQAqAQEiAAAADwAA" +
           "AFF1YWxpZmllZE5hbWVJbgAUAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA8AAABMb2NhbGl6ZWRUZXh0SW4A" +
           "FQEAAAABAAAAAAAAAAABACoBAR8AAAAMAAAAU3RhdHVzQ29kZUluABMBAAAAAQAAAAAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBxwUALgBExwUA" +
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
           "M01ldGhvZFR5cGUBAcgFAC8BAcgFyAUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQHJBQAuAETJBQAAlgMAAAABACoBARwAAAAJAAAAVmFyaWFudEluABgBAAAAAQAAAAAAAAAAAQAq" +
           "AQEgAAAADQAAAEVudW1lcmF0aW9uSW4AHQEAAAABAAAAAAAAAAABACoBAR4AAAALAAAAU3RydWN0dXJl" +
           "SW4AFgEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0" +
           "cHV0QXJndW1lbnRzAQHKBQAuAETKBQAAlgMAAAABACoBAR0AAAAKAAAAVmFyaWFudE91dAAYAQAAAAEA" +
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
           "T2JqZWN0VHlwZUluc3RhbmNlAQHLBQEBywXLBQAAAQAAAAAkAAEBzwUfAAAANWCJCgIAAAABABAAAABT" +
           "aW11bGF0aW9uQWN0aXZlAQHMBQMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHByb2R1Y2Ug" +
           "bmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARMwFAAAAAf////8BAf////8A" +
           "AAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBzQUALwEB+QPNBQAAAQH/////AQAAABdgqQoC" +
           "AAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAc4FAC4ARM4FAACWAQAAAAEAKgEBRgAAAAoAAABJdGVyYXRp" +
           "b25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5lcmF0ZS4B" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEBzwUALwEA" +
           "QQvPBQAAAQAAAAAkAQEBywUXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQHQBQAuAETQBQAAAA//////" +
           "AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQHRBQAuAETRBQAAABH/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEB0gUALgBE0gUAAAAR/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACgAAAFNvdXJjZU5hbWUBAdMFAC4ARNMFAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAQAAABU" +
           "aW1lAQHUBQAuAETUBQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZlVGltZQEB" +
           "1QUALgBE1QUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEB1wUALgBE1wUA" +
           "AAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQHYBQAuAETYBQAAAAX/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEB2QUALgBE2QUAAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEB2gUALgBE2gUAAAAV/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBAd0FAC4ARN0FAAAADP////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAgAAABCcmFuY2hJZAEB3gUALgBE3gUAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BgAAAFJldGFpbgEB3wUALgBE3wUAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVuYWJsZWRT" +
           "dGF0ZQEB4AUALwEAIyPgBQAAABX/////AQECAAAAAQAsIwABAfQFAQAsIwABAf0FAQAAABVgiQoCAAAA" +
           "AAACAAAASWQBAeEFAC4AROEFAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFsaXR5AQHp" +
           "BQAvAQAqI+kFAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAeoF" +
           "AC4AROoFAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEB6wUALwEA" +
           "KiPrBQAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQHsBQAuAETs" +
           "BQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQHtBQAvAQAqI+0FAAAAFf//" +
           "//8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAe4FAC4ARO4FAAABACYB////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEB7wUALgBE7wUAAAAM/////wEB////" +
           "/wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBAfAFAC8BAEQj8AUAAAEBAQAAAAEA+QsAAQDzCgAAAAAE" +
           "YYIKBAAAAAAABgAAAEVuYWJsZQEB8QUALwEAQyPxBQAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoEAAAA" +
           "AAAKAAAAQWRkQ29tbWVudAEB8gUALwEARSPyBQAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMBAfMFAC4ARPMFAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////" +
           "AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIA" +
           "AAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNv" +
           "bmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRTdGF0ZQEB" +
           "9AUALwEAIyP0BQAAABX/////AQEBAAAAAQAsIwEBAeAFAQAAABVgiQoCAAAAAAACAAAASWQBAfUFAC4A" +
           "RPUFAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBBgYALwEAlyMGBgAA" +
           "AQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAQcGAC4ARAcGAACW" +
           "AgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZv" +
           "ciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQA" +
           "AABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////" +
           "AAAAABdgiQoCAAAAAQAMAAAAQm9vbGVhblZhbHVlAQEKBgAvAD8KBgAAAAEBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAoAAABTQnl0ZVZhbHVlAQELBgAvAD8LBgAAAAIBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAkAAABCeXRlVmFsdWUBAQwGAC8APwwGAAAAAwEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACgAAAEludDE2VmFsdWUBAQ0GAC8APw0GAAAABAEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAFVJbnQxNlZhbHVlAQEOBgAvAD8OBgAAAAUBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAoAAABJbnQzMlZhbHVlAQEPBgAvAD8PBgAAAAYBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAsAAABVSW50MzJWYWx1ZQEBEAYALwA/EAYAAAAHAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAABdgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBEQYALwA/EQYAAAAIAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAABdgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBARIGAC8APxIGAAAACQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEACgAAAEZsb2F0VmFsdWUBARMGAC8APxMGAAAACgEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEACwAAAERvdWJsZVZhbHVlAQEUBgAvAD8UBgAAAAsBAAAAAQAAAAAAAAAB" +
           "Af////8AAAAAF2CJCgIAAAABAAsAAABTdHJpbmdWYWx1ZQEBFQYALwA/FQYAAAAMAQAAAAEAAAAAAAAA" +
           "AQH/////AAAAABdgiQoCAAAAAQANAAAARGF0ZVRpbWVWYWx1ZQEBFgYALwA/FgYAAAANAQAAAAEAAAAA" +
           "AAAAAQH/////AAAAABdgiQoCAAAAAQAJAAAAR3VpZFZhbHVlAQEXBgAvAD8XBgAAAA4BAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABAA8AAABCeXRlU3RyaW5nVmFsdWUBARgGAC8APxgGAAAADwEAAAAB" +
           "AAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAFhtbEVsZW1lbnRWYWx1ZQEBGQYALwA/GQYAAAAQ" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQALAAAATm9kZUlkVmFsdWUBARoGAC8APxoGAAAA" +
           "EQEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEAEwAAAEV4cGFuZGVkTm9kZUlkVmFsdWUBARsG" +
           "AC8APxsGAAAAEgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEAEgAAAFF1YWxpZmllZE5hbWVW" +
           "YWx1ZQEBHAYALwA/HAYAAAAUAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQASAAAATG9jYWxp" +
           "emVkVGV4dFZhbHVlAQEdBgAvAD8dBgAAABUBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAA8A" +
           "AABTdGF0dXNDb2RlVmFsdWUBAR4GAC8APx4GAAAAEwEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAA" +
           "AAEADAAAAFZhcmlhbnRWYWx1ZQEBHwYALwA/HwYAAAAYAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoC" +
           "AAAAAQAQAAAARW51bWVyYXRpb25WYWx1ZQEBIAYALwA/IAYAAAAdAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQAOAAAAU3RydWN0dXJlVmFsdWUBASEGAC8APyEGAAAAFgEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAE51bWJlclZhbHVlAQEiBgAvAD8iBgAAABoBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBASMGAC8APyMGAAAAGwEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEADQAAAFVJbnRlZ2VyVmFsdWUBASQGAC8APyQGAAAAHAEAAAABAAAAAAAA" +
           "AAEB/////wAAAAAXYIkKAgAAAAEACwAAAFZlY3RvclZhbHVlAQElBgAvAD8lBgAAAQF7BwEAAAABAAAA" +
           "AAAAAAEB/////wAAAAA=";
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

        /// <remarks />
        public BaseDataVariableState<Vector[]> VectorValue
        {
            get
            {
                return m_vectorValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vectorValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vectorValue = value;
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

            if (m_vectorValue != null)
            {
                children.Add(m_vectorValue);
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

                case TestData.BrowseNames.VectorValue:
                {
                    if (createOrReplace)
                    {
                        if (VectorValue == null)
                        {
                            if (replacement == null)
                            {
                                VectorValue = new BaseDataVariableState<Vector[]>(this);
                            }
                            else
                            {
                                VectorValue = (BaseDataVariableState<Vector[]>)replacement;
                            }
                        }
                    }

                    instance = VectorValue;
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
        private BaseDataVariableState<Vector[]> m_vectorValue;
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
           "eVZhbHVlT2JqZWN0VHlwZUluc3RhbmNlAQEmBgEBJgYmBgAAAQAAAAAkAAEBKgYQAAAANWCJCgIAAAAB" +
           "ABAAAABTaW11bGF0aW9uQWN0aXZlAQEnBgMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHBy" +
           "b2R1Y2UgbmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARCcGAAAAAf////8B" +
           "Af////8AAAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBKAYALwEB+QMoBgAAAQH/////AQAA" +
           "ABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBASkGAC4ARCkGAACWAQAAAAEAKgEBRgAAAAoAAABJ" +
           "dGVyYXRpb25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5l" +
           "cmF0ZS4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEB" +
           "KgYALwEAQQsqBgAAAQAAAAAkAQEBJgYXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQErBgAuAEQrBgAA" +
           "AA//////AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQEsBgAuAEQsBgAAABH/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBLQYALgBELQYAAAAR/////wEB/////wAAAAAV" +
           "YIkKAgAAAAAACgAAAFNvdXJjZU5hbWUBAS4GAC4ARC4GAAAADP////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAQAAABUaW1lAQEvBgAuAEQvBgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZl" +
           "VGltZQEBMAYALgBEMAYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEBMgYA" +
           "LgBEMgYAAAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQEzBgAuAEQzBgAAAAX/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBNAYALgBENAYAAAAR////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBNQYALgBENQYAAAAV////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBATgGAC4ARDgGAAAADP////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAgAAABCcmFuY2hJZAEBOQYALgBEOQYAAAAR/////wEB/////wAAAAAVYIkK" +
           "AgAAAAAABgAAAFJldGFpbgEBOgYALgBEOgYAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVu" +
           "YWJsZWRTdGF0ZQEBOwYALwEAIyM7BgAAABX/////AQECAAAAAQAsIwABAU8GAQAsIwABAVgGAQAAABVg" +
           "iQoCAAAAAAACAAAASWQBATwGAC4ARDwGAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFs" +
           "aXR5AQFEBgAvAQAqI0QGAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3Rh" +
           "bXABAUUGAC4AREUGAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEB" +
           "RgYALwEAKiNGBgAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQFH" +
           "BgAuAERHBgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQFIBgAvAQAqI0gG" +
           "AAAAFf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAUkGAC4AREkGAAAB" +
           "ACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEBSgYALgBESgYAAAAM////" +
           "/wEB/////wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBAUsGAC8BAEQjSwYAAAEBAQAAAAEA+QsAAQDz" +
           "CgAAAAAEYYIKBAAAAAAABgAAAEVuYWJsZQEBTAYALwEAQyNMBgAAAQEBAAAAAQD5CwABAPMKAAAAAARh" +
           "ggoEAAAAAAAKAAAAQWRkQ29tbWVudAEBTQYALwEARSNNBgAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoC" +
           "AAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAU4GAC4ARE4GAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElk" +
           "AA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4B" +
           "ACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8g" +
           "dGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRT" +
           "dGF0ZQEBTwYALwEAIyNPBgAAABX/////AQEBAAAAAQAsIwEBATsGAQAAABVgiQoCAAAAAAACAAAASWQB" +
           "AVAGAC4ARFAGAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBYQYALwEA" +
           "lyNhBgAAAQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAWIGAC4A" +
           "RGIGAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlm" +
           "aWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAAD" +
           "AAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAA" +
           "AQH/////AAAAABdgiQoCAAAAAQAKAAAAU0J5dGVWYWx1ZQEBZQYALwEAQAllBgAAAAIBAAAAAQAAAAAA" +
           "AAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQFpBgAuAERpBgAAAQB0A/////8BAf////8A" +
           "AAAAF2CJCgIAAAABAAkAAABCeXRlVmFsdWUBAWsGAC8BAEAJawYAAAADAQAAAAEAAAAAAAAAAQH/////" +
           "AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBbwYALgBEbwYAAAEAdAP/////AQH/////AAAAABdgiQoC" +
           "AAAAAQAKAAAASW50MTZWYWx1ZQEBcQYALwEAQAlxBgAAAAQBAAAAAQAAAAAAAAABAf////8BAAAAFWCJ" +
           "CgIAAAAAAAcAAABFVVJhbmdlAQF1BgAuAER1BgAAAQB0A/////8BAf////8AAAAAF2CJCgIAAAABAAsA" +
           "AABVSW50MTZWYWx1ZQEBdwYALwEAQAl3BgAAAAUBAAAAAQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAA" +
           "AAcAAABFVVJhbmdlAQF7BgAuAER7BgAAAQB0A/////8BAf////8AAAAAF2CJCgIAAAABAAoAAABJbnQz" +
           "MlZhbHVlAQF9BgAvAQBACX0GAAAABgEAAAABAAAAAAAAAAEB/////wEAAAAVYIkKAgAAAAAABwAAAEVV" +
           "UmFuZ2UBAYEGAC4ARIEGAAABAHQD/////wEB/////wAAAAAXYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVl" +
           "AQGDBgAvAQBACYMGAAAABwEAAAABAAAAAAAAAAEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UB" +
           "AYcGAC4ARIcGAAABAHQD/////wEB/////wAAAAAXYIkKAgAAAAEACgAAAEludDY0VmFsdWUBAYkGAC8B" +
           "AEAJiQYAAAAIAQAAAAEAAAAAAAAAAQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBjQYALgBE" +
           "jQYAAAEAdAP/////AQH/////AAAAABdgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBAY8GAC8BAEAJjwYA" +
           "AAAJAQAAAAEAAAAAAAAAAQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBkwYALgBEkwYAAAEA" +
           "dAP/////AQH/////AAAAABdgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBlQYALwEAQAmVBgAAAAoBAAAA" +
           "AQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGZBgAuAESZBgAAAQB0A/////8B" +
           "Af////8AAAAAF2CJCgIAAAABAAsAAABEb3VibGVWYWx1ZQEBmwYALwEAQAmbBgAAAAsBAAAAAQAAAAAA" +
           "AAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGfBgAuAESfBgAAAQB0A/////8BAf////8A" +
           "AAAAF2CJCgIAAAABAAsAAABOdW1iZXJWYWx1ZQEBoQYALwEAQAmhBgAAABoBAAAAAQAAAAAAAAABAf//" +
           "//8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGlBgAuAESlBgAAAQB0A/////8BAf////8AAAAAF2CJ" +
           "CgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBAacGAC8BAEAJpwYAAAAbAQAAAAEAAAAAAAAAAQH/////AQAA" +
           "ABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBqwYALgBEqwYAAAEAdAP/////AQH/////AAAAABdgiQoCAAAA" +
           "AQANAAAAVUludGVnZXJWYWx1ZQEBrQYALwEAQAmtBgAAABwBAAAAAQAAAAAAAAABAf////8BAAAAFWCJ" +
           "CgIAAAAAAAcAAABFVVJhbmdlAQGxBgAuAESxBgAAAQB0A/////8BAf////8AAAAA";
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
           "VmFsdWVPYmplY3RUeXBlSW5zdGFuY2UBAcoGAQHKBsoGAAABAAAAACQAAQHOBhkAAAA1YIkKAgAAAAEA" +
           "EAAAAFNpbXVsYXRpb25BY3RpdmUBAcsGAwAAAABHAAAASWYgdHJ1ZSB0aGUgc2VydmVyIHdpbGwgcHJv" +
           "ZHVjZSBuZXcgdmFsdWVzIGZvciBlYWNoIG1vbml0b3JlZCB2YXJpYWJsZS4ALgBEywYAAAAB/////wEB" +
           "/////wAAAAAEYYIKBAAAAAEADgAAAEdlbmVyYXRlVmFsdWVzAQHMBgAvAQH5A8wGAAABAf////8BAAAA" +
           "F2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBzQYALgBEzQYAAJYBAAAAAQAqAQFGAAAACgAAAEl0" +
           "ZXJhdGlvbnMAB/////8AAAAAAwAAAAAlAAAAVGhlIG51bWJlciBvZiBuZXcgdmFsdWVzIHRvIGdlbmVy" +
           "YXRlLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGCACgEAAAABAA0AAABDeWNsZUNvbXBsZXRlAQHO" +
           "BgAvAQBBC84GAAABAAAAACQBAQHKBhcAAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBAc8GAC4ARM8GAAAA" +
           "D/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAdAGAC4ARNAGAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQHRBgAuAETRBgAAABH/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAKAAAAU291cmNlTmFtZQEB0gYALgBE0gYAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BAAAAFRpbWUBAdMGAC4ARNMGAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVU" +
           "aW1lAQHUBgAuAETUBgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQHWBgAu" +
           "AETWBgAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAdcGAC4ARNcGAAAABf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQHYBgAuAETYBgAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQHZBgAuAETZBgAAABX/////" +
           "AQH/////AAAAABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEB3AYALgBE3AYAAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACAAAAEJyYW5jaElkAQHdBgAuAETdBgAAABH/////AQH/////AAAAABVgiQoC" +
           "AAAAAAAGAAAAUmV0YWluAQHeBgAuAETeBgAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAARW5h" +
           "YmxlZFN0YXRlAQHfBgAvAQAjI98GAAAAFf////8BAQIAAAABACwjAAEB8wYBACwjAAEB/AYBAAAAFWCJ" +
           "CgIAAAAAAAIAAABJZAEB4AYALgBE4AYAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxp" +
           "dHkBAegGAC8BACoj6AYAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFt" +
           "cAEB6QYALgBE6QYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQHq" +
           "BgAvAQAqI+oGAAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAesG" +
           "AC4AROsGAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAewGAC8BACoj7AYA" +
           "AAAV/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB7QYALgBE7QYAAAEA" +
           "JgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQHuBgAuAETuBgAAAAz/////" +
           "AQH/////AAAAAARhggoEAAAAAAAHAAAARGlzYWJsZQEB7wYALwEARCPvBgAAAQEBAAAAAQD5CwABAPMK" +
           "AAAAAARhggoEAAAAAAAGAAAARW5hYmxlAQHwBgAvAQBDI/AGAAABAQEAAAABAPkLAAEA8woAAAAABGGC" +
           "CgQAAAAAAAoAAABBZGRDb21tZW50AQHxBgAvAQBFI/EGAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIA" +
           "AAAAAA4AAABJbnB1dEFyZ3VtZW50cwEB8gYALgBE8gYAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQA" +
           "D/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEA" +
           "KgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0" +
           "aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAAAAAoAAABBY2tlZFN0" +
           "YXRlAQHzBgAvAQAjI/MGAAAAFf////8BAQEAAAABACwjAQEB3wYBAAAAFWCJCgIAAAAAAAIAAABJZAEB" +
           "9AYALgBE9AYAAAAB/////wEB/////wAAAAAEYYIKBAAAAAAACwAAAEFja25vd2xlZGdlAQEFBwAvAQCX" +
           "IwUHAAABAQEAAAABAPkLAAEA8CIBAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBBgcALgBE" +
           "BgcAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZp" +
           "ZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMA" +
           "AAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAAB" +
           "Af////8AAAAAFWCJCgIAAAABAAwAAABCb29sZWFuVmFsdWUBAQkHAC8APwkHAAABAbMG/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAQoHAC8APwoHAAABAbQG/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEBCwcALwA/CwcAAAEBtQb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAKAAAASW50MTZWYWx1ZQEBDAcALwA/DAcAAAEBtgb/////AQH/////AAAAABVgiQoCAAAAAQALAAAA" +
           "VUludDE2VmFsdWUBAQ0HAC8APw0HAAABAbcG/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMy" +
           "VmFsdWUBAQ4HAC8APw4HAAABAbgG/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVl" +
           "AQEPBwAvAD8PBwAAAQG5Bv////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2NFZhbHVlAQEQBwAv" +
           "AD8QBwAAAQG6Bv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBEQcALwA/EQcA" +
           "AAEBuwb/////AQH/////AAAAABVgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBEgcALwA/EgcAAAEBvAb/" +
           "////AQH/////AAAAABVgiQoCAAAAAQALAAAARG91YmxlVmFsdWUBARMHAC8APxMHAAABAb0G/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEACwAAAFN0cmluZ1ZhbHVlAQEUBwAvAD8UBwAAAQG+Bv////8BAf////8A" +
           "AAAAFWCJCgIAAAABAA0AAABEYXRlVGltZVZhbHVlAQEVBwAvAD8VBwAAAQG/Bv////8BAf////8AAAAA" +
           "FWCJCgIAAAABAAkAAABHdWlkVmFsdWUBARYHAC8APxYHAAABAcAG/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEADwAAAEJ5dGVTdHJpbmdWYWx1ZQEBFwcALwA/FwcAAAEBwQb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAPAAAAWG1sRWxlbWVudFZhbHVlAQEYBwAvAD8YBwAAAQHCBv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AAsAAABOb2RlSWRWYWx1ZQEBGQcALwA/GQcAAAEBwwb/////AQH/////AAAAABVgiQoCAAAAAQATAAAA" +
           "RXhwYW5kZWROb2RlSWRWYWx1ZQEBGgcALwA/GgcAAAEBxAb/////AQH/////AAAAABVgiQoCAAAAAQAS" +
           "AAAAUXVhbGlmaWVkTmFtZVZhbHVlAQEbBwAvAD8bBwAAAQHFBv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "ABIAAABMb2NhbGl6ZWRUZXh0VmFsdWUBARwHAC8APxwHAAABAcYG/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEADwAAAFN0YXR1c0NvZGVWYWx1ZQEBHQcALwA/HQcAAAEBxwb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAMAAAAVmFyaWFudFZhbHVlAQEeBwAvAD8eBwAAAQHIBv////8BAf////8AAAAA";
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
           "VmFsdWUxTWV0aG9kVHlwZQEBHwcALwEBHwcfBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBASAHAC4ARCAHAACWDAAAAAEAKgEBGgAAAAkAAABCb29sZWFuSW4BAbMG/////wAAAAAA" +
           "AQAqAQEYAAAABwAAAFNCeXRlSW4BAbQG/////wAAAAAAAQAqAQEXAAAABgAAAEJ5dGVJbgEBtQb/////" +
           "AAAAAAABACoBARgAAAAHAAAASW50MTZJbgEBtgb/////AAAAAAABACoBARkAAAAIAAAAVUludDE2SW4B" +
           "AbcG/////wAAAAAAAQAqAQEYAAAABwAAAEludDMySW4BAbgG/////wAAAAAAAQAqAQEZAAAACAAAAFVJ" +
           "bnQzMkluAQG5Bv////8AAAAAAAEAKgEBGAAAAAcAAABJbnQ2NEluAQG6Bv////8AAAAAAAEAKgEBGQAA" +
           "AAgAAABVSW50NjRJbgEBuwb/////AAAAAAABACoBARgAAAAHAAAARmxvYXRJbgEBvAb/////AAAAAAAB" +
           "ACoBARkAAAAIAAAARG91YmxlSW4BAb0G/////wAAAAAAAQAqAQEZAAAACAAAAFN0cmluZ0luAQG+Bv//" +
           "//8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVu" +
           "dHMBASEHAC4ARCEHAACWDAAAAAEAKgEBGwAAAAoAAABCb29sZWFuT3V0AQGzBv////8AAAAAAAEAKgEB" +
           "GQAAAAgAAABTQnl0ZU91dAEBtAb/////AAAAAAABACoBARgAAAAHAAAAQnl0ZU91dAEBtQb/////AAAA" +
           "AAABACoBARkAAAAIAAAASW50MTZPdXQBAbYG/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQxNk91dAEB" +
           "twb/////AAAAAAABACoBARkAAAAIAAAASW50MzJPdXQBAbgG/////wAAAAAAAQAqAQEaAAAACQAAAFVJ" +
           "bnQzMk91dAEBuQb/////AAAAAAABACoBARkAAAAIAAAASW50NjRPdXQBAboG/////wAAAAAAAQAqAQEa" +
           "AAAACQAAAFVJbnQ2NE91dAEBuwb/////AAAAAAABACoBARkAAAAIAAAARmxvYXRPdXQBAbwG/////wAA" +
           "AAAAAQAqAQEaAAAACQAAAERvdWJsZU91dAEBvQb/////AAAAAAABACoBARoAAAAJAAAAU3RyaW5nT3V0" +
           "AQG+Bv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
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
           "VmFsdWUyTWV0aG9kVHlwZQEBIgcALwEBIgciBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBASMHAC4ARCMHAACWCgAAAAEAKgEBGwAAAAoAAABEYXRlVGltZUluAQG/Bv////8AAAAA" +
           "AAEAKgEBFwAAAAYAAABHdWlkSW4BAcAG/////wAAAAAAAQAqAQEdAAAADAAAAEJ5dGVTdHJpbmdJbgEB" +
           "wQb/////AAAAAAABACoBAR0AAAAMAAAAWG1sRWxlbWVudEluAQHCBv////8AAAAAAAEAKgEBGQAAAAgA" +
           "AABOb2RlSWRJbgEBwwb/////AAAAAAABACoBASEAAAAQAAAARXhwYW5kZWROb2RlSWRJbgEBxAb/////" +
           "AAAAAAABACoBASAAAAAPAAAAUXVhbGlmaWVkTmFtZUluAQHFBv////8AAAAAAAEAKgEBIAAAAA8AAABM" +
           "b2NhbGl6ZWRUZXh0SW4BAcYG/////wAAAAAAAQAqAQEdAAAADAAAAFN0YXR1c0NvZGVJbgEBxwb/////" +
           "AAAAAAABACoBARoAAAAJAAAAVmFyaWFudEluAQHIBv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBASQHAC4ARCQHAACWCgAAAAEAKgEBHAAA" +
           "AAsAAABEYXRlVGltZU91dAEBvwb/////AAAAAAABACoBARgAAAAHAAAAR3VpZE91dAEBwAb/////AAAA" +
           "AAABACoBAR4AAAANAAAAQnl0ZVN0cmluZ091dAEBwQb/////AAAAAAABACoBAR4AAAANAAAAWG1sRWxl" +
           "bWVudE91dAEBwgb/////AAAAAAABACoBARoAAAAJAAAATm9kZUlkT3V0AQHDBv////8AAAAAAAEAKgEB" +
           "IgAAABEAAABFeHBhbmRlZE5vZGVJZE91dAEBxAb/////AAAAAAABACoBASEAAAAQAAAAUXVhbGlmaWVk" +
           "TmFtZU91dAEBxQb/////AAAAAAABACoBASEAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBxgb/////AAAA" +
           "AAABACoBAR4AAAANAAAAU3RhdHVzQ29kZU91dAEBxwb/////AAAAAAABACoBARsAAAAKAAAAVmFyaWFu" +
           "dE91dAEByAb/////AAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
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
           "YWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBJgcBASYHJgcAAAEAAAAAJAABASoHGQAAADVgiQoCAAAAAQAQ" +
           "AAAAU2ltdWxhdGlvbkFjdGl2ZQEBJwcDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9k" +
           "dWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAEQnBwAAAAH/////AQH/" +
           "////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBASgHAC8BAfkDKAcAAAEB/////wEAAAAX" +
           "YKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEpBwAuAEQpBwAAlgEAAAABACoBAUYAAAAKAAAASXRl" +
           "cmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJh" +
           "dGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBASoH" +
           "AC8BAEELKgcAAAEAAAAAJAEBASYHFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBKwcALgBEKwcAAAAP" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBLAcALgBELAcAAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAS0HAC4ARC0HAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAoAAABTb3VyY2VOYW1lAQEuBwAuAEQuBwAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAE" +
           "AAAAVGltZQEBLwcALgBELwcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRp" +
           "bWUBATAHAC4ARDAHAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBATIHAC4A" +
           "RDIHAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBMwcALgBEMwcAAAAF////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBATQHAC4ARDQHAAAAEf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBATUHAC4ARDUHAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQE4BwAuAEQ4BwAAAAz/////AQH/////" +
           "AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBATkHAC4ARDkHAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAYAAABSZXRhaW4BAToHAC4ARDoHAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFi" +
           "bGVkU3RhdGUBATsHAC8BACMjOwcAAAAV/////wEBAgAAAAEALCMAAQFPBwEALCMAAQFYBwEAAAAVYIkK" +
           "AgAAAAAAAgAAAElkAQE8BwAuAEQ8BwAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0" +
           "eQEBRAcALwEAKiNEBwAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1w" +
           "AQFFBwAuAERFBwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAUYH" +
           "AC8BACojRgcAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBRwcA" +
           "LgBERwcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBSAcALwEAKiNIBwAA" +
           "ABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQFJBwAuAERJBwAAAQAm" +
           "Af////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAUoHAC4AREoHAAAADP////8B" +
           "Af////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQFLBwAvAQBEI0sHAAABAQEAAAABAPkLAAEA8woA" +
           "AAAABGGCCgQAAAAAAAYAAABFbmFibGUBAUwHAC8BAEMjTAcAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIK" +
           "BAAAAAAACgAAAEFkZENvbW1lbnQBAU0HAC8BAEUjTQcAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQFOBwAuAEROBwAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP" +
           "/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAq" +
           "AQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRo" +
           "ZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3Rh" +
           "dGUBAU8HAC8BACMjTwcAAAAV/////wEBAQAAAAEALCMBAQE7BwEAAAAVYIkKAgAAAAAAAgAAAElkAQFQ" +
           "BwAuAERQBwAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAWEHAC8BAJcj" +
           "YQcAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFiBwAuAERi" +
           "BwAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmll" +
           "ciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAA" +
           "AAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEADAAAAEJvb2xlYW5WYWx1ZQEBZQcALwA/ZQcAAAEBswYBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABAAoAAABTQnl0ZVZhbHVlAQFmBwAvAD9mBwAAAQG0BgEAAAABAAAA" +
           "AAAAAAEB/////wAAAAAXYIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEBZwcALwA/ZwcAAAEBtQYBAAAAAQAA" +
           "AAAAAAABAf////8AAAAAF2CJCgIAAAABAAoAAABJbnQxNlZhbHVlAQFoBwAvAD9oBwAAAQG2BgEAAAAB" +
           "AAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAFVJbnQxNlZhbHVlAQFpBwAvAD9pBwAAAQG3BgEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACgAAAEludDMyVmFsdWUBAWoHAC8AP2oHAAABAbgG" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBAWsHAC8AP2sHAAAB" +
           "AbkGAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBbAcALwA/bAcA" +
           "AAEBugYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBbQcALwA/" +
           "bQcAAAEBuwYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAoAAABGbG9hdFZhbHVlAQFuBwAv" +
           "AD9uBwAAAQG8BgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAERvdWJsZVZhbHVlAQFv" +
           "BwAvAD9vBwAAAQG9BgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAFN0cmluZ1ZhbHVl" +
           "AQFwBwAvAD9wBwAAAQG+BgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADQAAAERhdGVUaW1l" +
           "VmFsdWUBAXEHAC8AP3EHAAABAb8GAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAJAAAAR3Vp" +
           "ZFZhbHVlAQFyBwAvAD9yBwAAAQHABgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAEJ5" +
           "dGVTdHJpbmdWYWx1ZQEBcwcALwA/cwcAAAEBwQYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAAB" +
           "AA8AAABYbWxFbGVtZW50VmFsdWUBAXQHAC8AP3QHAAABAcIGAQAAAAEAAAAAAAAAAQH/////AAAAABdg" +
           "iQoCAAAAAQALAAAATm9kZUlkVmFsdWUBAXUHAC8AP3UHAAABAcMGAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQATAAAARXhwYW5kZWROb2RlSWRWYWx1ZQEBdgcALwA/dgcAAAEBxAYBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABABIAAABRdWFsaWZpZWROYW1lVmFsdWUBAXcHAC8AP3cHAAABAcUG" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQASAAAATG9jYWxpemVkVGV4dFZhbHVlAQF4BwAv" +
           "AD94BwAAAQHGBgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAFN0YXR1c0NvZGVWYWx1" +
           "ZQEBeQcALwA/eQcAAAEBxwYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAwAAABWYXJpYW50" +
           "VmFsdWUBAXoHAC8AP3oHAAABAcgGAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
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

    #region VectorVariableState Class
    #if (!OPCUA_EXCLUDE_VectorVariableState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class VectorVariableState : BaseDataVariableState<TestData.Vector>
    {
        #region Constructors
        /// <remarks />
        public VectorVariableState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.VariableTypes.VectorVariableType, TestData.Namespaces.TestData, namespaceUris);
        }

        /// <remarks />
        protected override NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.DataTypes.Vector, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////FWCBAgIAAAABABoAAABWZWN0b3JWYXJp" +
           "YWJsZVR5cGVJbnN0YW5jZQEBfAcBAXwHfAcAAAEBewcBAf////8DAAAAFWCJCgIAAAABAAEAAABYAQF9" +
           "BwAuAER9BwAAAAv/////AQH/////AAAAABVgiQoCAAAAAQABAAAAWQEBfgcALgBEfgcAAAAL/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEAAQAAAFoBAX8HAC4ARH8HAAAAC/////8BAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<double> X
        {
            get
            {
                return m_x;
            }

            set
            {
                if (!Object.ReferenceEquals(m_x, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_x = value;
            }
        }

        /// <remarks />
        public PropertyState<double> Y
        {
            get
            {
                return m_y;
            }

            set
            {
                if (!Object.ReferenceEquals(m_y, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_y = value;
            }
        }

        /// <remarks />
        public PropertyState<double> Z
        {
            get
            {
                return m_z;
            }

            set
            {
                if (!Object.ReferenceEquals(m_z, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_z = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_x != null)
            {
                children.Add(m_x);
            }

            if (m_y != null)
            {
                children.Add(m_y);
            }

            if (m_z != null)
            {
                children.Add(m_z);
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
                case TestData.BrowseNames.X:
                {
                    if (createOrReplace)
                    {
                        if (X == null)
                        {
                            if (replacement == null)
                            {
                                X = new PropertyState<double>(this);
                            }
                            else
                            {
                                X = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = X;
                    break;
                }

                case TestData.BrowseNames.Y:
                {
                    if (createOrReplace)
                    {
                        if (Y == null)
                        {
                            if (replacement == null)
                            {
                                Y = new PropertyState<double>(this);
                            }
                            else
                            {
                                Y = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = Y;
                    break;
                }

                case TestData.BrowseNames.Z:
                {
                    if (createOrReplace)
                    {
                        if (Z == null)
                        {
                            if (replacement == null)
                            {
                                Z = new PropertyState<double>(this);
                            }
                            else
                            {
                                Z = (PropertyState<double>)replacement;
                            }
                        }
                    }

                    instance = Z;
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
        private PropertyState<double> m_x;
        private PropertyState<double> m_y;
        private PropertyState<double> m_z;
        #endregion
    }

    #region VectorVariableValue Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public class VectorVariableValue : BaseVariableValue
    {
        #region Constructors
        /// <remarks />
        public VectorVariableValue(VectorVariableState variable, Vector value, object dataLock) : base(dataLock)
        {
            m_value = value;

            if (m_value == null)
            {
                m_value = new Vector();
            }

            Initialize(variable);
        }
        #endregion

        #region Public Members
        /// <remarks />
        public VectorVariableState Variable
        {
            get { return m_variable; }
        }

        /// <remarks />
        public Vector Value
        {
            get { return m_value; }
            set { m_value = value; }
        }
        #endregion

        #region Private Methods
        private void Initialize(VectorVariableState variable)
        {
            lock (Lock)
            {
                m_variable = variable;

                variable.Value = m_value;

                variable.OnReadValue = OnReadValue;
                variable.OnWriteValue = OnWriteValue;

                BaseVariableState instance = null;
                List<BaseInstanceState> updateList = new List<BaseInstanceState>();
                updateList.Add(variable);

                instance = m_variable.X;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_X;
                    instance.OnWriteValue = OnWrite_X;
                    updateList.Add(instance);
                }
                instance = m_variable.Y;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_Y;
                    instance.OnWriteValue = OnWrite_Y;
                    updateList.Add(instance);
                }
                instance = m_variable.Z;
                if (instance != null)
                {
                    instance.OnReadValue = OnRead_Z;
                    instance.OnWriteValue = OnWrite_Z;
                    updateList.Add(instance);
                }

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

        private ServiceResult OnWriteValue(
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
                Vector newValue;
                if (value is ExtensionObject extensionObject)
                {
                    newValue = (Vector)extensionObject.Body;
                }
                else
                {
                    newValue = (Vector)value;
                }

                if (!Utils.IsEqual(m_value, newValue))
                {
                    UpdateChildrenChangeMasks(context, ref newValue, ref statusCode, ref timestamp);
                    Timestamp = timestamp;
                    m_value = (Vector)Write(newValue);
                    m_variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
                }
            }

            return ServiceResult.Good;
        }

        private void UpdateChildrenChangeMasks(ISystemContext context, ref Vector newValue, ref StatusCode statusCode, ref DateTime timestamp)
        {
            if (!Utils.IsEqual(m_value.X, newValue.X)) UpdateChildVariableStatus(m_variable.X, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.Y, newValue.Y)) UpdateChildVariableStatus(m_variable.Y, ref statusCode, ref timestamp);
            if (!Utils.IsEqual(m_value.Z, newValue.Z)) UpdateChildVariableStatus(m_variable.Z, ref statusCode, ref timestamp);
        }

        private void UpdateParent(ISystemContext context, ref StatusCode statusCode, ref DateTime timestamp)
        {
            Timestamp = timestamp;
            m_variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
            m_variable.ClearChangeMasks(context, false);
        }

        private void UpdateChildVariableStatus(BaseVariableState child, ref StatusCode statusCode, ref DateTime timestamp)
        {
            if (child == null) return;
            child.StatusCode = statusCode;
            if (timestamp == DateTime.MinValue)
            {
                timestamp = DateTime.UtcNow;
            }
            child.Timestamp = timestamp;
        }

        #region X Access Methods
        /// <remarks />
        private ServiceResult OnRead_X(
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

                var childVariable = m_variable?.X;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.X;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_X(
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
                UpdateChildVariableStatus(m_variable.X, ref statusCode, ref timestamp);
                m_value.X = (double)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Y Access Methods
        /// <remarks />
        private ServiceResult OnRead_Y(
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

                var childVariable = m_variable?.Y;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.Y;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Y(
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
                UpdateChildVariableStatus(m_variable.Y, ref statusCode, ref timestamp);
                m_value.Y = (double)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Z Access Methods
        /// <remarks />
        private ServiceResult OnRead_Z(
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

                var childVariable = m_variable?.Z;
                if (childVariable != null && StatusCode.IsBad(childVariable.StatusCode))
                {
                    value = null;
                    statusCode = childVariable.StatusCode;
                    return new ServiceResult(statusCode);
                }

                if (m_value != null)
                {
                    value = m_value.Z;
                }

                var result = Read(context, node, indexRange, dataEncoding, ref value, ref statusCode, ref timestamp);

                if (childVariable != null && ServiceResult.IsNotBad(result))
                {
                    timestamp = childVariable.Timestamp;
                    if (statusCode != childVariable.StatusCode)
                    {
                        statusCode = childVariable.StatusCode;
                        result = new ServiceResult(statusCode);
                    }
                }

                return result;
            }
        }

        /// <remarks />
        private ServiceResult OnWrite_Z(
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
                UpdateChildVariableStatus(m_variable.Z, ref statusCode, ref timestamp);
                m_value.Z = (double)Write(value);
                UpdateParent(context, ref statusCode, ref timestamp);
            }

            return ServiceResult.Good;
        }
        #endregion
        #endregion

        #region Private Fields
        private Vector m_value;
        private VectorVariableState m_variable;
        #endregion
    }
    #endregion
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
           "YWx1ZTFNZXRob2RUeXBlAQGCBwAvAQGCB4IHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBgwcALgBEgwcAAJYMAAAAAQAqAQEeAAAACQAAAEJvb2xlYW5JbgEBswYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEcAAAABwAAAFNCeXRlSW4BAbQGAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAYAAABCeXRlSW4B" +
           "AbUGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQxNkluAQG2BgEAAAABAAAAAAAAAAABACoBAR0A" +
           "AAAIAAAAVUludDE2SW4BAbcGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQzMkluAQG4BgEAAAAB" +
           "AAAAAAAAAAABACoBAR0AAAAIAAAAVUludDMySW4BAbkGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJ" +
           "bnQ2NEluAQG6BgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAAVUludDY0SW4BAbsGAQAAAAEAAAAAAAAA" +
           "AAEAKgEBHAAAAAcAAABGbG9hdEluAQG8BgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAARG91YmxlSW4B" +
           "Ab0GAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABTdHJpbmdJbgEBvgYBAAAAAQAAAAAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBhAcALgBEhAcA" +
           "AJYMAAAAAQAqAQEfAAAACgAAAEJvb2xlYW5PdXQBAbMGAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABT" +
           "Qnl0ZU91dAEBtAYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEJ5dGVPdXQBAbUGAQAAAAEAAAAAAAAA" +
           "AAEAKgEBHQAAAAgAAABJbnQxNk91dAEBtgYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAFVJbnQxNk91" +
           "dAEBtwYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAEludDMyT3V0AQG4BgEAAAABAAAAAAAAAAABACoB" +
           "AR4AAAAJAAAAVUludDMyT3V0AQG5BgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAASW50NjRPdXQBAboG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABVSW50NjRPdXQBAbsGAQAAAAEAAAAAAAAAAAEAKgEBHQAA" +
           "AAgAAABGbG9hdE91dAEBvAYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAERvdWJsZU91dAEBvQYBAAAA" +
           "AQAAAAAAAAAAAQAqAQEeAAAACQAAAFN0cmluZ091dAEBvgYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAA" +
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
           "YWx1ZTJNZXRob2RUeXBlAQGFBwAvAQGFB4UHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBhgcALgBEhgcAAJYKAAAAAQAqAQEfAAAACgAAAERhdGVUaW1lSW4BAb8GAQAAAAEAAAAA" +
           "AAAAAAEAKgEBGwAAAAYAAABHdWlkSW4BAcAGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABCeXRlU3Ry" +
           "aW5nSW4BAcEGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABYbWxFbGVtZW50SW4BAcIGAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHQAAAAgAAABOb2RlSWRJbgEBwwYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAEV4cGFu" +
           "ZGVkTm9kZUlkSW4BAcQGAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABRdWFsaWZpZWROYW1lSW4BAcUG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAcYGAQAAAAEAAAAAAAAAAAEA" +
           "KgEBIQAAAAwAAABTdGF0dXNDb2RlSW4BAccGAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABWYXJpYW50" +
           "SW4BAcgGAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABP" +
           "dXRwdXRBcmd1bWVudHMBAYcHAC4ARIcHAACWCgAAAAEAKgEBIAAAAAsAAABEYXRlVGltZU91dAEBvwYB" +
           "AAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEd1aWRPdXQBAcAGAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0A" +
           "AABCeXRlU3RyaW5nT3V0AQHBBgEAAAABAAAAAAAAAAABACoBASIAAAANAAAAWG1sRWxlbWVudE91dAEB" +
           "wgYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAE5vZGVJZE91dAEBwwYBAAAAAQAAAAAAAAAAAQAqAQEm" +
           "AAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0AQHEBgEAAAABAAAAAAAAAAABACoBASUAAAAQAAAAUXVhbGlm" +
           "aWVkTmFtZU91dAEBxQYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAExvY2FsaXplZFRleHRPdXQBAcYG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0AAABTdGF0dXNDb2RlT3V0AQHHBgEAAAABAAAAAAAAAAABACoB" +
           "AR8AAAAKAAAAVmFyaWFudE91dAEByAYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAA" +
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
           "VHlwZUluc3RhbmNlAQGIBwEBiAeIBwAA/////woAAAAEYYIKBAAAAAEADQAAAFNjYWxhck1ldGhvZDEB" +
           "AYkHAC8BAYkHiQcAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQGKBwAuAESK" +
           "BwAAlgsAAAABACoBARgAAAAJAAAAQm9vbGVhbkluAAH/////AAAAAAABACoBARYAAAAHAAAAU0J5dGVJ" +
           "bgAC/////wAAAAAAAQAqAQEVAAAABgAAAEJ5dGVJbgAD/////wAAAAAAAQAqAQEWAAAABwAAAEludDE2" +
           "SW4ABP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MTZJbgAF/////wAAAAAAAQAqAQEWAAAABwAAAElu" +
           "dDMySW4ABv////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MzJJbgAH/////wAAAAAAAQAqAQEWAAAABwAA" +
           "AEludDY0SW4ACP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50NjRJbgAJ/////wAAAAAAAQAqAQEWAAAA" +
           "BwAAAEZsb2F0SW4ACv////8AAAAAAAEAKgEBFwAAAAgAAABEb3VibGVJbgAL/////wAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBiwcALgBEiwcA" +
           "AJYLAAAAAQAqAQEZAAAACgAAAEJvb2xlYW5PdXQAAf////8AAAAAAAEAKgEBFwAAAAgAAABTQnl0ZU91" +
           "dAAC/////wAAAAAAAQAqAQEWAAAABwAAAEJ5dGVPdXQAA/////8AAAAAAAEAKgEBFwAAAAgAAABJbnQx" +
           "Nk91dAAE/////wAAAAAAAQAqAQEYAAAACQAAAFVJbnQxNk91dAAF/////wAAAAAAAQAqAQEXAAAACAAA" +
           "AEludDMyT3V0AAb/////AAAAAAABACoBARgAAAAJAAAAVUludDMyT3V0AAf/////AAAAAAABACoBARcA" +
           "AAAIAAAASW50NjRPdXQACP////8AAAAAAAEAKgEBGAAAAAkAAABVSW50NjRPdXQACf////8AAAAAAAEA" +
           "KgEBFwAAAAgAAABGbG9hdE91dAAK/////wAAAAAAAQAqAQEYAAAACQAAAERvdWJsZU91dAAL/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADQAAAFNjYWxhck1ldGhvZDIBAYwH" +
           "AC8BAYwHjAcAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQGNBwAuAESNBwAA" +
           "lgoAAAABACoBARcAAAAIAAAAU3RyaW5nSW4ADP////8AAAAAAAEAKgEBGQAAAAoAAABEYXRlVGltZUlu" +
           "AA3/////AAAAAAABACoBARUAAAAGAAAAR3VpZEluAA7/////AAAAAAABACoBARsAAAAMAAAAQnl0ZVN0" +
           "cmluZ0luAA//////AAAAAAABACoBARsAAAAMAAAAWG1sRWxlbWVudEluABD/////AAAAAAABACoBARcA" +
           "AAAIAAAATm9kZUlkSW4AEf////8AAAAAAAEAKgEBHwAAABAAAABFeHBhbmRlZE5vZGVJZEluABL/////" +
           "AAAAAAABACoBAR4AAAAPAAAAUXVhbGlmaWVkTmFtZUluABT/////AAAAAAABACoBAR4AAAAPAAAATG9j" +
           "YWxpemVkVGV4dEluABX/////AAAAAAABACoBARsAAAAMAAAAU3RhdHVzQ29kZUluABP/////AAAAAAAB" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQGOBwAu" +
           "AESOBwAAlgoAAAABACoBARgAAAAJAAAAU3RyaW5nT3V0AAz/////AAAAAAABACoBARoAAAALAAAARGF0" +
           "ZVRpbWVPdXQADf////8AAAAAAAEAKgEBFgAAAAcAAABHdWlkT3V0AA7/////AAAAAAABACoBARwAAAAN" +
           "AAAAQnl0ZVN0cmluZ091dAAP/////wAAAAAAAQAqAQEcAAAADQAAAFhtbEVsZW1lbnRPdXQAEP////8A" +
           "AAAAAAEAKgEBGAAAAAkAAABOb2RlSWRPdXQAEf////8AAAAAAAEAKgEBIAAAABEAAABFeHBhbmRlZE5v" +
           "ZGVJZE91dAAS/////wAAAAAAAQAqAQEfAAAAEAAAAFF1YWxpZmllZE5hbWVPdXQAFP////8AAAAAAAEA" +
           "KgEBHwAAABAAAABMb2NhbGl6ZWRUZXh0T3V0ABX/////AAAAAAABACoBARwAAAANAAAAU3RhdHVzQ29k" +
           "ZU91dAAT/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADQAAAFNjYWxh" +
           "ck1ldGhvZDMBAY8HAC8BAY8HjwcAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRz" +
           "AQGQBwAuAESQBwAAlgMAAAABACoBARgAAAAJAAAAVmFyaWFudEluABj/////AAAAAAABACoBARwAAAAN" +
           "AAAARW51bWVyYXRpb25JbgAd/////wAAAAAAAQAqAQEaAAAACwAAAFN0cnVjdHVyZUluABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQGR" +
           "BwAuAESRBwAAlgMAAAABACoBARkAAAAKAAAAVmFyaWFudE91dAAY/////wAAAAAAAQAqAQEdAAAADgAA" +
           "AEVudW1lcmF0aW9uT3V0AB3/////AAAAAAABACoBARsAAAAMAAAAU3RydWN0dXJlT3V0ABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAAAQAMAAAAQXJyYXlNZXRob2QxAQGSBwAv" +
           "AQGSB5IHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBkwcALgBEkwcAAJYL" +
           "AAAAAQAqAQEcAAAACQAAAEJvb2xlYW5JbgABAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABTQnl0ZUlu" +
           "AAIBAAAAAQAAAAAAAAAAAQAqAQEZAAAABgAAAEJ5dGVJbgADAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcA" +
           "AABJbnQxNkluAAQBAAAAAQAAAAAAAAAAAQAqAQEbAAAACAAAAFVJbnQxNkluAAUBAAAAAQAAAAAAAAAA" +
           "AQAqAQEaAAAABwAAAEludDMySW4ABgEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAVUludDMySW4ABwEA" +
           "AAABAAAAAAAAAAABACoBARoAAAAHAAAASW50NjRJbgAIAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABV" +
           "SW50NjRJbgAJAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABGbG9hdEluAAoBAAAAAQAAAAAAAAAAAQAq" +
           "AQEbAAAACAAAAERvdWJsZUluAAsBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAX" +
           "YKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBlAcALgBElAcAAJYLAAAAAQAqAQEdAAAACgAAAEJv" +
           "b2xlYW5PdXQAAQEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAU0J5dGVPdXQAAgEAAAABAAAAAAAAAAAB" +
           "ACoBARoAAAAHAAAAQnl0ZU91dAADAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABJbnQxNk91dAAEAQAA" +
           "AAEAAAAAAAAAAAEAKgEBHAAAAAkAAABVSW50MTZPdXQABQEAAAABAAAAAAAAAAABACoBARsAAAAIAAAA" +
           "SW50MzJPdXQABgEAAAABAAAAAAAAAAABACoBARwAAAAJAAAAVUludDMyT3V0AAcBAAAAAQAAAAAAAAAA" +
           "AQAqAQEbAAAACAAAAEludDY0T3V0AAgBAAAAAQAAAAAAAAAAAQAqAQEcAAAACQAAAFVJbnQ2NE91dAAJ" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABGbG9hdE91dAAKAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAkA" +
           "AABEb3VibGVPdXQACwEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAA" +
           "AQAMAAAAQXJyYXlNZXRob2QyAQGVBwAvAQGVB5UHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1" +
           "dEFyZ3VtZW50cwEBlgcALgBElgcAAJYKAAAAAQAqAQEbAAAACAAAAFN0cmluZ0luAAwBAAAAAQAAAAAA" +
           "AAAAAQAqAQEdAAAACgAAAERhdGVUaW1lSW4ADQEAAAABAAAAAAAAAAABACoBARkAAAAGAAAAR3VpZElu" +
           "AA4BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAEJ5dGVTdHJpbmdJbgAPAQAAAAEAAAAAAAAAAAEAKgEB" +
           "HwAAAAwAAABYbWxFbGVtZW50SW4AEAEAAAABAAAAAAAAAAABACoBARsAAAAIAAAATm9kZUlkSW4AEQEA" +
           "AAABAAAAAAAAAAABACoBASMAAAAQAAAARXhwYW5kZWROb2RlSWRJbgASAQAAAAEAAAAAAAAAAAEAKgEB" +
           "IgAAAA8AAABRdWFsaWZpZWROYW1lSW4AFAEAAAABAAAAAAAAAAABACoBASIAAAAPAAAATG9jYWxpemVk" +
           "VGV4dEluABUBAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAFN0YXR1c0NvZGVJbgATAQAAAAEAAAAAAAAA" +
           "AAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAZcH" +
           "AC4ARJcHAACWCgAAAAEAKgEBHAAAAAkAAABTdHJpbmdPdXQADAEAAAABAAAAAAAAAAABACoBAR4AAAAL" +
           "AAAARGF0ZVRpbWVPdXQADQEAAAABAAAAAAAAAAABACoBARoAAAAHAAAAR3VpZE91dAAOAQAAAAEAAAAA" +
           "AAAAAAEAKgEBIAAAAA0AAABCeXRlU3RyaW5nT3V0AA8BAAAAAQAAAAAAAAAAAQAqAQEgAAAADQAAAFht" +
           "bEVsZW1lbnRPdXQAEAEAAAABAAAAAAAAAAABACoBARwAAAAJAAAATm9kZUlkT3V0ABEBAAAAAQAAAAAA" +
           "AAAAAQAqAQEkAAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0ABIBAAAAAQAAAAAAAAAAAQAqAQEjAAAAEAAA" +
           "AFF1YWxpZmllZE5hbWVPdXQAFAEAAAABAAAAAAAAAAABACoBASMAAAAQAAAATG9jYWxpemVkVGV4dE91" +
           "dAAVAQAAAAEAAAAAAAAAAAEAKgEBIAAAAA0AAABTdGF0dXNDb2RlT3V0ABMBAAAAAQAAAAAAAAAAAQAo" +
           "AQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADAAAAEFycmF5TWV0aG9kMwEBmAcALwEBmAeY" +
           "BwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAZkHAC4ARJkHAACWAwAAAAEA" +
           "KgEBHAAAAAkAAABWYXJpYW50SW4AGAEAAAABAAAAAAAAAAABACoBASAAAAANAAAARW51bWVyYXRpb25J" +
           "bgAdAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAsAAABTdHJ1Y3R1cmVJbgAWAQAAAAEAAAAAAAAAAAEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAZoHAC4ARJoH" +
           "AACWAwAAAAEAKgEBHQAAAAoAAABWYXJpYW50T3V0ABgBAAAAAQAAAAAAAAAAAQAqAQEhAAAADgAAAEVu" +
           "dW1lcmF0aW9uT3V0AB0BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAFN0cnVjdHVyZU91dAAWAQAAAAEA" +
           "AAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQAAAABABEAAABVc2VyU2NhbGFyTWV0" +
           "aG9kMQEBmwcALwEBmwebBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAZwH" +
           "AC4ARJwHAACWDAAAAAEAKgEBGgAAAAkAAABCb29sZWFuSW4BAbMG/////wAAAAAAAQAqAQEYAAAABwAA" +
           "AFNCeXRlSW4BAbQG/////wAAAAAAAQAqAQEXAAAABgAAAEJ5dGVJbgEBtQb/////AAAAAAABACoBARgA" +
           "AAAHAAAASW50MTZJbgEBtgb/////AAAAAAABACoBARkAAAAIAAAAVUludDE2SW4BAbcG/////wAAAAAA" +
           "AQAqAQEYAAAABwAAAEludDMySW4BAbgG/////wAAAAAAAQAqAQEZAAAACAAAAFVJbnQzMkluAQG5Bv//" +
           "//8AAAAAAAEAKgEBGAAAAAcAAABJbnQ2NEluAQG6Bv////8AAAAAAAEAKgEBGQAAAAgAAABVSW50NjRJ" +
           "bgEBuwb/////AAAAAAABACoBARgAAAAHAAAARmxvYXRJbgEBvAb/////AAAAAAABACoBARkAAAAIAAAA" +
           "RG91YmxlSW4BAb0G/////wAAAAAAAQAqAQEZAAAACAAAAFN0cmluZ0luAQG+Bv////8AAAAAAAEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAZ0HAC4ARJ0H" +
           "AACWDAAAAAEAKgEBGwAAAAoAAABCb29sZWFuT3V0AQGzBv////8AAAAAAAEAKgEBGQAAAAgAAABTQnl0" +
           "ZU91dAEBtAb/////AAAAAAABACoBARgAAAAHAAAAQnl0ZU91dAEBtQb/////AAAAAAABACoBARkAAAAI" +
           "AAAASW50MTZPdXQBAbYG/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQxNk91dAEBtwb/////AAAAAAAB" +
           "ACoBARkAAAAIAAAASW50MzJPdXQBAbgG/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQzMk91dAEBuQb/" +
           "////AAAAAAABACoBARkAAAAIAAAASW50NjRPdXQBAboG/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQ2" +
           "NE91dAEBuwb/////AAAAAAABACoBARkAAAAIAAAARmxvYXRPdXQBAbwG/////wAAAAAAAQAqAQEaAAAA" +
           "CQAAAERvdWJsZU91dAEBvQb/////AAAAAAABACoBARoAAAAJAAAAU3RyaW5nT3V0AQG+Bv////8AAAAA" +
           "AAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQAAAABABEAAABVc2VyU2NhbGFyTWV0aG9kMgEB" +
           "ngcALwEBngeeBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAZ8HAC4ARJ8H" +
           "AACWCgAAAAEAKgEBGwAAAAoAAABEYXRlVGltZUluAQG/Bv////8AAAAAAAEAKgEBFwAAAAYAAABHdWlk" +
           "SW4BAcAG/////wAAAAAAAQAqAQEdAAAADAAAAEJ5dGVTdHJpbmdJbgEBwQb/////AAAAAAABACoBAR0A" +
           "AAAMAAAAWG1sRWxlbWVudEluAQHCBv////8AAAAAAAEAKgEBGQAAAAgAAABOb2RlSWRJbgEBwwb/////" +
           "AAAAAAABACoBASEAAAAQAAAARXhwYW5kZWROb2RlSWRJbgEBxAb/////AAAAAAABACoBASAAAAAPAAAA" +
           "UXVhbGlmaWVkTmFtZUluAQHFBv////8AAAAAAAEAKgEBIAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAcYG" +
           "/////wAAAAAAAQAqAQEdAAAADAAAAFN0YXR1c0NvZGVJbgEBxwb/////AAAAAAABACoBARoAAAAJAAAA" +
           "VmFyaWFudEluAQHIBv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8A" +
           "AABPdXRwdXRBcmd1bWVudHMBAaAHAC4ARKAHAACWCgAAAAEAKgEBHAAAAAsAAABEYXRlVGltZU91dAEB" +
           "vwb/////AAAAAAABACoBARgAAAAHAAAAR3VpZE91dAEBwAb/////AAAAAAABACoBAR4AAAANAAAAQnl0" +
           "ZVN0cmluZ091dAEBwQb/////AAAAAAABACoBAR4AAAANAAAAWG1sRWxlbWVudE91dAEBwgb/////AAAA" +
           "AAABACoBARoAAAAJAAAATm9kZUlkT3V0AQHDBv////8AAAAAAAEAKgEBIgAAABEAAABFeHBhbmRlZE5v" +
           "ZGVJZE91dAEBxAb/////AAAAAAABACoBASEAAAAQAAAAUXVhbGlmaWVkTmFtZU91dAEBxQb/////AAAA" +
           "AAABACoBASEAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBxgb/////AAAAAAABACoBAR4AAAANAAAAU3Rh" +
           "dHVzQ29kZU91dAEBxwb/////AAAAAAABACoBARsAAAAKAAAAVmFyaWFudE91dAEByAb/////AAAAAAAB" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAAAQAQAAAAVXNlckFycmF5TWV0aG9kMQEBoQcA" +
           "LwEBoQehBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAaIHAC4ARKIHAACW" +
           "DAAAAAEAKgEBHgAAAAkAAABCb29sZWFuSW4BAbMGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABTQnl0" +
           "ZUluAQG0BgEAAAABAAAAAAAAAAABACoBARsAAAAGAAAAQnl0ZUluAQG1BgEAAAABAAAAAAAAAAABACoB" +
           "ARwAAAAHAAAASW50MTZJbgEBtgYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAFVJbnQxNkluAQG3BgEA" +
           "AAABAAAAAAAAAAABACoBARwAAAAHAAAASW50MzJJbgEBuAYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAA" +
           "AFVJbnQzMkluAQG5BgEAAAABAAAAAAAAAAABACoBARwAAAAHAAAASW50NjRJbgEBugYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEdAAAACAAAAFVJbnQ2NEluAQG7BgEAAAABAAAAAAAAAAABACoBARwAAAAHAAAARmxvYXRJ" +
           "bgEBvAYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAERvdWJsZUluAQG9BgEAAAABAAAAAAAAAAABACoB" +
           "AR0AAAAIAAAAU3RyaW5nSW4BAb4GAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA" +
           "F2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAaMHAC4ARKMHAACWDAAAAAEAKgEBHwAAAAoAAABC" +
           "b29sZWFuT3V0AQGzBgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAAU0J5dGVPdXQBAbQGAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHAAAAAcAAABCeXRlT3V0AQG1BgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAASW50MTZP" +
           "dXQBAbYGAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABVSW50MTZPdXQBAbcGAQAAAAEAAAAAAAAAAAEA" +
           "KgEBHQAAAAgAAABJbnQzMk91dAEBuAYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAFVJbnQzMk91dAEB" +
           "uQYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAEludDY0T3V0AQG6BgEAAAABAAAAAAAAAAABACoBAR4A" +
           "AAAJAAAAVUludDY0T3V0AQG7BgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAARmxvYXRPdXQBAbwGAQAA" +
           "AAEAAAAAAAAAAAEAKgEBHgAAAAkAAABEb3VibGVPdXQBAb0GAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkA" +
           "AABTdHJpbmdPdXQBAb4GAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQA" +
           "AAABABAAAABVc2VyQXJyYXlNZXRob2QyAQGkBwAvAQGkB6QHAAABAf////8CAAAAF2CpCgIAAAAAAA4A" +
           "AABJbnB1dEFyZ3VtZW50cwEBpQcALgBEpQcAAJYKAAAAAQAqAQEfAAAACgAAAERhdGVUaW1lSW4BAb8G" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGwAAAAYAAABHdWlkSW4BAcAGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwA" +
           "AABCeXRlU3RyaW5nSW4BAcEGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABYbWxFbGVtZW50SW4BAcIG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABOb2RlSWRJbgEBwwYBAAAAAQAAAAAAAAAAAQAqAQElAAAA" +
           "EAAAAEV4cGFuZGVkTm9kZUlkSW4BAcQGAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABRdWFsaWZpZWRO" +
           "YW1lSW4BAcUGAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAcYGAQAAAAEA" +
           "AAAAAAAAAAEAKgEBIQAAAAwAAABTdGF0dXNDb2RlSW4BAccGAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkA" +
           "AABWYXJpYW50SW4BAcgGAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIA" +
           "AAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAaYHAC4ARKYHAACWCgAAAAEAKgEBIAAAAAsAAABEYXRlVGlt" +
           "ZU91dAEBvwYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEd1aWRPdXQBAcAGAQAAAAEAAAAAAAAAAAEA" +
           "KgEBIgAAAA0AAABCeXRlU3RyaW5nT3V0AQHBBgEAAAABAAAAAAAAAAABACoBASIAAAANAAAAWG1sRWxl" +
           "bWVudE91dAEBwgYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAE5vZGVJZE91dAEBwwYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEmAAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0AQHEBgEAAAABAAAAAAAAAAABACoBASUAAAAQ" +
           "AAAAUXVhbGlmaWVkTmFtZU91dAEBxQYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAExvY2FsaXplZFRl" +
           "eHRPdXQBAcYGAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0AAABTdGF0dXNDb2RlT3V0AQHHBgEAAAABAAAA" +
           "AAAAAAABACoBAR8AAAAKAAAAVmFyaWFudE91dAEByAYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAA" +
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
           "Q29uZGl0aW9uVHlwZUluc3RhbmNlAQGnBwEBpwenBwAA/////xYAAAAVYIkKAgAAAAAABwAAAEV2ZW50" +
           "SWQBAagHAC4ARKgHAAAAD/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAakHAC4A" +
           "RKkHAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQGqBwAuAESqBwAAABH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTmFtZQEBqwcALgBEqwcAAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAawHAC4ARKwHAAABACYB/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACwAAAFJlY2VpdmVUaW1lAQGtBwAuAEStBwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcA" +
           "AABNZXNzYWdlAQGvBwAuAESvBwAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkB" +
           "AbAHAC4ARLAHAAAABf////8BAf////8AAAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQGx" +
           "BwAuAESxBwAAABH/////AQH/////AAAAABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQGy" +
           "BwAuAESyBwAAABX/////AQH/////AAAAABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBtQcALgBE" +
           "tQcAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAEJyYW5jaElkAQG2BwAuAES2BwAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAAGAAAAUmV0YWluAQG3BwAuAES3BwAAAAH/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAMAAAARW5hYmxlZFN0YXRlAQG4BwAvAQAjI7gHAAAAFf////8BAf////8BAAAAFWCJCgIA" +
           "AAAAAAIAAABJZAEBuQcALgBEuQcAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxpdHkB" +
           "AcEHAC8BACojwQcAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB" +
           "wgcALgBEwgcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQHDBwAv" +
           "AQAqI8MHAAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAcQHAC4A" +
           "RMQHAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAcUHAC8BACojxQcAAAAV" +
           "/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBxgcALgBExgcAAAEAJgH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQHHBwAuAETHBwAAAAz/////AQH/" +
           "////AAAAAARhggoEAAAAAAAHAAAARGlzYWJsZQEByAcALwEARCPIBwAAAQEBAAAAAQD5CwABAPMKAAAA" +
           "AARhggoEAAAAAAAGAAAARW5hYmxlAQHJBwAvAQBDI8kHAAABAQEAAAABAPkLAAEA8woAAAAABGGCCgQA" +
           "AAAAAAoAAABBZGRDb21tZW50AQHKBwAvAQBFI8oHAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBywcALgBEywcAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD///" +
           "//8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEB" +
           "QgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUg" +
           "Y29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAABABIAAABNb25pdG9yZWRO" +
           "b2RlQ291bnQBAdAHAC4ARNAHAAAABv////8BAf////8AAAAA";
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