// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using ISession = Opc.Ua.Client.ISession;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests
{
    public sealed partial class ManagedApplicationPushTests
    {
        /// <summary>
        /// A hosted non-singleton device publishes and independently authorizes its configured applications.
        /// </summary>
        [Test]
        [Category("Onboarding")]
        public async Task HostedProvisionableDevicePublishesAndGatesConfiguredApplications()
        {
            const string firstUri = "urn:hosted:provisionable:application:first";
            const string secondUri = "urn:hosted:provisionable:application:second";
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                using TestTarget first = await TestTarget.CreateAsync(firstUri).ConfigureAwait(false);
                using TestTarget second = await TestTarget.CreateAsync(secondUri).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var listener = new TcpListener(IPAddress.Loopback, 0);
                int port;
                try
                {
                    listener.Start();
                    port = ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                finally
                {
                    listener.Stop();
                }
                var endpoint = new Uri($"opc.tcp://localhost:{port}/hosted-provisionable");
                string password = Guid.NewGuid().ToString("N");
                var users = new LinqUserDatabase();
                Assert.That(users.CreateUser("administrator", Encoding.UTF8.GetBytes(password),
                    [Role.AuthenticatedUser, Role.SecurityAdmin]), Is.True);
                Assert.That(users.CreateUser("observer", Encoding.UTF8.GetBytes(password),
                    [Role.AuthenticatedUser]), Is.True);

                var firstAuthorization = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
                var secondAuthorization = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
                int firstApproved = 1;
                firstAuthorization.Setup(value => value.AuthorizeAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                    .Returns((ISystemContext _, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (Volatile.Read(ref firstApproved) == 0)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        return default;
                    });
                secondAuthorization.Setup(value => value.AuthorizeAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask));
                var applications = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict);
                applications.Setup(value => value.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                    [
                        new(first, firstAuthorization.Object),
                        new(second, secondAuthorization.Object)
                    ]));
                var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
                security.Setup(value => value.CreateApplicationOperations())
                    .Returns(static () => new TestApplicationOperations());
                int registrarInstalled = 1;
                security.Setup(value => value.RequireInstalledRegistrarAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                    .Returns((ISystemContext _, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (Volatile.Read(ref registrarInstalled) == 0)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        return default;
                    });
                int secondApproved = 0;
                var software = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
                software.Setup(value => value.RequireReadyAsync(firstUri, It.IsAny<CancellationToken>()))
                    .Returns(default(ValueTask));
                software.Setup(value => value.RequireReadyAsync(secondUri, It.IsAny<CancellationToken>()))
                    .Returns((string _, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (Volatile.Read(ref secondApproved) == 0)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        return default;
                    });

                var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(s_telemetry);
                services.AddSingleton<IUserDatabase>(users);
                services.AddSingleton<IUserManagement>(provider =>
                    new UserManagement(provider.GetRequiredService<IUserDatabase>()));
                services.AddSingleton(Mock.Of<ITicketStore>(MockBehavior.Strict));
                services.AddSingleton(Mock.Of<IOnboardingTicketValidator>(MockBehavior.Strict));
                services.AddSingleton(Mock.Of<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict));
                services.AddSingleton(security.Object);
                services.AddSingleton(software.Object);
                int providerSelections = 0;
                services.AddOpcUa().AddServer(options =>
                {
                    options.ApplicationName = "HostedProvisionableDeviceTest";
                    options.ApplicationUri = "urn:hosted:provisionable:device";
                    options.ProductUri = "urn:opcfoundation:test:hosted-provisionable";
                    options.PkiRoot = Path.Combine(root, "server");
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeSignAndEncryptPolicies = true;
                    options.IncludeUnsecurePolicyNone = false;
                    options.IncludeEccPolicies = false;
                    options.EndpointUrls.Add(endpoint.AbsoluteUri);
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                })
                    .AddDefaultIdentityAuthenticators(options =>
                    {
                        options.EnableAnonymous = false;
                        options.EnableUserNamePassword = true;
                        options.EnableX509 = false;
                        options.EnableJwt = false;
                    })
                    .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
                    {
                        Name = "SecurityAdmin",
                        Identities =
                        {
                            new RoleIdentityMappingOptions
                            {
                                CriteriaType = IdentityCriteriaType.UserName,
                                Criteria = "administrator"
                            }
                        }
                    }))
                    .AddProvisionableDeviceApplications(_ =>
                    {
                        Interlocked.Increment(ref providerSelections);
                        return applications.Object;
                    })
                    .AddProvisionableDeviceRuntime(false, [])
                    .AddStartupTask((_, _, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        started.TrySetResult(true);
                        return default;
                    });

                ServiceProvider container = services.BuildServiceProvider();
                BackgroundService? hosted = null;
                try
                {
                    hosted = container.GetServices<IHostedService>().OfType<BackgroundService>()
                        .Single(static service => service.GetType().Name == "OpcUaServerHostedService");
                    await hosted.StartAsync(deadline.Token).ConfigureAwait(false);
                    Task execution = hosted.ExecuteTask ??
                        throw new InvalidOperationException("The device host did not start.");
                    Task completed = await Task.WhenAny(
                        started.Task, execution, Task.Delay(Timeout.Infinite, deadline.Token)).ConfigureAwait(false);
                    if (completed == execution)
                    {
                        await execution.ConfigureAwait(false);
                        throw new InvalidOperationException("The device host stopped before startup completed.");
                    }
                    await started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);

                    Assert.That(container.GetRequiredService<IProvisionableDeviceApplicationsProvider>(),
                        Is.SameAs(applications.Object));
                    Assert.That(providerSelections, Is.EqualTo(1));
                    applications.Verify(value => value.GetApplicationsAsync(It.IsAny<CancellationToken>()),
                        Times.Once);

                    await using var client = new ClientFixture(telemetry: s_telemetry);
                    await client.LoadClientConfigurationAsync(Path.Combine(root, "client"), "HostedDeviceClient")
                        .ConfigureAwait(false);
                    await using ISession administrator = await client.ConnectAsync(
                        endpoint, SecurityPolicies.Basic256Sha256, default,
                        new UserIdentity("administrator", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                    int index = administrator.NamespaceUris.GetIndex(
                        ProvisionableDeviceNodeManagerFactory.NamespaceUri);
                    Assert.That(index, Is.GreaterThan(0));
                    ushort namespaceIndex = checked((ushort)index);
                    var deviceId = new NodeId(29878);
                    var firstId = new NodeId($"Application:{firstUri}", namespaceIndex);
                    var secondId = new NodeId($"Application:{secondUri}", namespaceIndex);

                    DataValue singleton = await administrator.ReadValueAsync(
                        new NodeId(29879), deadline.Token)
                        .ConfigureAwait(false);
                    Assert.That(singleton.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(singleton.WrappedValue.TryGetValue(out bool isSingleton), Is.True);
                    Assert.That(isSingleton, Is.False);
                    var device = new ProvisionableDeviceTypeClient(administrator, deviceId, s_telemetry);
                    Assert.That((await device.RequestTicketsAsync(deadline.Token).ConfigureAwait(false)).Count,
                        Is.Zero);

                    BrowseResponse browse = await administrator.BrowseAsync(
                        null,
                        null,
                        0,
                        [new BrowseDescription
                        {
                            NodeId = deviceId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                            IncludeSubtypes = true,
                            ResultMask = (uint)BrowseResultMask.All
                        }],
                        deadline.Token).ConfigureAwait(false);
                    Assert.That(browse.Results, Has.Count.EqualTo(1));
                    Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    var components = new HashSet<NodeId>();
                    foreach (ReferenceDescription reference in browse.Results[0].References)
                    {
                        components.Add(ExpandedNodeId.ToNodeId(reference.NodeId, administrator.NamespaceUris));
                    }
                    Assert.That(components, Does.Contain(firstId));
                    Assert.That(components, Does.Contain(secondId));

                    var firstClient = new ServerConfigurationTypeClient(administrator, firstId, s_telemetry);
                    var secondClient = new ServerConfigurationTypeClient(administrator, secondId, s_telemetry);
                    var (_, firstCertificates) = await firstClient.GetCertificatesAsync(NodeId.Null, deadline.Token)
                        .ConfigureAwait(false);
                    Assert.That(firstCertificates, Has.Count.EqualTo(1));
                    using var firstCertificate = Certificate.FromRawData(firstCertificates[0]);
                    Assert.That(firstCertificate.Thumbprint, Is.EqualTo(first.CurrentThumbprint()));

                    ServiceResultException? unapproved = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await secondClient.GetCertificatesAsync(NodeId.Null, deadline.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(unapproved!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    secondAuthorization.Verify(value => value.AuthorizeAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Never);
                    Interlocked.Exchange(ref secondApproved, 1);
                    var (_, secondCertificates) = await secondClient.GetCertificatesAsync(NodeId.Null, deadline.Token)
                        .ConfigureAwait(false);
                    Assert.That(secondCertificates, Has.Count.EqualTo(1));
                    using var secondCertificate = Certificate.FromRawData(secondCertificates[0]);
                    Assert.That(secondCertificate.Thumbprint, Is.EqualTo(second.CurrentThumbprint()));
                    Assert.That(secondCertificate.Thumbprint, Is.Not.EqualTo(firstCertificate.Thumbprint));
                    using var managementCertificate = Certificate.FromRawData(
                        administrator.ConfiguredEndpoint.Description.ServerCertificate);
                    Assert.That(managementCertificate.Thumbprint, Is.Not.EqualTo(firstCertificate.Thumbprint));
                    Assert.That(managementCertificate.Thumbprint, Is.Not.EqualTo(secondCertificate.Thumbprint));

                    Interlocked.Exchange(ref firstApproved, 0);
                    ServiceResultException? applicationDenied = await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await firstClient.GetCertificatesAsync(NodeId.Null, deadline.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(applicationDenied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    var (_, stillAvailable) = await secondClient.GetCertificatesAsync(NodeId.Null, deadline.Token)
                        .ConfigureAwait(false);
                    Assert.That(stillAvailable.Count, Is.EqualTo(1));
                    Assert.That(stillAvailable[0], Is.EqualTo(secondCertificates[0]));

                    Interlocked.Exchange(ref registrarInstalled, 0);
                    ServiceResultException? registrarDenied = await Assert.ThrowsAsync<ServiceResultException>(
                        async () => await secondClient.GetCertificatesAsync(NodeId.Null, deadline.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(registrarDenied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    software.Verify(value => value.RequireReadyAsync(secondUri, It.IsAny<CancellationToken>()),
                        Times.Exactly(3));
                    secondAuthorization.Verify(value => value.AuthorizeAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
                    Interlocked.Exchange(ref registrarInstalled, 1);
                    Interlocked.Exchange(ref firstApproved, 1);
                    await using ISession observer = await client.ConnectAsync(
                        endpoint, SecurityPolicies.Basic256Sha256, default,
                        new UserIdentity("observer", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                    var observerClient = new ServerConfigurationTypeClient(observer, firstId, s_telemetry);
                    ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await observerClient.GetCertificatesAsync(NodeId.Null, deadline.Token).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    firstAuthorization.Verify(value => value.AuthorizeAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
                    secondAuthorization.Verify(value => value.AuthorizeAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
                    software.Verify(value => value.RequireReadyAsync(firstUri, It.IsAny<CancellationToken>()),
                        Times.Exactly(2));
                    software.Verify(value => value.RequireReadyAsync(secondUri, It.IsAny<CancellationToken>()),
                        Times.Exactly(3));
                    security.Verify(value => value.CreateApplicationOperations(), Times.Exactly(2));
                    security.Verify(value => value.RequireInstalledRegistrarAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
                    await observer.CloseAsync(5000, true).ConfigureAwait(false);
                    await administrator.CloseAsync(5000, true).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (hosted != null)
                        {
                            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            await hosted.StopAsync(stop.Token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await container.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
