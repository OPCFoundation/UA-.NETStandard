using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for concurrency issues in BaseVariableState class
    /// </summary>
    [TestFixture]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Category("NodeStateConcurrency")]
    [Parallelizable]
    public class NodeStateHandlerConcurrencyTests
    {

        // This data tests all handlers in NodeState and BaseVariableState class
        public static IEnumerable<TestCaseData> VariableHandlerTestCases
        {
            get
            {
                // Test OnWriteValue
                Action<BaseVariableState> action = (BaseVariableState state) => {
                    state.OnWriteValue = ValueChangedHandler;
                    state.OnWriteValue = null;
                };
                yield return new TestCaseData(Attributes.Value, new Variant(15.5), action);

                // Test OnSimpleWriteValue
                action = (BaseVariableState state) => {
                    state.OnSimpleWriteValue = NodeAttributeEventHandler;
                    state.OnSimpleWriteValue = null;
                };
                yield return new TestCaseData(Attributes.Value, new Variant(15.5), action);

                // Test OnWriteNodeId
                action = (BaseVariableState state) => {
                    state.OnWriteNodeId = NodeAttributeEventHandler;
                    state.OnWriteNodeId = null;
                };
                yield return new TestCaseData(Attributes.NodeId, new Variant(new NodeId(22, 7)), action);

                // Test OnWriteNodeClass
                action = (BaseVariableState state) => {
                    state.OnWriteNodeClass = NodeAttributeEventHandler;
                    state.OnWriteNodeClass = null;
                };
                yield return new TestCaseData(Attributes.NodeClass, new Variant((int)NodeClass.Variable), action);

                // Test OnWriteBrowseName
                action = (BaseVariableState state) => {
                    state.OnWriteBrowseName = NodeAttributeEventHandler;
                    state.OnWriteBrowseName = null;
                };
                yield return new TestCaseData(Attributes.BrowseName, new Variant(new QualifiedName("test")), action);

                // Test OnWriteDisplayName
                action = (BaseVariableState state) => {
                    state.OnWriteDisplayName = NodeAttributeEventHandler;
                    state.OnWriteDisplayName = null;
                };
                yield return new TestCaseData(Attributes.DisplayName, new Variant(new LocalizedText("test")), action);

                // Test OnWriteDescription
                action = (BaseVariableState state) => {
                    state.OnWriteDescription = NodeAttributeEventHandler;
                    state.OnWriteDescription = null;
                };
                yield return new TestCaseData(Attributes.Description, new Variant(new LocalizedText("test")), action);

                // Test OnWriteWriteMask
                action = (BaseVariableState state) => {
                    state.OnWriteWriteMask = NodeAttributeEventHandler;
                    state.OnWriteWriteMask = null;
                };
                yield return new TestCaseData(Attributes.WriteMask, new Variant((uint)AttributeWriteMask.WriteMask | (uint)AttributeWriteMask.UserWriteMask), action);

                // Test OnWriteUserWriteMask
                action = (BaseVariableState state) => {
                    state.OnWriteUserWriteMask = NodeAttributeEventHandler;
                    state.OnWriteUserWriteMask = null;
                };
                yield return new TestCaseData(Attributes.UserWriteMask, new Variant((uint)AttributeWriteMask.WriteMask | (uint)AttributeWriteMask.UserWriteMask), action);

                // Test OnWriteRolePermissions
                action = (BaseVariableState state) => {
                    state.OnWriteRolePermissions = NodeAttributeEventHandler;
                    state.OnWriteRolePermissions = null;
                };
                yield return new TestCaseData(Attributes.RolePermissions, new Variant(new ExtensionObject[] { }), action);

                // Test OnWriteAccessRestrictions
                action = (BaseVariableState state) => {
                    state.OnWriteAccessRestrictions = NodeAttributeEventHandler;
                    state.OnWriteAccessRestrictions = null;
                };
                yield return new TestCaseData(Attributes.AccessRestrictions, new Variant((ushort)AccessRestrictionType.EncryptionRequired), action);

                // Test OnWriteDataType
                action = (BaseVariableState state) => {
                    state.OnWriteDataType = NodeAttributeEventHandler;
                    state.OnWriteDataType = null;
                };
                yield return new TestCaseData(Attributes.DataType, new Variant(DataTypeIds.Double), action);

                // Test OnWriteValueRank
                action = (BaseVariableState state) => {
                    state.OnWriteValueRank = NodeAttributeEventHandler;
                    state.OnWriteValueRank = null;
                };
                yield return new TestCaseData(Attributes.ValueRank, new Variant(ValueRanks.Scalar), action);

                // Test OnWriteArrayDimensions
                action = (BaseVariableState state) => {
                    state.OnWriteArrayDimensions = NodeAttributeEventHandler;
                    state.OnWriteArrayDimensions = null;
                };
                yield return new TestCaseData(Attributes.ArrayDimensions, new Variant(new List<uint>() { 2, 2 }), action);

                // Test OnWriteAccessLevel
                action = (BaseVariableState state) => {
                    state.OnWriteAccessLevel = NodeAttributeEventHandler;
                    state.OnWriteAccessLevel = null;
                };
                yield return new TestCaseData(Attributes.AccessLevel, new Variant(AccessLevels.CurrentRead), action);

                // Test OnWriteUserAccessLevel
                action = (BaseVariableState state) => {
                    state.OnWriteUserAccessLevel = NodeAttributeEventHandler;
                    state.OnWriteUserAccessLevel = null;
                };
                yield return new TestCaseData(Attributes.UserAccessLevel, new Variant(AccessLevels.CurrentRead), action);

                // Test OnWriteMinimumSamplingInterval
                action = (BaseVariableState state) => {
                    state.OnWriteMinimumSamplingInterval = NodeAttributeEventHandler;
                    state.OnWriteMinimumSamplingInterval = null;
                };
                yield return new TestCaseData(Attributes.MinimumSamplingInterval, new Variant(1000.0), action);

                // Test OnWriteHistorizing
                action = (BaseVariableState state) => {
                    state.OnWriteHistorizing = NodeAttributeEventHandler;
                    state.OnWriteHistorizing = null;
                };
                yield return new TestCaseData(Attributes.Historizing, new Variant(true), action);
            }
        }

        // This test data test all handlers in BaseVariableTypeState and BaseTypeState class
        public static IEnumerable<TestCaseData> VariableTypeHandlerTestCases
        {
            get
            {
                // Test OnWriteDataType
                Action<BaseDataVariableTypeState> action = (BaseDataVariableTypeState state) => {
                    state.OnWriteDataType = NodeAttributeEventHandler;
                    state.OnWriteDataType = null;
                };
                yield return new TestCaseData(Attributes.DataType, new Variant(DataTypeIds.Double), action);

                // Test OnWriteValueRank
                action = (BaseDataVariableTypeState state) => {
                    state.OnWriteValueRank = NodeAttributeEventHandler;
                    state.OnWriteValueRank = null;
                };
                yield return new TestCaseData(Attributes.ValueRank, new Variant(ValueRanks.Scalar), action);

                // Test OnWriteArrayDimensions
                action = (BaseDataVariableTypeState state) => {
                    state.OnWriteArrayDimensions = NodeAttributeEventHandler;
                    state.OnWriteArrayDimensions = null;
                };
                yield return new TestCaseData(Attributes.ArrayDimensions, new Variant(new List<uint>() { 2, 2 }), action);

                // Test OnWriteIsAbstract
                action = (BaseDataVariableTypeState state) => {
                    state.OnWriteIsAbstract = NodeAttributeEventHandler;
                    state.OnWriteIsAbstract = null;
                };
                yield return new TestCaseData(Attributes.IsAbstract, new Variant(false), action);
            }
        }

        // This test data test all handlers in BaseObjectState class
        public static IEnumerable<TestCaseData> ObjectHandlerTestCases
        {
            get
            {
                // Test EventNotifier
                Action<BaseObjectState> action = (BaseObjectState state) => {
                    state.OnWriteEventNotifier = NodeAttributeEventHandler;
                    state.OnWriteEventNotifier = null;
                };
                yield return new TestCaseData(Attributes.EventNotifier, new Variant(EventNotifiers.SubscribeToEvents), action);

            }
        }

        // This test data test all handlers in MethodState class
        public static IEnumerable<TestCaseData> MethodHandlerTestCases
        {
            get
            {
                // Test Executable
                Action<MethodState> action = (MethodState state) => {
                    state.OnWriteExecutable = NodeAttributeEventHandler;
                    state.OnWriteExecutable = null;
                };
                yield return new TestCaseData(Attributes.Executable, new Variant(true), action);

                // Test UserExecutable
                action = (MethodState state) => {
                    state.OnWriteUserExecutable = NodeAttributeEventHandler;
                    state.OnWriteUserExecutable = null;
                };
                yield return new TestCaseData(Attributes.UserExecutable, new Variant(true), action);

            }
        }

        // This test data test all handlers in ReferenceTypeState class
        public static IEnumerable<TestCaseData> ReferenceTypeHandlerTestCases
        {
            get
            {
                // Test InverseName
                Action<ReferenceTypeState> action = (ReferenceTypeState state) => {
                    state.OnWriteInverseName = NodeAttributeEventHandler;
                    state.OnWriteInverseName = null;
                };
                yield return new TestCaseData(Attributes.InverseName, new Variant(new LocalizedText("inverse test")), action);

                // Test Symmetric
                action = (ReferenceTypeState state) => {
                    state.OnWriteSymmetric = NodeAttributeEventHandler;
                    state.OnWriteSymmetric = null;
                };
                yield return new TestCaseData(Attributes.Symmetric, new Variant(true), action);

            }
        }

        // This test data test all handlers in ViewState class
        public static IEnumerable<TestCaseData> ViewHandlerTestCases
        {
            get
            {
                // Test EventNotifier
                Action<ViewState> action = (ViewState state) => {
                    state.OnWriteEventNotifier = NodeAttributeEventHandler;
                    state.OnWriteEventNotifier = null;
                };
                yield return new TestCaseData(Attributes.EventNotifier, new Variant(EventNotifiers.SubscribeToEvents), action);

                // Test ContainsNoLoops
                action = (ViewState state) => {
                    state.OnWriteContainsNoLoops = NodeAttributeEventHandler;
                    state.OnWriteContainsNoLoops = null;
                };
                yield return new TestCaseData(Attributes.ContainsNoLoops, new Variant(true), action);

            }
        }

        // This test data test all handlers in DataTypeState class
        public static IEnumerable<TestCaseData> DataTypeHandlerTestCases
        {
            get
            {
                // Test DataTypeDefinition
                Action<DataTypeState> action = (DataTypeState state) => {
                    state.OnWriteDataTypeDefinition = NodeAttributeEventHandler;
                    state.OnWriteDataTypeDefinition = null;
                };
                yield return new TestCaseData(Attributes.DataTypeDefinition, new Variant(new ExtensionObject()), action);

            }
        }

        [TestCaseSource(nameof(VariableHandlerTestCases))]
        [Parallelizable]
        public void VariableNodeHandlerConcurrencyTest(
            uint attribute,
            Variant variant,
            Action<BaseVariableState> concurrentTaskAction)
        {
            var testNodeState = new AnalogUnitRangeState(null);
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            ExecuteNodeHandlerConcurrencyTest(
                systemContext,
                attribute,
                variant,
                testNodeState,
                concurrentTaskAction);

        }

        [TestCaseSource(nameof(VariableTypeHandlerTestCases))]
        [Parallelizable]
        public void VariableTypeNodeHandlerConcurrencyTest(
            uint attribute,
            Variant variant,
            Action<BaseDataVariableTypeState> concurrentTaskAction)
        {
            var testNodeState = new BaseDataVariableTypeState();
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            ExecuteNodeHandlerConcurrencyTest(
                systemContext,
                attribute,
                variant,
                testNodeState,
                concurrentTaskAction);

        }

        [TestCaseSource(nameof(ObjectHandlerTestCases))]
        [Parallelizable]
        public void ObjectNodeHandlerConcurrencyTest(
            uint attribute,
            Variant variant,
            Action<BaseObjectState> concurrentTaskAction)
        {
            var testNodeState = new BaseObjectState(null);
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            ExecuteNodeHandlerConcurrencyTest(
                systemContext,
                attribute,
                variant,
                testNodeState,
                concurrentTaskAction);

        }

        [TestCaseSource(nameof(MethodHandlerTestCases))]
        [Parallelizable]
        public void MethodNodeHandlerConcurrencyTest(
            uint attribute,
            Variant variant,
            Action<MethodState> concurrentTaskAction)
        {
            var testNodeState = new MethodState(null);
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            ExecuteNodeHandlerConcurrencyTest(
                systemContext,
                attribute,
                variant,
                testNodeState,
                concurrentTaskAction);

        }

        [TestCaseSource(nameof(ReferenceTypeHandlerTestCases))]
        [Parallelizable]
        public void ReferenceTypeNodeHandlerConcurrencyTest(
            uint attribute,
            Variant variant,
            Action<ReferenceTypeState> concurrentTaskAction)
        {
            var testNodeState = new ReferenceTypeState();
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            ExecuteNodeHandlerConcurrencyTest(
                systemContext,
                attribute,
                variant,
                testNodeState,
                concurrentTaskAction);

        }

        [TestCaseSource(nameof(ViewHandlerTestCases))]
        [Parallelizable]
        public void ViewNodeHandlerConcurrencyTest(
            uint attribute,
            Variant variant,
            Action<ViewState> concurrentTaskAction)
        {
            var testNodeState = new ViewState();
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            ExecuteNodeHandlerConcurrencyTest(
                systemContext,
                attribute,
                variant,
                testNodeState,
                concurrentTaskAction);

        }

        private void ExecuteNodeHandlerConcurrencyTest<T>(
            ISystemContext systemContext,
            uint attribute,
            Variant variant,
            T node,
            Action<T> concurrentTaskAction) where T : NodeState
        {

            node.WriteMask = AttributeWriteMask.AccessLevel | AttributeWriteMask.ArrayDimensions | AttributeWriteMask.BrowseName | AttributeWriteMask.ContainsNoLoops | AttributeWriteMask.DataType |
                            AttributeWriteMask.Description | AttributeWriteMask.DisplayName | AttributeWriteMask.EventNotifier | AttributeWriteMask.Executable | AttributeWriteMask.Historizing | AttributeWriteMask.InverseName | AttributeWriteMask.IsAbstract |
                            AttributeWriteMask.MinimumSamplingInterval | AttributeWriteMask.NodeClass | AttributeWriteMask.NodeId | AttributeWriteMask.Symmetric | AttributeWriteMask.UserAccessLevel | AttributeWriteMask.UserExecutable |
                            AttributeWriteMask.UserWriteMask | AttributeWriteMask.ValueForVariableType | AttributeWriteMask.ValueRank | AttributeWriteMask.WriteMask | AttributeWriteMask.RolePermissions | AttributeWriteMask.AccessRestrictions;

            if(node is BaseVariableState baseVariableState)
            {
                // Make Value attribute writable so that it is possible to test Value attribute writing
                var result = baseVariableState.WriteAttribute(systemContext, Attributes.AccessLevel, NumericRange.Empty, new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
                Assert.IsTrue(ServiceResult.IsGood(result));
                result = baseVariableState.WriteAttribute(systemContext, Attributes.UserAccessLevel, NumericRange.Empty, new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
                Assert.IsTrue(ServiceResult.IsGood(result));
            }
            
            bool running = true;

            var thread = new Thread(() => {
                while (running)
                {
                    concurrentTaskAction(node);
                }
            });

            thread.Start();

            DateTime utcNow = DateTime.UtcNow;

            while (DateTime.UtcNow - utcNow < TimeSpan.FromSeconds(1))
            {
                var writeResult = node.WriteAttribute(
                                    systemContext,
                                    attribute,
                                    NumericRange.Empty,
                                    new DataValue(variant));

                Assert.IsTrue(ServiceResult.IsGood(writeResult), "Expected Good ServiceResult but was: {0}", writeResult);
            }

            running = false;

            thread.Join();
        }

        private static ServiceResult ValueChangedHandler(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            return ServiceResult.Good;
        }

        private static ServiceResult NodeAttributeEventHandler<T>(
            ISystemContext context,
            NodeState node,
            ref T value)
        {
            return ServiceResult.Good;
        }
    }

}
