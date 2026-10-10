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
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task NativeTransparentProviderPathsCaptureOneOwnedSourceBeforePublicationAsync(
            bool dependencyInjection, bool authenticated)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct,
                authenticated ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var native = new NativeChannels(source.Session);
            var channels = new ActivationChannels(native);
            var services = new ServiceCollection();
            services.AddSingleton<IWotBindingChannelFactory>(channels);
            services.AddSingleton(destination.Server.NodeManagerLifecycle);
            services.AddOpcUa().AddWotRegistryServer();
            await using ServiceProvider provider = services.BuildServiceProvider();
            IWotProjectionHost host = dependencyInjection
                ? provider.GetRequiredService<IWotProjectionHost>()
                : new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                    new WotProjectionBindingRuntimeFactory(channels));
            ExpandedNodeId type = new("EventType", SourceNamespace);
            WotProjectionDocument document = TransparentProjection(
                EventForm(source.EndpointUrl), EventDeclaration("transparent-forwarding", type));
            WotProjectionHandle? handle = null;
            StatusCode status = StatusCodes.Good;
            try
            {
                try
                {
                    handle = await host.AddAsync(document, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    status = exception.StatusCode;
                }
                Assert.That(status, Is.EqualTo(authenticated
                    ? StatusCodes.Good : StatusCodes.BadSecurityChecksFailed));
                Assert.That(await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false),
                    Is.EqualTo(authenticated ? 1 : 0));
                Assert.That(channels.Channels, Has.Count.EqualTo(1));
                ActivationChannel channel = channels.Channels[0];
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(native.OpenCount, Is.EqualTo(1));
                    Assert.That(channel.CaptureCount, Is.EqualTo(1));
                    Assert.That(channel.SubscribeCount, Is.Zero);
                    Assert.That(channel.DisposeCount, Is.EqualTo(authenticated ? 0 : 1));
                    Assert.That(source.Session.SubscriptionCount, Is.Zero);
                    Assert.That(destination.Session.SubscriptionCount, Is.Zero);
                    Assert.That(source.Session.Connected, Is.True);
                }
                if (authenticated)
                {
                    Assert.That(channel.Source, Is.Not.Null);
                    Assert.That(channel.Source!.IsCurrent, Is.True);
                    Assert.That(channel.Source.IsAuthenticated, Is.True);
                    Assert.That(channel.Source.SessionId, Is.EqualTo(source.Session.SessionId));
                    await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                    NodeId descriptor = await CapacityDescriptorAsync(destination, "event", ct).ConfigureAwait(false);
                    ushort ns = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                    NodeId generation = await FindNativeChildAsync(destination.Session, descriptor,
                        new QualifiedName("Generation", ns), ct).ConfigureAwait(false);
                    DataValue value = await destination.Session.ReadValueAsync(generation, ct).ConfigureAwait(false);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(value.WrappedValue.TryGetValue(out uint published), Is.True);
                    Assert.That(published, Is.EqualTo(checked((uint)handle!.Generation)));
                    Assert.That(await ReadCapacityAvailabilityAsync(destination, descriptor, ct).ConfigureAwait(false),
                        Is.EqualTo(StatusCodes.Good));
                    NodeId serverUri = await FindNativeChildAsync(destination.Session, descriptor,
                        new QualifiedName("SourceServerUri", ns), ct).ConfigureAwait(false);
                    DataValue origin = await destination.Session.ReadValueAsync(serverUri, ct).ConfigureAwait(false);
                    Assert.That(origin.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(origin.WrappedValue.TryGetValue(out string? sourceUri), Is.True);
                    Assert.That(sourceUri, Is.EqualTo(channel.Source.ServerUri));
                }
            }
            finally
            {
                if (handle is not null)
                {
                    await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
                }
            }
            Assert.That(channels.Channels[0].DisposeCount, Is.EqualTo(1));
            Assert.That(channels.Channels[0].Source?.IsCurrent ?? false, Is.False);
            Assert.That(source.Session.Connected, Is.True, "The projection borrows, but does not own, this Session.");
        }

        [TestCase("missing")]
        [TestCase("lineage")]
        [TestCase("abstract")]
        [TestCase("sourceDeclaration")]
        [TestCase("localDeclaration")]
        [TestCase("notifier")]
        public async Task NativeTransparentUnsupportedShapeRejectsBeforePublicationAsync(string change)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            UANodeSet sourceNodes = SourceNodes();
            if (change == "missing")
            {
                sourceNodes.Items = [sourceNodes.Items![0]];
            }
            else if (change == "lineage")
            {
                ((UAObjectType)sourceNodes.Items![1]).References![0].Value = Ua.ObjectTypeIds.BaseObjectType.ToString();
            }
            else if (change == "abstract")
            {
                ((UAObjectType)sourceNodes.Items![1]).IsAbstract = true;
            }
            else if (change == "sourceDeclaration")
            {
                sourceNodes.Items = [.. sourceNodes.Items!, ActivationTypeProperty()];
            }
            await source.InstallAsync(sourceNodes, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            WotCompiledForm form = EventForm(source.EndpointUrl);
            if (change == "notifier")
            {
                form = new WotCompiledForm(form.Binding, form.AffordanceKind, form.AffordanceName, form.JsonPointer,
                    form.Operation, form.OpToken, form.Endpoint,
                    new WotAddressingDescriptor("nsu=" + SourceNamespace + ";s=Pump"),
                    form.OperationInfo, form.Payload, [], true, null, form.EventSelection, null);
            }
            ExpandedNodeId type = new("EventType", SourceNamespace);
            WotProjectionDocument document = TransparentProjection(
                form, EventDeclaration("transparent-forwarding", type));
            if (change == "localDeclaration")
            {
                using var stream = new MemoryStream(document.Sources[0].NodeSetXml, writable: false);
                UANodeSet types = UANodeSet.Read(stream)!;
                types.Items = [.. types.Items!, ActivationTypeProperty()];
                document = new WotProjectionDocument(document.ClosureKey,
                    [
                        new WotProjectionSource("unsupported-declarations", [SourceNamespace], Serialize(types)),
                        document.Sources[1]
                    ], document.BindingPlans);
            }
            var channels = new ActivationChannels(new NativeChannels(source.Session));
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            ArrayOf<NodeManagerRegistration> before = destination.Server.NodeManagerLifecycle.Registrations;
            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                _ = await host.AddAsync(document, ct).ConfigureAwait(false);
            });
            StatusCode expected = change switch
            {
                "missing" => StatusCodes.BadNodeIdUnknown,
                "lineage" or "abstract" => StatusCodes.BadTypeDefinitionInvalid,
                _ => StatusCodes.BadNotSupported
            };
            Assert.That(failure!.StatusCode, Is.EqualTo(expected));
            Assert.That(destination.Server.NodeManagerLifecycle.Registrations.ToArray(), Is.EqualTo(before.ToArray()));
            Assert.That(await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false), Is.Zero);
            Assert.That(channels.Channels, Has.Count.EqualTo(1));
            Assert.That(channels.Channels[0].DisposeCount, Is.EqualTo(1));
            Assert.That(channels.Channels[0].SubscribeCount, Is.Zero);
            Assert.That(channels.Channels[0].Source?.IsCurrent ?? false, Is.False);
            Assert.That(source.Session.SubscriptionCount, Is.Zero);
            Assert.That(source.Session.Connected, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeTransparentUnsupportedCaptureRejectsAndDisposesChannelAsync(bool unsupportedProvider)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var destination = new NativeEndpoint();
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            WotCompiledForm form = EventForm(destination.EndpointUrl);
            var fakeChannel = new FakeWotBindingChannel(form);
            var fakeFactory = new FakeWotBindingChannelFactory();
            fakeFactory.SetChannel(form, fakeChannel);
            var channels = new ActivationChannels(unsupportedProvider
                ? new NativeChannels(Mock.Of<ISession>()) : fakeFactory);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            ExpandedNodeId type = new("EventType", SourceNamespace);
            ArrayOf<NodeManagerRegistration> before = destination.Server.NodeManagerLifecycle.Registrations;
            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                _ = await host.AddAsync(TransparentProjection(
                    form, EventDeclaration("transparent-forwarding", type)), ct).ConfigureAwait(false);
            });
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(destination.Server.NodeManagerLifecycle.Registrations.ToArray(), Is.EqualTo(before.ToArray()));
            Assert.That(await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false), Is.Zero);
            Assert.That(channels.Channels, Has.Count.EqualTo(1));
            Assert.That(channels.Channels[0].CaptureCount, Is.EqualTo(1));
            Assert.That(channels.Channels[0].DisposeCount, Is.EqualTo(1));
            Assert.That(channels.Channels[0].SubscribeCount, Is.Zero);
            Assert.That(destination.Session.SubscriptionCount, Is.Zero);
            if (!unsupportedProvider)
            {
                Assert.That(fakeChannel.DisposeCount, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeTransparentInterruptedCaptureRetainsOldGenerationAsync(bool invalidateSource)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var captured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var channels = new ActivationChannels(new NativeChannels(source.Session))
            {
                AfterCapture = async (_, token) =>
                {
                    captured.TrySetResult(true);
                    if (invalidateSource)
                    {
                        await source.Session.CloseAsync(token).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    }
                }
            };
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            ExpandedNodeId localType = new("EventType", LocalNamespace);
            WotProjectionHandle active = await host.AddAsync(Projection(localType, EventForm(source.EndpointUrl),
                EventDeclaration("local-re-emission", localType)), ct).ConfigureAwait(false);
            try
            {
                ArrayOf<NodeManagerRegistration> before = destination.Server.NodeManagerLifecycle.Registrations;
                var generations = before.ConvertAll(registration => registration.Generation).ToArray();
                ExpandedNodeId type = new("EventType", SourceNamespace);
                Task<WotProjectionHandle> replacement = host.ShadowReloadAsync(active,
                    TransparentProjection(EventForm(source.EndpointUrl),
                        EventDeclaration("transparent-forwarding", type)), cancellation.Token).AsTask();
                await captured.Task.WaitAsync(ct).ConfigureAwait(false);
                if (invalidateSource)
                {
                    ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        _ = await replacement.ConfigureAwait(false));
                    Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                }
                else
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        _ = await replacement.ConfigureAwait(false));
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(destination.Server.NodeManagerLifecycle.Registrations.ToArray(),
                        Is.EqualTo(before.ToArray()));
                    Assert.That(destination.Server.NodeManagerLifecycle.Registrations
                        .ConvertAll(registration => registration.Generation).ToArray(), Is.EqualTo(generations));
                    Assert.That(channels.Channels, Has.Count.EqualTo(1));
                    Assert.That(channels.Channels[0].CaptureCount, Is.EqualTo(1));
                    Assert.That(channels.Channels[0].DisposeCount, Is.EqualTo(1));
                    Assert.That(channels.Channels[0].Source!.IsCurrent, Is.False);
                    Assert.That(channels.Channels[0].SubscribeCount, Is.Zero);
                    Assert.That(destination.Session.SubscriptionCount, Is.Zero);
                }
                Assert.That(await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false), Is.EqualTo(1));
            }
            finally
            {
                await host.RemoveAsync(active, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NativeTransparentLostHostContinuityRejectsBeforeOpeningSourceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var destination = new NativeEndpoint();
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            EventManager manager = destination.Server.CurrentInstance.EventManager;
            manager.AdmitEvent(destination.Server.CurrentInstance.DefaultSystemContext, new BaseEventState(null));
            Assert.That(StatusCode.IsBad(manager.EventIdentityAdmissionStatus), Is.True);
            var channels = new NativeChannels(destination.Session);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            ArrayOf<NodeManagerRegistration> before = destination.Server.NodeManagerLifecycle.Registrations;
            ExpandedNodeId type = new("EventType", SourceNamespace);
            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                _ = await host.AddAsync(TransparentProjection(EventForm(destination.EndpointUrl),
                    EventDeclaration("transparent-forwarding", type)), ct).ConfigureAwait(false);
            });
            Assert.That(failure!.StatusCode, Is.EqualTo(manager.EventIdentityAdmissionStatus));
            Assert.That(channels.OpenCount, Is.Zero);
            Assert.That(destination.Server.NodeManagerLifecycle.Registrations.ToArray(), Is.EqualTo(before.ToArray()));
            Assert.That(await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false), Is.Zero);
        }

        [Test]
        public async Task NativeTransparentKnownConditionAuthorityRejectsReplacementBeforePublicationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var first = new NativeEndpoint();
            await using var second = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await first.StartAsync(false, ct).ConfigureAwait(false);
            await second.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            var calls = Channel.CreateBounded<NativeConditionCall>(2);
            NodeId firstMethod = await InstallActionSourceAsync(first, calls.Writer, ct).ConfigureAwait(false);
            NodeId secondMethod = await InstallActionSourceAsync(second, calls.Writer, ct).ConfigureAwait(false);
            await first.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            await second.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var channels = new ActivationChannels(new EndpointNativeChannels(
                new Dictionary<string, ISession>(StringComparer.Ordinal)
                {
                    [first.EndpointUrl] = first.Session,
                    [second.EndpointUrl] = second.Session
                }));
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            WotProjectionDocument desired = ActionProjection("transparent-forwarding", second.EndpointUrl,
                NodeId.ToExpandedNodeId(secondMethod, second.Session.NamespaceUris).ToString());
            WotProjectionHandle active = await host.AddAsync(ActionProjection("transparent-forwarding", first.EndpointUrl,
                NodeId.ToExpandedNodeId(firstMethod, first.Session.NamespaceUris).ToString()), ct).ConfigureAwait(false);
            try
            {
                ArrayOf<NodeManagerRegistration> before = destination.Server.NodeManagerLifecycle.Registrations;
                ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    _ = await host.ShadowReloadAsync(active, desired, ct).ConfigureAwait(false);
                });
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(destination.Server.NodeManagerLifecycle.Registrations.ToArray(),
                    Is.EqualTo(before.ToArray()));
                Assert.That(channels.Channels[0].Source!.IsCurrent, Is.True);
                Assert.That(channels.Channels[^1].Source!.IsCurrent, Is.False);
                Assert.That(channels.Channels[^1].DisposeCount, Is.EqualTo(1));
                Assert.That(first.Session.SubscriptionCount, Is.Zero);
                Assert.That(second.Session.SubscriptionCount, Is.Zero);
                Assert.That(destination.Session.SubscriptionCount, Is.Zero);
                Assert.That(calls.Reader.TryRead(out _), Is.False);
            }
            finally
            {
                await host.RemoveAsync(active, CancellationToken.None).ConfigureAwait(false);
            }
            WotProjectionHandle recovered = await host.AddAsync(desired, ct).ConfigureAwait(false);
            await host.RemoveAsync(recovered, CancellationToken.None).ConfigureAwait(false);
            Assert.That(channels.Channels.TrueForAll(channel => channel.DisposeCount == 1), Is.True);
        }

        private static UAVariable ActivationTypeProperty()
        {
            return new UAVariable
            {
                NodeId = "ns=1;s=EventType.Units",
                BrowseName = "1:Units",
                DataType = "i=12",
                ValueRank = -1,
                References =
                [
                    new Reference { ReferenceType = "i=46", IsForward = false, Value = "ns=1;s=EventType" },
                    new Reference { ReferenceType = "i=40", Value = "i=68" }
                ]
            };
        }

        private sealed class ActivationChannels(IWotBindingChannelFactory inner) : IWotBindingChannelFactory
        {
            public List<ActivationChannel> Channels { get; } = [];

            public Func<WotEventSource, CancellationToken, ValueTask>? AfterCapture { get; init; }

            public async ValueTask<IWotBindingChannel> OpenChannelAsync(
                WotCompiledForm form, CancellationToken cancellationToken = default)
            {
                IWotBindingChannel opened = await inner.OpenChannelAsync(form, cancellationToken).ConfigureAwait(false);
                var channel = new ActivationChannel(opened, AfterCapture);
                Channels.Add(channel);
                return channel;
            }
        }

        private sealed class ActivationChannel(
            IWotBindingChannel inner,
            Func<WotEventSource, CancellationToken, ValueTask>? afterCapture) :
            IWotCapturedEventChannel, IWotCapturedConditionActionChannel
        {
            public WotCompiledForm Form => inner.Form;

            public WotEventSource? Source { get; private set; }

            public int CaptureCount { get; private set; }

            public int SubscribeCount { get; private set; }

            public int DisposeCount { get; private set; }

            public async ValueTask<WotEventSource> CaptureEventSourceAsync(CancellationToken cancellationToken = default)
            {
                CaptureCount++;
                if (inner is not IWotCapturedEventChannel capturing)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
                }
                Source = await capturing.CaptureEventSourceAsync(cancellationToken).ConfigureAwait(false);
                if (afterCapture is not null)
                {
                    await afterCapture(Source, cancellationToken).ConfigureAwait(false);
                }
                return Source;
            }

            public ValueTask<WotCapturedConditionAction> CaptureConditionActionAsync(
                CancellationToken cancellationToken = default)
            {
                return ((IWotCapturedConditionActionChannel)inner).CaptureConditionActionAsync(cancellationToken);
            }

            public ValueTask<IWotSubscription> SubscribeCapturedEventAsync(
                bool captureConditionFields, Action<WotNotification> onEvent,
                CancellationToken cancellationToken = default)
            {
                SubscribeCount++;
                return ((IWotCapturedEventChannel)inner).SubscribeCapturedEventAsync(
                    captureConditionFields, onEvent, cancellationToken);
            }

            public ValueTask<IWotSubscription> SubscribeCapturedEventAsync(
                NodeId coreEventType, Action<WotNotification> onEvent,
                CancellationToken cancellationToken = default)
            {
                SubscribeCount++;
                return ((IWotCapturedEventChannel)inner).SubscribeCapturedEventAsync(
                    coreEventType, onEvent, cancellationToken);
            }

            public ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
            {
                return inner.ReadAsync(cancellationToken);
            }

            public ValueTask<WotWriteResult> WriteAsync(DataValue value, CancellationToken cancellationToken = default)
            {
                return inner.WriteAsync(value, cancellationToken);
            }

            public ValueTask<WotInvokeResult> InvokeAsync(
                IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
            {
                return inner.InvokeAsync(inputs, cancellationToken);
            }

            public ValueTask<IWotSubscription> ObserveAsync(
                Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
            {
                return inner.ObserveAsync(onNotification, cancellationToken);
            }

            public ValueTask<IWotSubscription> SubscribeEventAsync(
                Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
            {
                SubscribeCount++;
                return inner.SubscribeEventAsync(onEvent, cancellationToken);
            }

            public async ValueTask DisposeAsync()
            {
                DisposeCount++;
                await inner.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
#endif
