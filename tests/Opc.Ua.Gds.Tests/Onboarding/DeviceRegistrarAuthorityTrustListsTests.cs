/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests managed authority file services independently of disk storage and server transport.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarAuthorityTrustListsTests
    {
        /// <summary>
        /// Both nodes use managed locations and release the independently opened descriptor handles.
        /// </summary>
        [Test]
        public async Task AuthorityNodesUseManagedStoreResolverAndDisposeOwnedHandles()
        {
            var harness = new Harness();
            using (var binding = harness.Bind())
            {
                foreach (TrustListState node in new[]
                {
                    harness.Administration.TicketAuthorities!,
                    harness.Administration.DeviceIdentityAuthorities!
                })
                {
                    OpenWithMasksMethodState method = node.OpenWithMasks!;
                    OpenWithMasksMethodStateResult result = await method.OnCallAsync!(
                        harness.Context, method, node.NodeId, 0, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                    Assert.That(result.FileHandle, Is.Not.Zero);
                    Assert.That(node.Writable!.Value, Is.True);
                    Assert.That(node.UserWritable!.Value, Is.True);
                    Assert.That(node.RolePermissions.Count, Is.EqualTo(1));
                    Assert.That(node.RolePermissions[0].RoleId, Is.EqualTo(harness.RegistrarRole));
                }
                foreach (Mock<ICertificateStore> descriptor in harness.Descriptors)
                {
                    descriptor.Verify(s => s.Dispose(), Times.Once);
                }
                harness.Resolver.Verify(r => r.OpenCertificateStore(
                    It.IsAny<string>(), "TestStore", true), Times.Exactly(4));
            }
            foreach (Mock<ICertificateStore> store in harness.Stores)
            {
                store.Verify(s => s.Dispose(), Times.Once);
            }
        }

        /// <summary>
        /// Writes notify only the corresponding managed list, without promoting another list's contents.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AddingAuthorityNotifiesOnlyItsManagedTrustList(bool identityAuthorities)
        {
            var harness = new Harness();
            using var binding = harness.Bind();
            using Certificate certificate = CertificateBuilder.Create("CN=Registrar authority")
                .SetRSAKeySize(2048).CreateForRSA();
            TrustListState node = identityAuthorities
                ? harness.Administration.DeviceIdentityAuthorities!
                : harness.Administration.TicketAuthorities!;
            AddCertificateMethodState method = node.AddCertificate!;

            AddCertificateMethodStateResult result = await method.OnCallAsync!(
                harness.Context, method, node.NodeId, ByteString.From(certificate.RawData.AsSpan()),
                true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            TrustListIdentifier expected = identityAuthorities
                ? OnboardingIdentityTrustProvider.DeviceIdentityAuthorities
                : OnboardingTicketTrustProvider.TicketAuthorities;
            harness.Manager.Verify(m => m.NotifyTrustListChanged(expected, true, false), Times.Once);
            harness.Manager.Verify(m => m.NotifyTrustListChanged(
                It.Is<TrustListIdentifier>(id => id != expected), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
            int trustedIndex = identityAuthorities ? 2 : 0;
            harness.Stores[trustedIndex].Verify(s => s.AddAsync(
                It.IsAny<Certificate>(), null, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Neither a role label on an insecure channel nor a secure channel without that role grants access.
        /// </summary>
        [TestCase(false, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(true, MessageSecurityMode.Sign)]
        [TestCase(true, MessageSecurityMode.None)]
        public async Task AuthorityReadsRequireEncryptedChannelAndRegistrarAdmin(
            bool admin, MessageSecurityMode mode)
        {
            StatusCode expected = admin ? StatusCodes.BadSecurityModeInsufficient : StatusCodes.BadUserAccessDenied;
            var harness = new Harness(admin, mode);
            using var binding = harness.Bind();
            TrustListState node = harness.Administration.TicketAuthorities!;
            OpenWithMasksMethodState method = node.OpenWithMasks!;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await method.OnCallAsync!(
                    harness.Context, method, node.NodeId, 0, CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            harness.Resolver.Verify(r => r.OpenCertificateStore(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// Missing issuer configuration cannot silently substitute the trusted store or leave its first binding open.
        /// </summary>
        [Test]
        public void MissingIssuerStoreRejectsBindingAndDisposesPreviouslyOwnedDescriptors()
        {
            var harness = new Harness();
            harness.Manager.Setup(m => m.OpenIssuerStore(
                OnboardingIdentityTrustProvider.DeviceIdentityAuthorities)).Returns((ICertificateStore?)null);

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() => harness.Bind());

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(harness.Administration.TicketAuthorities!.Handle, Is.Null);
            foreach (Mock<ICertificateStore> descriptor in harness.Descriptors.Take(3))
            {
                descriptor.Verify(s => s.Dispose(), Times.Once);
            }
        }

        /// <summary>
        /// A live binding cannot be replaced without its owner shutting it down.
        /// </summary>
        [Test]
        public void RebindingDoesNotReplaceExistingAuthorityHandlers()
        {
            var harness = new Harness();
            using var binding = harness.Bind();
            TrustListState node = harness.Administration.TicketAuthorities!;
            OpenWithMasksMethodStateMethodAsyncCallHandler original = node.OpenWithMasks!.OnCallAsync!;

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() => harness.Bind());

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(node.OpenWithMasks.OnCallAsync, Is.SameAs(original));
        }

        /// <summary>
        /// A custom manager cannot silently fall back to process-global certificate store resolution.
        /// </summary>
        [Test]
        public void MissingManagerScopedResolverRejectsBeforeOpeningStoresOrBindingMethods()
        {
            var harness = new Harness();
            var manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                new DeviceRegistrarAuthorityTrustLists(
                    harness.Administration, harness.Context, manager.Object,
                    new Mock<IAuditEventServer>().Object, harness.Telemetry));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            manager.VerifyNoOtherCalls();
            Assert.That(harness.Administration.TicketAuthorities!.Handle, Is.Null);
            Assert.That(harness.Administration.DeviceIdentityAuthorities!.Handle, Is.Null);
        }

        /// <summary>
        /// Provides independently owned store descriptors and file-service stores through a manager-scoped resolver.
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// Loads the generated model and an authenticated session without opening a socket or disk store.
            /// </summary>
            public Harness(
                bool admin = true,
                MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt)
            {
                Telemetry = NUnitTelemetryContext.Create();
                ServiceMessageContext messages = ServiceMessageContext.Create(Telemetry);
                messages.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
                messages.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
                RegistrarRole = ExpandedNodeId.ToNodeId(
                    Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, messages.NamespaceUris);
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(i => i.GrantedRoleIds).Returns(admin ? [RegistrarRole] : []);
                var session = new Mock<Opc.Ua.Server.ISession>();
                session.SetupGet(s => s.Id).Returns(new NodeId("authority-session", 0));
                session.SetupGet(s => s.EffectiveIdentity).Returns(identity.Object);
                var channel = new SecureChannelContext(
                    "authority-channel",
                    new EndpointDescription
                    {
                        SecurityMode = mode,
                        SecurityPolicyUri = mode == MessageSecurityMode.None
                            ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256
                    },
                    RequestEncoding.Binary, new byte[] { 1, 2, 3 });
                var operation = new OperationContext(
                    new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, session.Object);
                Context = new SessionSystemContext(operation, Telemetry)
                {
                    NamespaceUris = messages.NamespaceUris,
                    ServerUris = messages.ServerUris,
                    EncodeableFactory = messages.Factory,
                    TypeTable = new TypeTable(messages.NamespaceUris)
                };
                Administration = new NodeStateCollection().AddOpcUaOnboarding(Context)
                    .OfType<DeviceRegistrarState>().Single().Administration!;
                Resolver = Manager.As<ICertificateStoreResolver>();
                TrustListIdentifier[] lists =
                [
                    OnboardingTicketTrustProvider.TicketAuthorities,
                    OnboardingIdentityTrustProvider.DeviceIdentityAuthorities
                ];
                for (int i = 0; i < lists.Length; i++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int index = (i * 2) + j;
                        string path = $"authority-store-{index}";
                        Descriptors[index] = new Mock<ICertificateStore>();
                        Descriptors[index].SetupGet(s => s.StorePath).Returns(path);
                        Descriptors[index].SetupGet(s => s.StoreType).Returns("TestStore");
                        Stores[index] = new Mock<ICertificateStore>();
                        Stores[index].Setup(s => s.FindByThumbprintAsync(
                            It.IsAny<string>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(() => new CertificateCollection());
                        Stores[index].Setup(s => s.AddAsync(
                            It.IsAny<Certificate>(), null, It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);
                        Resolver.Setup(r => r.OpenCertificateStore(path, "TestStore", true))
                            .Returns(Stores[index].Object);
                    }
                    Manager.Setup(m => m.OpenTrustedStore(lists[i])).Returns(Descriptors[i * 2].Object);
                    Manager.Setup(m => m.OpenIssuerStore(lists[i])).Returns(Descriptors[(i * 2) + 1].Object);
                }
            }

            /// <summary>
            /// Creates the owned binding over the test's managed locations.
            /// </summary>
            public DeviceRegistrarAuthorityTrustLists Bind()
            {
                return new DeviceRegistrarAuthorityTrustLists(
                    Administration, Context, Manager.Object, new Mock<IAuditEventServer>().Object, Telemetry);
            }

            /// <summary>
            /// The test's telemetry context.
            /// </summary>
            public ITelemetryContext Telemetry { get; }

            /// <summary>
            /// The generated registrar administration instance.
            /// </summary>
            public DeviceRegistrarAdminState Administration { get; }

            /// <summary>
            /// The authenticated method-call context.
            /// </summary>
            public SessionSystemContext Context { get; }

            /// <summary>
            /// The namespace-correct administrative role identifier.
            /// </summary>
            public NodeId RegistrarRole { get; }

            /// <summary>
            /// The borrowed manager, including trust-change notifications.
            /// </summary>
            public Mock<ICertificateManager> Manager { get; } = new();

            /// <summary>
            /// The same manager's instance-scoped certificate store resolver.
            /// </summary>
            public Mock<ICertificateStoreResolver> Resolver { get; }

            /// <summary>
            /// The independently owned descriptors opened while binding.
            /// </summary>
            public Mock<ICertificateStore>[] Descriptors { get; } = new Mock<ICertificateStore>[4];

            /// <summary>
            /// The independently owned stores used by file-service operations.
            /// </summary>
            public Mock<ICertificateStore>[] Stores { get; } = new Mock<ICertificateStore>[4];
        }
    }
}
