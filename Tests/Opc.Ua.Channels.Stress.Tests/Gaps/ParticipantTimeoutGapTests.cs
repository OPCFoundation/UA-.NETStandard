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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Channels.Stress.Tests.Fakes;

namespace Opc.Ua.Channels.Stress.Tests.Gaps
{
    /// <summary>
    /// Documents that a participant which never completes blocks the whole reconnect cycle.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("Gaps")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ParticipantTimeoutGapTests : GapTestBase
    {
        [Test]
        [Explicit("Documents production gap: OnReconnectAsync has no bounded timeout.")]
        [CancelAfter(30_000)]
        public async Task HungParticipantBlocksReconnectAsync(
            CancellationToken ct)
        {
            using var bindings = new FakeChannelBindings();
            ClientChannelManager manager = CreateManager(bindings);
            await using (manager.ConfigureAwait(false))
            {
                ConfiguredEndpoint endpoint = CreateEndpoint();
                var participant = new FakeParticipant(endpoint);
                var participantEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseParticipant = new TaskCompletionSource<ParticipantReconnectResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                participant.ConfigureOnReconnect(async (_, reconnectCt) =>
                {
                    participantEntered.TrySetResult(true);
                    return await releaseParticipant.Task.WaitAsync(reconnectCt).ConfigureAwait(false);
                });
                IManagedTransportChannel? channel = null;
                Task? reconnectTask = null;

                try
                {
                    channel = await manager.GetAsync(participant, ct).ConfigureAwait(false);

                    reconnectTask = manager.ReconnectAsync(channel, ct).AsTask();
                    await participantEntered.Task.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);

                    Task delayTask = Task.Delay(ObservationWindow, ct);
                    Task completed = await Task.WhenAny(reconnectTask, delayTask).ConfigureAwait(false);

                    // Reference: this is the same scenario as Contract/HungParticipantTests.L1_HUNG1.
                    // Kept here for discoverability under the Gaps folder.
                    // TODO: https://github.com/OPCFoundation/UA-.NETStandard/issues/#TODO-FILL-IN
                    // When participant timeouts land, update this test to expect timeout handling instead of a hang.
                    Assert.Multiple(() =>
                    {
                        Assert.That(
                            completed,
                            Is.SameAs(delayTask),
                            "ReconnectAsync should still be waiting because participant callbacks are unbounded.");
                        Assert.That(reconnectTask.IsCompleted, Is.False);
                        Assert.That(channel.State, Is.EqualTo(ChannelState.TransportConnectedSessionReactivating));
                    });

                    releaseParticipant.SetResult(ParticipantReconnectResult.Reactivated);
                    await reconnectTask.WaitAsync(AssertionTimeout, ct).ConfigureAwait(false);
                    Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                }
                finally
                {
                    releaseParticipant.TrySetResult(ParticipantReconnectResult.Reactivated);
                    if (reconnectTask != null)
                    {
                        try
                        {
                            await reconnectTask.WaitAsync(AssertionTimeout, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            // Best-effort cleanup for the known-failing gap test.
                        }
                    }

                    channel?.Dispose();
                }
            }
        }
    }
}
