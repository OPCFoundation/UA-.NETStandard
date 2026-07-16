/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Microbenchmark for V2 subscription notification pooling.
    /// Compares the unpooled <c>new</c> path against the pooled
    /// <c>CreateInstance() + Reuse()</c> path for each publish-payload
    /// type tagged <c>Poolable="true"</c> in the design XML
    /// (<see cref="MonitoredItemNotification"/>,
    /// <see cref="DataChangeNotification"/>,
    /// <see cref="EventFieldList"/>,
    /// <see cref="EventNotificationList"/>).
    /// </summary>
    /// <remarks>
    /// To run interactively:
    /// <code>
    /// dotnet test tests/Opc.Ua.Client.Tests --filter "FullyQualifiedName~PooledNotificationBenchmarks"
    /// </code>
    /// To run as a BenchmarkDotNet harness, invoke the test fixture's
    /// <c>[GlobalSetup]</c>/<c>[Benchmark]</c> via the standard BDN
    /// runner. The <c>[Test]</c> attributes also let the suite double
    /// as a smoke test under the unit-test runner.
    /// </remarks>
    [TestFixture]
    [MemoryDiagnoser]
    [Category("PooledNotificationBenchmarks")]
    [NonParallelizable]
    [Config(typeof(InProcessConfig))]
    public class PooledNotificationBenchmarks
    {
        /// <summary>
        /// In-process toolchain config — skips BDN's auto-generated
        /// project rebuild (which times out for a test assembly that
        /// transitively pulls hundreds of references).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by BenchmarkDotNet via reflection")]
        private sealed class InProcessConfig : ManualConfig
        {
            public InProcessConfig()
            {
                AddJob(Job.ShortRun
                    .WithToolchain(InProcessEmitToolchain.Instance)
                    .WithStrategy(RunStrategy.Throughput));
            }
        }

        private const int Iterations = 1000;

        [GlobalSetup]
        [OneTimeSetUp]
        public void Setup()
        {
            // Pre-warm the pools so the first benchmark iteration
            // doesn't pay the cost of populating them.
            for (int i = 0; i < Iterations; i++)
            {
                var m = (MonitoredItemNotification)
                    MonitoredItemNotificationActivator.Instance.CreateInstance();
                m.Reuse();
                var d = (DataChangeNotification)
                    DataChangeNotificationActivator.Instance.CreateInstance();
                d.Reuse();
                var e = (EventFieldList)
                    EventFieldListActivator.Instance.CreateInstance();
                e.Reuse();
                var en = (EventNotificationList)
                    EventNotificationListActivator.Instance.CreateInstance();
                en.Reuse();
            }
        }

        [Benchmark(Baseline = true, Description = "new MonitoredItemNotification()")]
        [Test]
        public void UnpooledMonitoredItemNotification()
        {
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                var item = new MonitoredItemNotification
                {
                    ClientHandle = (uint)i
                };
                sum += (int)item.ClientHandle;
            }
            Assert.That(sum, Is.GreaterThanOrEqualTo(0));
        }

        [Benchmark(Description = "MonitoredItemNotificationActivator + Reuse")]
        [Test]
        public void PooledMonitoredItemNotification()
        {
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                var item = (MonitoredItemNotification)
                    MonitoredItemNotificationActivator.Instance.CreateInstance();
                item.ClientHandle = (uint)i;
                sum += (int)item.ClientHandle;
                item.Reuse();
            }
            Assert.That(sum, Is.GreaterThanOrEqualTo(0));
        }

        [Benchmark(Description = "new DataChangeNotification()")]
        [Test]
        public void UnpooledDataChangeNotification()
        {
            int count = 0;
            for (int i = 0; i < Iterations; i++)
            {
                var notification = new DataChangeNotification
                {
                    MonitoredItems = new MonitoredItemNotification[]
                    {
                        new() { ClientHandle = (uint)i }
                    }
                };
                count += notification.MonitoredItems.Count;
            }
            Assert.That(count, Is.EqualTo(Iterations));
        }

        [Benchmark(Description = "DataChangeNotificationActivator + Reuse")]
        [Test]
        public void PooledDataChangeNotification()
        {
            int count = 0;
            for (int i = 0; i < Iterations; i++)
            {
                var notification = (DataChangeNotification)
                    DataChangeNotificationActivator.Instance.CreateInstance();
                var item = (MonitoredItemNotification)
                    MonitoredItemNotificationActivator.Instance.CreateInstance();
                item.ClientHandle = (uint)i;
                notification.MonitoredItems = new MonitoredItemNotification[] { item };
                count += notification.MonitoredItems.Count;
                item.Reuse();
                notification.Reuse();
            }
            Assert.That(count, Is.EqualTo(Iterations));
        }

        [Benchmark(Description = "new EventFieldList()")]
        [Test]
        public void UnpooledEventFieldList()
        {
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                var item = new EventFieldList
                {
                    ClientHandle = (uint)i
                };
                sum += (int)item.ClientHandle;
            }
            Assert.That(sum, Is.GreaterThanOrEqualTo(0));
        }

        [Benchmark(Description = "EventFieldListActivator + Reuse")]
        [Test]
        public void PooledEventFieldList()
        {
            int sum = 0;
            for (int i = 0; i < Iterations; i++)
            {
                var item = (EventFieldList)
                    EventFieldListActivator.Instance.CreateInstance();
                item.ClientHandle = (uint)i;
                sum += (int)item.ClientHandle;
                item.Reuse();
            }
            Assert.That(sum, Is.GreaterThanOrEqualTo(0));
        }
    }
}
