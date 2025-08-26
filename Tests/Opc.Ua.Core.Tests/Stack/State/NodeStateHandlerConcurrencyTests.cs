using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for concurrency issues in BaseVariableState class
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Category("NodeStateConcurrency")]
    [Parallelizable]
    public class NodeStateHandlerConcurrencyTests
    {
        /// <summary>
        /// This data tests all handlers in NodeState and BaseVariableState class
        /// </summary>
        public static IEnumerable<TestCaseData> VariableHandlerTestCases
        {
            get
            {
                // Test OnWriteValue
                Action<BaseVariableState> action = state =>
                {
                    state.OnWriteValue = ValueChangedHandler;
                    state.OnWriteValue = null;
                };
                yield return new TestCaseData(Attributes.Value, new Variant(15.5), action);

                // Test OnSimpleWriteValue
                action = state =>
                {
                    state.OnSimpleWriteValue = NodeAttributeEventHandler;
                    state.OnSimpleWriteValue = null;
                };
                yield return new TestCaseData(Attributes.Value, new Variant(15.5), action);

                // Test OnWriteNodeId
                action = state =>
                {
                    state.OnWriteNodeId = NodeAttributeEventHandler;
                    state.OnWriteNodeId = null;
                };
                yield return new TestCaseData(
                    Attributes.NodeId,
                    new Variant(new NodeId(22, 7)),
                    action);

                // Test OnWriteNodeClass
                action = state =>
                {
                    state.OnWriteNodeClass = NodeAttributeEventHandler;
                    state.OnWriteNodeClass = null;
                };
                yield return new TestCaseData(
                    Attributes.NodeClass,
                    new Variant((int)NodeClass.Variable),
                    action);

                // Test OnWriteBrowseName
                action = state =>
                {
                    state.OnWriteBrowseName = NodeAttributeEventHandler;
                    state.OnWriteBrowseName = null;
                };
                yield return new TestCaseData(
                    Attributes.BrowseName,
                    new Variant(new QualifiedName("test")),
                    action);

                // Test OnWriteDisplayName
                action = state =>
                {
                    state.OnWriteDisplayName = NodeAttributeEventHandler;
                    state.OnWriteDisplayName = null;
                };
                yield return new TestCaseData(
                    Attributes.DisplayName,
                    new Variant(new LocalizedText("test")),
                    action);

                // Test OnWriteDescription
                action = state =>
                {
                    state.OnWriteDescription = NodeAttributeEventHandler;
                    state.OnWriteDescription = null;
                };
                yield return new TestCaseData(
                    Attributes.Description,
                    new Variant(new LocalizedText("test")),
                    action);

                // Test OnWriteWriteMask
                action = state =>
                {
                    state.OnWriteWriteMask = NodeAttributeEventHandler;
                    state.OnWriteWriteMask = null;
                };
                yield return new TestCaseData(
                    Attributes.WriteMask,
                    new Variant(
                        (uint)AttributeWriteMask.WriteMask |
                        (uint)AttributeWriteMask.UserWriteMask),
                    action);

                // Test OnWriteUserWriteMask
                action = state =>
                {
                    state.OnWriteUserWriteMask = NodeAttributeEventHandler;
                    state.OnWriteUserWriteMask = null;
                };
                yield return new TestCaseData(
                    Attributes.UserWriteMask,
                    new Variant(
                        (uint)AttributeWriteMask.WriteMask |
                        (uint)AttributeWriteMask.UserWriteMask),
                    action);

                // Test OnWriteRolePermissions
                action = state =>
                {
                    state.OnWriteRolePermissions = NodeAttributeEventHandler;
                    state.OnWriteRolePermissions = null;
                };
                yield return new TestCaseData(
                    Attributes.RolePermissions,
                    new Variant(s_value),
                    action);

                // Test OnWriteAccessRestrictions
                action = state =>
                {
                    state.OnWriteAccessRestrictions = NodeAttributeEventHandler;
                    state.OnWriteAccessRestrictions = null;
                };
                yield return new TestCaseData(
                    Attributes.AccessRestrictions,
                    new Variant((ushort)AccessRestrictionType.EncryptionRequired),
                    action);

                // Test OnWriteDataType
                action = state =>
                {
                    state.OnWriteDataType = NodeAttributeEventHandler;
                    state.OnWriteDataType = null;
                };
                yield return new TestCaseData(
                    Attributes.DataType,
                    new Variant(DataTypeIds.Double),
                    action);

                // Test OnWriteValueRank
                action = state =>
                {
                    state.OnWriteValueRank = NodeAttributeEventHandler;
                    state.OnWriteValueRank = null;
                };
                yield return new TestCaseData(
                    Attributes.ValueRank,
                    new Variant(ValueRanks.Scalar),
                    action);

                // Test OnWriteArrayDimensions
                action = state =>
                {
                    state.OnWriteArrayDimensions = NodeAttributeEventHandler;
                    state.OnWriteArrayDimensions = null;
                };
                yield return new TestCaseData(
                    Attributes.ArrayDimensions,
                    new Variant(new List<uint> { 2, 2 }),
                    action);

                // Test OnWriteAccessLevel
                action = state =>
                {
                    state.OnWriteAccessLevel = NodeAttributeEventHandler;
                    state.OnWriteAccessLevel = null;
                };
                yield return new TestCaseData(
                    Attributes.AccessLevel,
                    new Variant(AccessLevels.CurrentRead),
                    action);

                // Test OnWriteUserAccessLevel
                action = state =>
                {
                    state.OnWriteUserAccessLevel = NodeAttributeEventHandler;
                    state.OnWriteUserAccessLevel = null;
                };
                yield return new TestCaseData(
                    Attributes.UserAccessLevel,
                    new Variant(AccessLevels.CurrentRead),
                    action);

                // Test OnWriteMinimumSamplingInterval
                action = state =>
                {
                    state.OnWriteMinimumSamplingInterval = NodeAttributeEventHandler;
                    state.OnWriteMinimumSamplingInterval = null;
                };
                yield return new TestCaseData(
                    Attributes.MinimumSamplingInterval,
                    new Variant(1000.0),
                    action);

                // Test OnWriteHistorizing
                action = state =>
                {
                    state.OnWriteHistorizing = NodeAttributeEventHandler;
                    state.OnWriteHistorizing = null;
                };
                yield return new TestCaseData(Attributes.Historizing, new Variant(true), action);
            }
        }

        /// <summary>
        /// This test data test all handlers in BaseVariableTypeState and BaseTypeState class
        /// </summary>
        public static IEnumerable<TestCaseData> VariableTypeHandlerTestCases
        {
            get
            {
                // Test OnWriteDataType
                Action<BaseDataVariableTypeState> action = state =>
                {
                    state.OnWriteDataType = NodeAttributeEventHandler;
                    state.OnWriteDataType = null;
                };
                yield return new TestCaseData(
                    Attributes.DataType,
                    new Variant(DataTypeIds.Double),
                    action);

                // Test OnWriteValueRank
                action = state =>
                {
                    state.OnWriteValueRank = NodeAttributeEventHandler;
                    state.OnWriteValueRank = null;
                };
                yield return new TestCaseData(
                    Attributes.ValueRank,
                    new Variant(ValueRanks.Scalar),
                    action);

                // Test OnWriteArrayDimensions
                action = state =>
                {
                    state.OnWriteArrayDimensions = NodeAttributeEventHandler;
                    state.OnWriteArrayDimensions = null;
                };
                yield return new TestCaseData(
                    Attributes.ArrayDimensions,
                    new Variant(new List<uint> { 2, 2 }),
                    action);

                // Test OnWriteIsAbstract
                action = state =>
                {
                    state.OnWriteIsAbstract = NodeAttributeEventHandler;
                    state.OnWriteIsAbstract = null;
                };
                yield return new TestCaseData(Attributes.IsAbstract, new Variant(false), action);
            }
        }

        /// <summary>
        /// This test data test all handlers in BaseObjectState class
        /// </summary>
        public static IEnumerable<TestCaseData> ObjectHandlerTestCases
        {
            get
            {
                // Test EventNotifier
                Action<BaseObjectState> action = state =>
                {
                    state.OnWriteEventNotifier = NodeAttributeEventHandler;
                    state.OnWriteEventNotifier = null;
                };
                yield return new TestCaseData(
                    Attributes.EventNotifier,
                    new Variant(EventNotifiers.SubscribeToEvents),
                    action);
            }
        }

        /// <summary>
        /// This test data test all handlers in MethodState class
        /// </summary>
        public static IEnumerable<TestCaseData> MethodHandlerTestCases
        {
            get
            {
                // Test Executable
                Action<MethodState> action = state =>
                {
                    state.OnWriteExecutable = NodeAttributeEventHandler;
                    state.OnWriteExecutable = null;
                };
                yield return new TestCaseData(Attributes.Executable, new Variant(true), action);

                // Test UserExecutable
                action = state =>
                {
                    state.OnWriteUserExecutable = NodeAttributeEventHandler;
                    state.OnWriteUserExecutable = null;
                };
                yield return new TestCaseData(Attributes.UserExecutable, new Variant(true), action);
            }
        }

        /// <summary>
        /// This test data test all handlers in ReferenceTypeState class
        /// </summary>
        public static IEnumerable<TestCaseData> ReferenceTypeHandlerTestCases
        {
            get
            {
                // Test InverseName
                Action<ReferenceTypeState> action = state =>
                {
                    state.OnWriteInverseName = NodeAttributeEventHandler;
                    state.OnWriteInverseName = null;
                };
                yield return new TestCaseData(
                    Attributes.InverseName,
                    new Variant(new LocalizedText("inverse test")),
                    action);

                // Test Symmetric
                action = state =>
                {
                    state.OnWriteSymmetric = NodeAttributeEventHandler;
                    state.OnWriteSymmetric = null;
                };
                yield return new TestCaseData(Attributes.Symmetric, new Variant(true), action);
            }
        }

        /// <summary>
        /// This test data test all handlers in ViewState class
        /// </summary>
        public static IEnumerable<TestCaseData> ViewHandlerTestCases
        {
            get
            {
                // Test EventNotifier
                Action<ViewState> action = state =>
                {
                    state.OnWriteEventNotifier = NodeAttributeEventHandler;
                    state.OnWriteEventNotifier = null;
                };
                yield return new TestCaseData(
                    Attributes.EventNotifier,
                    new Variant(EventNotifiers.SubscribeToEvents),
                    action);

                // Test ContainsNoLoops
                action = state =>
                {
                    state.OnWriteContainsNoLoops = NodeAttributeEventHandler;
                    state.OnWriteContainsNoLoops = null;
                };
                yield return new TestCaseData(
                    Attributes.ContainsNoLoops,
                    new Variant(true),
                    action);
            }
        }

        /// <summary>
        /// This test data test all handlers in DataTypeState class
        /// </summary>
        public static IEnumerable<TestCaseData> DataTypeHandlerTestCases
        {
            get
            {
                // Test DataTypeDefinition
                Action<DataTypeState> action = state =>
                {
                    state.OnWriteDataTypeDefinition = NodeAttributeEventHandler;
                    state.OnWriteDataTypeDefinition = null;
                };
                yield return new TestCaseData(
                    Attributes.DataTypeDefinition,
                    new Variant(new ExtensionObject()),
                    action);
            }
        }

        private static readonly ExtensionObject[] s_value = [];

        [TestCaseSource(nameof(VariableHandlerTestCases))]
        [Parallelizable]
        public void VariableNodeHandlerConcurrencyTest(
            uint attribute,
            Variant variant,
            Action<BaseVariableState> concurrentTaskAction)
        {
            var testNodeState = new AnalogUnitRangeState(null);
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
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

            var systemContext = new SystemContext
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
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

            var systemContext = new SystemContext
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
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

            var systemContext = new SystemContext
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
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

            var systemContext = new SystemContext
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
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

            var systemContext = new SystemContext
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", 7),
                new QualifiedName("TestNode", 7),
                new LocalizedText("TestNode"),
                true);

            ExecuteNodeHandlerConcurrencyTest(
                systemContext,
                attribute,
                variant,
                testNodeState,
                concurrentTaskAction);
        }

        private static void ExecuteNodeHandlerConcurrencyTest<T>(
            ISystemContext systemContext,
            uint attribute,
            Variant variant,
            T node,
            Action<T> concurrentTaskAction)
            where T : NodeState
        {
            node.WriteMask =
                AttributeWriteMask.AccessLevel |
                AttributeWriteMask.ArrayDimensions |
                AttributeWriteMask.BrowseName |
                AttributeWriteMask.ContainsNoLoops |
                AttributeWriteMask.DataType |
                AttributeWriteMask.Description |
                AttributeWriteMask.DisplayName |
                AttributeWriteMask.EventNotifier |
                AttributeWriteMask.Executable |
                AttributeWriteMask.Historizing |
                AttributeWriteMask.InverseName |
                AttributeWriteMask.IsAbstract |
                AttributeWriteMask.MinimumSamplingInterval |
                AttributeWriteMask.NodeClass |
                AttributeWriteMask.NodeId |
                AttributeWriteMask.Symmetric |
                AttributeWriteMask.UserAccessLevel |
                AttributeWriteMask.UserExecutable |
                AttributeWriteMask.UserWriteMask |
                AttributeWriteMask.ValueForVariableType |
                AttributeWriteMask.ValueRank |
                AttributeWriteMask.WriteMask |
                AttributeWriteMask.RolePermissions |
                AttributeWriteMask.AccessRestrictions;

            if (node is BaseVariableState baseVariableState)
            {
                // Make Value attribute writable so that it is possible to test Value attribute writing
                ServiceResult result = baseVariableState.WriteAttribute(
                    systemContext,
                    Attributes.AccessLevel,
                    NumericRange.Empty,
                    new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
                Assert.IsTrue(ServiceResult.IsGood(result));
                result = baseVariableState.WriteAttribute(
                    systemContext,
                    Attributes.UserAccessLevel,
                    NumericRange.Empty,
                    new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
                Assert.IsTrue(ServiceResult.IsGood(result));
            }

            bool running = true;

            var thread = new Thread(() =>
            {
                while (running)
                {
                    concurrentTaskAction(node);
                }
            });

            thread.Start();

            DateTime utcNow = DateTime.UtcNow;

            while (DateTime.UtcNow - utcNow < TimeSpan.FromSeconds(1))
            {
                ServiceResult writeResult = node.WriteAttribute(
                    systemContext,
                    attribute,
                    NumericRange.Empty,
                    new DataValue(variant));

                Assert.IsTrue(
                    ServiceResult.IsGood(writeResult),
                    "Expected Good ServiceResult but was: {0}",
                    writeResult);
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
