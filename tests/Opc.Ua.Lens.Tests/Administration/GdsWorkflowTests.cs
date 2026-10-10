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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.Gds;
using UaLens.Plugins.GdsManagement;
using UaLens.Plugins.GdsPush;
using UaLens.ViewModels;

namespace UaLens.Tests.Administration;

[TestFixture]
public sealed class GdsWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompositionUsesInjectedIssuanceClientAndDelivery(bool https)
    {
        await using var context = new AdministrationTestContext();
        var client = new Mock<IGdsManagementClient>(MockBehavior.Strict);
        client.SetupGet(value => value.EndpointUrl).Returns(GdsEndpoint);
        client.Setup(value => value.GetCertificateGroupsAsync(new NodeId(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrayOf<NodeId>.Empty);
        client.Setup(value => value.StartNewKeyPairRequestAsync(
                It.IsAny<GdsIssuanceTarget>(), NodeId.Null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeId(99));
        var certificate = new GdsIssuedCertificate(new NodeId(99), ByteString.From([1]), default, []);
        client.Setup(value => value.FinishRequestAsync(new NodeId(1), new NodeId(99), It.IsAny<CancellationToken>()))
            .ReturnsAsync(certificate);
        var delivery = new Mock<IGdsCertificateDelivery>(MockBehavior.Strict);
        delivery.Setup(value => value.DeliverAsync(
                It.Is<GdsIssuanceTarget>(target => target.Https == https), certificate, It.IsAny<CancellationToken>()))
            .ReturnsAsync("injected delivery");
        var issuance = new Mock<IGdsCertificateIssuance>(MockBehavior.Strict);
        issuance.Setup(value => value.IssueAndDeliverAsync(
                It.IsAny<GdsIssuanceTarget>(), client.Object, delivery.Object, It.IsAny<CancellationToken>()))
            .Returns((
                GdsIssuanceTarget target,
                IGdsManagementClient actualClient,
                IGdsCertificateDelivery actualDelivery,
                CancellationToken token) => new GdsCertificateIssuance().IssueAndDeliverAsync(
                    target, actualClient, actualDelivery, token));
        var services = new ServiceCollection();
        services.AddSingleton(client.Object);
        services.AddSingleton(delivery.Object);
        services.AddSingleton(issuance.Object);
        services.AddUaLens();
        await using ServiceProvider provider = services.BuildServiceProvider();
        IPluginFactory factory = provider.GetRequiredService<IPluginFactory>();
        await using var plugin = (GdsManagementPlugin)factory.Create(
            PluginKind.GdsManagement, context.Host,
            _ => throw new InvalidOperationException("The GDS composition factory was not used."));
        context.Workspace.CurrentRegisteredApp = Registration();
        plugin.SelectedApp = Application();

        await (https ? plugin.IssueNewHttpsCertificateCommand : plugin.IssueNewCertificateCommand)
            .ExecuteAsync(null).ConfigureAwait(false);

        Assert.That(plugin.LastOperationResult, Does.Contain("injected delivery"));
        issuance.Verify(value => value.IssueAndDeliverAsync(
            It.IsAny<GdsIssuanceTarget>(), client.Object, delivery.Object, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(value => value.StartNewKeyPairRequestAsync(
            It.Is<GdsIssuanceTarget>(target => target.Https == https), NodeId.Null,
            It.IsAny<CancellationToken>()), Times.Once);
        delivery.Verify(value => value.DeliverAsync(
            It.IsAny<GdsIssuanceTarget>(), certificate, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.Connection.IsConnected, Is.False);
    }

    [Test]
    public async Task CompositionUsesInjectedPushClient()
    {
        await using var context = new AdministrationTestContext();
        var client = new Mock<IGdsPushClient>(MockBehavior.Strict);
        client.Setup(value => value.ReadTrustListAsync(TrustListMasks.All, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrustListDataType());
        using var rejected = new CertificateCollection();
        client.Setup(value => value.GetRejectedListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rejected);
        var services = new ServiceCollection();
        services.AddSingleton(client.Object);
        services.AddUaLens();
        await using ServiceProvider provider = services.BuildServiceProvider();
        IPluginFactory factory = provider.GetRequiredService<IPluginFactory>();
        await using var plugin = (GdsPushPlugin)factory.Create(
            PluginKind.GdsPush, context.Host,
            _ => throw new InvalidOperationException("The push composition factory was not used."));
        plugin.Trusted.Add(new GdsCertItem { Thumbprint = "stale" });

        await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(false);

        Assert.That(plugin.Trusted, Is.Empty);
        Assert.That(plugin.LastOperationResult, Does.StartWith("Refreshed"));
        client.Verify(value => value.ReadTrustListAsync(TrustListMasks.All, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(value => value.GetRejectedListAsync(It.IsAny<CancellationToken>()), Times.Once);
        client.VerifyNoOtherCalls();
        Assert.That(context.Connection.IsConnected, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MatchingIssuanceKeepsCapturedApplicationAndDeliveryDespiteSelectionChange(bool https)
    {
        await using var context = new AdministrationTestContext();
        var client = new Mock<IGdsManagementClient>(MockBehavior.Strict);
        client.SetupGet(value => value.EndpointUrl).Returns(GdsEndpoint);
        var delivery = new Mock<IGdsCertificateDelivery>(MockBehavior.Strict);
        await using var plugin = new GdsManagementPlugin(
            context.Host, client: client.Object, delivery: delivery.Object);
        context.Workspace.CurrentRegisteredApp = Registration();
        plugin.SelectedApp = Application();
        var calls = new List<string>();
        GdsIssuanceTarget? requested = null;
        GdsIssuanceTarget? delivered = null;
        var certificate = new GdsIssuedCertificate(
            new NodeId(99), ByteString.From([1, 2, 3]), ByteString.From([4, 5]), []);
        client.Setup(value => value.GetCertificateGroupsAsync(new NodeId(1), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                calls.Add("groups:A");
                plugin.SelectedApp = Application(2);
                context.Workspace.CurrentRegisteredApp = Registration(2);
            })
            .ReturnsAsync(ArrayOf<NodeId>.Empty);
        client.Setup(value => value.StartNewKeyPairRequestAsync(
                It.IsAny<GdsIssuanceTarget>(), NodeId.Null, It.IsAny<CancellationToken>()))
            .Callback<GdsIssuanceTarget, NodeId, CancellationToken>((target, _, _) =>
            {
                requested = target;
                calls.Add("start:A");
            })
            .ReturnsAsync(certificate.RequestId);
        client.Setup(value => value.FinishRequestAsync(
                new NodeId(1), certificate.RequestId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("finish:A"))
            .ReturnsAsync(certificate);
        delivery.Setup(value => value.DeliverAsync(
                It.IsAny<GdsIssuanceTarget>(), certificate, It.IsAny<CancellationToken>()))
            .Callback<GdsIssuanceTarget, GdsIssuedCertificate, CancellationToken>((target, _, _) =>
            {
                delivered = target;
                calls.Add("deliver:A");
            })
            .ReturnsAsync("recorded delivery");

        await (https ? plugin.IssueNewHttpsCertificateCommand : plugin.IssueNewCertificateCommand)
            .ExecuteAsync(null).ConfigureAwait(false);

        Assert.That(calls, Is.EqualTo(s_expectedIssuanceCalls));
        Assert.That(delivered, Is.SameAs(requested));
        Assert.That(delivered!.ApplicationId, Is.EqualTo(new NodeId(1)));
        Assert.That(delivered.Https, Is.EqualTo(https));
        Assert.That(delivered.CertificateTypeId, Is.EqualTo(https
            ? Opc.Ua.ObjectTypeIds.HttpsCertificateType
            : Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType));
        Assert.That(delivered.Subject, Is.EqualTo("CN=Application 1"));
        Assert.That(delivered.Domains.ToArray(), Is.EqualTo(s_expectedDomains));
        Assert.That(delivered.DeliveryContext.CertificatePrivateKeyPath, Is.EqualTo("application1.pfx"));
        Assert.That(delivered.DeliveryContext.HttpsCertificatePrivateKeyPath, Is.EqualTo("https1.pfx"));
        Assert.That(plugin.LastOperationResult, Does.StartWith(https ? "Issued HTTPS cert" : "Issued cert"));
        Assert.That(plugin.IsBusy, Is.False);
    }

    [TestCase(2, GdsEndpoint, false, "another application")]
    [TestCase(1, GdsEndpoint, true, "Register the selected application")]
    [TestCase(1, OtherGdsEndpoint, false, "GDS endpoint")]
    [TestCase(1, "", false, "GDS endpoint")]
    public async Task InvalidIssuanceNeverInvokesClientOrDelivery(
        int selectedId, string registeredEndpoint, bool absent, string explanation)
    {
        await using var context = new AdministrationTestContext();
        var client = new Mock<IGdsManagementClient>(MockBehavior.Strict);
        client.SetupGet(value => value.EndpointUrl).Returns(GdsEndpoint);
        var delivery = new Mock<IGdsCertificateDelivery>(MockBehavior.Strict);
        await using var plugin = new GdsManagementPlugin(
            context.Host, client: client.Object, delivery: delivery.Object);
        plugin.SelectedApp = Application(selectedId);
        context.Workspace.CurrentRegisteredApp = absent
            ? null
            : Registration() with { GdsEndpointUrl = registeredEndpoint };

        await plugin.IssueNewCertificateCommand.ExecuteAsync(null).ConfigureAwait(false);

        Assert.That(plugin.LastOperationResult, Does.Contain(explanation));
        Assert.That(client.Invocations, Has.Count.EqualTo(1));
        Assert.That(client.Invocations[0].Method.Name, Is.EqualTo("get_EndpointUrl"));
        delivery.VerifyNoOtherCalls();
        Assert.That(plugin.IsBusy, Is.False);
    }

    [Test]
    public void ClientEndpointChangeIsRejectedBeforeDelivery()
    {
        var client = new Mock<IGdsManagementClient>(MockBehavior.Strict);
        string endpoint = GdsEndpoint;
        client.SetupGet(value => value.EndpointUrl).Returns(() => endpoint);
        GdsIssuanceTarget target = GdsIssuanceTarget.Create(
            Application(), Registration(), GdsEndpoint, Opc.Ua.ObjectTypeIds.HttpsCertificateType, true);
        client.Setup(value => value.GetCertificateGroupsAsync(new NodeId(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrayOf<NodeId>.Empty);
        client.Setup(value => value.StartNewKeyPairRequestAsync(target, NodeId.Null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeId(99));
        client.Setup(value => value.FinishRequestAsync(new NodeId(1), new NodeId(99), It.IsAny<CancellationToken>()))
            .Callback(() => endpoint = OtherGdsEndpoint)
            .ReturnsAsync(new GdsIssuedCertificate(new NodeId(99), ByteString.From([1]), default, []));
        var delivery = new Mock<IGdsCertificateDelivery>(MockBehavior.Strict);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new GdsCertificateIssuance().IssueAndDeliverAsync(target, client.Object, delivery.Object)
                .ConfigureAwait(false));
        delivery.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ImportedRegistrationRetainsGdsIdentityAndUsesSnapshotOfPushDestination()
    {
        RegisteredApplicationContext imported = RegisteredApplicationContextXml.ToRecord(
            RegisteredApplicationContextXml.ToDto(Registration() with
            {
                RegistrationType = GdsRegistrationType.ServerPush,
                PushEndpoint = new EndpointDescription("opc.tcp://application.example:4840/push")
            }));
        GdsIssuanceTarget target = GdsIssuanceTarget.Create(
            Application(), imported, GdsEndpoint, Opc.Ua.ObjectTypeIds.HttpsCertificateType, true);
        imported.PushEndpoint!.EndpointUrl = "opc.tcp://another.example:4840/push";
        target.DeliveryContext.PushEndpoint!.EndpointUrl = "opc.tcp://third.example:4840/push";
        var client = new Mock<IGdsManagementClient>(MockBehavior.Strict);
        client.SetupGet(value => value.EndpointUrl).Returns(GdsEndpoint);
        client.Setup(value => value.GetCertificateGroupsAsync(new NodeId(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrayOf<NodeId>.Empty);
        client.Setup(value => value.StartNewKeyPairRequestAsync(target, NodeId.Null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NodeId(99));
        client.Setup(value => value.FinishRequestAsync(new NodeId(1), new NodeId(99), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GdsIssuedCertificate(new NodeId(99), ByteString.From([1]), default, []));
        string? deliveredEndpoint = null;
        var delivery = new Mock<IGdsCertificateDelivery>(MockBehavior.Strict);
        delivery.Setup(value => value.DeliverAsync(
                target, It.IsAny<GdsIssuedCertificate>(), It.IsAny<CancellationToken>()))
            .Callback<GdsIssuanceTarget, GdsIssuedCertificate, CancellationToken>((captured, _, _) =>
                deliveredEndpoint = captured.DeliveryContext.PushEndpoint!.EndpointUrl)
            .ReturnsAsync("recorded push");

        await new GdsCertificateIssuance().IssueAndDeliverAsync(target, client.Object, delivery.Object)
            .ConfigureAwait(false);

        Assert.That(imported.GdsEndpointUrl, Is.EqualTo(GdsEndpoint));
        Assert.That(deliveredEndpoint, Is.EqualTo("opc.tcp://application.example:4840/push"));
        Assert.That(target.DeliveryContext.RegistrationType, Is.EqualTo(GdsRegistrationType.ServerPush));
    }

    [TestCase(true, false, false)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, true)]
    [TestCase(false, false, true)]
    public async Task RegistrationMutationRefreshOwnsBusyAndReportsDistinctFailures(
        bool register, bool refreshFails, bool mutationFails)
    {
        await using var context = new AdministrationTestContext();
        var client = new Mock<IGdsManagementClient>(MockBehavior.Strict);
        client.SetupGet(value => value.EndpointUrl).Returns(GdsEndpoint);
        var calls = new List<string>();
        var refresh = new TaskCompletionSource<ArrayOf<ApplicationDescription>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Setup(value => value.RegisterApplicationAsync(
                It.IsAny<ApplicationRecordDataType>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("register"))
            .Returns(() => mutationFails
                ? ValueTask.FromException<NodeId>(new InvalidOperationException("mutation denied"))
                : ValueTask.FromResult(new NodeId(1)));
        client.Setup(value => value.UnregisterApplicationAsync(new NodeId(1), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("unregister"))
            .Returns(() => mutationFails
                ? ValueTask.FromException(new InvalidOperationException("mutation denied"))
                : ValueTask.CompletedTask);
        client.Setup(value => value.QueryApplicationsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("query"))
            .Returns(() => new ValueTask<ArrayOf<ApplicationDescription>>(refresh.Task));
        client.Setup(value => value.FindApplicationAsync("urn:application:1", It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("resolve"))
            .ReturnsAsync((ArrayOf<ApplicationRecordDataType>)[Application().Record!]);
        await using var plugin = new GdsManagementPlugin(context.Host, client: client.Object);
        plugin.AllApps.Add(Application(2));

        Task operation = register ? plugin.RegisterAsync(Registration()) : plugin.UnregisterAsync(Application());
        if (!mutationFails)
        {
            Assert.That(plugin.IsBusy, Is.True);
            Assert.That(operation.IsCompleted, Is.False);
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(calls, Has.Count.EqualTo(2));
            if (refreshFails)
            {
                refresh.SetException(new InvalidOperationException("read denied"));
            }
            else
            {
                refresh.SetResult([new ApplicationDescription { ApplicationUri = "urn:application:1" }]);
            }
        }
        await operation.ConfigureAwait(false);

        Assert.That(plugin.IsBusy, Is.False);
        Assert.That(calls[0], Is.EqualTo(register ? "register" : "unregister"));
        if (mutationFails)
        {
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(plugin.LastOperationResult, Does.Contain("failed: mutation denied"));
        }
        else if (refreshFails)
        {
            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(plugin.LastOperationResult, Does.Contain("Retry Refresh, not the completed operation"));
            Assert.That(plugin.LastOperationResult, Does.StartWith(register ? "Registered" : "Unregistered"));
        }
        else
        {
            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(plugin.AllApps, Has.Count.EqualTo(1));
            Assert.That(plugin.FilteredApps[0].ApplicationId, Is.EqualTo(new NodeId(1)));
            Assert.That(plugin.FilteredApps[0].GdsEndpointUrl, Is.EqualTo(GdsEndpoint));
            if (register)
            {
                Assert.That(context.Workspace.CurrentRegisteredApp!.GdsEndpointUrl, Is.EqualTo(GdsEndpoint));
            }
        }
    }

    [TestCase(true, false, false)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, true)]
    [TestCase(false, false, true)]
    public async Task PushMutationRefreshOwnsBusyAndReportsDistinctFailures(
        bool add, bool refreshFails, bool mutationFails)
    {
        await using var context = new AdministrationTestContext();
        using X509Certificate2 certificate = CreateCertificate();
        string thumbprint = certificate.Thumbprint;
        var client = new Mock<IGdsPushClient>(MockBehavior.Strict);
        var calls = new List<string>();
        var refresh = new TaskCompletionSource<TrustListDataType>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Setup(value => value.AddCertificateAsync(
                It.IsAny<Certificate>(), true, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("add"))
            .Returns(() => mutationFails
                ? ValueTask.FromException(new InvalidOperationException("mutation denied"))
                : ValueTask.CompletedTask);
        client.Setup(value => value.RemoveCertificateAsync(thumbprint, true, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("remove"))
            .Returns(() => mutationFails
                ? ValueTask.FromException(new InvalidOperationException("mutation denied"))
                : ValueTask.CompletedTask);
        client.Setup(value => value.ReadTrustListAsync(TrustListMasks.All, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("read"))
            .Returns(() => new ValueTask<TrustListDataType>(refresh.Task));
        using var rejected = new CertificateCollection();
        client.Setup(value => value.GetRejectedListAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("rejected"))
            .ReturnsAsync(rejected);
        await using var plugin = new GdsPushPlugin(context.Host, client.Object);
        plugin.SelectedCertificate = new GdsCertItem { Thumbprint = thumbprint, Subject = "Application" };
        plugin.Trusted.Add(new GdsCertItem { Thumbprint = "stale" });

        Task operation = add
            ? plugin.AddCertificateAsync(new AddCertificateResult(certificate, TrustListBucket.Trusted))
            : plugin.RemoveCertificateCommand.ExecuteAsync(null);
        if (!mutationFails)
        {
            Assert.That(plugin.IsBusy, Is.True);
            Assert.That(operation.IsCompleted, Is.False);
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(false);
            Assert.That(calls, Has.Count.EqualTo(2));
            if (refreshFails)
            {
                refresh.SetException(new InvalidOperationException("read denied"));
            }
            else
            {
                refresh.SetResult(new TrustListDataType
                {
                    TrustedCertificates = add ? [ByteString.From(certificate.RawData)] : []
                });
            }
        }
        await operation.ConfigureAwait(false);

        Assert.That(plugin.IsBusy, Is.False);
        Assert.That(calls[0], Is.EqualTo(add ? "add" : "remove"));
        if (mutationFails)
        {
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(plugin.LastOperationResult, Does.Contain("failed: mutation denied"));
        }
        else if (refreshFails)
        {
            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(plugin.LastOperationResult, Does.Contain("Retry Refresh, not the completed operation"));
            Assert.That(plugin.LastOperationResult, Does.StartWith(add ? "Added" : "Removed"));
        }
        else
        {
            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(plugin.Trusted, Has.Count.EqualTo(add ? 1 : 0));
            if (add)
            {
                Assert.That(plugin.Trusted[0].Thumbprint, Is.EqualTo(thumbprint));
            }
        }
    }

    private static RegisteredApplicationContext Registration(int id = 1)
        => new(new NodeId((uint)id), $"urn:application:{id}", $"Application {id}", "urn:product",
            GdsRegistrationType.ClientPull, [], [],
            Domains: $"application{id}.example, localhost",
            CertificateSubjectName: $"CN=Application {id}",
            CertificatePublicKeyPath: $"application{id}.cer",
            CertificatePrivateKeyPath: $"application{id}.pfx",
            HttpsCertificatePublicKeyPath: $"https{id}.cer",
            HttpsCertificatePrivateKeyPath: $"https{id}.pfx")
        {
            GdsEndpointUrl = GdsEndpoint
        };

    private static RegisteredApp Application(int id = 1)
        => RegisteredApp.FromRecord(new ApplicationRecordDataType
        {
            ApplicationId = new NodeId((uint)id),
            ApplicationUri = $"urn:application:{id}",
            ApplicationNames = [new LocalizedText($"Application {id}")]
        }, GdsEndpoint);

    private static X509Certificate2 CreateCertificate()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Application", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private const string GdsEndpoint = "opc.tcp://gds-a.example:4840/GDS";
    private const string OtherGdsEndpoint = "opc.tcp://gds-b.example:4840/GDS";
    private static readonly string[] s_expectedIssuanceCalls = ["groups:A", "start:A", "finish:A", "deliver:A"];
    private static readonly string[] s_expectedDomains = ["application1.example", "localhost"];
}
