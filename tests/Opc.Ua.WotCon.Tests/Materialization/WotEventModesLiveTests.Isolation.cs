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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Export;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [Test]
        public async Task NativeTransparentAuthoritiesCannotAliasAsync()
        {
            await CheckTransparentAuthorityIsolationAsync(false).ConfigureAwait(false);
        }

        [Test]
        public async Task NativeRejectedTransparentSourceDoesNotStopOtherSourceAsync()
        {
            await CheckTransparentAuthorityIsolationAsync(true).ConfigureAwait(false);
        }

        private static async Task CheckTransparentAuthorityIsolationAsync(bool requireSourceContinuity)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var first = new NativeEndpoint();
            await using var second = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await first.StartAsync(false, ct).ConfigureAwait(false);
            await second.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await first.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await second.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await first.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            await second.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            WotCompiledForm firstForm = EventForm(first.EndpointUrl);
            var secondForm = new WotCompiledForm(
                firstForm.Binding, Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, "other", "/events/other/forms/0",
                WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                new WotEndpointDescriptor("opc.tcp", null, -1, second.EndpointUrl),
                new WotAddressingDescriptor("i=2253"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                null, WotEventSelection.Default, null);
            ExpandedNodeId type = new("EventType", SourceNamespace);
            WotProjectedAffordance firstDeclaration = EventDeclaration("transparent-forwarding", type);
            WotProjectedAffordance secondDeclaration = new WotProjectedAffordance(
                Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, "other", "/events/other", type.ToString(),
                new ExpandedNodeId("Owner", LocalNamespace).ToString())
                .WithIdentityMode(WoTEventIdentityModeEnum.TransparentForwarding);
            WotProjectionDocument original = TransparentProjection(firstForm, firstDeclaration);
            WotBindingPlan plan = new WotBindingPlan(
                "urn:wot:d3:two-authorities", [], [firstForm, secondForm], [], [])
                .WithProjectedAffordances([firstDeclaration, secondDeclaration]);
            var document = new WotProjectionDocument(original.ClosureKey, original.Sources, [plan]);
            var channels = new EndpointNativeChannels(new Dictionary<string, ISession>(StringComparer.Ordinal)
            {
                [first.EndpointUrl] = first.Session,
                [second.EndpointUrl] = second.Session
            });
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            WotProjectionHandle handle = await host.AddAsync(document, ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeEvents events = await NativeEvents.OpenAsync(destination.Session, ct)
                    .ConfigureAwait(false);
                ByteString firstId = ByteString.From(new byte[] { 0xD3, 0xE0, 0x01, 0x01 });
                await first.ReportAsync(firstId, ct).ConfigureAwait(false);
                ArrayOf<Variant> accepted = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(accepted[0].TryGetValue(out ByteString acceptedId), Is.True);
                Assert.That(acceptedId, Is.EqualTo(firstId));
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
                NodeId other = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("other", metadata), ct).ConfigureAwait(false);
                NodeId availability = await FindNativeChildAsync(destination.Session, other,
                    new QualifiedName("Availability", metadata), ct).ConfigureAwait(false);
                await second.ReportAsync(ByteString.From(new byte[] { 0xD3, 0xE0, 0x02, 0x01 }), ct)
                    .ConfigureAwait(false);
                StatusCode status = StatusCodes.Good;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    DataValue value = await destination.Session.ReadValueAsync(availability, ct).ConfigureAwait(false);
                    Assert.That(value.WrappedValue.TryGetValue(out status), Is.True);
                    if (status == StatusCodes.BadSecurityChecksFailed)
                    {
                        break;
                    }
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
                Assert.That(status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed),
                    "Different authenticated authorities must not publish one aliased semantic source NodeId.");
                if (requireSourceContinuity)
                {
                    ByteString nextId = ByteString.From(new byte[] { 0xD3, 0xE0, 0x01, 0x02 });
                    await first.ReportAsync(nextId, ct).ConfigureAwait(false);
                    using var delivery = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delivery.CancelAfter(TimeSpan.FromSeconds(5));
                    ArrayOf<Variant> next = await events.ReadAsync(delivery.Token).ConfigureAwait(false);
                    Assert.That(next[0].TryGetValue(out ByteString deliveredId), Is.True);
                    Assert.That(deliveredId, Is.EqualTo(nextId),
                        "Rejecting another authority must not stop the admitted source.");
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NativeProvenanceAuthorizationPrecedesLookupAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            ExpandedNodeId type = new("EventType", SourceNamespace);
            WotCompiledForm form = EventForm(source.EndpointUrl);
            WotProjectionDocument original = TransparentProjection(
                form, EventDeclaration("transparent-forwarding", type));
            using var stream = new MemoryStream(original.Sources[1].NodeSetXml, writable: false);
            UANodeSet local = UANodeSet.Read(stream) ??
                throw new InvalidOperationException("The local metadata test model was not read.");
            local.Items![0].RolePermissions =
            [
                new RolePermission
                {
                    Value = Ua.ObjectIds.WellKnownRole_Anonymous.ToString(),
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.ReceiveEvents)
                }
            ];
            var document = new WotProjectionDocument(original.ClosureKey,
                [
                    original.Sources[0],
                    new WotProjectionSource("permission-owner", [LocalNamespace], Serialize(local))
                ],
                original.BindingPlans);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotProjectionHandle handle = await host.AddAsync(document, ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeEvents events = await NativeEvents.OpenAsync(destination.Session, ct)
                    .ConfigureAwait(false);
                ByteString sourceId = ByteString.From(new byte[] { 0xD3, 0xE1, 0x01, 0x01 });
                await source.ReportAsync(sourceId, ct).ConfigureAwait(false);
                ArrayOf<Variant> fields = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(fields[0].TryGetValue(out ByteString observedId), Is.True);
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
                NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("event", metadata), ct).ConfigureAwait(false);
                var client = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                foreach (ByteString eventId in new[] { observedId, ByteString.From(new byte[] { 0x01 }) })
                {
                    ServiceResultException? denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    {
                        _ = await client.GetEventProvenanceAsync(eventId, ct).ConfigureAwait(false);
                    });
                    Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeConditionActionAuthorizationPrecedesRouteLookupAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            var calls = Channel.CreateBounded<NativeConditionCall>(4);
            NodeId sourceMethod = await InstallActionSourceAsync(source, calls.Writer, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            WotProjectionDocument original = ActionProjection(mode, source.EndpointUrl,
                NodeId.ToExpandedNodeId(sourceMethod, source.Session.NamespaceUris).ToString());
            using var stream = new MemoryStream(original.Sources[1].NodeSetXml, writable: false);
            UANodeSet local = UANodeSet.Read(stream) ??
                throw new InvalidOperationException("The action authorization model was not read.");
            UANode actionNode = Array.Find(local.Items!, static node => node.NodeId == "ns=1;s=Ack") ??
                throw new InvalidOperationException("The declared action was not exported.");
            actionNode.RolePermissions =
            [
                new RolePermission
                {
                    Value = Ua.ObjectIds.WellKnownRole_Anonymous.ToString(),
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];
            var document = new WotProjectionDocument(original.ClosureKey,
                [
                    original.Sources[0],
                    new WotProjectionSource("action-permission-owner", [LocalNamespace], Serialize(local))
                ],
                original.BindingPlans);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotProjectionHandle handle = await host.AddAsync(document, ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                ByteString sourceId = ByteString.From(new byte[] { 0xD3, 0xB3, 0x01, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", true, true, sourceId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> fields = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(fields[0].TryGetValue(out ByteString observedId), Is.True);
                Assert.That(fields[8].TryGetValue(out NodeId condition), Is.True);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId action = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Ack", destination.Session.NamespaceUris);
                NodeId inherited = await FindNativeChildAsync(destination.Session, condition,
                    QualifiedName.From(Ua.BrowseNames.Acknowledge), ct).ConfigureAwait(false);
                ArrayOf<ByteString> ids = [observedId, ByteString.From(new byte[] { 0xD3, 0xFF })];
                for (int index = 0; index < ids.Count; index++)
                {
                    ByteString eventId = ids[index];
                    var comment = new LocalizedText("Unauthorized occurrence action");
                    CallMethodResult authored = await InvokeNativeActionAsync(
                        destination.Session, owner, action, eventId, comment, ct).ConfigureAwait(false);
                    CallMethodResult standard = await InvokeNativeActionAsync(
                        destination.Session, condition, inherited, eventId, comment, ct).ConfigureAwait(false);
                    Assert.That(authored.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(standard.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(calls.Reader.TryRead(out _), Is.False);
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
                calls.Writer.TryComplete();
            }
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeRetiringGenerationUsesCapturedActionsUntilDrainAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            var calls = Channel.CreateBounded<NativeConditionCall>(128);
            NodeId sourceMethodId = await InstallActionSourceAsync(source, calls.Writer, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            string sourceMethod = NodeId.ToExpandedNodeId(sourceMethodId, source.Session.NamespaceUris).ToString();
            WotProjectionDocument document = ActionProjection(mode, source.EndpointUrl, sourceMethod);
            var channels = new SwitchingNativeChannels(source.Session);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            WotProjectionHandle active = await host.AddAsync(document, ct).ConfigureAwait(false);
            NativeConditionEvents? retained = null;
            bool retainedClosed = false;
            string root = Path.Combine(
                Environment.GetEnvironmentVariable("WOT_EVENT_TEST_ROOT") ?? Path.GetTempPath(),
                "d3-replacement-" + Guid.NewGuid().ToString("N"));
            var replacementClient = new ClientFixture(NUnitTelemetryContext.Create());
            ISession? replacementSession = null;
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId action = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Ack", destination.Session.NamespaceUris);
                retained = await NativeConditionEvents.OpenAsync(destination.Session, ct, owner).ConfigureAwait(false);
                ByteString firstSourceId = ByteString.From(new byte[] { 0xD3, 0xF0, 0x01, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", true, true, firstSourceId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> oldFields = await retained.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(oldFields[0].TryGetValue(out ByteString oldEventId), Is.True);
                await replacementClient.LoadClientConfigurationAsync(root, "D3ReplacementClient").ConfigureAwait(false);
                replacementSession = await replacementClient.ConnectAsync(
                    new Uri(source.EndpointUrl), SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                await replacementSession.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
                channels.Session = replacementSession;
                WotProjectionHandle replacement = await host.ShadowReloadAsync(active, document, ct)
                    .ConfigureAwait(false);
                Assert.That(replacement.Generation, Is.GreaterThan(active.Generation));
                active = replacement;
                var comment = new LocalizedText("Retained generation");
                CallMethodResult result = await InvokeNativeActionAsync(
                    destination.Session, owner, action, oldEventId, comment, ct).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                NativeConditionCall oldCall = await calls.Reader.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(oldCall.EventId, Is.EqualTo(firstSourceId));
                Assert.That(oldCall.SessionId, Is.EqualTo(source.Session.SessionId));
                Assert.That(oldCall.SessionId, Is.Not.EqualTo(replacementSession.SessionId));

                await retained.DisposeAsync().ConfigureAwait(false);
                retainedClosed = true;
                StatusCode retired = StatusCodes.Good;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    result = await InvokeNativeActionAsync(
                        destination.Session, owner, action, oldEventId, comment, ct).ConfigureAwait(false);
                    retired = result.StatusCode;
                    if (StatusCode.IsBad(retired))
                    {
                        break;
                    }
                    NativeConditionCall draining = await calls.Reader.ReadAsync(ct).ConfigureAwait(false);
                    Assert.That(draining.SessionId, Is.EqualTo(source.Session.SessionId));
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
                Assert.That(retired, Is.EqualTo(StatusCodes.BadEventIdUnknown));
                Assert.That(calls.Reader.TryRead(out _), Is.False);

                await using NativeConditionEvents current = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct, owner).ConfigureAwait(false);
                ByteString currentSourceId = ByteString.From(new byte[] { 0xD3, 0xF0, 0x01, 0x02 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true, currentSourceId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> currentFields = await current.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(currentFields[0].TryGetValue(out ByteString currentEventId), Is.True);
                await replacementSession.ReconnectAsync(null, null, ct).ConfigureAwait(false);
                CallMethodResult invalidated = await InvokeNativeActionAsync(
                    destination.Session, owner, action, currentEventId, comment, ct).ConfigureAwait(false);
                Assert.That(invalidated.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(calls.Reader.TryRead(out _), Is.False);
            }
            finally
            {
                if (retained is not null && !retainedClosed)
                {
                    await retained.DisposeAsync().ConfigureAwait(false);
                }
                await host.RemoveAsync(active, CancellationToken.None).ConfigureAwait(false);
                if (replacementSession is not null)
                {
                    await replacementSession.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    replacementSession.Dispose();
                }
                calls.Writer.TryComplete();
                await replacementClient.DisposeAsync().ConfigureAwait(false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private sealed class EndpointNativeChannels(Dictionary<string, ISession> sessions) : IWotBindingChannelFactory
        {
            public ValueTask<IWotBindingChannel> OpenChannelAsync(
                WotCompiledForm form, CancellationToken cancellationToken = default)
            {
                if (form.Endpoint.BaseUri is not { } endpoint || !sessions.TryGetValue(endpoint, out ISession? session))
                {
                    throw new InvalidOperationException("The test did not authorize this source endpoint.");
                }
                return new NativeChannels(session).OpenChannelAsync(form, cancellationToken);
            }
        }

        private sealed class SwitchingNativeChannels(ISession session) : IWotBindingChannelFactory
        {
            public ISession Session { get; set; } = session;

            public ValueTask<IWotBindingChannel> OpenChannelAsync(
                WotCompiledForm form, CancellationToken cancellationToken = default)
            {
                return new NativeChannels(Session).OpenChannelAsync(form, cancellationToken);
            }
        }
    }
}
#endif
