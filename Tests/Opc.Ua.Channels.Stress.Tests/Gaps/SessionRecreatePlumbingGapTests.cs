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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Fakes;

namespace Opc.Ua.Channels.Stress.Tests.Gaps
{
    /// <summary>
    /// Documents that RequiresSessionRecreate is currently only aggregated as a ready outcome.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("Gaps")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionRecreatePlumbingGapTests : GapTestBase
    {
        [Test]
        [Explicit("Documents production gap: RequiresSessionRecreate is not wired through to Session.RecreateAsync.")]
        [CancelAfter(30_000)]
        public async Task RequiresSessionRecreateDoesNotInvokeAnythingAsync(
            CancellationToken ct)
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

                    // TODO: https://github.com/OPCFoundation/UA-.NETStandard/issues/#TODO-FILL-IN
                    // When the wiring lands, expect RecreateAsync or the equivalent callback exactly once.
                    Assert.Multiple(() =>
                    {
                        Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                        Assert.That(participant.ReconnectInvocationCount, Is.EqualTo(1));
                        Assert.That(participant.RecreateInvocationCount, Is.Zero);
                    });
                }
                finally
                {
                    channel?.Dispose();
                }
            }
        }

        private sealed class RecreateRecordingParticipant : IReconnectParticipant
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

            public ValueTask RecreateAsync(CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_recreateInvocationCount);
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

            private int m_reconnectInvocationCount;
            private int m_recreateInvocationCount;
        }
    }
}
