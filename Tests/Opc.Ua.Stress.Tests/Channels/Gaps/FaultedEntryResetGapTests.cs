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
using Opc.Ua.Stress.Tests.Channels.Fakes;

namespace Opc.Ua.Stress.Tests.Channels.Gaps
{
    /// <summary>
    /// Documents the current missing reset path for faulted managed-channel entries.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("Gaps")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class FaultedEntryResetGapTests : GapTestBase
    {
        [Test]
        [Explicit("Documents production gap: IClientChannelManager.ResetFaultedAsync is not implemented. " +
            "After Faulted, callers must Dispose+reacquire.")]
        [CancelAfter(30_000)]
        public async Task FaultedEntryCannotRecoverViaReconnectAsync(
            CancellationToken ct)
        {
            using var bindings = new FakeChannelBindings();
            ClientChannelManager manager = CreateManager(bindings);
            await using (manager.ConfigureAwait(false))
            {
                ConfiguredEndpoint endpoint = CreateEndpoint();
                var participant = new FakeParticipant(endpoint);
                participant.ConfigureOnReconnect(
                    static (_, _) => ReconnectResultAsync(ParticipantReconnectResult.FatalForChannel));
                IManagedTransportChannel? channel = null;

                try
                {
                    channel = await manager.GetAsync(participant, ct).ConfigureAwait(false);

                    await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

                    Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));

                    participant.ConfigureOnReconnect(
                        static (_, _) => ReconnectResultAsync(ParticipantReconnectResult.Reactivated));

                    // TODO: https://github.com/OPCFoundation/UA-.NETStandard/issues/#TODO-FILL-IN
                    // When ResetFaultedAsync lands, update this test to call it and assert the channel returns to Ready.
                    ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await manager.ReconnectAsync(channel, ct).ConfigureAwait(false));

                    Assert.Multiple(() =>
                    {
                        Assert.That(exception, Is.Not.Null);
                        Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                        Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
                    });
                }
                finally
                {
                    channel?.Dispose();
                }
            }
        }
    }
}
