using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Moq;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [MemoryDiagnoser]
    public class MonitoredItemBenchmarks
    {
        private DataValue m_valueDouble;
        private DataValue m_lastValueDouble;
        private DataValue m_valueFloat;
        private DataValue m_lastValueFloat;
        private DataValue m_valueArrayDouble;
        private DataValue m_lastValueArrayDouble;
        private DataChangeFilter m_filter;
        private MonitoredItem m_monitoredItem;
        private MonitoredItemQueueFactory m_queueFactory;
        private BaseEventState m_event1;
        private BaseEventState m_event2;
        private readonly double m_range = 100.0;
        private const int kIterations = 10000;
        private static readonly double[] s_value = [1.0, 2.0, 3.0, 4.0, 5.0];
        private static readonly double[] s_valueArray = [1.0, 2.0, 3.0, 4.0, 5.0];

        [GlobalSetup]
        public void Setup()
        {
            m_valueDouble = new DataValue(new Variant(10.0));
            m_lastValueDouble = new DataValue(new Variant(10.0));
            m_valueFloat = new DataValue(new Variant(10.0f));
            m_lastValueFloat = new DataValue(new Variant(10.0f));
            m_valueArrayDouble = new DataValue(new Variant(s_value));
            m_lastValueArrayDouble = new DataValue(new Variant(s_valueArray));
            m_filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue
            };

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var eventFilter = new EventFilter
            {
                SelectClauses = [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventId)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.EventType)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.SourceNode)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.SourceName)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.Time)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.ReceiveTime)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.Message)],
                        AttributeId = Attributes.Value
                    },
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [new QualifiedName(BrowseNames.Severity)],
                        AttributeId = Attributes.Value
                    }
                ],

                WhereClause = new ContentFilter
                {
                    Elements = [
                        new ContentFilterElement
                        {
                            FilterOperator = FilterOperator.GreaterThanOrEqual,
                            FilterOperands = [
                                new ExtensionObject(new SimpleAttributeOperand
                                {
                                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                    BrowsePath = [new QualifiedName(BrowseNames.Severity)],
                                    AttributeId = Attributes.Value
                                }),
                                new ExtensionObject(new LiteralOperand { Value = Variant.From((ushort)100) })
                            ]
                        }
                    ]
                }
            };

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            m_queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactory);

            var nodeManagerMock = new Mock<INodeManager>();

            m_monitoredItem = new MonitoredItem(
                serverMock.Object,
                nodeManagerMock.Object,
                null,
                1,
                2,
                new ReadValueId { NodeId = ObjectIds.Server },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                eventFilter,
                eventFilter,
                null,
                1000.0,
                1,
                true,
                1000);

            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serverMock.Object.NamespaceUris
            };
            m_event1 = new BaseEventState(null);
            m_event1.Initialize(systemContext, null, EventSeverity.Medium, new LocalizedText("Event 1"));
            m_event1.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
            m_event1.SetChildValue(systemContext, BrowseNames.SourceName, "Internal 1", false);

            m_event2 = new BaseEventState(null);
            m_event2.Initialize(systemContext, null, EventSeverity.High, new LocalizedText("Event 2"));
            m_event2.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
            m_event2.SetChildValue(systemContext, BrowseNames.SourceName, "Internal 2", false);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            m_monitoredItem?.Dispose();
            m_event1?.Dispose();
            m_event2?.Dispose();
            m_queueFactory?.Dispose();
        }

        [Benchmark]
        [BenchmarkCategory("Double")]
        public void ValueChangedDoubleSame()
        {
            for (int i = 0; i < kIterations; i++)
            {
                MonitoredItem.ValueChanged(m_valueDouble, null, m_lastValueDouble, null, m_filter, m_range);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Float")]
        public void ValueChangedFloatSame()
        {
            for (int i = 0; i < kIterations; i++)
            {
                MonitoredItem.ValueChanged(m_valueFloat, null, m_lastValueFloat, null, m_filter, m_range);
            }
        }

        [Benchmark]
        [BenchmarkCategory("DoubleArray")]
        public void ValueChangedDoubleArraySame()
        {
            for (int i = 0; i < kIterations; i++)
            {
                MonitoredItem.ValueChanged(m_valueArrayDouble, null, m_lastValueArrayDouble, null, m_filter, m_range);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Event")]
        public void QueueEvent()
        {
            for (int i = 0; i < kIterations; i++)
            {
                m_monitoredItem.QueueEvent(i % 2 == 0 ? m_event1 : m_event2);
            }
        }
    }
}
