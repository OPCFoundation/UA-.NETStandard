using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Opc.Ua.Server.Tests
{
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    public class MonitoredItemBenchmarks
    {
        private DataValue m_valueDouble;
        private DataValue m_lastValueDouble;
        private DataValue m_valueFloat;
        private DataValue m_lastValueFloat;
        private DataValue m_valueArrayDouble;
        private DataValue m_lastValueArrayDouble;
        private DataChangeFilter m_filter;
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
    }
}
