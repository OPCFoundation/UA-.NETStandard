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

    #region ScalarStructureVariableState Class
    #if (!OPCUA_EXCLUDE_ScalarStructureVariableState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ScalarStructureVariableState : BaseDataVariableState<TestData.ScalarStructureDataType>
    {
        #region Constructors
        /// <remarks />
        public ScalarStructureVariableState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.VariableTypes.ScalarStructureVariableType, TestData.Namespaces.TestData, namespaceUris);
        }

        /// <remarks />
        protected override NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.DataTypes.ScalarStructureDataType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////FWCBAgIAAAABACMAAABTY2FsYXJTdHJ1" +
           "Y3R1cmVWYXJpYWJsZVR5cGVJbnN0YW5jZQEBNwQBATcENwQAAAEBNgQBAf////8bAAAAFWCJCgIAAAAB" +
           "AAwAAABCb29sZWFuVmFsdWUBATgEAC8APzgEAAAAAf////8BAf////8AAAAAFWCJCgIAAAABAAoAAABT" +
           "Qnl0ZVZhbHVlAQE5BAAvAD85BAAAAAL/////AQH/////AAAAABVgiQoCAAAAAQAJAAAAQnl0ZVZhbHVl" +
           "AQE6BAAvAD86BAAAAAP/////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50MTZWYWx1ZQEBOwQALwA/" +
           "OwQAAAAE/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQxNlZhbHVlAQE8BAAvAD88BAAAAAX/" +
           "////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50MzJWYWx1ZQEBPQQALwA/PQQAAAAG/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVlAQE+BAAvAD8+BAAAAAf/////AQH/////AAAAABVg" +
           "iQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBPwQALwA/PwQAAAAI/////wEB/////wAAAAAVYIkKAgAAAAEA" +
           "CwAAAFVJbnQ2NFZhbHVlAQFABAAvAD9ABAAAAAn/////AQH/////AAAAABVgiQoCAAAAAQAKAAAARmxv" +
           "YXRWYWx1ZQEBQQQALwA/QQQAAAAK/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAERvdWJsZVZhbHVl" +
           "AQFCBAAvAD9CBAAAAAv/////AQH/////AAAAABVgiQoCAAAAAQALAAAAU3RyaW5nVmFsdWUBAUMEAC8A" +
           "P0MEAAAADP////8BAf////8AAAAAFWCJCgIAAAABAA0AAABEYXRlVGltZVZhbHVlAQFEBAAvAD9EBAAA" +
           "AA3/////AQH/////AAAAABVgiQoCAAAAAQAJAAAAR3VpZFZhbHVlAQFFBAAvAD9FBAAAAA7/////AQH/" +
           "////AAAAABVgiQoCAAAAAQAPAAAAQnl0ZVN0cmluZ1ZhbHVlAQFGBAAvAD9GBAAAAA//////AQH/////" +
           "AAAAABVgiQoCAAAAAQAPAAAAWG1sRWxlbWVudFZhbHVlAQFHBAAvAD9HBAAAABD/////AQH/////AAAA" +
           "ABVgiQoCAAAAAQALAAAATm9kZUlkVmFsdWUBAUgEAC8AP0gEAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAABABMAAABFeHBhbmRlZE5vZGVJZFZhbHVlAQFJBAAvAD9JBAAAABL/////AQH/////AAAAABVgiQoC" +
           "AAAAAQASAAAAUXVhbGlmaWVkTmFtZVZhbHVlAQFKBAAvAD9KBAAAABT/////AQH/////AAAAABVgiQoC" +
           "AAAAAQASAAAATG9jYWxpemVkVGV4dFZhbHVlAQFLBAAvAD9LBAAAABX/////AQH/////AAAAABVgiQoC" +
           "AAAAAQAPAAAAU3RhdHVzQ29kZVZhbHVlAQFMBAAvAD9MBAAAABP/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAMAAAAVmFyaWFudFZhbHVlAQFNBAAvAD9NBAAAABj/////AQH/////AAAAABVgiQoCAAAAAQAQAAAA" +
           "RW51bWVyYXRpb25WYWx1ZQEBTgQALwA/TgQAAAAd/////wEB/////wAAAAAVYIkKAgAAAAEADgAAAFN0" +
           "cnVjdHVyZVZhbHVlAQFPBAAvAD9PBAAAABb/////AQH/////AAAAABVgiQoCAAAAAQALAAAATnVtYmVy" +
           "VmFsdWUBAVAEAC8AP1AEAAAAGv////8BAf////8AAAAAFWCJCgIAAAABAAwAAABJbnRlZ2VyVmFsdWUB" +
           "AVEEAC8AP1EEAAAAG/////8BAf////8AAAAAFWCJCgIAAAABAA0AAABVSW50ZWdlclZhbHVlAQFSBAAv" +
           "AD9SBAAAABz/////AQH/////AAAAAA==";
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

    #region ScalarStructureVariableValue Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public class ScalarStructureVariableValue : BaseVariableValue
    {
        #region Constructors
        /// <remarks />
        public ScalarStructureVariableValue(ScalarStructureVariableState variable, ScalarStructureDataType value, object dataLock) : base(dataLock)
        {
            m_value = value;

            if (m_value == null)
            {
                m_value = new ScalarStructureDataType();
            }

            Initialize(variable);
        }
        #endregion

        #region Public Members
        /// <remarks />
        public ScalarStructureVariableState Variable
        {
            get { return m_variable; }
        }

        /// <remarks />
        public ScalarStructureDataType Value
        {
            get { return m_value; }
            set { m_value = value; }
        }
        #endregion

        #region Private Methods
        private void Initialize(ScalarStructureVariableState variable)
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
                ScalarStructureDataType newValue;
                if (value is ExtensionObject extensionObject)
                {
                    newValue = (ScalarStructureDataType)extensionObject.Body;
                }
                else
                {
                    newValue = (ScalarStructureDataType)value;
                }

                if (!Utils.IsEqual(m_value, newValue))
                {
                    UpdateChildrenChangeMasks(context, ref newValue, ref statusCode, ref timestamp);
                    Timestamp = timestamp;
                    m_value = (ScalarStructureDataType)Write(newValue);
                    m_variable.UpdateChangeMasks(NodeStateChangeMasks.Value);
                }
            }

            return ServiceResult.Good;
        }

        private void UpdateChildrenChangeMasks(ISystemContext context, ref ScalarStructureDataType newValue, ref StatusCode statusCode, ref DateTime timestamp)
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
        private ScalarStructureDataType m_value;
        private ScalarStructureVariableState m_variable;
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
           "ZTFNZXRob2RUeXBlAQFTBAAvAQFTBFMEAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEBVAQALgBEVAQAAJYLAAAAAQAqAQEYAAAACQAAAEJvb2xlYW5JbgAB/////wAAAAAAAQAqAQEW" +
           "AAAABwAAAFNCeXRlSW4AAv////8AAAAAAAEAKgEBFQAAAAYAAABCeXRlSW4AA/////8AAAAAAAEAKgEB" +
           "FgAAAAcAAABJbnQxNkluAAT/////AAAAAAABACoBARcAAAAIAAAAVUludDE2SW4ABf////8AAAAAAAEA" +
           "KgEBFgAAAAcAAABJbnQzMkluAAb/////AAAAAAABACoBARcAAAAIAAAAVUludDMySW4AB/////8AAAAA" +
           "AAEAKgEBFgAAAAcAAABJbnQ2NEluAAj/////AAAAAAABACoBARcAAAAIAAAAVUludDY0SW4ACf////8A" +
           "AAAAAAEAKgEBFgAAAAcAAABGbG9hdEluAAr/////AAAAAAABACoBARcAAAAIAAAARG91YmxlSW4AC///" +
           "//8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVu" +
           "dHMBAVUEAC4ARFUEAACWCwAAAAEAKgEBGQAAAAoAAABCb29sZWFuT3V0AAH/////AAAAAAABACoBARcA" +
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
           "ZTJNZXRob2RUeXBlAQFWBAAvAQFWBFYEAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEBVwQALgBEVwQAAJYKAAAAAQAqAQEXAAAACAAAAFN0cmluZ0luAAz/////AAAAAAABACoBARkA" +
           "AAAKAAAARGF0ZVRpbWVJbgAN/////wAAAAAAAQAqAQEVAAAABgAAAEd1aWRJbgAO/////wAAAAAAAQAq" +
           "AQEbAAAADAAAAEJ5dGVTdHJpbmdJbgAP/////wAAAAAAAQAqAQEbAAAADAAAAFhtbEVsZW1lbnRJbgAQ" +
           "/////wAAAAAAAQAqAQEXAAAACAAAAE5vZGVJZEluABH/////AAAAAAABACoBAR8AAAAQAAAARXhwYW5k" +
           "ZWROb2RlSWRJbgAS/////wAAAAAAAQAqAQEeAAAADwAAAFF1YWxpZmllZE5hbWVJbgAU/////wAAAAAA" +
           "AQAqAQEeAAAADwAAAExvY2FsaXplZFRleHRJbgAV/////wAAAAAAAQAqAQEbAAAADAAAAFN0YXR1c0Nv" +
           "ZGVJbgAT/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1" +
           "dEFyZ3VtZW50cwEBWAQALgBEWAQAAJYKAAAAAQAqAQEYAAAACQAAAFN0cmluZ091dAAM/////wAAAAAA" +
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
           "ZTNNZXRob2RUeXBlAQFZBAAvAQFZBFkEAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3Vt" +
           "ZW50cwEBWgQALgBEWgQAAJYDAAAAAQAqAQEYAAAACQAAAFZhcmlhbnRJbgAY/////wAAAAAAAQAqAQEc" +
           "AAAADQAAAEVudW1lcmF0aW9uSW4AHf////8AAAAAAAEAKgEBGgAAAAsAAABTdHJ1Y3R1cmVJbgAW////" +
           "/wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50" +
           "cwEBWwQALgBEWwQAAJYDAAAAAQAqAQEZAAAACgAAAFZhcmlhbnRPdXQAGP////8AAAAAAAEAKgEBHQAA" +
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
           "ZU9iamVjdFR5cGVJbnN0YW5jZQEBXAQBAVwEXAQAAAEAAAAAJAABAWAEIgAAADVgiQoCAAAAAQAQAAAA" +
           "U2ltdWxhdGlvbkFjdGl2ZQEBXQQDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9kdWNl" +
           "IG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAERdBAAAAAH/////AQH/////" +
           "AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAV4EAC8BAfkDXgQAAAEB/////wEAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFfBAAuAERfBAAAlgEAAAABACoBAUYAAAAKAAAASXRlcmF0" +
           "aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJhdGUu" +
           "AQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAWAEAC8B" +
           "AEELYAQAAAEAAAAAJAEBAVwEFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBYQQALgBEYQQAAAAP////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBYgQALgBEYgQAAAAR/////wEB/////wAA" +
           "AAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAWMEAC4ARGMEAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAoAAABTb3VyY2VOYW1lAQFkBAAuAERkBAAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAEAAAA" +
           "VGltZQEBZQQALgBEZQQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRpbWUB" +
           "AWYEAC4ARGYEAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAWgEAC4ARGgE" +
           "AAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBaQQALgBEaQQAAAAF/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAWoEAC4ARGoEAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAWsEAC4ARGsEAAAAFf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQFuBAAuAERuBAAAAAz/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAW8EAC4ARG8EAAAAEf////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAYAAABSZXRhaW4BAXAEAC4ARHAEAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFibGVk" +
           "U3RhdGUBAXEEAC8BACMjcQQAAAAV/////wEBAgAAAAEALCMAAQGFBAEALCMAAQGOBAEAAAAVYIkKAgAA" +
           "AAAAAgAAAElkAQFyBAAuAERyBAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0eQEB" +
           "egQALwEAKiN6BAAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQF7" +
           "BAAuAER7BAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAXwEAC8B" +
           "ACojfAQAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBfQQALgBE" +
           "fQQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBfgQALwEAKiN+BAAAABX/" +
           "////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQF/BAAuAER/BAAAAQAmAf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAYAEAC4ARIAEAAAADP////8BAf//" +
           "//8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQGBBAAvAQBEI4EEAAABAQEAAAABAPkLAAEA8woAAAAA" +
           "BGGCCgQAAAAAAAYAAABFbmFibGUBAYIEAC8BAEMjggQAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIKBAAA" +
           "AAAACgAAAEFkZENvbW1lbnQBAYMEAC8BAEUjgwQAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAAAAAA" +
           "DgAAAElucHV0QXJndW1lbnRzAQGEBAAuAESEBAAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP////" +
           "/wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFC" +
           "AAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBj" +
           "b25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3RhdGUB" +
           "AYUEAC8BACMjhQQAAAAV/////wEBAQAAAAEALCMBAQFxBAEAAAAVYIkKAgAAAAAAAgAAAElkAQGGBAAu" +
           "AESGBAAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAZcEAC8BAJcjlwQA" +
           "AAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQGYBAAuAESYBAAA" +
           "lgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBm" +
           "b3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAk" +
           "AAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAVYIkKAgAAAAEADAAAAEJvb2xlYW5WYWx1ZQEBmwQALwA/mwQAAAAB/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAZwEAC8AP5wEAAAAAv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AAkAAABCeXRlVmFsdWUBAZ0EAC8AP50EAAAAA/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQx" +
           "NlZhbHVlAQGeBAAvAD+eBAAAAAT/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDE2VmFsdWUB" +
           "AZ8EAC8AP58EAAAABf////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQzMlZhbHVlAQGgBAAvAD+g" +
           "BAAAAAb/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBAaEEAC8AP6EEAAAAB///" +
           "//8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2NFZhbHVlAQGiBAAvAD+iBAAAAAj/////AQH/////" +
           "AAAAABVgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBAaMEAC8AP6MEAAAACf////8BAf////8AAAAAFWCJ" +
           "CgIAAAABAAoAAABGbG9hdFZhbHVlAQGkBAAvAD+kBAAAAAr/////AQH/////AAAAABVgiQoCAAAAAQAL" +
           "AAAARG91YmxlVmFsdWUBAaUEAC8AP6UEAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABTdHJp" +
           "bmdWYWx1ZQEBpgQALwA/pgQAAAAM/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAERhdGVUaW1lVmFs" +
           "dWUBAacEAC8AP6cEAAAADf////8BAf////8AAAAAFWCJCgIAAAABAAkAAABHdWlkVmFsdWUBAagEAC8A" +
           "P6gEAAAADv////8BAf////8AAAAAFWCJCgIAAAABAA8AAABCeXRlU3RyaW5nVmFsdWUBAakEAC8AP6kE" +
           "AAAAD/////8BAf////8AAAAAFWCJCgIAAAABAA8AAABYbWxFbGVtZW50VmFsdWUBAaoEAC8AP6oEAAAA" +
           "EP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABOb2RlSWRWYWx1ZQEBqwQALwA/qwQAAAAR/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEAEwAAAEV4cGFuZGVkTm9kZUlkVmFsdWUBAawEAC8AP6wEAAAAEv////8B" +
           "Af////8AAAAAFWCJCgIAAAABABIAAABRdWFsaWZpZWROYW1lVmFsdWUBAa0EAC8AP60EAAAAFP////8B" +
           "Af////8AAAAAFWCJCgIAAAABABIAAABMb2NhbGl6ZWRUZXh0VmFsdWUBAa4EAC8AP64EAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAABAA8AAABTdGF0dXNDb2RlVmFsdWUBAa8EAC8AP68EAAAAE/////8BAf//" +
           "//8AAAAAFWCJCgIAAAABAAwAAABWYXJpYW50VmFsdWUBAbAEAC8AP7AEAAAAGP////8BAf////8AAAAA" +
           "FWCJCgIAAAABABAAAABFbnVtZXJhdGlvblZhbHVlAQGxBAAvAD+xBAAAAB3/////AQH/////AAAAABVg" +
           "iQoCAAAAAQAOAAAAU3RydWN0dXJlVmFsdWUBAbIEAC8AP7IEAAAAFv////8BAf////8AAAAAFWCJCgIA" +
           "AAABAAsAAABOdW1iZXJWYWx1ZQEBswQALwA/swQAAAAa/////wEB/////wAAAAAVYIkKAgAAAAEADAAA" +
           "AEludGVnZXJWYWx1ZQEBtAQALwA/tAQAAAAb/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAFVJbnRl" +
           "Z2VyVmFsdWUBAbUEAC8AP7UEAAAAHP////8BAf////8AAAAAFWCJCgIAAAABAAsAAABWZWN0b3JWYWx1" +
           "ZQEBtgQALwEBYQe2BAAAAQFgB/////8BAf////8DAAAAFWCJCgIAAAABAAEAAABYAQG3BAAuAES3BAAA" +
           "AAv/////AQH/////AAAAABVgiQoCAAAAAQABAAAAWQEBuAQALgBEuAQAAAAL/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEAAQAAAFoBAbkEAC4ARLkEAAAAC/////8BAf////8AAAAAFWCJCgIAAAABABAAAABWZWN0" +
           "b3JVbmlvblZhbHVlAQH+DQAvAD/+DQAAAQEADv////8BAf////8AAAAAFWCJCgIAAAABAB0AAABWZWN0" +
           "b3JXaXRoT3B0aW9uYWxGaWVsZHNWYWx1ZQEB/w0ALwA//w0AAAEBAQ7/////AQH/////AAAAABVgiQoC" +
           "AAAAAQAUAAAATXVsdGlwbGVWZWN0b3JzVmFsdWUBAR4OAC8APx4OAAABAR8O/////wEB/////wAAAAA=";
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

        /// <remarks />
        public BaseDataVariableState<VectorUnion> VectorUnionValue
        {
            get
            {
                return m_vectorUnionValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vectorUnionValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vectorUnionValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<VectorWithOptionalFields> VectorWithOptionalFieldsValue
        {
            get
            {
                return m_vectorWithOptionalFieldsValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vectorWithOptionalFieldsValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vectorWithOptionalFieldsValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<MultipleVectors> MultipleVectorsValue
        {
            get
            {
                return m_multipleVectorsValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_multipleVectorsValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_multipleVectorsValue = value;
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

            if (m_vectorUnionValue != null)
            {
                children.Add(m_vectorUnionValue);
            }

            if (m_vectorWithOptionalFieldsValue != null)
            {
                children.Add(m_vectorWithOptionalFieldsValue);
            }

            if (m_multipleVectorsValue != null)
            {
                children.Add(m_multipleVectorsValue);
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

                case TestData.BrowseNames.VectorUnionValue:
                {
                    if (createOrReplace)
                    {
                        if (VectorUnionValue == null)
                        {
                            if (replacement == null)
                            {
                                VectorUnionValue = new BaseDataVariableState<VectorUnion>(this);
                            }
                            else
                            {
                                VectorUnionValue = (BaseDataVariableState<VectorUnion>)replacement;
                            }
                        }
                    }

                    instance = VectorUnionValue;
                    break;
                }

                case TestData.BrowseNames.VectorWithOptionalFieldsValue:
                {
                    if (createOrReplace)
                    {
                        if (VectorWithOptionalFieldsValue == null)
                        {
                            if (replacement == null)
                            {
                                VectorWithOptionalFieldsValue = new BaseDataVariableState<VectorWithOptionalFields>(this);
                            }
                            else
                            {
                                VectorWithOptionalFieldsValue = (BaseDataVariableState<VectorWithOptionalFields>)replacement;
                            }
                        }
                    }

                    instance = VectorWithOptionalFieldsValue;
                    break;
                }

                case TestData.BrowseNames.MultipleVectorsValue:
                {
                    if (createOrReplace)
                    {
                        if (MultipleVectorsValue == null)
                        {
                            if (replacement == null)
                            {
                                MultipleVectorsValue = new BaseDataVariableState<MultipleVectors>(this);
                            }
                            else
                            {
                                MultipleVectorsValue = (BaseDataVariableState<MultipleVectors>)replacement;
                            }
                        }
                    }

                    instance = MultipleVectorsValue;
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
        private BaseDataVariableState<VectorUnion> m_vectorUnionValue;
        private BaseDataVariableState<VectorWithOptionalFields> m_vectorWithOptionalFieldsValue;
        private BaseDataVariableState<MultipleVectors> m_multipleVectorsValue;
        #endregion
    }
    #endif
    #endregion

    #region StructureValueObjectState Class
    #if (!OPCUA_EXCLUDE_StructureValueObjectState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class StructureValueObjectState : TestDataObjectState
    {
        #region Constructors
        /// <remarks />
        public StructureValueObjectState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(TestData.ObjectTypes.StructureValueObjectType, TestData.Namespaces.TestData, namespaceUris);
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
           "AQAAABgAAABodHRwOi8vdGVzdC5vcmcvVUEvRGF0YS//////BGCAAgEAAAABACAAAABTdHJ1Y3R1cmVW" +
           "YWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBugQBAboEugQAAAEAAAAAJAABAb4EBQAAADVgiQoCAAAAAQAQ" +
           "AAAAU2ltdWxhdGlvbkFjdGl2ZQEBuwQDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9k" +
           "dWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAES7BAAAAAH/////AQH/" +
           "////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAbwEAC8BAfkDvAQAAAEB/////wEAAAAX" +
           "YKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQG9BAAuAES9BAAAlgEAAAABACoBAUYAAAAKAAAASXRl" +
           "cmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJh" +
           "dGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAb4E" +
           "AC8BAEELvgQAAAEAAAAAJAEBAboEFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBvwQALgBEvwQAAAAP" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBwAQALgBEwAQAAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBAcEEAC4ARMEEAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAoAAABTb3VyY2VOYW1lAQHCBAAuAETCBAAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAE" +
           "AAAAVGltZQEBwwQALgBEwwQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRp" +
           "bWUBAcQEAC4ARMQEAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBAcYEAC4A" +
           "RMYEAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBxwQALgBExwQAAAAF////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAcgEAC4ARMgEAAAAEf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBAckEAC4ARMkEAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQHMBAAuAETMBAAAAAz/////AQH/////" +
           "AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAc0EAC4ARM0EAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAYAAABSZXRhaW4BAc4EAC4ARM4EAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFi" +
           "bGVkU3RhdGUBAc8EAC8BACMjzwQAAAAV/////wEBAgAAAAEALCMAAQHjBAEALCMAAQHsBAEAAAAVYIkK" +
           "AgAAAAAAAgAAAElkAQHQBAAuAETQBAAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0" +
           "eQEB2AQALwEAKiPYBAAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1w" +
           "AQHZBAAuAETZBAAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBAdoE" +
           "AC8BACoj2gQAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB2wQA" +
           "LgBE2wQAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEB3AQALwEAKiPcBAAA" +
           "ABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQHdBAAuAETdBAAAAQAm" +
           "Af////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAd4EAC4ARN4EAAAADP////8B" +
           "Af////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQHfBAAvAQBEI98EAAABAQEAAAABAPkLAAEA8woA" +
           "AAAABGGCCgQAAAAAAAYAAABFbmFibGUBAeAEAC8BAEMj4AQAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIK" +
           "BAAAAAAACgAAAEFkZENvbW1lbnQBAeEEAC8BAEUj4QQAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQHiBAAuAETiBAAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP" +
           "/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAq" +
           "AQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRo" +
           "ZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3Rh" +
           "dGUBAeMEAC8BACMj4wQAAAAV/////wEBAQAAAAEALCMBAQHPBAEAAAAVYIkKAgAAAAAAAgAAAElkAQHk" +
           "BAAuAETkBAAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAfUEAC8BAJcj" +
           "9QQAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQH2BAAuAET2" +
           "BAAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmll" +
           "ciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAA" +
           "AAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAVYIkKAgAAAAEADwAAAFNjYWxhclN0cnVjdHVyZQEB+QQALwEBNwT5BAAAAQE2BP////8B" +
           "Af////8bAAAAFWCJCgIAAAABAAwAAABCb29sZWFuVmFsdWUBAfoEAC8AP/oEAAAAAf////8BAf////8A" +
           "AAAAFWCJCgIAAAABAAoAAABTQnl0ZVZhbHVlAQH7BAAvAD/7BAAAAAL/////AQH/////AAAAABVgiQoC" +
           "AAAAAQAJAAAAQnl0ZVZhbHVlAQH8BAAvAD/8BAAAAAP/////AQH/////AAAAABVgiQoCAAAAAQAKAAAA" +
           "SW50MTZWYWx1ZQEB/QQALwA//QQAAAAE/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQxNlZh" +
           "bHVlAQH+BAAvAD/+BAAAAAX/////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50MzJWYWx1ZQEB/wQA" +
           "LwA//wQAAAAG/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVlAQEABQAvAD8ABQAA" +
           "AAf/////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBAQUALwA/AQUAAAAI/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQ2NFZhbHVlAQECBQAvAD8CBQAAAAn/////AQH/////AAAA" +
           "ABVgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBAwUALwA/AwUAAAAK/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEACwAAAERvdWJsZVZhbHVlAQEEBQAvAD8EBQAAAAv/////AQH/////AAAAABVgiQoCAAAAAQALAAAA" +
           "U3RyaW5nVmFsdWUBAQUFAC8APwUFAAAADP////8BAf////8AAAAAFWCJCgIAAAABAA0AAABEYXRlVGlt" +
           "ZVZhbHVlAQEGBQAvAD8GBQAAAA3/////AQH/////AAAAABVgiQoCAAAAAQAJAAAAR3VpZFZhbHVlAQEH" +
           "BQAvAD8HBQAAAA7/////AQH/////AAAAABVgiQoCAAAAAQAPAAAAQnl0ZVN0cmluZ1ZhbHVlAQEIBQAv" +
           "AD8IBQAAAA//////AQH/////AAAAABVgiQoCAAAAAQAPAAAAWG1sRWxlbWVudFZhbHVlAQEJBQAvAD8J" +
           "BQAAABD/////AQH/////AAAAABVgiQoCAAAAAQALAAAATm9kZUlkVmFsdWUBAQoFAC8APwoFAAAAEf//" +
           "//8BAf////8AAAAAFWCJCgIAAAABABMAAABFeHBhbmRlZE5vZGVJZFZhbHVlAQELBQAvAD8LBQAAABL/" +
           "////AQH/////AAAAABVgiQoCAAAAAQASAAAAUXVhbGlmaWVkTmFtZVZhbHVlAQEMBQAvAD8MBQAAABT/" +
           "////AQH/////AAAAABVgiQoCAAAAAQASAAAATG9jYWxpemVkVGV4dFZhbHVlAQENBQAvAD8NBQAAABX/" +
           "////AQH/////AAAAABVgiQoCAAAAAQAPAAAAU3RhdHVzQ29kZVZhbHVlAQEOBQAvAD8OBQAAABP/////" +
           "AQH/////AAAAABVgiQoCAAAAAQAMAAAAVmFyaWFudFZhbHVlAQEPBQAvAD8PBQAAABj/////AQH/////" +
           "AAAAABVgiQoCAAAAAQAQAAAARW51bWVyYXRpb25WYWx1ZQEBEAUALwA/EAUAAAAd/////wEB/////wAA" +
           "AAAVYIkKAgAAAAEADgAAAFN0cnVjdHVyZVZhbHVlAQERBQAvAD8RBQAAABb/////AQH/////AAAAABVg" +
           "iQoCAAAAAQALAAAATnVtYmVyVmFsdWUBARIFAC8APxIFAAAAGv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AAwAAABJbnRlZ2VyVmFsdWUBARMFAC8APxMFAAAAG/////8BAf////8AAAAAFWCJCgIAAAABAA0AAABV" +
           "SW50ZWdlclZhbHVlAQEUBQAvAD8UBQAAABz/////AQH/////AAAAABVgiQoCAAAAAQAPAAAAVmVjdG9y" +
           "U3RydWN0dXJlAQEVBQAvAQFhBxUFAAABAWAH/////wEB/////wMAAAAVYIkKAgAAAAEAAQAAAFgBARYF" +
           "AC4ARBYFAAAAC/////8BAf////8AAAAAFWCJCgIAAAABAAEAAABZAQEXBQAuAEQXBQAAAAv/////AQH/" +
           "////AAAAABVgiQoCAAAAAQABAAAAWgEBGAUALgBEGAUAAAAL/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public ScalarStructureVariableState ScalarStructure
        {
            get
            {
                return m_scalarStructure;
            }

            set
            {
                if (!Object.ReferenceEquals(m_scalarStructure, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_scalarStructure = value;
            }
        }

        /// <remarks />
        public VectorVariableState VectorStructure
        {
            get
            {
                return m_vectorStructure;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vectorStructure, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vectorStructure = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_scalarStructure != null)
            {
                children.Add(m_scalarStructure);
            }

            if (m_vectorStructure != null)
            {
                children.Add(m_vectorStructure);
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
                case TestData.BrowseNames.ScalarStructure:
                {
                    if (createOrReplace)
                    {
                        if (ScalarStructure == null)
                        {
                            if (replacement == null)
                            {
                                ScalarStructure = new ScalarStructureVariableState(this);
                            }
                            else
                            {
                                ScalarStructure = (ScalarStructureVariableState)replacement;
                            }
                        }
                    }

                    instance = ScalarStructure;
                    break;
                }

                case TestData.BrowseNames.VectorStructure:
                {
                    if (createOrReplace)
                    {
                        if (VectorStructure == null)
                        {
                            if (replacement == null)
                            {
                                VectorStructure = new VectorVariableState(this);
                            }
                            else
                            {
                                VectorStructure = (VectorVariableState)replacement;
                            }
                        }
                    }

                    instance = VectorStructure;
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
        private ScalarStructureVariableState m_scalarStructure;
        private VectorVariableState m_vectorStructure;
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
           "YXJWYWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBGQUBARkFGQUAAAEAAAAAJAABAR0FEAAAADVgiQoCAAAA" +
           "AQAQAAAAU2ltdWxhdGlvbkFjdGl2ZQEBGgUDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBw" +
           "cm9kdWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAEQaBQAAAAH/////" +
           "AQH/////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBARsFAC8BAfkDGwUAAAEB/////wEA" +
           "AAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEcBQAuAEQcBQAAlgEAAAABACoBAUYAAAAKAAAA" +
           "SXRlcmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2Vu" +
           "ZXJhdGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUB" +
           "AR0FAC8BAEELHQUAAAEAAAAAJAEBARkFFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBHgUALgBEHgUA" +
           "AAAP/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBHwUALgBEHwUAAAAR/////wEB" +
           "/////wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBASAFAC4ARCAFAAAAEf////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAoAAABTb3VyY2VOYW1lAQEhBQAuAEQhBQAAAAz/////AQH/////AAAAABVgiQoCAAAA" +
           "AAAEAAAAVGltZQEBIgUALgBEIgUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2" +
           "ZVRpbWUBASMFAC4ARCMFAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBASUF" +
           "AC4ARCUFAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBJgUALgBEJgUAAAAF" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBAScFAC4ARCcFAAAAEf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBASgFAC4ARCgFAAAAFf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQErBQAuAEQrBQAAAAz/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBASwFAC4ARCwFAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAYAAABSZXRhaW4BAS0FAC4ARC0FAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABF" +
           "bmFibGVkU3RhdGUBAS4FAC8BACMjLgUAAAAV/////wEBAgAAAAEALCMAAQFCBQEALCMAAQFLBQEAAAAV" +
           "YIkKAgAAAAAAAgAAAElkAQEvBQAuAEQvBQAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVh" +
           "bGl0eQEBNwUALwEAKiM3BQAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0" +
           "YW1wAQE4BQAuAEQ4BQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkB" +
           "ATkFAC8BACojOQUAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB" +
           "OgUALgBEOgUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBOwUALwEAKiM7" +
           "BQAAABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQE8BQAuAEQ8BQAA" +
           "AQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAT0FAC4ARD0FAAAADP//" +
           "//8BAf////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQE+BQAvAQBEIz4FAAABAQEAAAABAPkLAAEA" +
           "8woAAAAABGGCCgQAAAAAAAYAAABFbmFibGUBAT8FAC8BAEMjPwUAAAEBAQAAAAEA+QsAAQDzCgAAAAAE" +
           "YYIKBAAAAAAACgAAAEFkZENvbW1lbnQBAUAFAC8BAEUjQAUAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkK" +
           "AgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFBBQAuAERBBQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJ" +
           "ZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQu" +
           "AQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRv" +
           "IHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2Vk" +
           "U3RhdGUBAUIFAC8BACMjQgUAAAAV/////wEBAQAAAAEALCMBAQEuBQEAAAAVYIkKAgAAAAAAAgAAAElk" +
           "AQFDBQAuAERDBQAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAVQFAC8B" +
           "AJcjVAUAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFVBQAu" +
           "AERVBQAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRp" +
           "ZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAA" +
           "AwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAA" +
           "AAEB/////wAAAAAVYIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAVgFAC8BAEAJWAUAAAAC/////wEB////" +
           "/wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAVwFAC4ARFwFAAABAHQD/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEACQAAAEJ5dGVWYWx1ZQEBXgUALwEAQAleBQAAAAP/////AQH/////AQAAABVgiQoCAAAAAAAH" +
           "AAAARVVSYW5nZQEBYgUALgBEYgUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAAAQAKAAAASW50MTZW" +
           "YWx1ZQEBZAUALwEAQAlkBQAAAAT/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBaAUA" +
           "LgBEaAUAAAEAdAP/////AQH/////AAAAABVgiQoCAAAAAQALAAAAVUludDE2VmFsdWUBAWoFAC8BAEAJ" +
           "agUAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAW4FAC4ARG4FAAABAHQD////" +
           "/wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMyVmFsdWUBAXAFAC8BAEAJcAUAAAAG/////wEB////" +
           "/wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAXQFAC4ARHQFAAABAHQD/////wEB/////wAAAAAVYIkK" +
           "AgAAAAEACwAAAFVJbnQzMlZhbHVlAQF2BQAvAQBACXYFAAAAB/////8BAf////8BAAAAFWCJCgIAAAAA" +
           "AAcAAABFVVJhbmdlAQF6BQAuAER6BQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2" +
           "NFZhbHVlAQF8BQAvAQBACXwFAAAACP////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGA" +
           "BQAuAESABQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBggUALwEA" +
           "QAmCBQAAAAn/////AQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBhgUALgBEhgUAAAEAdAP/" +
           "////AQH/////AAAAABVgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBiAUALwEAQAmIBQAAAAr/////AQH/" +
           "////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBjAUALgBEjAUAAAEAdAP/////AQH/////AAAAABVg" +
           "iQoCAAAAAQALAAAARG91YmxlVmFsdWUBAY4FAC8BAEAJjgUAAAAL/////wEB/////wEAAAAVYIkKAgAA" +
           "AAAABwAAAEVVUmFuZ2UBAZIFAC4ARJIFAAABAHQD/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAE51" +
           "bWJlclZhbHVlAQGUBQAvAQBACZQFAAAAGv////8BAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdl" +
           "AQGYBQAuAESYBQAAAQB0A/////8BAf////8AAAAAFWCJCgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBAZoF" +
           "AC8BAEAJmgUAAAAb/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAZ4FAC4ARJ4FAAAB" +
           "AHQD/////wEB/////wAAAAAVYIkKAgAAAAEADQAAAFVJbnRlZ2VyVmFsdWUBAaAFAC8BAEAJoAUAAAAc" +
           "/////wEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UBAaQFAC4ARKQFAAABAHQD/////wEB////" +
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
           "MU1ldGhvZFR5cGUBAacFAC8BAacFpwUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQGoBQAuAESoBQAAlgsAAAABACoBARwAAAAJAAAAQm9vbGVhbkluAAEBAAAAAQAAAAAAAAAAAQAq" +
           "AQEaAAAABwAAAFNCeXRlSW4AAgEAAAABAAAAAAAAAAABACoBARkAAAAGAAAAQnl0ZUluAAMBAAAAAQAA" +
           "AAAAAAAAAQAqAQEaAAAABwAAAEludDE2SW4ABAEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAVUludDE2" +
           "SW4ABQEAAAABAAAAAAAAAAABACoBARoAAAAHAAAASW50MzJJbgAGAQAAAAEAAAAAAAAAAAEAKgEBGwAA" +
           "AAgAAABVSW50MzJJbgAHAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABJbnQ2NEluAAgBAAAAAQAAAAAA" +
           "AAAAAQAqAQEbAAAACAAAAFVJbnQ2NEluAAkBAAAAAQAAAAAAAAAAAQAqAQEaAAAABwAAAEZsb2F0SW4A" +
           "CgEAAAABAAAAAAAAAAABACoBARsAAAAIAAAARG91YmxlSW4ACwEAAAABAAAAAAAAAAABACgBAQAAAAEA" +
           "AAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQGpBQAuAESpBQAAlgsA" +
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
           "Mk1ldGhvZFR5cGUBAaoFAC8BAaoFqgUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQGrBQAuAESrBQAAlgoAAAABACoBARsAAAAIAAAAU3RyaW5nSW4ADAEAAAABAAAAAAAAAAABACoB" +
           "AR0AAAAKAAAARGF0ZVRpbWVJbgANAQAAAAEAAAAAAAAAAAEAKgEBGQAAAAYAAABHdWlkSW4ADgEAAAAB" +
           "AAAAAAAAAAABACoBAR8AAAAMAAAAQnl0ZVN0cmluZ0luAA8BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAA" +
           "AFhtbEVsZW1lbnRJbgAQAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABOb2RlSWRJbgARAQAAAAEAAAAA" +
           "AAAAAAEAKgEBIwAAABAAAABFeHBhbmRlZE5vZGVJZEluABIBAAAAAQAAAAAAAAAAAQAqAQEiAAAADwAA" +
           "AFF1YWxpZmllZE5hbWVJbgAUAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA8AAABMb2NhbGl6ZWRUZXh0SW4A" +
           "FQEAAAABAAAAAAAAAAABACoBAR8AAAAMAAAAU3RhdHVzQ29kZUluABMBAAAAAQAAAAAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBrAUALgBErAUA" +
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
           "M01ldGhvZFR5cGUBAa0FAC8BAa0FrQUAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1l" +
           "bnRzAQGuBQAuAESuBQAAlgMAAAABACoBARwAAAAJAAAAVmFyaWFudEluABgBAAAAAQAAAAAAAAAAAQAq" +
           "AQEgAAAADQAAAEVudW1lcmF0aW9uSW4AHQEAAAABAAAAAAAAAAABACoBAR4AAAALAAAAU3RydWN0dXJl" +
           "SW4AFgEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0" +
           "cHV0QXJndW1lbnRzAQGvBQAuAESvBQAAlgMAAAABACoBAR0AAAAKAAAAVmFyaWFudE91dAAYAQAAAAEA" +
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
           "T2JqZWN0VHlwZUluc3RhbmNlAQGwBQEBsAWwBQAAAQAAAAAkAAEBtAUiAAAANWCJCgIAAAABABAAAABT" +
           "aW11bGF0aW9uQWN0aXZlAQGxBQMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHByb2R1Y2Ug" +
           "bmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARLEFAAAAAf////8BAf////8A" +
           "AAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBsgUALwEB+QOyBQAAAQH/////AQAAABdgqQoC" +
           "AAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAbMFAC4ARLMFAACWAQAAAAEAKgEBRgAAAAoAAABJdGVyYXRp" +
           "b25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5lcmF0ZS4B" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEBtAUALwEA" +
           "QQu0BQAAAQAAAAAkAQEBsAUXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQG1BQAuAES1BQAAAA//////" +
           "AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQG2BQAuAES2BQAAABH/////AQH/////AAAA" +
           "ABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBtwUALgBEtwUAAAAR/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACgAAAFNvdXJjZU5hbWUBAbgFAC4ARLgFAAAADP////8BAf////8AAAAAFWCJCgIAAAAAAAQAAABU" +
           "aW1lAQG5BQAuAES5BQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZlVGltZQEB" +
           "ugUALgBEugUAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEBvAUALgBEvAUA" +
           "AAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQG9BQAuAES9BQAAAAX/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBvgUALgBEvgUAAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBvwUALgBEvwUAAAAV/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBAcIFAC4ARMIFAAAADP////8BAf////8AAAAA" +
           "FWCJCgIAAAAAAAgAAABCcmFuY2hJZAEBwwUALgBEwwUAAAAR/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BgAAAFJldGFpbgEBxAUALgBExAUAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVuYWJsZWRT" +
           "dGF0ZQEBxQUALwEAIyPFBQAAABX/////AQECAAAAAQAsIwABAdkFAQAsIwABAeIFAQAAABVgiQoCAAAA" +
           "AAACAAAASWQBAcYFAC4ARMYFAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFsaXR5AQHO" +
           "BQAvAQAqI84FAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAc8F" +
           "AC4ARM8FAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEB0AUALwEA" +
           "KiPQBQAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQHRBQAuAETR" +
           "BQAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQHSBQAvAQAqI9IFAAAAFf//" +
           "//8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAdMFAC4ARNMFAAABACYB////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEB1AUALgBE1AUAAAAM/////wEB////" +
           "/wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBAdUFAC8BAEQj1QUAAAEBAQAAAAEA+QsAAQDzCgAAAAAE" +
           "YYIKBAAAAAAABgAAAEVuYWJsZQEB1gUALwEAQyPWBQAAAQEBAAAAAQD5CwABAPMKAAAAAARhggoEAAAA" +
           "AAAKAAAAQWRkQ29tbWVudAEB1wUALwEARSPXBQAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoCAAAAAAAO" +
           "AAAASW5wdXRBcmd1bWVudHMBAdgFAC4ARNgFAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////" +
           "AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIA" +
           "AAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNv" +
           "bmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRTdGF0ZQEB" +
           "2QUALwEAIyPZBQAAABX/////AQEBAAAAAQAsIwEBAcUFAQAAABVgiQoCAAAAAAACAAAASWQBAdoFAC4A" +
           "RNoFAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEB6wUALwEAlyPrBQAA" +
           "AQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAewFAC4AROwFAACW" +
           "AgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZv" +
           "ciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQA" +
           "AABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////" +
           "AAAAABdgiQoCAAAAAQAMAAAAQm9vbGVhblZhbHVlAQHvBQAvAD/vBQAAAAEBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAoAAABTQnl0ZVZhbHVlAQHwBQAvAD/wBQAAAAIBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAkAAABCeXRlVmFsdWUBAfEFAC8AP/EFAAAAAwEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACgAAAEludDE2VmFsdWUBAfIFAC8AP/IFAAAABAEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAFVJbnQxNlZhbHVlAQHzBQAvAD/zBQAAAAUBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAoAAABJbnQzMlZhbHVlAQH0BQAvAD/0BQAAAAYBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAsAAABVSW50MzJWYWx1ZQEB9QUALwA/9QUAAAAHAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAABdgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEB9gUALwA/9gUAAAAIAQAAAAEAAAAAAAAAAQH/" +
           "////AAAAABdgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBAfcFAC8AP/cFAAAACQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEACgAAAEZsb2F0VmFsdWUBAfgFAC8AP/gFAAAACgEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEACwAAAERvdWJsZVZhbHVlAQH5BQAvAD/5BQAAAAsBAAAAAQAAAAAAAAAB" +
           "Af////8AAAAAF2CJCgIAAAABAAsAAABTdHJpbmdWYWx1ZQEB+gUALwA/+gUAAAAMAQAAAAEAAAAAAAAA" +
           "AQH/////AAAAABdgiQoCAAAAAQANAAAARGF0ZVRpbWVWYWx1ZQEB+wUALwA/+wUAAAANAQAAAAEAAAAA" +
           "AAAAAQH/////AAAAABdgiQoCAAAAAQAJAAAAR3VpZFZhbHVlAQH8BQAvAD/8BQAAAA4BAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABAA8AAABCeXRlU3RyaW5nVmFsdWUBAf0FAC8AP/0FAAAADwEAAAAB" +
           "AAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAFhtbEVsZW1lbnRWYWx1ZQEB/gUALwA//gUAAAAQ" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQALAAAATm9kZUlkVmFsdWUBAf8FAC8AP/8FAAAA" +
           "EQEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEAEwAAAEV4cGFuZGVkTm9kZUlkVmFsdWUBAQAG" +
           "AC8APwAGAAAAEgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEAEgAAAFF1YWxpZmllZE5hbWVW" +
           "YWx1ZQEBAQYALwA/AQYAAAAUAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQASAAAATG9jYWxp" +
           "emVkVGV4dFZhbHVlAQECBgAvAD8CBgAAABUBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAA8A" +
           "AABTdGF0dXNDb2RlVmFsdWUBAQMGAC8APwMGAAAAEwEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAA" +
           "AAEADAAAAFZhcmlhbnRWYWx1ZQEBBAYALwA/BAYAAAAYAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoC" +
           "AAAAAQAQAAAARW51bWVyYXRpb25WYWx1ZQEBBQYALwA/BQYAAAAdAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQAOAAAAU3RydWN0dXJlVmFsdWUBAQYGAC8APwYGAAAAFgEAAAABAAAAAAAAAAEB////" +
           "/wAAAAAXYIkKAgAAAAEACwAAAE51bWJlclZhbHVlAQEHBgAvAD8HBgAAABoBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CJCgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBAQgGAC8APwgGAAAAGwEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEADQAAAFVJbnRlZ2VyVmFsdWUBAQkGAC8APwkGAAAAHAEAAAABAAAAAAAA" +
           "AAEB/////wAAAAAXYIkKAgAAAAEACwAAAFZlY3RvclZhbHVlAQEKBgAvAD8KBgAAAQFgBwEAAAABAAAA" +
           "AAAAAAEB/////wAAAAAXYIkKAgAAAAEAEAAAAFZlY3RvclVuaW9uVmFsdWUBARgOAC8APxgOAAABAQAO" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAdAAAAVmVjdG9yV2l0aE9wdGlvbmFsRmllbGRz" +
           "VmFsdWUBARkOAC8APxkOAAABAQEOAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAUAAAATXVs" +
           "dGlwbGVWZWN0b3JzVmFsdWUBASsOAC8APysOAAABAR8OAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
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

        /// <remarks />
        public BaseDataVariableState<VectorUnion[]> VectorUnionValue
        {
            get
            {
                return m_vectorUnionValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vectorUnionValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vectorUnionValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<VectorWithOptionalFields[]> VectorWithOptionalFieldsValue
        {
            get
            {
                return m_vectorWithOptionalFieldsValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vectorWithOptionalFieldsValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vectorWithOptionalFieldsValue = value;
            }
        }

        /// <remarks />
        public BaseDataVariableState<MultipleVectors[]> MultipleVectorsValue
        {
            get
            {
                return m_multipleVectorsValue;
            }

            set
            {
                if (!Object.ReferenceEquals(m_multipleVectorsValue, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_multipleVectorsValue = value;
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

            if (m_vectorUnionValue != null)
            {
                children.Add(m_vectorUnionValue);
            }

            if (m_vectorWithOptionalFieldsValue != null)
            {
                children.Add(m_vectorWithOptionalFieldsValue);
            }

            if (m_multipleVectorsValue != null)
            {
                children.Add(m_multipleVectorsValue);
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

                case TestData.BrowseNames.VectorUnionValue:
                {
                    if (createOrReplace)
                    {
                        if (VectorUnionValue == null)
                        {
                            if (replacement == null)
                            {
                                VectorUnionValue = new BaseDataVariableState<VectorUnion[]>(this);
                            }
                            else
                            {
                                VectorUnionValue = (BaseDataVariableState<VectorUnion[]>)replacement;
                            }
                        }
                    }

                    instance = VectorUnionValue;
                    break;
                }

                case TestData.BrowseNames.VectorWithOptionalFieldsValue:
                {
                    if (createOrReplace)
                    {
                        if (VectorWithOptionalFieldsValue == null)
                        {
                            if (replacement == null)
                            {
                                VectorWithOptionalFieldsValue = new BaseDataVariableState<VectorWithOptionalFields[]>(this);
                            }
                            else
                            {
                                VectorWithOptionalFieldsValue = (BaseDataVariableState<VectorWithOptionalFields[]>)replacement;
                            }
                        }
                    }

                    instance = VectorWithOptionalFieldsValue;
                    break;
                }

                case TestData.BrowseNames.MultipleVectorsValue:
                {
                    if (createOrReplace)
                    {
                        if (MultipleVectorsValue == null)
                        {
                            if (replacement == null)
                            {
                                MultipleVectorsValue = new BaseDataVariableState<MultipleVectors[]>(this);
                            }
                            else
                            {
                                MultipleVectorsValue = (BaseDataVariableState<MultipleVectors[]>)replacement;
                            }
                        }
                    }

                    instance = MultipleVectorsValue;
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
        private BaseDataVariableState<VectorUnion[]> m_vectorUnionValue;
        private BaseDataVariableState<VectorWithOptionalFields[]> m_vectorWithOptionalFieldsValue;
        private BaseDataVariableState<MultipleVectors[]> m_multipleVectorsValue;
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
           "eVZhbHVlT2JqZWN0VHlwZUluc3RhbmNlAQELBgEBCwYLBgAAAQAAAAAkAAEBDwYQAAAANWCJCgIAAAAB" +
           "ABAAAABTaW11bGF0aW9uQWN0aXZlAQEMBgMAAAAARwAAAElmIHRydWUgdGhlIHNlcnZlciB3aWxsIHBy" +
           "b2R1Y2UgbmV3IHZhbHVlcyBmb3IgZWFjaCBtb25pdG9yZWQgdmFyaWFibGUuAC4ARAwGAAAAAf////8B" +
           "Af////8AAAAABGGCCgQAAAABAA4AAABHZW5lcmF0ZVZhbHVlcwEBDQYALwEB+QMNBgAAAQH/////AQAA" +
           "ABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAQ4GAC4ARA4GAACWAQAAAAEAKgEBRgAAAAoAAABJ" +
           "dGVyYXRpb25zAAf/////AAAAAAMAAAAAJQAAAFRoZSBudW1iZXIgb2YgbmV3IHZhbHVlcyB0byBnZW5l" +
           "cmF0ZS4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARggAoBAAAAAQANAAAAQ3ljbGVDb21wbGV0ZQEB" +
           "DwYALwEAQQsPBgAAAQAAAAAkAQEBCwYXAAAAFWCJCgIAAAAAAAcAAABFdmVudElkAQEQBgAuAEQQBgAA" +
           "AA//////AQH/////AAAAABVgiQoCAAAAAAAJAAAARXZlbnRUeXBlAQERBgAuAEQRBgAAABH/////AQH/" +
           "////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTm9kZQEBEgYALgBEEgYAAAAR/////wEB/////wAAAAAV" +
           "YIkKAgAAAAAACgAAAFNvdXJjZU5hbWUBARMGAC4ARBMGAAAADP////8BAf////8AAAAAFWCJCgIAAAAA" +
           "AAQAAABUaW1lAQEUBgAuAEQUBgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAsAAABSZWNlaXZl" +
           "VGltZQEBFQYALgBEFQYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAATWVzc2FnZQEBFwYA" +
           "LgBEFwYAAAAV/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAFNldmVyaXR5AQEYBgAuAEQYBgAAAAX/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAQAAAAQ29uZGl0aW9uQ2xhc3NJZAEBGQYALgBEGQYAAAAR////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEgAAAENvbmRpdGlvbkNsYXNzTmFtZQEBGgYALgBEGgYAAAAV////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAADQAAAENvbmRpdGlvbk5hbWUBAR0GAC4ARB0GAAAADP////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAgAAABCcmFuY2hJZAEBHgYALgBEHgYAAAAR/////wEB/////wAAAAAVYIkK" +
           "AgAAAAAABgAAAFJldGFpbgEBHwYALgBEHwYAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAEVu" +
           "YWJsZWRTdGF0ZQEBIAYALwEAIyMgBgAAABX/////AQECAAAAAQAsIwABATQGAQAsIwABAT0GAQAAABVg" +
           "iQoCAAAAAAACAAAASWQBASEGAC4ARCEGAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABRdWFs" +
           "aXR5AQEpBgAvAQAqIykGAAAAE/////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3Rh" +
           "bXABASoGAC4ARCoGAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAExhc3RTZXZlcml0eQEB" +
           "KwYALwEAKiMrBgAAAAX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEs" +
           "BgAuAEQsBgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABDb21tZW50AQEtBgAvAQAqIy0G" +
           "AAAAFf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAS4GAC4ARC4GAAAB" +
           "ACYB/////wEB/////wAAAAAVYIkKAgAAAAAADAAAAENsaWVudFVzZXJJZAEBLwYALgBELwYAAAAM////" +
           "/wEB/////wAAAAAEYYIKBAAAAAAABwAAAERpc2FibGUBATAGAC8BAEQjMAYAAAEBAQAAAAEA+QsAAQDz" +
           "CgAAAAAEYYIKBAAAAAAABgAAAEVuYWJsZQEBMQYALwEAQyMxBgAAAQEBAAAAAQD5CwABAPMKAAAAAARh" +
           "ggoEAAAAAAAKAAAAQWRkQ29tbWVudAEBMgYALwEARSMyBgAAAQEBAAAAAQD5CwABAA0LAQAAABdgqQoC" +
           "AAAAAAAOAAAASW5wdXRBcmd1bWVudHMBATMGAC4ARDMGAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElk" +
           "AA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlmaWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4B" +
           "ACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAADAAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8g" +
           "dGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAAAQH/////AAAAABVgiQoCAAAAAAAKAAAAQWNrZWRT" +
           "dGF0ZQEBNAYALwEAIyM0BgAAABX/////AQEBAAAAAQAsIwEBASAGAQAAABVgiQoCAAAAAAACAAAASWQB" +
           "ATUGAC4ARDUGAAAAAf////8BAf////8AAAAABGGCCgQAAAAAAAsAAABBY2tub3dsZWRnZQEBRgYALwEA" +
           "lyNGBgAAAQEBAAAAAQD5CwABAPAiAQAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAUcGAC4A" +
           "REcGAACWAgAAAAEAKgEBRgAAAAcAAABFdmVudElkAA//////AAAAAAMAAAAAKAAAAFRoZSBpZGVudGlm" +
           "aWVyIGZvciB0aGUgZXZlbnQgdG8gY29tbWVudC4BACoBAUIAAAAHAAAAQ29tbWVudAAV/////wAAAAAD" +
           "AAAAACQAAABUaGUgY29tbWVudCB0byBhZGQgdG8gdGhlIGNvbmRpdGlvbi4BACgBAQAAAAEAAAAAAAAA" +
           "AQH/////AAAAABdgiQoCAAAAAQAKAAAAU0J5dGVWYWx1ZQEBSgYALwEAQAlKBgAAAAIBAAAAAQAAAAAA" +
           "AAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQFOBgAuAEROBgAAAQB0A/////8BAf////8A" +
           "AAAAF2CJCgIAAAABAAkAAABCeXRlVmFsdWUBAVAGAC8BAEAJUAYAAAADAQAAAAEAAAAAAAAAAQH/////" +
           "AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBVAYALgBEVAYAAAEAdAP/////AQH/////AAAAABdgiQoC" +
           "AAAAAQAKAAAASW50MTZWYWx1ZQEBVgYALwEAQAlWBgAAAAQBAAAAAQAAAAAAAAABAf////8BAAAAFWCJ" +
           "CgIAAAAAAAcAAABFVVJhbmdlAQFaBgAuAERaBgAAAQB0A/////8BAf////8AAAAAF2CJCgIAAAABAAsA" +
           "AABVSW50MTZWYWx1ZQEBXAYALwEAQAlcBgAAAAUBAAAAAQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAA" +
           "AAcAAABFVVJhbmdlAQFgBgAuAERgBgAAAQB0A/////8BAf////8AAAAAF2CJCgIAAAABAAoAAABJbnQz" +
           "MlZhbHVlAQFiBgAvAQBACWIGAAAABgEAAAABAAAAAAAAAAEB/////wEAAAAVYIkKAgAAAAAABwAAAEVV" +
           "UmFuZ2UBAWYGAC4ARGYGAAABAHQD/////wEB/////wAAAAAXYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVl" +
           "AQFoBgAvAQBACWgGAAAABwEAAAABAAAAAAAAAAEB/////wEAAAAVYIkKAgAAAAAABwAAAEVVUmFuZ2UB" +
           "AWwGAC4ARGwGAAABAHQD/////wEB/////wAAAAAXYIkKAgAAAAEACgAAAEludDY0VmFsdWUBAW4GAC8B" +
           "AEAJbgYAAAAIAQAAAAEAAAAAAAAAAQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBcgYALgBE" +
           "cgYAAAEAdAP/////AQH/////AAAAABdgiQoCAAAAAQALAAAAVUludDY0VmFsdWUBAXQGAC8BAEAJdAYA" +
           "AAAJAQAAAAEAAAAAAAAAAQH/////AQAAABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBeAYALgBEeAYAAAEA" +
           "dAP/////AQH/////AAAAABdgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEBegYALwEAQAl6BgAAAAoBAAAA" +
           "AQAAAAAAAAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQF+BgAuAER+BgAAAQB0A/////8B" +
           "Af////8AAAAAF2CJCgIAAAABAAsAAABEb3VibGVWYWx1ZQEBgAYALwEAQAmABgAAAAsBAAAAAQAAAAAA" +
           "AAABAf////8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGEBgAuAESEBgAAAQB0A/////8BAf////8A" +
           "AAAAF2CJCgIAAAABAAsAAABOdW1iZXJWYWx1ZQEBhgYALwEAQAmGBgAAABoBAAAAAQAAAAAAAAABAf//" +
           "//8BAAAAFWCJCgIAAAAAAAcAAABFVVJhbmdlAQGKBgAuAESKBgAAAQB0A/////8BAf////8AAAAAF2CJ" +
           "CgIAAAABAAwAAABJbnRlZ2VyVmFsdWUBAYwGAC8BAEAJjAYAAAAbAQAAAAEAAAAAAAAAAQH/////AQAA" +
           "ABVgiQoCAAAAAAAHAAAARVVSYW5nZQEBkAYALgBEkAYAAAEAdAP/////AQH/////AAAAABdgiQoCAAAA" +
           "AQANAAAAVUludGVnZXJWYWx1ZQEBkgYALwEAQAmSBgAAABwBAAAAAQAAAAAAAAABAf////8BAAAAFWCJ" +
           "CgIAAAAAAAcAAABFVVJhbmdlAQGWBgAuAESWBgAAAQB0A/////8BAf////8AAAAA";
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
           "VmFsdWVPYmplY3RUeXBlSW5zdGFuY2UBAa8GAQGvBq8GAAABAAAAACQAAQGzBhkAAAA1YIkKAgAAAAEA" +
           "EAAAAFNpbXVsYXRpb25BY3RpdmUBAbAGAwAAAABHAAAASWYgdHJ1ZSB0aGUgc2VydmVyIHdpbGwgcHJv" +
           "ZHVjZSBuZXcgdmFsdWVzIGZvciBlYWNoIG1vbml0b3JlZCB2YXJpYWJsZS4ALgBEsAYAAAAB/////wEB" +
           "/////wAAAAAEYYIKBAAAAAEADgAAAEdlbmVyYXRlVmFsdWVzAQGxBgAvAQH5A7EGAAABAf////8BAAAA" +
           "F2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBsgYALgBEsgYAAJYBAAAAAQAqAQFGAAAACgAAAEl0" +
           "ZXJhdGlvbnMAB/////8AAAAAAwAAAAAlAAAAVGhlIG51bWJlciBvZiBuZXcgdmFsdWVzIHRvIGdlbmVy" +
           "YXRlLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGCACgEAAAABAA0AAABDeWNsZUNvbXBsZXRlAQGz" +
           "BgAvAQBBC7MGAAABAAAAACQBAQGvBhcAAAAVYIkKAgAAAAAABwAAAEV2ZW50SWQBAbQGAC4ARLQGAAAA" +
           "D/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAbUGAC4ARLUGAAAAEf////8BAf//" +
           "//8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQG2BgAuAES2BgAAABH/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAKAAAAU291cmNlTmFtZQEBtwYALgBEtwYAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAA" +
           "BAAAAFRpbWUBAbgGAC4ARLgGAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAACwAAAFJlY2VpdmVU" +
           "aW1lAQG5BgAuAES5BgAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcAAABNZXNzYWdlAQG7BgAu" +
           "AES7BgAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkBAbwGAC4ARLwGAAAABf//" +
           "//8BAf////8AAAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQG9BgAuAES9BgAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQG+BgAuAES+BgAAABX/////" +
           "AQH/////AAAAABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBwQYALgBEwQYAAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACAAAAEJyYW5jaElkAQHCBgAuAETCBgAAABH/////AQH/////AAAAABVgiQoC" +
           "AAAAAAAGAAAAUmV0YWluAQHDBgAuAETDBgAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAARW5h" +
           "YmxlZFN0YXRlAQHEBgAvAQAjI8QGAAAAFf////8BAQIAAAABACwjAAEB2AYBACwjAAEB4QYBAAAAFWCJ" +
           "CgIAAAAAAAIAAABJZAEBxQYALgBExQYAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxp" +
           "dHkBAc0GAC8BACojzQYAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFt" +
           "cAEBzgYALgBEzgYAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQHP" +
           "BgAvAQAqI88GAAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAdAG" +
           "AC4ARNAGAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAdEGAC8BACoj0QYA" +
           "AAAV/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB0gYALgBE0gYAAAEA" +
           "JgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQHTBgAuAETTBgAAAAz/////" +
           "AQH/////AAAAAARhggoEAAAAAAAHAAAARGlzYWJsZQEB1AYALwEARCPUBgAAAQEBAAAAAQD5CwABAPMK" +
           "AAAAAARhggoEAAAAAAAGAAAARW5hYmxlAQHVBgAvAQBDI9UGAAABAQEAAAABAPkLAAEA8woAAAAABGGC" +
           "CgQAAAAAAAoAAABBZGRDb21tZW50AQHWBgAvAQBFI9YGAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIA" +
           "AAAAAA4AAABJbnB1dEFyZ3VtZW50cwEB1wYALgBE1wYAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQA" +
           "D/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEA" +
           "KgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0" +
           "aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAAAAAoAAABBY2tlZFN0" +
           "YXRlAQHYBgAvAQAjI9gGAAAAFf////8BAQEAAAABACwjAQEBxAYBAAAAFWCJCgIAAAAAAAIAAABJZAEB" +
           "2QYALgBE2QYAAAAB/////wEB/////wAAAAAEYYIKBAAAAAAACwAAAEFja25vd2xlZGdlAQHqBgAvAQCX" +
           "I+oGAAABAQEAAAABAPkLAAEA8CIBAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEB6wYALgBE" +
           "6wYAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD/////8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZp" +
           "ZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEBQgAAAAcAAABDb21tZW50ABX/////AAAAAAMA" +
           "AAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUgY29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAAB" +
           "Af////8AAAAAFWCJCgIAAAABAAwAAABCb29sZWFuVmFsdWUBAe4GAC8AP+4GAAABAZgG/////wEB////" +
           "/wAAAAAVYIkKAgAAAAEACgAAAFNCeXRlVmFsdWUBAe8GAC8AP+8GAAABAZkG/////wEB/////wAAAAAV" +
           "YIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEB8AYALwA/8AYAAAEBmgb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAKAAAASW50MTZWYWx1ZQEB8QYALwA/8QYAAAEBmwb/////AQH/////AAAAABVgiQoCAAAAAQALAAAA" +
           "VUludDE2VmFsdWUBAfIGAC8AP/IGAAABAZwG/////wEB/////wAAAAAVYIkKAgAAAAEACgAAAEludDMy" +
           "VmFsdWUBAfMGAC8AP/MGAAABAZ0G/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFVJbnQzMlZhbHVl" +
           "AQH0BgAvAD/0BgAAAQGeBv////8BAf////8AAAAAFWCJCgIAAAABAAoAAABJbnQ2NFZhbHVlAQH1BgAv" +
           "AD/1BgAAAQGfBv////8BAf////8AAAAAFWCJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEB9gYALwA/9gYA" +
           "AAEBoAb/////AQH/////AAAAABVgiQoCAAAAAQAKAAAARmxvYXRWYWx1ZQEB9wYALwA/9wYAAAEBoQb/" +
           "////AQH/////AAAAABVgiQoCAAAAAQALAAAARG91YmxlVmFsdWUBAfgGAC8AP/gGAAABAaIG/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEACwAAAFN0cmluZ1ZhbHVlAQH5BgAvAD/5BgAAAQGjBv////8BAf////8A" +
           "AAAAFWCJCgIAAAABAA0AAABEYXRlVGltZVZhbHVlAQH6BgAvAD/6BgAAAQGkBv////8BAf////8AAAAA" +
           "FWCJCgIAAAABAAkAAABHdWlkVmFsdWUBAfsGAC8AP/sGAAABAaUG/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEADwAAAEJ5dGVTdHJpbmdWYWx1ZQEB/AYALwA//AYAAAEBpgb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAPAAAAWG1sRWxlbWVudFZhbHVlAQH9BgAvAD/9BgAAAQGnBv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "AAsAAABOb2RlSWRWYWx1ZQEB/gYALwA//gYAAAEBqAb/////AQH/////AAAAABVgiQoCAAAAAQATAAAA" +
           "RXhwYW5kZWROb2RlSWRWYWx1ZQEB/wYALwA//wYAAAEBqQb/////AQH/////AAAAABVgiQoCAAAAAQAS" +
           "AAAAUXVhbGlmaWVkTmFtZVZhbHVlAQEABwAvAD8ABwAAAQGqBv////8BAf////8AAAAAFWCJCgIAAAAB" +
           "ABIAAABMb2NhbGl6ZWRUZXh0VmFsdWUBAQEHAC8APwEHAAABAasG/////wEB/////wAAAAAVYIkKAgAA" +
           "AAEADwAAAFN0YXR1c0NvZGVWYWx1ZQEBAgcALwA/AgcAAAEBrAb/////AQH/////AAAAABVgiQoCAAAA" +
           "AQAMAAAAVmFyaWFudFZhbHVlAQEDBwAvAD8DBwAAAQGtBv////8BAf////8AAAAA";
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
           "VmFsdWUxTWV0aG9kVHlwZQEBBAcALwEBBAcEBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBAQUHAC4ARAUHAACWDAAAAAEAKgEBGgAAAAkAAABCb29sZWFuSW4BAZgG/////wAAAAAA" +
           "AQAqAQEYAAAABwAAAFNCeXRlSW4BAZkG/////wAAAAAAAQAqAQEXAAAABgAAAEJ5dGVJbgEBmgb/////" +
           "AAAAAAABACoBARgAAAAHAAAASW50MTZJbgEBmwb/////AAAAAAABACoBARkAAAAIAAAAVUludDE2SW4B" +
           "AZwG/////wAAAAAAAQAqAQEYAAAABwAAAEludDMySW4BAZ0G/////wAAAAAAAQAqAQEZAAAACAAAAFVJ" +
           "bnQzMkluAQGeBv////8AAAAAAAEAKgEBGAAAAAcAAABJbnQ2NEluAQGfBv////8AAAAAAAEAKgEBGQAA" +
           "AAgAAABVSW50NjRJbgEBoAb/////AAAAAAABACoBARgAAAAHAAAARmxvYXRJbgEBoQb/////AAAAAAAB" +
           "ACoBARkAAAAIAAAARG91YmxlSW4BAaIG/////wAAAAAAAQAqAQEZAAAACAAAAFN0cmluZ0luAQGjBv//" +
           "//8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVu" +
           "dHMBAQYHAC4ARAYHAACWDAAAAAEAKgEBGwAAAAoAAABCb29sZWFuT3V0AQGYBv////8AAAAAAAEAKgEB" +
           "GQAAAAgAAABTQnl0ZU91dAEBmQb/////AAAAAAABACoBARgAAAAHAAAAQnl0ZU91dAEBmgb/////AAAA" +
           "AAABACoBARkAAAAIAAAASW50MTZPdXQBAZsG/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQxNk91dAEB" +
           "nAb/////AAAAAAABACoBARkAAAAIAAAASW50MzJPdXQBAZ0G/////wAAAAAAAQAqAQEaAAAACQAAAFVJ" +
           "bnQzMk91dAEBngb/////AAAAAAABACoBARkAAAAIAAAASW50NjRPdXQBAZ8G/////wAAAAAAAQAqAQEa" +
           "AAAACQAAAFVJbnQ2NE91dAEBoAb/////AAAAAAABACoBARkAAAAIAAAARmxvYXRPdXQBAaEG/////wAA" +
           "AAAAAQAqAQEaAAAACQAAAERvdWJsZU91dAEBogb/////AAAAAAABACoBARoAAAAJAAAAU3RyaW5nT3V0" +
           "AQGjBv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA";
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
           "VmFsdWUyTWV0aG9kVHlwZQEBBwcALwEBBwcHBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRB" +
           "cmd1bWVudHMBAQgHAC4ARAgHAACWCgAAAAEAKgEBGwAAAAoAAABEYXRlVGltZUluAQGkBv////8AAAAA" +
           "AAEAKgEBFwAAAAYAAABHdWlkSW4BAaUG/////wAAAAAAAQAqAQEdAAAADAAAAEJ5dGVTdHJpbmdJbgEB" +
           "pgb/////AAAAAAABACoBAR0AAAAMAAAAWG1sRWxlbWVudEluAQGnBv////8AAAAAAAEAKgEBGQAAAAgA" +
           "AABOb2RlSWRJbgEBqAb/////AAAAAAABACoBASEAAAAQAAAARXhwYW5kZWROb2RlSWRJbgEBqQb/////" +
           "AAAAAAABACoBASAAAAAPAAAAUXVhbGlmaWVkTmFtZUluAQGqBv////8AAAAAAAEAKgEBIAAAAA8AAABM" +
           "b2NhbGl6ZWRUZXh0SW4BAasG/////wAAAAAAAQAqAQEdAAAADAAAAFN0YXR1c0NvZGVJbgEBrAb/////" +
           "AAAAAAABACoBARoAAAAJAAAAVmFyaWFudEluAQGtBv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf//" +
           "//8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAQkHAC4ARAkHAACWCgAAAAEAKgEBHAAA" +
           "AAsAAABEYXRlVGltZU91dAEBpAb/////AAAAAAABACoBARgAAAAHAAAAR3VpZE91dAEBpQb/////AAAA" +
           "AAABACoBAR4AAAANAAAAQnl0ZVN0cmluZ091dAEBpgb/////AAAAAAABACoBAR4AAAANAAAAWG1sRWxl" +
           "bWVudE91dAEBpwb/////AAAAAAABACoBARoAAAAJAAAATm9kZUlkT3V0AQGoBv////8AAAAAAAEAKgEB" +
           "IgAAABEAAABFeHBhbmRlZE5vZGVJZE91dAEBqQb/////AAAAAAABACoBASEAAAAQAAAAUXVhbGlmaWVk" +
           "TmFtZU91dAEBqgb/////AAAAAAABACoBASEAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBqwb/////AAAA" +
           "AAABACoBAR4AAAANAAAAU3RhdHVzQ29kZU91dAEBrAb/////AAAAAAABACoBARsAAAAKAAAAVmFyaWFu" +
           "dE91dAEBrQb/////AAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
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
           "YWx1ZU9iamVjdFR5cGVJbnN0YW5jZQEBCwcBAQsHCwcAAAEAAAAAJAABAQ8HGQAAADVgiQoCAAAAAQAQ" +
           "AAAAU2ltdWxhdGlvbkFjdGl2ZQEBDAcDAAAAAEcAAABJZiB0cnVlIHRoZSBzZXJ2ZXIgd2lsbCBwcm9k" +
           "dWNlIG5ldyB2YWx1ZXMgZm9yIGVhY2ggbW9uaXRvcmVkIHZhcmlhYmxlLgAuAEQMBwAAAAH/////AQH/" +
           "////AAAAAARhggoEAAAAAQAOAAAAR2VuZXJhdGVWYWx1ZXMBAQ0HAC8BAfkDDQcAAAEB/////wEAAAAX" +
           "YKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQEOBwAuAEQOBwAAlgEAAAABACoBAUYAAAAKAAAASXRl" +
           "cmF0aW9ucwAH/////wAAAAADAAAAACUAAABUaGUgbnVtYmVyIG9mIG5ldyB2YWx1ZXMgdG8gZ2VuZXJh" +
           "dGUuAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYIAKAQAAAAEADQAAAEN5Y2xlQ29tcGxldGUBAQ8H" +
           "AC8BAEELDwcAAAEAAAAAJAEBAQsHFwAAABVgiQoCAAAAAAAHAAAARXZlbnRJZAEBEAcALgBEEAcAAAAP" +
           "/////wEB/////wAAAAAVYIkKAgAAAAAACQAAAEV2ZW50VHlwZQEBEQcALgBEEQcAAAAR/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAACgAAAFNvdXJjZU5vZGUBARIHAC4ARBIHAAAAEf////8BAf////8AAAAAFWCJ" +
           "CgIAAAAAAAoAAABTb3VyY2VOYW1lAQETBwAuAEQTBwAAAAz/////AQH/////AAAAABVgiQoCAAAAAAAE" +
           "AAAAVGltZQEBFAcALgBEFAcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAALAAAAUmVjZWl2ZVRp" +
           "bWUBARUHAC4ARBUHAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAE1lc3NhZ2UBARcHAC4A" +
           "RBcHAAAAFf////8BAf////8AAAAAFWCJCgIAAAAAAAgAAABTZXZlcml0eQEBGAcALgBEGAcAAAAF////" +
           "/wEB/////wAAAAAVYIkKAgAAAAAAEAAAAENvbmRpdGlvbkNsYXNzSWQBARkHAC4ARBkHAAAAEf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAABIAAABDb25kaXRpb25DbGFzc05hbWUBARoHAC4ARBoHAAAAFf////8B" +
           "Af////8AAAAAFWCJCgIAAAAAAA0AAABDb25kaXRpb25OYW1lAQEdBwAuAEQdBwAAAAz/////AQH/////" +
           "AAAAABVgiQoCAAAAAAAIAAAAQnJhbmNoSWQBAR4HAC4ARB4HAAAAEf////8BAf////8AAAAAFWCJCgIA" +
           "AAAAAAYAAABSZXRhaW4BAR8HAC4ARB8HAAAAAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABFbmFi" +
           "bGVkU3RhdGUBASAHAC8BACMjIAcAAAAV/////wEBAgAAAAEALCMAAQE0BwEALCMAAQE9BwEAAAAVYIkK" +
           "AgAAAAAAAgAAAElkAQEhBwAuAEQhBwAAAAH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAUXVhbGl0" +
           "eQEBKQcALwEAKiMpBwAAABP/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1w" +
           "AQEqBwAuAEQqBwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABMYXN0U2V2ZXJpdHkBASsH" +
           "AC8BACojKwcAAAAF/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBLAcA" +
           "LgBELAcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAHAAAAQ29tbWVudAEBLQcALwEAKiMtBwAA" +
           "ABX/////AQH/////AQAAABVgiQoCAAAAAAAPAAAAU291cmNlVGltZXN0YW1wAQEuBwAuAEQuBwAAAQAm" +
           "Af////8BAf////8AAAAAFWCJCgIAAAAAAAwAAABDbGllbnRVc2VySWQBAS8HAC4ARC8HAAAADP////8B" +
           "Af////8AAAAABGGCCgQAAAAAAAcAAABEaXNhYmxlAQEwBwAvAQBEIzAHAAABAQEAAAABAPkLAAEA8woA" +
           "AAAABGGCCgQAAAAAAAYAAABFbmFibGUBATEHAC8BAEMjMQcAAAEBAQAAAAEA+QsAAQDzCgAAAAAEYYIK" +
           "BAAAAAAACgAAAEFkZENvbW1lbnQBATIHAC8BAEUjMgcAAAEBAQAAAAEA+QsAAQANCwEAAAAXYKkKAgAA" +
           "AAAADgAAAElucHV0QXJndW1lbnRzAQEzBwAuAEQzBwAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP" +
           "/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmllciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAq" +
           "AQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAAAAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRo" +
           "ZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAVYIkKAgAAAAAACgAAAEFja2VkU3Rh" +
           "dGUBATQHAC8BACMjNAcAAAAV/////wEBAQAAAAEALCMBAQEgBwEAAAAVYIkKAgAAAAAAAgAAAElkAQE1" +
           "BwAuAEQ1BwAAAAH/////AQH/////AAAAAARhggoEAAAAAAALAAAAQWNrbm93bGVkZ2UBAUYHAC8BAJcj" +
           "RgcAAAEBAQAAAAEA+QsAAQDwIgEAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFHBwAuAERH" +
           "BwAAlgIAAAABACoBAUYAAAAHAAAARXZlbnRJZAAP/////wAAAAADAAAAACgAAABUaGUgaWRlbnRpZmll" +
           "ciBmb3IgdGhlIGV2ZW50IHRvIGNvbW1lbnQuAQAqAQFCAAAABwAAAENvbW1lbnQAFf////8AAAAAAwAA" +
           "AAAkAAAAVGhlIGNvbW1lbnQgdG8gYWRkIHRvIHRoZSBjb25kaXRpb24uAQAoAQEAAAABAAAAAAAAAAEB" +
           "/////wAAAAAXYIkKAgAAAAEADAAAAEJvb2xlYW5WYWx1ZQEBSgcALwA/SgcAAAEBmAYBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABAAoAAABTQnl0ZVZhbHVlAQFLBwAvAD9LBwAAAQGZBgEAAAABAAAA" +
           "AAAAAAEB/////wAAAAAXYIkKAgAAAAEACQAAAEJ5dGVWYWx1ZQEBTAcALwA/TAcAAAEBmgYBAAAAAQAA" +
           "AAAAAAABAf////8AAAAAF2CJCgIAAAABAAoAAABJbnQxNlZhbHVlAQFNBwAvAD9NBwAAAQGbBgEAAAAB" +
           "AAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAFVJbnQxNlZhbHVlAQFOBwAvAD9OBwAAAQGcBgEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACgAAAEludDMyVmFsdWUBAU8HAC8AP08HAAABAZ0G" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQALAAAAVUludDMyVmFsdWUBAVAHAC8AP1AHAAAB" +
           "AZ4GAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAKAAAASW50NjRWYWx1ZQEBUQcALwA/UQcA" +
           "AAEBnwYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAsAAABVSW50NjRWYWx1ZQEBUgcALwA/" +
           "UgcAAAEBoAYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAoAAABGbG9hdFZhbHVlAQFTBwAv" +
           "AD9TBwAAAQGhBgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAERvdWJsZVZhbHVlAQFU" +
           "BwAvAD9UBwAAAQGiBgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEACwAAAFN0cmluZ1ZhbHVl" +
           "AQFVBwAvAD9VBwAAAQGjBgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADQAAAERhdGVUaW1l" +
           "VmFsdWUBAVYHAC8AP1YHAAABAaQGAQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQAJAAAAR3Vp" +
           "ZFZhbHVlAQFXBwAvAD9XBwAAAQGlBgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAEJ5" +
           "dGVTdHJpbmdWYWx1ZQEBWAcALwA/WAcAAAEBpgYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAAB" +
           "AA8AAABYbWxFbGVtZW50VmFsdWUBAVkHAC8AP1kHAAABAacGAQAAAAEAAAAAAAAAAQH/////AAAAABdg" +
           "iQoCAAAAAQALAAAATm9kZUlkVmFsdWUBAVoHAC8AP1oHAAABAagGAQAAAAEAAAAAAAAAAQH/////AAAA" +
           "ABdgiQoCAAAAAQATAAAARXhwYW5kZWROb2RlSWRWYWx1ZQEBWwcALwA/WwcAAAEBqQYBAAAAAQAAAAAA" +
           "AAABAf////8AAAAAF2CJCgIAAAABABIAAABRdWFsaWZpZWROYW1lVmFsdWUBAVwHAC8AP1wHAAABAaoG" +
           "AQAAAAEAAAAAAAAAAQH/////AAAAABdgiQoCAAAAAQASAAAATG9jYWxpemVkVGV4dFZhbHVlAQFdBwAv" +
           "AD9dBwAAAQGrBgEAAAABAAAAAAAAAAEB/////wAAAAAXYIkKAgAAAAEADwAAAFN0YXR1c0NvZGVWYWx1" +
           "ZQEBXgcALwA/XgcAAAEBrAYBAAAAAQAAAAAAAAABAf////8AAAAAF2CJCgIAAAABAAwAAABWYXJpYW50" +
           "VmFsdWUBAV8HAC8AP18HAAABAa0GAQAAAAEAAAAAAAAAAQH/////AAAAAA==";
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
           "YWJsZVR5cGVJbnN0YW5jZQEBYQcBAWEHYQcAAAEBYAcBAf////8DAAAAFWCJCgIAAAABAAEAAABYAQFi" +
           "BwAuAERiBwAAAAv/////AQH/////AAAAABVgiQoCAAAAAQABAAAAWQEBYwcALgBEYwcAAAAL/////wEB" +
           "/////wAAAAAVYIkKAgAAAAEAAQAAAFoBAWQHAC4ARGQHAAAAC/////8BAf////8AAAAA";
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
           "YWx1ZTFNZXRob2RUeXBlAQFnBwAvAQFnB2cHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBaAcALgBEaAcAAJYMAAAAAQAqAQEeAAAACQAAAEJvb2xlYW5JbgEBmAYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEcAAAABwAAAFNCeXRlSW4BAZkGAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAYAAABCeXRlSW4B" +
           "AZoGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQxNkluAQGbBgEAAAABAAAAAAAAAAABACoBAR0A" +
           "AAAIAAAAVUludDE2SW4BAZwGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJbnQzMkluAQGdBgEAAAAB" +
           "AAAAAAAAAAABACoBAR0AAAAIAAAAVUludDMySW4BAZ4GAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABJ" +
           "bnQ2NEluAQGfBgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAAVUludDY0SW4BAaAGAQAAAAEAAAAAAAAA" +
           "AAEAKgEBHAAAAAcAAABGbG9hdEluAQGhBgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAARG91YmxlSW4B" +
           "AaIGAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABTdHJpbmdJbgEBowYBAAAAAQAAAAAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBaQcALgBEaQcA" +
           "AJYMAAAAAQAqAQEfAAAACgAAAEJvb2xlYW5PdXQBAZgGAQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABT" +
           "Qnl0ZU91dAEBmQYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEJ5dGVPdXQBAZoGAQAAAAEAAAAAAAAA" +
           "AAEAKgEBHQAAAAgAAABJbnQxNk91dAEBmwYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAFVJbnQxNk91" +
           "dAEBnAYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAEludDMyT3V0AQGdBgEAAAABAAAAAAAAAAABACoB" +
           "AR4AAAAJAAAAVUludDMyT3V0AQGeBgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAASW50NjRPdXQBAZ8G" +
           "AQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABVSW50NjRPdXQBAaAGAQAAAAEAAAAAAAAAAAEAKgEBHQAA" +
           "AAgAAABGbG9hdE91dAEBoQYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAERvdWJsZU91dAEBogYBAAAA" +
           "AQAAAAAAAAAAAQAqAQEeAAAACQAAAFN0cmluZ091dAEBowYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAA" +
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
           "YWx1ZTJNZXRob2RUeXBlAQFqBwAvAQFqB2oHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFy" +
           "Z3VtZW50cwEBawcALgBEawcAAJYKAAAAAQAqAQEfAAAACgAAAERhdGVUaW1lSW4BAaQGAQAAAAEAAAAA" +
           "AAAAAAEAKgEBGwAAAAYAAABHdWlkSW4BAaUGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABCeXRlU3Ry" +
           "aW5nSW4BAaYGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABYbWxFbGVtZW50SW4BAacGAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHQAAAAgAAABOb2RlSWRJbgEBqAYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAEV4cGFu" +
           "ZGVkTm9kZUlkSW4BAakGAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABRdWFsaWZpZWROYW1lSW4BAaoG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAasGAQAAAAEAAAAAAAAAAAEA" +
           "KgEBIQAAAAwAAABTdGF0dXNDb2RlSW4BAawGAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABWYXJpYW50" +
           "SW4BAa0GAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABP" +
           "dXRwdXRBcmd1bWVudHMBAWwHAC4ARGwHAACWCgAAAAEAKgEBIAAAAAsAAABEYXRlVGltZU91dAEBpAYB" +
           "AAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEd1aWRPdXQBAaUGAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0A" +
           "AABCeXRlU3RyaW5nT3V0AQGmBgEAAAABAAAAAAAAAAABACoBASIAAAANAAAAWG1sRWxlbWVudE91dAEB" +
           "pwYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAE5vZGVJZE91dAEBqAYBAAAAAQAAAAAAAAAAAQAqAQEm" +
           "AAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0AQGpBgEAAAABAAAAAAAAAAABACoBASUAAAAQAAAAUXVhbGlm" +
           "aWVkTmFtZU91dAEBqgYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAExvY2FsaXplZFRleHRPdXQBAasG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0AAABTdGF0dXNDb2RlT3V0AQGsBgEAAAABAAAAAAAAAAABACoB" +
           "AR8AAAAKAAAAVmFyaWFudE91dAEBrQYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAA" +
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
           "VHlwZUluc3RhbmNlAQFtBwEBbQdtBwAA/////woAAAAEYYIKBAAAAAEADQAAAFNjYWxhck1ldGhvZDEB" +
           "AW4HAC8BAW4HbgcAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFvBwAuAERv" +
           "BwAAlgsAAAABACoBARgAAAAJAAAAQm9vbGVhbkluAAH/////AAAAAAABACoBARYAAAAHAAAAU0J5dGVJ" +
           "bgAC/////wAAAAAAAQAqAQEVAAAABgAAAEJ5dGVJbgAD/////wAAAAAAAQAqAQEWAAAABwAAAEludDE2" +
           "SW4ABP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MTZJbgAF/////wAAAAAAAQAqAQEWAAAABwAAAElu" +
           "dDMySW4ABv////8AAAAAAAEAKgEBFwAAAAgAAABVSW50MzJJbgAH/////wAAAAAAAQAqAQEWAAAABwAA" +
           "AEludDY0SW4ACP////8AAAAAAAEAKgEBFwAAAAgAAABVSW50NjRJbgAJ/////wAAAAAAAQAqAQEWAAAA" +
           "BwAAAEZsb2F0SW4ACv////8AAAAAAAEAKgEBFwAAAAgAAABEb3VibGVJbgAL/////wAAAAAAAQAoAQEA" +
           "AAABAAAAAAAAAAEB/////wAAAAAXYKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBcAcALgBEcAcA" +
           "AJYLAAAAAQAqAQEZAAAACgAAAEJvb2xlYW5PdXQAAf////8AAAAAAAEAKgEBFwAAAAgAAABTQnl0ZU91" +
           "dAAC/////wAAAAAAAQAqAQEWAAAABwAAAEJ5dGVPdXQAA/////8AAAAAAAEAKgEBFwAAAAgAAABJbnQx" +
           "Nk91dAAE/////wAAAAAAAQAqAQEYAAAACQAAAFVJbnQxNk91dAAF/////wAAAAAAAQAqAQEXAAAACAAA" +
           "AEludDMyT3V0AAb/////AAAAAAABACoBARgAAAAJAAAAVUludDMyT3V0AAf/////AAAAAAABACoBARcA" +
           "AAAIAAAASW50NjRPdXQACP////8AAAAAAAEAKgEBGAAAAAkAAABVSW50NjRPdXQACf////8AAAAAAAEA" +
           "KgEBFwAAAAgAAABGbG9hdE91dAAK/////wAAAAAAAQAqAQEYAAAACQAAAERvdWJsZU91dAAL/////wAA" +
           "AAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADQAAAFNjYWxhck1ldGhvZDIBAXEH" +
           "AC8BAXEHcQcAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRzAQFyBwAuAERyBwAA" +
           "lgoAAAABACoBARcAAAAIAAAAU3RyaW5nSW4ADP////8AAAAAAAEAKgEBGQAAAAoAAABEYXRlVGltZUlu" +
           "AA3/////AAAAAAABACoBARUAAAAGAAAAR3VpZEluAA7/////AAAAAAABACoBARsAAAAMAAAAQnl0ZVN0" +
           "cmluZ0luAA//////AAAAAAABACoBARsAAAAMAAAAWG1sRWxlbWVudEluABD/////AAAAAAABACoBARcA" +
           "AAAIAAAATm9kZUlkSW4AEf////8AAAAAAAEAKgEBHwAAABAAAABFeHBhbmRlZE5vZGVJZEluABL/////" +
           "AAAAAAABACoBAR4AAAAPAAAAUXVhbGlmaWVkTmFtZUluABT/////AAAAAAABACoBAR4AAAAPAAAATG9j" +
           "YWxpemVkVGV4dEluABX/////AAAAAAABACoBARsAAAAMAAAAU3RhdHVzQ29kZUluABP/////AAAAAAAB" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQFzBwAu" +
           "AERzBwAAlgoAAAABACoBARgAAAAJAAAAU3RyaW5nT3V0AAz/////AAAAAAABACoBARoAAAALAAAARGF0" +
           "ZVRpbWVPdXQADf////8AAAAAAAEAKgEBFgAAAAcAAABHdWlkT3V0AA7/////AAAAAAABACoBARwAAAAN" +
           "AAAAQnl0ZVN0cmluZ091dAAP/////wAAAAAAAQAqAQEcAAAADQAAAFhtbEVsZW1lbnRPdXQAEP////8A" +
           "AAAAAAEAKgEBGAAAAAkAAABOb2RlSWRPdXQAEf////8AAAAAAAEAKgEBIAAAABEAAABFeHBhbmRlZE5v" +
           "ZGVJZE91dAAS/////wAAAAAAAQAqAQEfAAAAEAAAAFF1YWxpZmllZE5hbWVPdXQAFP////8AAAAAAAEA" +
           "KgEBHwAAABAAAABMb2NhbGl6ZWRUZXh0T3V0ABX/////AAAAAAABACoBARwAAAANAAAAU3RhdHVzQ29k" +
           "ZU91dAAT/////wAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADQAAAFNjYWxh" +
           "ck1ldGhvZDMBAXQHAC8BAXQHdAcAAAEB/////wIAAAAXYKkKAgAAAAAADgAAAElucHV0QXJndW1lbnRz" +
           "AQF1BwAuAER1BwAAlgMAAAABACoBARgAAAAJAAAAVmFyaWFudEluABj/////AAAAAAABACoBARwAAAAN" +
           "AAAARW51bWVyYXRpb25JbgAd/////wAAAAAAAQAqAQEaAAAACwAAAFN0cnVjdHVyZUluABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAABdgqQoCAAAAAAAPAAAAT3V0cHV0QXJndW1lbnRzAQF2" +
           "BwAuAER2BwAAlgMAAAABACoBARkAAAAKAAAAVmFyaWFudE91dAAY/////wAAAAAAAQAqAQEdAAAADgAA" +
           "AEVudW1lcmF0aW9uT3V0AB3/////AAAAAAABACoBARsAAAAMAAAAU3RydWN0dXJlT3V0ABb/////AAAA" +
           "AAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAAAQAMAAAAQXJyYXlNZXRob2QxAQF3BwAv" +
           "AQF3B3cHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1dEFyZ3VtZW50cwEBeAcALgBEeAcAAJYL" +
           "AAAAAQAqAQEcAAAACQAAAEJvb2xlYW5JbgABAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABTQnl0ZUlu" +
           "AAIBAAAAAQAAAAAAAAAAAQAqAQEZAAAABgAAAEJ5dGVJbgADAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcA" +
           "AABJbnQxNkluAAQBAAAAAQAAAAAAAAAAAQAqAQEbAAAACAAAAFVJbnQxNkluAAUBAAAAAQAAAAAAAAAA" +
           "AQAqAQEaAAAABwAAAEludDMySW4ABgEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAVUludDMySW4ABwEA" +
           "AAABAAAAAAAAAAABACoBARoAAAAHAAAASW50NjRJbgAIAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABV" +
           "SW50NjRJbgAJAQAAAAEAAAAAAAAAAAEAKgEBGgAAAAcAAABGbG9hdEluAAoBAAAAAQAAAAAAAAAAAQAq" +
           "AQEbAAAACAAAAERvdWJsZUluAAsBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAAAAEB/////wAAAAAX" +
           "YKkKAgAAAAAADwAAAE91dHB1dEFyZ3VtZW50cwEBeQcALgBEeQcAAJYLAAAAAQAqAQEdAAAACgAAAEJv" +
           "b2xlYW5PdXQAAQEAAAABAAAAAAAAAAABACoBARsAAAAIAAAAU0J5dGVPdXQAAgEAAAABAAAAAAAAAAAB" +
           "ACoBARoAAAAHAAAAQnl0ZU91dAADAQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABJbnQxNk91dAAEAQAA" +
           "AAEAAAAAAAAAAAEAKgEBHAAAAAkAAABVSW50MTZPdXQABQEAAAABAAAAAAAAAAABACoBARsAAAAIAAAA" +
           "SW50MzJPdXQABgEAAAABAAAAAAAAAAABACoBARwAAAAJAAAAVUludDMyT3V0AAcBAAAAAQAAAAAAAAAA" +
           "AQAqAQEbAAAACAAAAEludDY0T3V0AAgBAAAAAQAAAAAAAAAAAQAqAQEcAAAACQAAAFVJbnQ2NE91dAAJ" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGwAAAAgAAABGbG9hdE91dAAKAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAkA" +
           "AABEb3VibGVPdXQACwEAAAABAAAAAAAAAAABACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAA" +
           "AQAMAAAAQXJyYXlNZXRob2QyAQF6BwAvAQF6B3oHAAABAf////8CAAAAF2CpCgIAAAAAAA4AAABJbnB1" +
           "dEFyZ3VtZW50cwEBewcALgBEewcAAJYKAAAAAQAqAQEbAAAACAAAAFN0cmluZ0luAAwBAAAAAQAAAAAA" +
           "AAAAAQAqAQEdAAAACgAAAERhdGVUaW1lSW4ADQEAAAABAAAAAAAAAAABACoBARkAAAAGAAAAR3VpZElu" +
           "AA4BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAEJ5dGVTdHJpbmdJbgAPAQAAAAEAAAAAAAAAAAEAKgEB" +
           "HwAAAAwAAABYbWxFbGVtZW50SW4AEAEAAAABAAAAAAAAAAABACoBARsAAAAIAAAATm9kZUlkSW4AEQEA" +
           "AAABAAAAAAAAAAABACoBASMAAAAQAAAARXhwYW5kZWROb2RlSWRJbgASAQAAAAEAAAAAAAAAAAEAKgEB" +
           "IgAAAA8AAABRdWFsaWZpZWROYW1lSW4AFAEAAAABAAAAAAAAAAABACoBASIAAAAPAAAATG9jYWxpemVk" +
           "VGV4dEluABUBAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAFN0YXR1c0NvZGVJbgATAQAAAAEAAAAAAAAA" +
           "AAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAXwH" +
           "AC4ARHwHAACWCgAAAAEAKgEBHAAAAAkAAABTdHJpbmdPdXQADAEAAAABAAAAAAAAAAABACoBAR4AAAAL" +
           "AAAARGF0ZVRpbWVPdXQADQEAAAABAAAAAAAAAAABACoBARoAAAAHAAAAR3VpZE91dAAOAQAAAAEAAAAA" +
           "AAAAAAEAKgEBIAAAAA0AAABCeXRlU3RyaW5nT3V0AA8BAAAAAQAAAAAAAAAAAQAqAQEgAAAADQAAAFht" +
           "bEVsZW1lbnRPdXQAEAEAAAABAAAAAAAAAAABACoBARwAAAAJAAAATm9kZUlkT3V0ABEBAAAAAQAAAAAA" +
           "AAAAAQAqAQEkAAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0ABIBAAAAAQAAAAAAAAAAAQAqAQEjAAAAEAAA" +
           "AFF1YWxpZmllZE5hbWVPdXQAFAEAAAABAAAAAAAAAAABACoBASMAAAAQAAAATG9jYWxpemVkVGV4dE91" +
           "dAAVAQAAAAEAAAAAAAAAAAEAKgEBIAAAAA0AAABTdGF0dXNDb2RlT3V0ABMBAAAAAQAAAAAAAAAAAQAo" +
           "AQEAAAABAAAAAAAAAAEB/////wAAAAAEYYIKBAAAAAEADAAAAEFycmF5TWV0aG9kMwEBfQcALwEBfQd9" +
           "BwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAX4HAC4ARH4HAACWAwAAAAEA" +
           "KgEBHAAAAAkAAABWYXJpYW50SW4AGAEAAAABAAAAAAAAAAABACoBASAAAAANAAAARW51bWVyYXRpb25J" +
           "bgAdAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAsAAABTdHJ1Y3R1cmVJbgAWAQAAAAEAAAAAAAAAAAEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAX8HAC4ARH8H" +
           "AACWAwAAAAEAKgEBHQAAAAoAAABWYXJpYW50T3V0ABgBAAAAAQAAAAAAAAAAAQAqAQEhAAAADgAAAEVu" +
           "dW1lcmF0aW9uT3V0AB0BAAAAAQAAAAAAAAAAAQAqAQEfAAAADAAAAFN0cnVjdHVyZU91dAAWAQAAAAEA" +
           "AAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQAAAABABEAAABVc2VyU2NhbGFyTWV0" +
           "aG9kMQEBgAcALwEBgAeABwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAYEH" +
           "AC4ARIEHAACWDAAAAAEAKgEBGgAAAAkAAABCb29sZWFuSW4BAZgG/////wAAAAAAAQAqAQEYAAAABwAA" +
           "AFNCeXRlSW4BAZkG/////wAAAAAAAQAqAQEXAAAABgAAAEJ5dGVJbgEBmgb/////AAAAAAABACoBARgA" +
           "AAAHAAAASW50MTZJbgEBmwb/////AAAAAAABACoBARkAAAAIAAAAVUludDE2SW4BAZwG/////wAAAAAA" +
           "AQAqAQEYAAAABwAAAEludDMySW4BAZ0G/////wAAAAAAAQAqAQEZAAAACAAAAFVJbnQzMkluAQGeBv//" +
           "//8AAAAAAAEAKgEBGAAAAAcAAABJbnQ2NEluAQGfBv////8AAAAAAAEAKgEBGQAAAAgAAABVSW50NjRJ" +
           "bgEBoAb/////AAAAAAABACoBARgAAAAHAAAARmxvYXRJbgEBoQb/////AAAAAAABACoBARkAAAAIAAAA" +
           "RG91YmxlSW4BAaIG/////wAAAAAAAQAqAQEZAAAACAAAAFN0cmluZ0luAQGjBv////8AAAAAAAEAKAEB" +
           "AAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAYIHAC4ARIIH" +
           "AACWDAAAAAEAKgEBGwAAAAoAAABCb29sZWFuT3V0AQGYBv////8AAAAAAAEAKgEBGQAAAAgAAABTQnl0" +
           "ZU91dAEBmQb/////AAAAAAABACoBARgAAAAHAAAAQnl0ZU91dAEBmgb/////AAAAAAABACoBARkAAAAI" +
           "AAAASW50MTZPdXQBAZsG/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQxNk91dAEBnAb/////AAAAAAAB" +
           "ACoBARkAAAAIAAAASW50MzJPdXQBAZ0G/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQzMk91dAEBngb/" +
           "////AAAAAAABACoBARkAAAAIAAAASW50NjRPdXQBAZ8G/////wAAAAAAAQAqAQEaAAAACQAAAFVJbnQ2" +
           "NE91dAEBoAb/////AAAAAAABACoBARkAAAAIAAAARmxvYXRPdXQBAaEG/////wAAAAAAAQAqAQEaAAAA" +
           "CQAAAERvdWJsZU91dAEBogb/////AAAAAAABACoBARoAAAAJAAAAU3RyaW5nT3V0AQGjBv////8AAAAA" +
           "AAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQAAAABABEAAABVc2VyU2NhbGFyTWV0aG9kMgEB" +
           "gwcALwEBgweDBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAYQHAC4ARIQH" +
           "AACWCgAAAAEAKgEBGwAAAAoAAABEYXRlVGltZUluAQGkBv////8AAAAAAAEAKgEBFwAAAAYAAABHdWlk" +
           "SW4BAaUG/////wAAAAAAAQAqAQEdAAAADAAAAEJ5dGVTdHJpbmdJbgEBpgb/////AAAAAAABACoBAR0A" +
           "AAAMAAAAWG1sRWxlbWVudEluAQGnBv////8AAAAAAAEAKgEBGQAAAAgAAABOb2RlSWRJbgEBqAb/////" +
           "AAAAAAABACoBASEAAAAQAAAARXhwYW5kZWROb2RlSWRJbgEBqQb/////AAAAAAABACoBASAAAAAPAAAA" +
           "UXVhbGlmaWVkTmFtZUluAQGqBv////8AAAAAAAEAKgEBIAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAasG" +
           "/////wAAAAAAAQAqAQEdAAAADAAAAFN0YXR1c0NvZGVJbgEBrAb/////AAAAAAABACoBARoAAAAJAAAA" +
           "VmFyaWFudEluAQGtBv////8AAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIAAAAAAA8A" +
           "AABPdXRwdXRBcmd1bWVudHMBAYUHAC4ARIUHAACWCgAAAAEAKgEBHAAAAAsAAABEYXRlVGltZU91dAEB" +
           "pAb/////AAAAAAABACoBARgAAAAHAAAAR3VpZE91dAEBpQb/////AAAAAAABACoBAR4AAAANAAAAQnl0" +
           "ZVN0cmluZ091dAEBpgb/////AAAAAAABACoBAR4AAAANAAAAWG1sRWxlbWVudE91dAEBpwb/////AAAA" +
           "AAABACoBARoAAAAJAAAATm9kZUlkT3V0AQGoBv////8AAAAAAAEAKgEBIgAAABEAAABFeHBhbmRlZE5v" +
           "ZGVJZE91dAEBqQb/////AAAAAAABACoBASEAAAAQAAAAUXVhbGlmaWVkTmFtZU91dAEBqgb/////AAAA" +
           "AAABACoBASEAAAAQAAAATG9jYWxpemVkVGV4dE91dAEBqwb/////AAAAAAABACoBAR4AAAANAAAAU3Rh" +
           "dHVzQ29kZU91dAEBrAb/////AAAAAAABACoBARsAAAAKAAAAVmFyaWFudE91dAEBrQb/////AAAAAAAB" +
           "ACgBAQAAAAEAAAAAAAAAAQH/////AAAAAARhggoEAAAAAQAQAAAAVXNlckFycmF5TWV0aG9kMQEBhgcA" +
           "LwEBhgeGBwAAAQH/////AgAAABdgqQoCAAAAAAAOAAAASW5wdXRBcmd1bWVudHMBAYcHAC4ARIcHAACW" +
           "DAAAAAEAKgEBHgAAAAkAAABCb29sZWFuSW4BAZgGAQAAAAEAAAAAAAAAAAEAKgEBHAAAAAcAAABTQnl0" +
           "ZUluAQGZBgEAAAABAAAAAAAAAAABACoBARsAAAAGAAAAQnl0ZUluAQGaBgEAAAABAAAAAAAAAAABACoB" +
           "ARwAAAAHAAAASW50MTZJbgEBmwYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAFVJbnQxNkluAQGcBgEA" +
           "AAABAAAAAAAAAAABACoBARwAAAAHAAAASW50MzJJbgEBnQYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAA" +
           "AFVJbnQzMkluAQGeBgEAAAABAAAAAAAAAAABACoBARwAAAAHAAAASW50NjRJbgEBnwYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEdAAAACAAAAFVJbnQ2NEluAQGgBgEAAAABAAAAAAAAAAABACoBARwAAAAHAAAARmxvYXRJ" +
           "bgEBoQYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAERvdWJsZUluAQGiBgEAAAABAAAAAAAAAAABACoB" +
           "AR0AAAAIAAAAU3RyaW5nSW4BAaMGAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAA" +
           "F2CpCgIAAAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAYgHAC4ARIgHAACWDAAAAAEAKgEBHwAAAAoAAABC" +
           "b29sZWFuT3V0AQGYBgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAAU0J5dGVPdXQBAZkGAQAAAAEAAAAA" +
           "AAAAAAEAKgEBHAAAAAcAAABCeXRlT3V0AQGaBgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAASW50MTZP" +
           "dXQBAZsGAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkAAABVSW50MTZPdXQBAZwGAQAAAAEAAAAAAAAAAAEA" +
           "KgEBHQAAAAgAAABJbnQzMk91dAEBnQYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAFVJbnQzMk91dAEB" +
           "ngYBAAAAAQAAAAAAAAAAAQAqAQEdAAAACAAAAEludDY0T3V0AQGfBgEAAAABAAAAAAAAAAABACoBAR4A" +
           "AAAJAAAAVUludDY0T3V0AQGgBgEAAAABAAAAAAAAAAABACoBAR0AAAAIAAAARmxvYXRPdXQBAaEGAQAA" +
           "AAEAAAAAAAAAAAEAKgEBHgAAAAkAAABEb3VibGVPdXQBAaIGAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkA" +
           "AABTdHJpbmdPdXQBAaMGAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAABGGCCgQA" +
           "AAABABAAAABVc2VyQXJyYXlNZXRob2QyAQGJBwAvAQGJB4kHAAABAf////8CAAAAF2CpCgIAAAAAAA4A" +
           "AABJbnB1dEFyZ3VtZW50cwEBigcALgBEigcAAJYKAAAAAQAqAQEfAAAACgAAAERhdGVUaW1lSW4BAaQG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBGwAAAAYAAABHdWlkSW4BAaUGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwA" +
           "AABCeXRlU3RyaW5nSW4BAaYGAQAAAAEAAAAAAAAAAAEAKgEBIQAAAAwAAABYbWxFbGVtZW50SW4BAacG" +
           "AQAAAAEAAAAAAAAAAAEAKgEBHQAAAAgAAABOb2RlSWRJbgEBqAYBAAAAAQAAAAAAAAAAAQAqAQElAAAA" +
           "EAAAAEV4cGFuZGVkTm9kZUlkSW4BAakGAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABRdWFsaWZpZWRO" +
           "YW1lSW4BAaoGAQAAAAEAAAAAAAAAAAEAKgEBJAAAAA8AAABMb2NhbGl6ZWRUZXh0SW4BAasGAQAAAAEA" +
           "AAAAAAAAAAEAKgEBIQAAAAwAAABTdGF0dXNDb2RlSW4BAawGAQAAAAEAAAAAAAAAAAEAKgEBHgAAAAkA" +
           "AABWYXJpYW50SW4BAa0GAQAAAAEAAAAAAAAAAAEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAF2CpCgIA" +
           "AAAAAA8AAABPdXRwdXRBcmd1bWVudHMBAYsHAC4ARIsHAACWCgAAAAEAKgEBIAAAAAsAAABEYXRlVGlt" +
           "ZU91dAEBpAYBAAAAAQAAAAAAAAAAAQAqAQEcAAAABwAAAEd1aWRPdXQBAaUGAQAAAAEAAAAAAAAAAAEA" +
           "KgEBIgAAAA0AAABCeXRlU3RyaW5nT3V0AQGmBgEAAAABAAAAAAAAAAABACoBASIAAAANAAAAWG1sRWxl" +
           "bWVudE91dAEBpwYBAAAAAQAAAAAAAAAAAQAqAQEeAAAACQAAAE5vZGVJZE91dAEBqAYBAAAAAQAAAAAA" +
           "AAAAAQAqAQEmAAAAEQAAAEV4cGFuZGVkTm9kZUlkT3V0AQGpBgEAAAABAAAAAAAAAAABACoBASUAAAAQ" +
           "AAAAUXVhbGlmaWVkTmFtZU91dAEBqgYBAAAAAQAAAAAAAAAAAQAqAQElAAAAEAAAAExvY2FsaXplZFRl" +
           "eHRPdXQBAasGAQAAAAEAAAAAAAAAAAEAKgEBIgAAAA0AAABTdGF0dXNDb2RlT3V0AQGsBgEAAAABAAAA" +
           "AAAAAAABACoBAR8AAAAKAAAAVmFyaWFudE91dAEBrQYBAAAAAQAAAAAAAAAAAQAoAQEAAAABAAAAAAAA" +
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
           "Q29uZGl0aW9uVHlwZUluc3RhbmNlAQGMBwEBjAeMBwAA/////xYAAAAVYIkKAgAAAAAABwAAAEV2ZW50" +
           "SWQBAY0HAC4ARI0HAAAAD/////8BAf////8AAAAAFWCJCgIAAAAAAAkAAABFdmVudFR5cGUBAY4HAC4A" +
           "RI4HAAAAEf////8BAf////8AAAAAFWCJCgIAAAAAAAoAAABTb3VyY2VOb2RlAQGPBwAuAESPBwAAABH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAKAAAAU291cmNlTmFtZQEBkAcALgBEkAcAAAAM/////wEB////" +
           "/wAAAAAVYIkKAgAAAAAABAAAAFRpbWUBAZEHAC4ARJEHAAABACYB/////wEB/////wAAAAAVYIkKAgAA" +
           "AAAACwAAAFJlY2VpdmVUaW1lAQGSBwAuAESSBwAAAQAmAf////8BAf////8AAAAAFWCJCgIAAAAAAAcA" +
           "AABNZXNzYWdlAQGUBwAuAESUBwAAABX/////AQH/////AAAAABVgiQoCAAAAAAAIAAAAU2V2ZXJpdHkB" +
           "AZUHAC4ARJUHAAAABf////8BAf////8AAAAAFWCJCgIAAAAAABAAAABDb25kaXRpb25DbGFzc0lkAQGW" +
           "BwAuAESWBwAAABH/////AQH/////AAAAABVgiQoCAAAAAAASAAAAQ29uZGl0aW9uQ2xhc3NOYW1lAQGX" +
           "BwAuAESXBwAAABX/////AQH/////AAAAABVgiQoCAAAAAAANAAAAQ29uZGl0aW9uTmFtZQEBmgcALgBE" +
           "mgcAAAAM/////wEB/////wAAAAAVYIkKAgAAAAAACAAAAEJyYW5jaElkAQGbBwAuAESbBwAAABH/////" +
           "AQH/////AAAAABVgiQoCAAAAAAAGAAAAUmV0YWluAQGcBwAuAEScBwAAAAH/////AQH/////AAAAABVg" +
           "iQoCAAAAAAAMAAAARW5hYmxlZFN0YXRlAQGdBwAvAQAjI50HAAAAFf////8BAf////8BAAAAFWCJCgIA" +
           "AAAAAAIAAABJZAEBngcALgBEngcAAAAB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAFF1YWxpdHkB" +
           "AaYHAC8BACojpgcAAAAT/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEB" +
           "pwcALgBEpwcAAAEAJgH/////AQH/////AAAAABVgiQoCAAAAAAAMAAAATGFzdFNldmVyaXR5AQGoBwAv" +
           "AQAqI6gHAAAABf////8BAf////8BAAAAFWCJCgIAAAAAAA8AAABTb3VyY2VUaW1lc3RhbXABAakHAC4A" +
           "RKkHAAABACYB/////wEB/////wAAAAAVYIkKAgAAAAAABwAAAENvbW1lbnQBAaoHAC8BACojqgcAAAAV" +
           "/////wEB/////wEAAAAVYIkKAgAAAAAADwAAAFNvdXJjZVRpbWVzdGFtcAEBqwcALgBEqwcAAAEAJgH/" +
           "////AQH/////AAAAABVgiQoCAAAAAAAMAAAAQ2xpZW50VXNlcklkAQGsBwAuAESsBwAAAAz/////AQH/" +
           "////AAAAAARhggoEAAAAAAAHAAAARGlzYWJsZQEBrQcALwEARCOtBwAAAQEBAAAAAQD5CwABAPMKAAAA" +
           "AARhggoEAAAAAAAGAAAARW5hYmxlAQGuBwAvAQBDI64HAAABAQEAAAABAPkLAAEA8woAAAAABGGCCgQA" +
           "AAAAAAoAAABBZGRDb21tZW50AQGvBwAvAQBFI68HAAABAQEAAAABAPkLAAEADQsBAAAAF2CpCgIAAAAA" +
           "AA4AAABJbnB1dEFyZ3VtZW50cwEBsAcALgBEsAcAAJYCAAAAAQAqAQFGAAAABwAAAEV2ZW50SWQAD///" +
           "//8AAAAAAwAAAAAoAAAAVGhlIGlkZW50aWZpZXIgZm9yIHRoZSBldmVudCB0byBjb21tZW50LgEAKgEB" +
           "QgAAAAcAAABDb21tZW50ABX/////AAAAAAMAAAAAJAAAAFRoZSBjb21tZW50IHRvIGFkZCB0byB0aGUg" +
           "Y29uZGl0aW9uLgEAKAEBAAAAAQAAAAAAAAABAf////8AAAAAFWCJCgIAAAABABIAAABNb25pdG9yZWRO" +
           "b2RlQ291bnQBAbUHAC4ARLUHAAAABv////8BAf////8AAAAA";
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