// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// The fake consumer is a System.Reflection.DispatchProxy, which .NET Framework lacks.
#if NET
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dekaf.Consumer;
using NUnit.Framework;
using Opc.Ua.PubSub.Kafka.Internal;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Tests that the Dekaf adapter rebuilds a failed consumer instead of
    /// ending consumption, without a broker.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka consumer restart after a consume failure")]
    [CancelAfter(20000)]
    public sealed class DekafConsumerRestartTests
    {
        private static readonly string[] s_topics = ["topic-a", "topic-b"];

        [Test]
        public async Task FailedConsumerIsReplacedAndResubscribedWithSameGroupAsync()
        {
            var failing = new FakeConsumer { FailConsume = true };
            var healthy = new FakeConsumer();
            await using var harness = new Harness(failing, healthy);
            await harness.StartAsync().ConfigureAwait(false);

            await WaitUntilAsync(() => healthy.ConsumeCount == 1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Groups, Has.Count.EqualTo(2));
                Assert.That(harness.Groups[1], Is.EqualTo(harness.Groups[0]), "A rebuild must rejoin the same group.");
                Assert.That(healthy.Subscriptions, Has.Count.EqualTo(1));
                Assert.That(healthy.Subscriptions[0], Is.EquivalentTo(s_topics));
                Assert.That(failing.CloseCount, Is.EqualTo(1));
                Assert.That(failing.DisposeCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task CleanupFailureOfFailedConsumerStillSwitchesToReplacementAsync()
        {
            var failing = new FakeConsumer { FailConsume = true, ThrowOnCleanup = true };
            var healthy = new FakeConsumer();
            await using var harness = new Harness(failing, healthy);
            await harness.StartAsync().ConfigureAwait(false);

            await WaitUntilAsync(() => healthy.ConsumeCount == 1).ConfigureAwait(false);

            Assert.That(failing.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RepeatedRebuildFailuresAreRetriedAsync()
        {
            var failing = new FakeConsumer { FailConsume = true };
            var healthy = new FakeConsumer();
            await using var harness = new Harness(failing, null, null, healthy);
            await harness.StartAsync().ConfigureAwait(false);

            await WaitUntilAsync(() => healthy.ConsumeCount == 1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.BuildCount, Is.EqualTo(4));
                Assert.That(healthy.Subscriptions[0], Is.EquivalentTo(s_topics));
            });
        }

        [Test]
        public async Task DisposeDuringRebuildCompletesAsync()
        {
            var failing = new FakeConsumer { FailConsume = true };
            var harness = new Harness(failing, Harness.Hang);
            await harness.StartAsync().ConfigureAwait(false);
            await WaitUntilAsync(() => harness.BuildCount == 2).ConfigureAwait(false);

            Task dispose = harness.DisposeAsync().AsTask();

            Assert.That(
                await Task.WhenAny(dispose, Task.Delay(5000)).ConfigureAwait(false),
                Is.SameAs(dispose),
                "Shutdown must cancel a rebuild that waits for the broker.");
            await dispose.ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeDuringBackOffCompletesAsync()
        {
            var failing = new FakeConsumer { FailConsume = true };
            var harness = new Harness(failing);
            harness.Adapter.ConsumeRetryDelay = _ => TimeSpan.FromHours(1);
            await harness.StartAsync().ConfigureAwait(false);
            await WaitUntilAsync(() => failing.ConsumeCount == 1).ConfigureAwait(false);

            Task dispose = harness.DisposeAsync().AsTask();

            Assert.That(
                await Task.WhenAny(dispose, Task.Delay(5000)).ConfigureAwait(false),
                Is.SameAs(dispose),
                "Shutdown must cancel the back-off.");
            await dispose.ConfigureAwait(false);
            Assert.That(harness.BuildCount, Is.EqualTo(1));
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Condition was not met within 8 s.");
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adapter wired to a scripted sequence of consumers. A <see langword="null"/>
        /// entry makes that build throw; <see cref="Hang"/> makes it wait for cancellation.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            public static readonly FakeConsumer Hang = new();

            public Harness(params FakeConsumer?[] consumers)
            {
                m_consumers = consumers;
                Adapter = new DekafKafkaClientAdapter(NUnitTelemetryContext.Create(), TimeProvider.System)
                {
                    ConsumeRetryDelay = _ => TimeSpan.FromMilliseconds(10),
                    ConsumerFactory = BuildAsync
                };
            }

            public DekafKafkaClientAdapter Adapter { get; }

            public List<string> Groups { get; } = [];

            public int BuildCount => Volatile.Read(ref m_buildCount);

            public async Task StartAsync()
            {
                await Adapter.ConnectAsync(
                    new KafkaConnectionOptions { Endpoint = KafkaTestHelper.EndpointUrl },
                    CancellationToken.None).ConfigureAwait(false);
                await Adapter.SubscribeAsync(s_topics, CancellationToken.None).ConfigureAwait(false);
            }

            public ValueTask DisposeAsync()
            {
                return Adapter.DisposeAsync();
            }

            private async ValueTask<IKafkaConsumer<byte[], byte[]>> BuildAsync(
                KafkaConnectionOptions options,
                string groupId,
                CancellationToken ct)
            {
                int index = Interlocked.Increment(ref m_buildCount) - 1;
                lock (Groups)
                {
                    Groups.Add(groupId);
                }
                FakeConsumer? next = index < m_consumers.Length ? m_consumers[index] : new FakeConsumer();
                if (ReferenceEquals(next, Hang))
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                if (next is null)
                {
                    throw new InvalidOperationException("Broker unavailable.");
                }
                return next.CreateProxy();
            }

            private readonly FakeConsumer?[] m_consumers;
            private int m_buildCount;
        }

        /// <summary>
        /// Scripted state behind a <see cref="ConsumerProxy"/>.
        /// </summary>
        internal sealed class FakeConsumer
        {
            public bool FailConsume { get; init; }

            public bool ThrowOnCleanup { get; init; }

            public List<string[]> Subscriptions { get; } = [];

            public int ConsumeCount => Volatile.Read(ref m_consumeCount);

            public int CloseCount => Volatile.Read(ref m_closeCount);

            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public IKafkaConsumer<byte[], byte[]> CreateProxy()
            {
                IKafkaConsumer<byte[], byte[]> proxy = DispatchProxy.Create<IKafkaConsumer<byte[], byte[]>, ConsumerProxy>();
                ((ConsumerProxy)(object)proxy).State = this;
                return proxy;
            }

            public object? Invoke(object proxy, MethodInfo method, object?[]? args)
            {
                switch (method.Name)
                {
                    case "Subscribe" when args is { Length: 1 } && args[0] is string[] topics:
                        lock (Subscriptions)
                        {
                            Subscriptions.Add(topics);
                        }
                        break;
                    case "ConsumeAsync":
                        return ConsumeAsync((CancellationToken)args![0]!);
                    case "CloseAsync":
                        Interlocked.Increment(ref m_closeCount);
                        if (ThrowOnCleanup)
                        {
                            throw new InvalidOperationException("Close failed.");
                        }
                        break;
                    case "DisposeAsync":
                        Interlocked.Increment(ref m_disposeCount);
                        if (ThrowOnCleanup)
                        {
                            throw new InvalidOperationException("Dispose failed.");
                        }
                        break;
                }
                return DefaultResult(proxy, method.ReturnType);
            }

            private async IAsyncEnumerable<ConsumeResult<byte[], byte[]>> ConsumeAsync(
                [EnumeratorCancellation] CancellationToken ct)
            {
                Interlocked.Increment(ref m_consumeCount);
                await Task.Yield();
                if (FailConsume)
                {
                    throw new InvalidOperationException("Unsupported compression codec.");
                }
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                yield break;
            }

            private static object? DefaultResult(object proxy, Type type)
            {
                if (type == typeof(void))
                {
                    return null;
                }
                if (type == typeof(ValueTask))
                {
                    return default(ValueTask);
                }
                if (type == typeof(Task))
                {
                    return Task.CompletedTask;
                }
                if (type.IsInstanceOfType(proxy))
                {
                    return proxy;
                }
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }

            private int m_consumeCount;
            private int m_closeCount;
            private int m_disposeCount;
        }

        /// <summary>
        /// Forwards every consumer call to its <see cref="FakeConsumer"/>.
        /// </summary>
        public class ConsumerProxy : DispatchProxy
        {
            internal FakeConsumer State { get; set; } = null!;

            /// <inheritdoc/>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return State.Invoke(this, targetMethod!, args);
            }
        }
    }
}
#endif
