/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// <see cref="UaSCUaBinaryChannel.Dispose()"/> runs the dispose overrides
    /// once. The listener's Dispose and ChannelClosed on the receive thread
    /// can dispose the same channel concurrently, and the overrides release
    /// certificates that must not be released twice.
    /// </summary>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class UaSCBinaryChannelDisposeTests
    {
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_buffers = null!;
        private ChannelQuotas m_quotas = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_buffers = new BufferManager("dispose-test", 8192, m_telemetry);
            m_quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry));
        }

        [Test]
        public void DisposeTwiceRunsTheOverrideOnce()
        {
            var channel = new CountingChannel(m_buffers, m_quotas, m_telemetry, blockFirstDispose: false);

            channel.Dispose();
            channel.Dispose();

            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A second Dispose while the first is still inside the override
        /// returns without running the override again.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposeRunsTheOverrideOnceAsync()
        {
            var channel = new CountingChannel(m_buffers, m_quotas, m_telemetry, blockFirstDispose: true);

            Task first = Task.Run(channel.Dispose);
            Assert.That(channel.FirstDisposeEntered.Wait(s_timeout), Is.True, "the first Dispose did not start");

            Task second = Task.Run(channel.Dispose);
            Task completed = await Task.WhenAny(second, Task.Delay(s_timeout)).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(second), "the second Dispose waited for or re-ran the override");

            channel.ReleaseFirstDispose.Set();
            await first.ConfigureAwait(false);

            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        private sealed class CountingChannel : UaSCUaBinaryChannel
        {
            private readonly bool m_blockFirstDispose;
            private int m_disposeCount;

            public CountingChannel(
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry,
                bool blockFirstDispose)
                : base(
                    "dispose-test",
                    bufferManager,
                    quotas,
                    (Certificate?)null,
                    [],
                    MessageSecurityMode.None,
                    SecurityPolicies.None,
                    telemetry)
            {
                m_blockFirstDispose = blockFirstDispose;
            }

            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public ManualResetEventSlim FirstDisposeEntered { get; } = new();

            public ManualResetEventSlim ReleaseFirstDispose { get; } = new();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // Only the first call blocks, so an unguarded second call
                    // fails the count instead of hanging the test.
                    if (Interlocked.Increment(ref m_disposeCount) == 1 && m_blockFirstDispose)
                    {
                        FirstDisposeEntered.Set();
                        ReleaseFirstDispose.Wait(s_timeout);
                    }
                }
                base.Dispose(disposing);
            }
        }
    }
}
