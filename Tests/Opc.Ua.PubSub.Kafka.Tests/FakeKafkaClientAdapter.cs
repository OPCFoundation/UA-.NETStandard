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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Kafka.Internal;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// In-memory implementation of <see cref="IKafkaClientAdapter"/> used by the Kafka tests.
    /// </summary>
    internal sealed class FakeKafkaClientAdapter : IKafkaClientAdapter
    {
        private readonly FakeKafkaBus m_bus;
        private readonly TimeProvider m_timeProvider;
        private readonly ConcurrentQueue<KafkaMessage> m_produced = new();
        private readonly Lock m_sync = new();
        private readonly List<string> m_subscriptions = new();
        private readonly List<string> m_unsubscriptions = new();
        private bool m_isConnected;
        private bool m_disposed;

        public FakeKafkaClientAdapter(FakeKafkaBus? bus = null, TimeProvider? timeProvider = null)
        {
            m_bus = bus ?? FakeKafkaBus.Shared;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        public IReadOnlyCollection<KafkaMessage> ProducedMessages => m_produced;

        public IReadOnlyList<string> Subscriptions
        {
            get
            {
                lock (m_sync)
                {
                    return m_subscriptions.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Unsubscriptions
        {
            get
            {
                lock (m_sync)
                {
                    return m_unsubscriptions.ToArray();
                }
            }
        }

        public int ConnectCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public Func<KafkaConnectionOptions, CancellationToken, ValueTask>? OnConnect { get; set; }

        public Func<CancellationToken, ValueTask>? OnDisconnect { get; set; }

        public Func<KafkaMessage, CancellationToken, ValueTask>? OnProduce { get; set; }

        public bool IsConnected
        {
            get
            {
                lock (m_sync)
                {
                    return m_isConnected;
                }
            }
        }

        public bool Disposed => m_disposed;

        public event EventHandler<KafkaIncomingMessageEventArgs>? IncomingMessage;

        public event EventHandler<KafkaConnectionStateChangedEventArgs>? ConnectionStateChanged;

        public async ValueTask ConnectAsync(KafkaConnectionOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            if (OnConnect is not null)
            {
                await OnConnect(options, cancellationToken).ConfigureAwait(false);
            }
            lock (m_sync)
            {
                m_isConnected = true;
            }
            ConnectionStateChanged?.Invoke(
                this,
                new KafkaConnectionStateChangedEventArgs(isConnected: true, reason: "Connected"));
        }

        public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCount++;
            if (OnDisconnect is not null)
            {
                await OnDisconnect(cancellationToken).ConfigureAwait(false);
            }
            bool wasConnected;
            lock (m_sync)
            {
                wasConnected = m_isConnected;
                m_isConnected = false;
            }
            m_bus.Unsubscribe(this, Subscriptions);
            if (wasConnected)
            {
                ConnectionStateChanged?.Invoke(
                    this,
                    new KafkaConnectionStateChangedEventArgs(isConnected: false, reason: "Disconnected"));
            }
        }

        public ValueTask SubscribeAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                foreach (string topic in topics)
                {
                    if (!m_subscriptions.Contains(topic))
                    {
                        m_subscriptions.Add(topic);
                    }
                }
            }
            m_bus.Subscribe(this, topics);
            return default;
        }

        public ValueTask UnsubscribeAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_sync)
            {
                foreach (string topic in topics)
                {
                    m_unsubscriptions.Add(topic);
                    m_subscriptions.Remove(topic);
                }
            }
            m_bus.Unsubscribe(this, topics);
            return default;
        }

        public async ValueTask ProduceAsync(KafkaMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KafkaMessage copy = Copy(message);
            m_produced.Enqueue(copy);
            if (OnProduce is not null)
            {
                await OnProduce(copy, cancellationToken).ConfigureAwait(false);
            }
            m_bus.Publish(copy, cancellationToken);
        }

        public void RaiseIncomingMessage(KafkaMessage message, DateTimeUtc receivedAt)
        {
            IncomingMessage?.Invoke(this, new KafkaIncomingMessageEventArgs(Copy(message), receivedAt));
        }

        public void RaiseConnectionStateChanged(bool isConnected, string? reason = null)
        {
            lock (m_sync)
            {
                m_isConnected = isConnected;
            }
            ConnectionStateChanged?.Invoke(
                this,
                new KafkaConnectionStateChangedEventArgs(isConnected, reason));
        }

        public ValueTask DisposeAsync()
        {
            m_disposed = true;
            m_bus.Unsubscribe(this, Subscriptions);
            return default;
        }

        internal void Deliver(KafkaMessage message)
        {
            RaiseIncomingMessage(
                message,
                DateTimeUtc.From(m_timeProvider.GetUtcNow().UtcDateTime));
        }

        private static KafkaMessage Copy(KafkaMessage message)
        {
            IReadOnlyDictionary<string, string>? headers = message.Headers is null
                ? null
                : message.Headers.ToDictionary(
                    static header => header.Key,
                    static header => header.Value,
                    StringComparer.Ordinal);
            return new KafkaMessage(
                message.Topic,
                message.Key.ToArray(),
                message.Value.ToArray(),
                message.ContentType,
                headers);
        }
    }

    /// <summary>
    /// Shared in-memory Kafka topic bus used by fake adapters.
    /// </summary>
    internal sealed class FakeKafkaBus
    {
        private readonly Lock m_sync = new();
        private readonly Dictionary<string, List<FakeKafkaClientAdapter>> m_subscribers = new(StringComparer.Ordinal);

        public static FakeKafkaBus Shared { get; } = new FakeKafkaBus();

        public void Subscribe(FakeKafkaClientAdapter adapter, IReadOnlyList<string> topics)
        {
            lock (m_sync)
            {
                foreach (string topic in topics)
                {
                    if (!m_subscribers.TryGetValue(topic, out List<FakeKafkaClientAdapter>? subscribers))
                    {
                        subscribers = new List<FakeKafkaClientAdapter>();
                        m_subscribers[topic] = subscribers;
                    }
                    if (!subscribers.Contains(adapter))
                    {
                        subscribers.Add(adapter);
                    }
                }
            }
        }

        public void Unsubscribe(FakeKafkaClientAdapter adapter, IReadOnlyList<string> topics)
        {
            lock (m_sync)
            {
                foreach (string topic in topics)
                {
                    if (m_subscribers.TryGetValue(topic, out List<FakeKafkaClientAdapter>? subscribers))
                    {
                        subscribers.Remove(adapter);
                        if (subscribers.Count == 0)
                        {
                            m_subscribers.Remove(topic);
                        }
                    }
                }
            }
        }

        public void Publish(KafkaMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FakeKafkaClientAdapter[] subscribers;
            lock (m_sync)
            {
                subscribers = m_subscribers.TryGetValue(message.Topic, out List<FakeKafkaClientAdapter>? targets)
                    ? targets.ToArray()
                    : Array.Empty<FakeKafkaClientAdapter>();
            }
            foreach (FakeKafkaClientAdapter subscriber in subscribers)
            {
                subscriber.Deliver(message);
            }
        }
    }

    /// <summary>
    /// Fake Kafka client factory that hands adapters to transports under test.
    /// </summary>
    internal sealed class FakeKafkaClientFactory : IKafkaClientFactory
    {
        private readonly Queue<FakeKafkaClientAdapter> m_adapters = new();
        private readonly FakeKafkaBus m_bus;

        public FakeKafkaClientFactory(FakeKafkaBus? bus = null)
        {
            m_bus = bus ?? new FakeKafkaBus();
            Adapter = new FakeKafkaClientAdapter(m_bus);
            m_adapters.Enqueue(Adapter);
        }

        public FakeKafkaClientFactory(params FakeKafkaClientAdapter[] adapters)
        {
            if (adapters.Length == 0)
            {
                throw new ArgumentException("At least one adapter is required.", nameof(adapters));
            }
            m_bus = new FakeKafkaBus();
            Adapter = adapters[0];
            foreach (FakeKafkaClientAdapter adapter in adapters)
            {
                m_adapters.Enqueue(adapter);
            }
        }

        public FakeKafkaClientAdapter Adapter { get; }

        public int CreateCount { get; private set; }

        IKafkaClientAdapter IKafkaClientFactory.Create(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            CreateCount++;
            if (m_adapters.Count > 0)
            {
                return m_adapters.Dequeue();
            }
            return new FakeKafkaClientAdapter(m_bus, timeProvider);
        }
    }
}
