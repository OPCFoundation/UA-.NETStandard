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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Plugins.Continuity;
using UaLens.Telemetry;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Tests.Diagnose;

internal sealed class ContinuityStackTestContext
{
    public ContinuityStackTestContext(string name)
    {
        Name = name;
        Telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        ServiceMessageContext context = ServiceMessageContext.Create(Telemetry);
        Session.SetupGet(value => value.Connected).Returns(() => Connected);
        Session.SetupGet(value => value.SessionId).Returns(new NodeId(1u));
        Session.SetupGet(value => value.MessageContext).Returns(context);
        Session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
        Session.SetupGet(value => value.OperationLimits).Returns(new OperationLimits());
        Session.SetupGet(value => value.Identity).Returns(Identity.Object);
        Session.SetupGet(value => value.Endpoint).Returns(new EndpointDescription
        {
            EndpointUrl = "opc.tcp://continuity.test:4840",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            Server = new ApplicationDescription { ApplicationUri = "urn:continuity:test" }
        });
        Identity.SetupGet(value => value.TokenType).Returns(UserTokenType.Anonymous);
        ISubscriptionManager? manager = Manager.Object;
        Session.Setup(value => value.TryGetSubscriptionManager(out manager)).Returns(true);
        Manager.Setup(value => value.Add(It.IsAny<ISubscriptionNotificationHandler>(),
            It.IsAny<IOptionsMonitor<SubscriptionOptions>>()))
            .Returns((ISubscriptionNotificationHandler _, IOptionsMonitor<SubscriptionOptions> options) =>
            {
                Trace.Add("Add");
                LastOptions = options.CurrentValue;
                return AddSubscription((uint)(100 + Subscriptions.Count)).Subscription.Object;
            });
        Manager.SetupGet(value => value.Items).Returns(() =>
            Subscriptions.Where(value => !value.Disposed).Select(value => (ISubscription)value.Subscription.Object)
                .Concat(Unrelated).ToArray());
        Manager.Setup(value => value.SaveAsync(It.IsAny<Stream>(), It.IsAny<IServiceMessageContext>(),
            It.IsAny<IEnumerable<ISubscription>?>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, IServiceMessageContext _, IEnumerable<ISubscription>? subscriptions,
                CancellationToken ct) =>
            {
                SavedSubscriptions = subscriptions!.ToList();
                Trace.Add("Save");
                return stream.WriteAsync(new byte[] { 1, 2, 3, 4 }, ct);
            });
        Manager.Setup(value => value.LoadAsync(It.IsAny<Stream>(), It.IsAny<IServiceMessageContext>(),
            It.IsAny<Func<string, ISubscriptionNotificationHandler>>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((Stream _, IServiceMessageContext _, Func<string, ISubscriptionNotificationHandler> factory,
                bool transfer, CancellationToken ct) => LoadAsync(factory, transfer, ct));
    }

    public string Name { get; }

    public AppTelemetryContext Telemetry { get; }

    public Mock<ISession> Session { get; } = new();

    public Mock<IUserIdentity> Identity { get; } = new();

    public Mock<ISubscriptionManager> Manager { get; } = new();

    public List<string> Trace { get; } = new();

    public List<ContinuityStackTestSubscription> Subscriptions { get; } = new();

    public List<ISubscription> Unrelated { get; } = new();

    public List<ISubscription> SavedSubscriptions { get; private set; } = new();

    public bool Connected { get; set; } = true;

    public bool DenyDurability { get; set; }

    public bool TransferSucceeds { get; set; }

    public bool ReportLoadState { get; set; } = true;

    public bool? TransferRequested { get; private set; }

    public SubscriptionOptions? LastOptions { get; private set; }

    private ContinuityStackTestSubscription AddSubscription(uint serverId)
    {
        var subscription = new ContinuityStackTestSubscription(this, serverId);
        Subscriptions.Add(subscription);
        return subscription;
    }

    private async ValueTask<IReadOnlyList<ISubscription>> LoadAsync(
        Func<string, ISubscriptionNotificationHandler> factory,
        bool transfer,
        CancellationToken ct)
    {
        TransferRequested = transfer;
        Trace.Add("Load");
        ContinuityStackTestSubscription subscription = AddSubscription(transfer && TransferSucceeds ? 100u : 200u);
        subscription.AddItem("sample-1");
        ISubscriptionNotificationHandler handler = factory("sample");
        if (ReportLoadState)
        {
            await handler.OnSubscriptionStateChangedAsync(
                subscription.Subscription.Object,
                transfer && TransferSucceeds ? SubscriptionState.Modified : SubscriptionState.Created,
                transfer && TransferSucceeds ? PublishState.Transferred : PublishState.None,
                ct).ConfigureAwait(false);
        }
        return new[] { subscription.Subscription.Object };
    }
}
