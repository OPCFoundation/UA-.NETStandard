/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Tcp
{
    /// <summary>
    /// Measures what the channel pays to serialise its state, in isolation from
    /// the network.
    /// </summary>
    /// <remarks>
    /// The gate replaced a monitor the channel took on every message, so the
    /// question this answers is whether that replacement costs throughput.
    /// <see cref="MonitorEnterExit"/> is the baseline: it is the construct that
    /// was there before.
    /// <para>
    /// Read the numbers against
    /// <c>SymmetricChannelCryptoBenchmarks.EncryptSignThenDecryptVerify</c>,
    /// which is the per-message work the gate is taken around. A cost that does
    /// not show up beside the cryptography is not a throughput regression.
    /// </para>
    /// <para>
    /// The <c>[Benchmark]</c> methods deliberately carry no assertions and
    /// return a value instead: an NUnit constraint allocates several hundred
    /// bytes, which at these magnitudes would be most of what was measured. The
    /// <c>[Test]</c> methods below assert the same code so it stays correct and
    /// compiled even when nobody runs the benchmark.
    /// </para>
    /// <para>
    /// Run with:
    /// <c>dotnet run -c Release -f net10.0 -- --filter '*ChannelGateBenchmarks*' --buildTimeout 900</c>
    /// (the default two-minute build timeout is too short for this solution).
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ChannelGate")]
    [Category("Benchmark")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [MemoryDiagnoser]
    [BenchmarkCategory("ChannelGate")]
    [NonParallelizable]
    public class ChannelGateBenchmarks
    {
        [SetUp]
        [GlobalSetup]
        public void Setup()
        {
            m_gate = new ChannelGate();
            m_semaphore = new SemaphoreSlim(1, 1);
        }

        [TearDown]
        [GlobalCleanup]
        public void Cleanup()
        {
            m_semaphore?.Dispose();
            m_semaphore = null!;
        }

        /// <summary>
        /// The construct the gate replaced, measured on the same machine.
        /// </summary>
        /// <returns>A value, so the body cannot be optimised away.</returns>
        [Benchmark(Baseline = true)]
        public int MonitorEnterExit()
        {
            lock (m_monitor)
            {
                return ++m_sink;
            }
        }

        /// <summary>
        /// A bare semaphore, which is what the gate now is. The difference
        /// between this and <see cref="GateEnterLeave"/> is what the wrapper
        /// costs.
        /// </summary>
        /// <returns>A value, so the body cannot be optimised away.</returns>
        [Benchmark]
        public int SemaphoreWaitRelease()
        {
            m_semaphore.Wait();
            try
            {
                return ++m_sink;
            }
            finally
            {
                m_semaphore.Release();
            }
        }

        /// <summary>
        /// The uncontended synchronous acquisition, which is what every path
        /// that does not await pays.
        /// </summary>
        /// <returns>A value, so the body cannot be optimised away.</returns>
        [Benchmark]
        public int GateEnterLeave()
        {
            using (m_gate.Enter())
            {
                return ++m_sink;
            }
        }

        /// <summary>
        /// The uncontended asynchronous acquisition, which is what the receive
        /// loop pays per chunk.
        /// </summary>
        /// <returns>A value, so the body cannot be optimised away.</returns>
        /// <remarks>
        /// This is the number that matters most: it is on the per-message path.
        /// It completes synchronously in the default software configuration, so
        /// what is measured beyond <see cref="GateEnterLeave"/> is the state
        /// machine this method itself needs, not a suspension.
        /// </remarks>
        [Benchmark]
        public async Task<int> GateEnterAsyncUncontendedAsync()
        {
            using (await m_gate.EnterAsync().ConfigureAwait(false))
            {
                return ++m_sink;
            }
        }

        [Test]
        public void GateReleasesOnEveryMeasuredPath()
        {
            Assert.Multiple(() =>
            {
                Assert.That(MonitorEnterExit(), Is.GreaterThan(0));
                Assert.That(SemaphoreWaitRelease(), Is.GreaterThan(0));
                Assert.That(GateEnterLeave(), Is.GreaterThan(0));
                Assert.That(m_gate.IsHeldBySomeContextForTest, Is.False);
            });
        }

        /// <summary>
        /// Confirms the uncontended asynchronous acquisition does not suspend,
        /// which is what keeps the channel's sequencing identical to the monitor
        /// it replaced.
        /// </summary>
        [Test]
        public async Task GateEnterAsyncCompletesSynchronouslyWhenUncontendedAsync()
        {
            ValueTask<ChannelGate.Releaser> pending = m_gate.EnterAsync();

            bool completedSynchronously = pending.IsCompleted;

            using (await pending.ConfigureAwait(false))
            {
                Assert.That(completedSynchronously, Is.True);
            }

            Assert.That(
                await GateEnterAsyncUncontendedAsync().ConfigureAwait(false),
                Is.GreaterThan(0));
            Assert.That(m_gate.IsHeldBySomeContextForTest, Is.False);
        }

        private ChannelGate m_gate = new();
        private SemaphoreSlim m_semaphore = new(1, 1);
        private readonly System.Threading.Lock m_monitor = new();
        private int m_sink;
    }
}
