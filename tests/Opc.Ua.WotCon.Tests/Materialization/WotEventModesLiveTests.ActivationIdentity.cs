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

#if NET8_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission", false)]
        [TestCase("transparent-forwarding", false)]
        [TestCase("transparent-forwarding", true)]
        public async Task NativeIdentityEnforcementStartsAtTransparentActivationAsync(
            string mode, bool removeBeforePublication)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            bool transparent = mode == "transparent-forwarding";
            ExpandedNodeId type = new("EventType", transparent ? SourceNamespace : LocalNamespace);
            var channels = new NativeChannels(source.Session);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            WotProjectionHandle? active = await host.AddAsync(
                Projection(type, EventForm(source.EndpointUrl), EventDeclaration(mode, type)), ct).ConfigureAwait(false);
            try
            {
                Assert.That(channels.OpenCount, Is.EqualTo(transparent ? 1 : 0));
                Assert.That(source.Session.SubscriptionCount, Is.Zero);
                Assert.That(destination.Session.SubscriptionCount, Is.Zero);
                if (removeBeforePublication)
                {
                    await host.RemoveAsync(active, ct).ConfigureAwait(false);
                    active = null;
                }

                var context = destination.Server.CurrentInstance.DefaultSystemContext;
                ByteString eventId = ByteString.From(new byte[] { 0xD3, 0xC3, 0x05, 0x01 });
                BaseEventState first = NativeActivationEvent(eventId, 612);
                await destination.Server.CurrentInstance.ReportEventAsync(context, first, ct).ConfigureAwait(false);
                Assert.That(destination.Server.CurrentInstance.EventManager.EventIdentityAdmissionStatus,
                    Is.EqualTo(StatusCodes.Good));
                StatusCode outcome = StatusCodes.Good;
                try
                {
                    await destination.Server.CurrentInstance.ReportEventAsync(
                        context, NativeActivationEvent(eventId, 999), ct).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    outcome = exception.StatusCode;
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(outcome, Is.EqualTo(transparent
                        ? StatusCodes.BadSecurityChecksFailed : StatusCodes.Good));
                    Assert.That(destination.Server.CurrentInstance.EventManager.EventIdentityAdmissionStatus,
                        Is.EqualTo(transparent ? StatusCodes.Good : StatusCodes.BadNotSupported));
                    Assert.That(first.Severity!.Value, Is.EqualTo((ushort)612));
                    Assert.That(source.Session.SubscriptionCount, Is.Zero);
                    Assert.That(destination.Session.SubscriptionCount, Is.Zero);
                }
                if (transparent)
                {
                    await destination.Server.CurrentInstance.ReportEventAsync(
                        context, NativeActivationEvent(eventId, 612), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                if (active is not null)
                {
                    await host.RemoveAsync(active, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private static BaseEventState NativeActivationEvent(ByteString eventId, ushort severity)
        {
            var state = new BaseEventState(null) { TypeDefinitionId = Ua.ObjectTypeIds.BaseEventType };
            state.EventId = PropertyState<ByteString>.With<VariantBuilder>(state, eventId);
            state.EventType = PropertyState<NodeId>.With<VariantBuilder>(state, state.TypeDefinitionId);
            state.SourceNode = PropertyState<NodeId>.With<VariantBuilder>(state, Ua.ObjectIds.Server);
            state.SourceName = PropertyState<string>.With<VariantBuilder>(state, "Server");
            state.Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, SourceTime);
            state.ReceiveTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, SourceReceiveTime);
            state.Message = PropertyState<LocalizedText>.With<VariantBuilder>(
                state, new LocalizedText("Native occurrence before any projected reservation"));
            state.Severity = PropertyState<ushort>.With<VariantBuilder>(state, severity);
            return state;
        }
    }
}
#endif
