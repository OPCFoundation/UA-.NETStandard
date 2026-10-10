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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("add")]
        [TestCase("shadow")]
        [TestCase("immediate")]
        public async Task NativeTransparentUnauthenticatedSourceRejectsBeforePublicationAsync(string operation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct, SecurityPolicies.None).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var channels = new NativeChannels(source.Session);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            WotProjectionHandle? active = null;
            try
            {
                if (operation != "add")
                {
                    ExpandedNodeId localType = new("EventType", LocalNamespace);
                    active = await host.AddAsync(Projection(localType, EventForm(source.EndpointUrl),
                        EventDeclaration("local-re-emission", localType)), ct).ConfigureAwait(false);
                }
                ArrayOf<NodeManagerRegistration> before = destination.Server.NodeManagerLifecycle.Registrations;
                var generations = before.ConvertAll(registration => (registration.Id, registration.Generation))
                    .ToArray();
                int ownersBefore = await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false);
                ExpandedNodeId sourceType = new("EventType", SourceNamespace);
                WotProjectionDocument document = TransparentProjection(EventForm(source.EndpointUrl),
                    EventDeclaration("transparent-forwarding", sourceType));
                StatusCode status = StatusCodes.Good;
                try
                {
                    active = operation switch
                    {
                        "shadow" => await host.ShadowReloadAsync(active!, document, ct).ConfigureAwait(false),
                        "immediate" => await host.ImmediateReloadAsync(active!, document, ct).ConfigureAwait(false),
                        _ => await host.AddAsync(document, ct).ConfigureAwait(false)
                    };
                }
                catch (ServiceResultException exception)
                {
                    status = exception.StatusCode;
                }

                int ownersAfter = await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false);
                ArrayOf<NodeManagerRegistration> after = destination.Server.NodeManagerLifecycle.Registrations;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                    Assert.That(after.ConvertAll(registration => (registration.Id, registration.Generation)).ToArray(),
                        Is.EqualTo(generations), "Admission must fail before any live registration changes.");
                    Assert.That(after.ToArray(), Is.EqualTo(before.ToArray()),
                        "The original generation must remain active.");
                    Assert.That(ownersAfter, Is.EqualTo(ownersBefore),
                        "A failed initial activation must not expose its notifier through native Browse.");
                    Assert.That(channels.OpenCount, Is.EqualTo(1));
                    Assert.That(source.Session.SubscriptionCount, Is.Zero);
                    Assert.That(destination.Session.SubscriptionCount, Is.Zero);
                    Assert.That(source.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.None));
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

        [TestCase("transparent-forwarding", 1)]
        [TestCase("local-re-emission", 0)]
        public async Task NativeEventActivationDoesNotRequireNotificationsOrSubscribersAsync(
            string mode, int expectedChannels)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var channels = new NativeChannels(source.Session);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            ExpandedNodeId type = new("EventType",
                mode == "transparent-forwarding" ? SourceNamespace : LocalNamespace);
            WotProjectionHandle handle = await host.AddAsync(
                Projection(type, EventForm(source.EndpointUrl), EventDeclaration(mode, type)), ct).ConfigureAwait(false);
            try
            {
                Assert.That(await BrowseActivationOwnersAsync(destination, ct).ConfigureAwait(false), Is.EqualTo(1));
                NodeManagerRegistration registration = destination.Server.NodeManagerLifecycle.Registrations
                    .Span.ToArray().Single(value => value.Id == handle.Registration!.Id);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(registration.Generation, Is.EqualTo(handle.Generation));
                    Assert.That(channels.OpenCount, Is.EqualTo(expectedChannels));
                    Assert.That(source.Session.SubscriptionCount, Is.Zero);
                    Assert.That(destination.Session.SubscriptionCount, Is.Zero);
                    Assert.That(source.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task<int> BrowseActivationOwnersAsync(NativeEndpoint destination, CancellationToken ct)
        {
            await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            BrowseResponse response = await destination.Session.BrowseAsync(null, null, 0,
                [
                    new BrowseDescription
                    {
                        NodeId = Ua.ObjectIds.ObjectsFolder,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], ct).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.True);
            return response.Results[0].References.Span.ToArray().Count(reference =>
                reference.BrowseName.Name == "Owner" &&
                destination.Session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex) == LocalNamespace);
        }
    }
}
#endif
