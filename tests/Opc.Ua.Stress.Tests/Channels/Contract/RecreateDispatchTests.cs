// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Verifies fire-and-forget recreate dispatch for participants that request session recreation.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [Category("ChannelManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class RecreateDispatchTests : ContractTestBase
    {
        [Test]
        [CancelAfter(30_000)]
        public async Task RequiresSessionRecreateInvokesRecreateAsync(CancellationToken ct)
        {
            using var bindings = new FakeChannelBindings();
            ClientChannelManager manager = CreateManager(bindings);
            await using (manager.ConfigureAwait(false))
            {
                ConfiguredEndpoint endpoint = CreateEndpoint();
                var participant = new RecreateRecordingParticipant(endpoint);
                IManagedTransportChannel? channel = null;

                try
                {
                    channel = await manager.GetAsync(participant, ct).ConfigureAwait(false);

                    await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);
                    await participant.RecreateInvoked.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);

                    Assert.Multiple(() =>
                    {
                        Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                        Assert.That(participant.ReconnectInvocationCount, Is.EqualTo(1));
                        Assert.That(participant.RecreateInvocationCount, Is.EqualTo(1));
                    });
                }
                finally
                {
                    channel?.Dispose();
                }
            }
        }

        private sealed class RecreateRecordingParticipant : IRecreateAwareReconnectParticipant
        {
            public RecreateRecordingParticipant(ConfiguredEndpoint endpoint)
            {
                Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
                Id = Guid.NewGuid().ToString("N");
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public int ReconnectInvocationCount => Volatile.Read(ref m_reconnectInvocationCount);

            public int RecreateInvocationCount => Volatile.Read(ref m_recreateInvocationCount);

            public Task RecreateInvoked => m_recreateInvoked.Task;

            /// <inheritdoc/>
            public IRetryBudget? CreateReconnectBudget(TimeProvider timeProvider)
            {
                return null;
            }

            public ValueTask RecreateAsync(CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_recreateInvocationCount);
                m_recreateInvoked.TrySetResult(true);
                return new ValueTask();
            }

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                if (channel == null)
                {
                    throw new ArgumentNullException(nameof(channel));
                }

                _ = reconnectAttempt;
                ct.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_reconnectInvocationCount);
                return ReconnectResultAsync(ParticipantReconnectResult.RequiresSessionRecreate);
            }

            private readonly TaskCompletionSource<bool> m_recreateInvoked = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_reconnectInvocationCount;
            private int m_recreateInvocationCount;
        }
    }
}
