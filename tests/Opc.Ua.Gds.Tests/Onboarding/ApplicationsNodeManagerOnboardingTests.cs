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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies the GDS request handlers' argument-specific onboarding gate before request-store access.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ApplicationsNodeManagerOnboardingTests : GdsTestFixture
    {
        /// <summary>
        /// A GDS administrator cannot bypass a known device's software or ownership denial.
        /// </summary>
        [TestCase("newKey")]
        [TestCase("signing")]
        [TestCase("finish")]
        [TestCase("trustList")]
        [TestCase("certificates")]
        public async Task KnownOnboardingDenialCannotFallBackToAdministratorPrivilege(string handler)
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using var manager = CreateManager(database, requests.Object);
            ApplicationRecordDataType application = CreateTestApplicationRecord("OnboardingGateDenied");
            NodeId applicationId = database.RegisterApplication(application);
            application.ApplicationId = applicationId;
            ServerSystemContext context = CreateContext(GdsRole.CertificateAuthorityAdmin);
            var gate = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
            var denial = new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            gate.Setup(g => g.AuthorizeApplicationAsync(
                context, It.Is<ApplicationRecordDataType>(a => a.ApplicationId == applicationId),
                It.IsAny<CancellationToken>())).ThrowsAsync(denial);
            manager.DeviceRegistrarCertificateAuthorization = gate.Object;

            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(manager, handler, context, applicationId).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(denial));
            gate.Verify(g => g.AuthorizeApplicationAsync(
                context, It.Is<ApplicationRecordDataType>(a => a.ApplicationId == applicationId),
                It.IsAny<CancellationToken>()), Times.Once);
            gate.VerifyNoOtherCalls();
            requests.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An unrelated application must still pass the original GDS authorization, even when onboarding is enabled.
        /// </summary>
        [TestCase("newKey")]
        [TestCase("signing")]
        [TestCase("finish")]
        [TestCase("trustList")]
        [TestCase("certificates")]
        public async Task ApplicationOutsideOnboardingRetainsOriginalGdsAuthorization(string handler)
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using var manager = CreateManager(database, requests.Object);
            ApplicationRecordDataType application = CreateTestApplicationRecord("OutsideOnboarding");
            NodeId applicationId = database.RegisterApplication(application);
            ServerSystemContext context = CreateContext(Role.AuthenticatedUser);
            var gate = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
            gate.Setup(g => g.AuthorizeApplicationAsync(
                context, It.Is<ApplicationRecordDataType>(a => a.ApplicationId == applicationId),
                It.IsAny<CancellationToken>())).ReturnsAsync(false);
            manager.DeviceRegistrarCertificateAuthorization = gate.Object;

            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(manager, handler, context, applicationId).ConfigureAwait(false));

            Assert.That(actual!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            requests.VerifyNoOtherCalls();
            gate.Verify(g => g.AuthorizeApplicationAsync(
                context, It.IsAny<ApplicationRecordDataType>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Without onboarding configured, the existing administrator/error-result behavior is unchanged.
        /// </summary>
        [TestCase("newKey")]
        [TestCase("signing")]
        [TestCase("finish")]
        [TestCase("trustList")]
        [TestCase("certificates")]
        public async Task DefaultHandlersPreserveUnknownApplicationResult(string handler)
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using var manager = CreateManager(database, requests.Object);
            ServiceResult result = await InvokeAsync(
                manager, handler, CreateContext(GdsRole.CertificateAuthorityAdmin),
                new NodeId(Guid.NewGuid(), manager.ApplicationsNamespaceIndex)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            requests.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Onboarding cannot stage an unrecoverable pending credential without an explicit issuance store.
        /// </summary>
        [Test]
        public async Task OnboardingCompletionRequiresIssuanceStoreBeforeRequestAccess()
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using var manager = CreateManager(database, requests.Object);
            NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("MissingIssuanceStore"));
            ServerSystemContext context = CreateContext(GdsRole.CertificateAuthorityAdmin);
            var authorization = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
            authorization.Setup(g => g.AuthorizeApplicationAsync(
                context, It.IsAny<ApplicationRecordDataType>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            manager.DeviceRegistrarCertificateAuthorization = authorization.Object;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(manager, "finish", context, applicationId).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            requests.VerifyNoOtherCalls();
            authorization.Verify(g => g.RecordCertificateIssuedAsync(
                It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(), It.IsAny<ByteString>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A retained receipt cannot bypass Part 12's rejection of an already accepted request.
        /// </summary>
        [Test]
        public async Task AcceptedRequestDoesNotReplayItsRetainedIssuance()
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using var manager = CreateManager(database, requests.Object);
            NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("AcceptedIssuance"));
            NodeId requestId = new("request", 1);
            string? group = null;
            string? type = null;
            ByteString certificate = default;
            ByteString privateKey = default;
            requests.Setup(r => r.FinishRequest(
                applicationId, requestId, out group, out type, out certificate, out privateKey))
                .Returns(CertificateRequestState.Accepted);
            var issuanceStore = new Mock<ICertificateRequestIssuanceStore>(MockBehavior.Strict);
            manager.CertificateRequestIssuanceStore = issuanceStore.Object;

            ServiceResult result = await InvokeAsync(
                manager, "finish", CreateContext(GdsRole.CertificateAuthorityAdmin), applicationId)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            issuanceStore.VerifyNoOtherCalls();
            requests.Verify(r => r.AcceptRequest(It.IsAny<NodeId>(), It.IsAny<ByteString>()), Times.Never);
        }

        /// <summary>
        /// A recreated handler reuses published material when pending-state publication failed before acceptance.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecreatedManagerReusesReceiptAfterPendingPublicationFailsWithoutConsumingRequest(
            bool newKeyPair)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var database = new LinqApplicationsDatabase();
                var secrets = new InMemorySecretStore();
                using Certificate issued = CertificateBuilder.Create("CN=Receipt retry").SetRSAKeySize(2048)
                    .CreateForRSA();
                ByteString certificate = ByteString.From(issued.RawData);
                ByteString privateKey = newKeyPair ? ByteString.From(new byte[] { 5, 6, 7 }) : default;
                Mock<ICertificateGroup> group = CreateCertificateGroup(path, certificate);
                char[]? passwordStaging = null;
                group.Setup(g => g.NewKeyPairRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string>(),
                    It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<char[]>(),
                    It.IsAny<CancellationToken>())).Callback<
                        ApplicationRecordDataType, NodeId, string, string[], string, char[], CancellationToken>(
                            (_, _, _, _, _, password, _) =>
                            {
                                Assert.That(password, Is.EqualTo("request-password".ToCharArray()));
                                passwordStaging = password;
                            })
                    .ReturnsAsync(() => new X509Certificate2KeyPair(
                        Certificate.FromRawData(certificate), "PFX", privateKey));
                var authorization = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
                authorization.Setup(g => g.AuthorizeApplicationAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(true);
                int publications = 0;
                var failure = new IOException("Pending-state publication committed before its response was lost.");
                ByteString pending = default;
                authorization.Setup(g => g.RecordCertificateIssuedAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(), It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>())).Callback<
                        ISystemContext, ApplicationRecordDataType, ByteString, CancellationToken>((_, _, bytes, _) =>
                    {
                        pending = bytes;
                        if (++publications == 1)
                        {
                            throw failure;
                        }
                    }).Returns(default(ValueTask));
                NodeId applicationId;
                NodeId requestId;
                using (var receiptStore = new SecretStoreCertificateRequestIssuanceStore(
                    secrets, ReferenceServer.CurrentInstance.NamespaceUris, "gds"))
                using (ApplicationsNodeManager manager = CreateConfiguredManager(
                    database, group.Object, path, authorization.Object, receiptStore))
                {
                    await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false);
                    applicationId = database.RegisterApplication(CreateTestApplicationRecord("ReceiptRetry"));
                    requestId = newKeyPair
                        ? database.StartNewKeyPairRequest(applicationId, "DefaultApplicationGroup",
                            nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                            "CN=Receipt retry", ["localhost"], "PFX", "request-password", "test")
                        : database.StartSigningRequest(applicationId, "DefaultApplicationGroup",
                            nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                            ByteString.From(new byte[] { 1 }), "test");
                    database.ApproveRequest(requestId, false);

                    IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                        await manager.OnFinishRequestAsync(
                            CreateContext(GdsRole.CertificateAuthorityAdmin), new MethodState(null), default,
                            applicationId, requestId, CancellationToken.None).ConfigureAwait(false));

                    Assert.That(actual, Is.SameAs(failure));
                    Assert.That(pending, Is.EqualTo(certificate));
                    Assert.That(database.FinishRequest(applicationId, requestId, out _, out _, out _, out _),
                        Is.EqualTo(CertificateRequestState.Approved));
                }
                using var restoredStore = new SecretStoreCertificateRequestIssuanceStore(
                    secrets, ReferenceServer.CurrentInstance.NamespaceUris, "gds");
                using ApplicationsNodeManager restored = CreateConfiguredManager(
                    database, group.Object, path, authorization.Object, restoredStore);
                await restored.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                FinishRequestMethodStateResult result = await restored.OnFinishRequestAsync(
                    CreateContext(GdsRole.CertificateAuthorityAdmin), new MethodState(null), default,
                    applicationId, requestId, CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.Certificate, Is.EqualTo(certificate));
                Assert.That(result.PrivateKey, Is.EqualTo(privateKey));
                Assert.That(pending, Is.EqualTo(certificate));
                Assert.That(publications, Is.EqualTo(2));
                Assert.That(database.FinishRequest(applicationId, requestId, out _, out _, out _, out _),
                    Is.EqualTo(CertificateRequestState.Accepted));
                group.Verify(g => g.SigningRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string[]>(),
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()),
                    newKeyPair ? Times.Never() : Times.Once());
                group.Verify(g => g.NewKeyPairRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string>(),
                    It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<char[]>(),
                    It.IsAny<CancellationToken>()), newKeyPair ? Times.Once() : Times.Never());
                if (newKeyPair)
                {
                    Assert.That(passwordStaging, Is.Not.Null.And.Not.Empty);
                    Assert.That(passwordStaging, Is.All.EqualTo('\0'));
                }
                FinishRequestMethodStateResult accepted = await restored.OnFinishRequestAsync(
                    CreateContext(GdsRole.CertificateAuthorityAdmin), new MethodState(null), default,
                    applicationId, requestId, CancellationToken.None).ConfigureAwait(false);
                Assert.That(accepted.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(publications, Is.EqualTo(2));
                GetCertificatesMethodStateResult assigned = await restored.OnGetCertificatesAsync(
                    CreateContext(GdsRole.CertificateAuthorityAdmin), new MethodState(null), default,
                    applicationId, default, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(assigned.ServiceResult), Is.True);
                Assert.That(assigned.CertificateTypeIds,
                    Is.EqualTo(new[] { Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType }.ToArrayOf()));
                Assert.That(assigned.Certificates, Is.EqualTo(new[] { certificate }.ToArrayOf()));
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }

        /// <summary>
        /// Issuance-store failure propagates before pending publication, database certificate updates or acceptance.
        /// </summary>
        [Test]
        public async Task ReceiptPublicationFailureLeavesRequestApprovedAndNeverPublishesPendingCredential()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var database = new LinqApplicationsDatabase();
                using Certificate issued = CertificateBuilder.Create("CN=Receipt publication").SetRSAKeySize(2048)
                    .CreateForRSA();
                Mock<ICertificateGroup> group = CreateCertificateGroup(path, ByteString.From(issued.RawData));
                var authorization = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
                authorization.Setup(g => g.AuthorizeApplicationAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(true);
                var failure = new IOException("Receipt publication failed.");
                var receipts = new Mock<ICertificateRequestIssuanceStore>(MockBehavior.Strict);
                receipts.Setup(s => s.GetOrCreateAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<Func<CancellationToken, ValueTask<CertificateRequestIssuance>>>(),
                    It.IsAny<CancellationToken>())).Returns(async (
                        NodeId _, NodeId _,
                        Func<CancellationToken, ValueTask<CertificateRequestIssuance>> create,
                        CancellationToken token) =>
                    {
                        await create(token).ConfigureAwait(false);
                        throw failure;
                    });
                using ApplicationsNodeManager manager = CreateConfiguredManager(
                    database, group.Object, path, authorization.Object, receipts.Object);
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("ReceiptUnpublished"));
                NodeId requestId = database.StartSigningRequest(applicationId, "DefaultApplicationGroup",
                    nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                    ByteString.From(new byte[] { 1 }), "test");
                database.ApproveRequest(requestId, false);

                IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                    await manager.OnFinishRequestAsync(
                        CreateContext(GdsRole.CertificateAuthorityAdmin), new MethodState(null), default,
                        applicationId, requestId, CancellationToken.None).ConfigureAwait(false));

                Assert.That(actual, Is.SameAs(failure));
                Assert.That(database.FinishRequest(applicationId, requestId, out _, out _, out _, out _),
                    Is.EqualTo(CertificateRequestState.Approved));
                Assert.That(database.GetApplicationCertificate(
                    applicationId, nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType), out _), Is.False);
                authorization.Verify(g => g.RecordCertificateIssuedAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(), It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                group.Verify(g => g.SigningRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string[]>(),
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }

        /// <summary>
        /// A provider cannot substitute material issued under a different certificate group or type.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ReceiptProfileMismatchCannotPublishPendingCredentialOrConsumeRequest(bool wrongGroup)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var database = new LinqApplicationsDatabase();
                ByteString certificate = ByteString.From(new byte[] { 1 });
                Mock<ICertificateGroup> group = CreateCertificateGroup(path, certificate);
                var authorization = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
                authorization.Setup(g => g.AuthorizeApplicationAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(true);
                var receipt = new CertificateRequestIssuance(
                    wrongGroup ? "another-group" : "DefaultApplicationGroup",
                    wrongGroup ? nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType) : "another-type",
                    certificate, default, []);
                var receipts = new Mock<ICertificateRequestIssuanceStore>(MockBehavior.Strict);
                receipts.Setup(s => s.GetOrCreateAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(),
                    It.IsAny<Func<CancellationToken, ValueTask<CertificateRequestIssuance>>>(),
                    It.IsAny<CancellationToken>())).Returns(new ValueTask<CertificateRequestIssuance>(receipt));
                using ApplicationsNodeManager manager = CreateConfiguredManager(
                    database, group.Object, path, authorization.Object, receipts.Object);
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("ReceiptMismatch"));
                NodeId requestId = database.StartSigningRequest(applicationId, "DefaultApplicationGroup",
                    nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                    ByteString.From(new byte[] { 1 }), "test");
                database.ApproveRequest(requestId, false);

                ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.OnFinishRequestAsync(
                        CreateContext(GdsRole.CertificateAuthorityAdmin), new MethodState(null), default,
                        applicationId, requestId, CancellationToken.None).ConfigureAwait(false));

                Assert.That(actual!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(database.FinishRequest(applicationId, requestId, out _, out _, out _, out _),
                    Is.EqualTo(CertificateRequestState.Approved));
                authorization.Verify(g => g.RecordCertificateIssuedAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(), It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                group.Verify(g => g.SigningRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string[]>(),
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }

        /// <summary>
        /// Competing completions on one manager cannot issue twice or replay a request accepted by the first call.
        /// </summary>
        [Test]
        public async Task ConcurrentCompletionUsesOneIssuanceAndRejectsTheAlreadyAcceptedRetry()
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var database = new LinqApplicationsDatabase();
                using Certificate issued = CertificateBuilder.Create("CN=Concurrent receipt").SetRSAKeySize(2048)
                    .CreateForRSA();
                ByteString certificate = ByteString.From(issued.RawData);
                Mock<ICertificateGroup> group = CreateCertificateGroup(path, certificate);
                var signing = new TaskCompletionSource<Certificate>(TaskCreationOptions.RunContinuationsAsynchronously);
                group.Setup(g => g.SigningRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string[]>(),
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>())).Returns(signing.Task);
                var authorization = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
                authorization.Setup(g => g.AuthorizeApplicationAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(true);
                authorization.Setup(g => g.RecordCertificateIssuedAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(), certificate,
                    It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                using var receipts = new SecretStoreCertificateRequestIssuanceStore(
                    new InMemorySecretStore(), ReferenceServer.CurrentInstance.NamespaceUris, "gds");
                using ApplicationsNodeManager manager = CreateConfiguredManager(
                    database, group.Object, path, authorization.Object, receipts);
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("ConcurrentReceipt"));
                NodeId requestId = database.StartSigningRequest(applicationId, "DefaultApplicationGroup",
                    nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                    ByteString.From(new byte[] { 1 }), "test");
                database.ApproveRequest(requestId, false);
                ServerSystemContext context = CreateContext(GdsRole.CertificateAuthorityAdmin);
                var method = new MethodState(null);
                Task<FinishRequestMethodStateResult> first = manager.OnFinishRequestAsync(
                    context, method, default, applicationId, requestId, CancellationToken.None).AsTask();
                Task<FinishRequestMethodStateResult> retry = manager.OnFinishRequestAsync(
                    context, method, default, applicationId, requestId, CancellationToken.None).AsTask();
                bool firstWasWaiting = !first.IsCompleted;
                bool retryWasWaiting = !retry.IsCompleted;
                signing.SetResult(Certificate.FromRawData(certificate));

                FinishRequestMethodStateResult[] results = await Task.WhenAll(first, retry).ConfigureAwait(false);

                Assert.That(firstWasWaiting, Is.True);
                Assert.That(retryWasWaiting, Is.True);
                Assert.That(ServiceResult.IsGood(results[0].ServiceResult), Is.True);
                Assert.That(results[0].Certificate, Is.EqualTo(certificate));
                Assert.That(results[1].ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                authorization.Verify(g => g.RecordCertificateIssuedAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<ApplicationRecordDataType>(), certificate,
                    It.IsAny<CancellationToken>()), Times.Once);
                group.Verify(g => g.SigningRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string[]>(),
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }

        /// <summary>
        /// The new-key method awaits provider authorization instead of continuing with a pending decision.
        /// </summary>
        [Test]
        public async Task NewKeyHandlerAwaitsAsynchronousAuthorizationBeforeTouchingRequestStore()
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using var manager = CreateManager(database, requests.Object);
            ApplicationRecordDataType application = CreateTestApplicationRecord("AwaitOnboardingGate");
            NodeId applicationId = database.RegisterApplication(application);
            ServerSystemContext context = CreateContext(GdsRole.CertificateAuthorityAdmin);
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
            gate.Setup(g => g.AuthorizeApplicationAsync(
                context, It.IsAny<ApplicationRecordDataType>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(decision.Task));
            manager.DeviceRegistrarCertificateAuthorization = gate.Object;
            Task<ServiceResult> pending = InvokeAsync(manager, "newKey", context, applicationId).AsTask();

            Assert.That(pending.IsCompleted, Is.False);
            requests.VerifyNoOtherCalls();
            var denial = new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            decision.SetException(denial);
            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await pending.ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(denial));
            requests.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An existing device channel may read only a store assigned to a currently authorized relation.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task OnboardingTrustListReadRequiresAssignedStoreAndCurrentApplicationPolicy(bool assigned)
        {
            var database = new LinqApplicationsDatabase();
            using var manager = CreateManager(database, Mock.Of<ICertificateRequest>());
            ApplicationRecordDataType application = CreateTestApplicationRecord("OnboardingTrustList");
            NodeId applicationId = database.RegisterApplication(application);
            database.SetApplicationTrustLists(
                applicationId, nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType), "assigned-store");
            ServerSystemContext context = CreateContext(GdsRole.CertificateAuthorityAdmin);
            var gate = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
            gate.Setup(g => g.GetChannelApplicationsAsync(context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ArrayOf<DeviceRegistrarApplicationBinding>(
                new DeviceRegistrarApplicationBinding[]
                {
                    new DeviceRegistrarApplicationBinding("urn:device:test", applicationId, false, "urn:protocol:test")
                }));
            gate.Setup(g => g.AuthorizeApplicationAsync(
                context, It.Is<ApplicationRecordDataType>(a => a.ApplicationId == applicationId),
                It.IsAny<CancellationToken>())).ReturnsAsync(true);
            manager.DeviceRegistrarCertificateAuthorization = gate.Object;
            var store = new CertificateStoreIdentifier(assigned ? "assigned-store" : "another-device-store");

            if (assigned)
            {
                await manager.AuthorizeOnboardingTrustListReadAsync(context, store, CancellationToken.None)
                    .ConfigureAwait(false);
                gate.Verify(g => g.AuthorizeApplicationAsync(
                    context, It.IsAny<ApplicationRecordDataType>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            else
            {
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.AuthorizeOnboardingTrustListReadAsync(context, store, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                gate.Verify(g => g.AuthorizeApplicationAsync(
                    context, It.IsAny<ApplicationRecordDataType>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        /// <summary>
        /// A failed application-policy recheck cannot use the caller's global GDS administrator role instead.
        /// </summary>
        [Test]
        public async Task OnboardingTrustListPolicyFailureCannotUseAdministratorFallback()
        {
            var database = new LinqApplicationsDatabase();
            using var manager = CreateManager(database, Mock.Of<ICertificateRequest>());
            ApplicationRecordDataType application = CreateTestApplicationRecord("OnboardingTrustListRevoked");
            NodeId applicationId = database.RegisterApplication(application);
            database.SetApplicationTrustLists(
                applicationId, nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType), "assigned-store");
            ServerSystemContext context = CreateContext(GdsRole.CertificateAuthorityAdmin);
            var gate = new Mock<IDeviceRegistrarCertificateAuthorization>(MockBehavior.Strict);
            gate.Setup(g => g.GetChannelApplicationsAsync(context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ArrayOf<DeviceRegistrarApplicationBinding>(
                new DeviceRegistrarApplicationBinding[]
                {
                    new DeviceRegistrarApplicationBinding("urn:device:test", applicationId, false, "urn:protocol:test")
                }));
            var failure = new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            gate.Setup(g => g.AuthorizeApplicationAsync(
                context, It.IsAny<ApplicationRecordDataType>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
            manager.DeviceRegistrarCertificateAuthorization = gate.Object;

            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await manager.AuthorizeOnboardingTrustListReadAsync(
                    context, new CertificateStoreIdentifier("assigned-store"), CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
        }

        /// <summary>
        /// Push enrollment uses the same approval queue, issuer and immutable receipt after provider restart.
        /// </summary>
        [TestCase(0, false, false)]
        [TestCase(2, false, false)]
        [TestCase(4, false, false)]
        [TestCase(5, false, false)]
        [TestCase(0, true, false)]
        [TestCase(2, true, false)]
        [TestCase(4, true, false)]
        [TestCase(5, true, false)]
        [TestCase(0, false, true)]
        [TestCase(2, false, true)]
        [TestCase(4, false, true)]
        [TestCase(5, false, true)]
        public async Task PushEnrollmentRecoversInterruptedTransitionsWithoutIssuingAgain(
            int failedJournalWrite,
            bool useOverride,
            bool externalAuthentication)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var database = new LinqApplicationsDatabase();
                var secrets = new InMemorySecretStore();
                var journal = new Mock<ISecretStore>(MockBehavior.Strict);
                journal.SetupGet(value => value.StoreType).Returns(secrets.StoreType);
                journal.Setup(value => value.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                    .Returns<SecretIdentifier, CancellationToken>((id, ct) => secrets.GetAsync(id, ct));
                int journalWrites = 0;
                journal.Setup(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                    .Returns<SecretIdentifier, ReadOnlyMemory<byte>, CancellationToken>(async (id, bytes, ct) =>
                    {
                        if (++journalWrites == failedJournalWrite)
                        {
                            throw new IOException("The enrollment checkpoint was not published.");
                        }
                        await secrets.SetAsync(id, bytes, ct).ConfigureAwait(false);
                    });
                var lifecycle = new MemoryDeviceRegistrarLifecycle();
                using Certificate issued = CertificateBuilder.Create("CN=Push Enrollment").SetRSAKeySize(2048)
                    .CreateForRSA();
                ByteString certificate = ByteString.From(issued.RawData.AsSpan());
                Mock<ICertificateGroup> group = CreateCertificateGroup(path, certificate);
                NodeId selectedType = useOverride
                    ? Ua.ObjectTypeIds.RsaMinApplicationCertificateType
                    : Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
                string selectedTypeName = useOverride
                    ? nameof(Ua.ObjectTypeIds.RsaMinApplicationCertificateType)
                    : nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
                group.Object.CertificateTypes =
                [
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType
                ];
                group.Object.Configuration.CertificateTypes =
                [
                    nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                    nameof(Ua.ObjectTypeIds.RsaMinApplicationCertificateType)
                ];
                using var receipts = new SecretStoreCertificateRequestIssuanceStore(
                    secrets, ReferenceServer.CurrentInstance.NamespaceUris, "push-receipts");
                using ApplicationsNodeManager manager = CreateConfiguredManager(
                    database, group.Object, path, Mock.Of<IDeviceRegistrarCertificateAuthorization>(), receipts);
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                database.NamespaceIndex = manager.ApplicationsNamespaceIndex;
                NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("PushEnrollment"));
                OnboardingIdentitySelection identity = externalAuthentication
                    ? OnboardingIdentitySelection.FromExternalAuthentication(
                        certificate, "urn:device:push-enrollment")
                    : new OnboardingIdentitySelection(
                        certificate, new Opc.Ua.Onboarding.DeviceIdentityTicketType(),
                        "urn:device:push-enrollment", "retained-ticket", string.Empty);
                var external = new Mock<IDeviceRegistrarExternalAuthenticationProvider>(MockBehavior.Strict);
                external.Setup(value => value.EvaluateAsync(certificate, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeviceRegistrarExternalAuthenticationResult(identity.ProductInstanceUri,
                        new DeviceRegistrarOnboardingDecision(
                            CreateTestApplicationRecord("PushEnrollment"), false, null)));
                await lifecycle.RecordCandidateAsync(
                    identity.ProductInstanceUri, applicationId, certificate, false).ConfigureAwait(false);
                ByteString csr = ByteString.From(new byte[] { 1, 2, 3 });
                DeviceRegistrarPushEnrollment? first = null;
                using (var provider = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                    manager, journal.Object, "push", ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry,
                    externalAuthentication ? external.Object : null))
                {
                    if (failedJournalWrite != 0)
                    {
                        await Assert.ThrowsAsync<IOException>(async () =>
                            await EnrollAsync(provider, _ => new ValueTask<ByteString>(csr)).ConfigureAwait(false));
                    }
                    else
                    {
                        first = await EnrollAsync(provider, _ => new ValueTask<ByteString>(csr)).ConfigureAwait(false);
                    }
                }
                using var restored = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                    manager, secrets, "push", ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry,
                    externalAuthentication ? external.Object : null);
                NodeId originalRequest = await restored.FindRequestAsync(identity.ProductInstanceUri, applicationId)
                    .ConfigureAwait(false);
                int originalRequestCount = database.CertificateRequests.Count;
                ServiceResultException? changedDuringRecovery =
                    await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await restored.GetOrCreateAsync(identity, applicationId,
                            useOverride
                                ? Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
                                : Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                            _ => throw new InvalidOperationException("Recovery must not replace the original CSR."))
                            .ConfigureAwait(false));
                Assert.That(changedDuringRecovery!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(database.CertificateRequests, Has.Count.EqualTo(originalRequestCount));
                Assert.That(await restored.FindRequestAsync(identity.ProductInstanceUri, applicationId)
                    .ConfigureAwait(false), Is.EqualTo(originalRequest));
                if (failedJournalWrite is 2 or 4)
                {
                    Assert.That((await restored.FindRequestAsync(identity.ProductInstanceUri, applicationId)
                        .ConfigureAwait(false)).IsNull, Is.True);
                    NodeId recoveredRequest = failedJournalWrite == 4
                        ? new NodeId(
                            database.CertificateRequests.Single().RequestId, manager.ApplicationsNamespaceIndex)
                        : NodeId.Null;
                    if (failedJournalWrite == 4)
                    {
                        NodeId unrelated = database.StartSigningRequest(
                            applicationId, "DefaultApplicationGroup", "RsaSha256ApplicationCertificateType",
                            ByteString.From(new byte[] { 9 }), "AnotherRequester");
                        ServiceResultException? mismatch = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await restored.RecoverAsync(identity, applicationId, csr, unrelated).ConfigureAwait(false));
                        Assert.That(mismatch!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                    }
                    await restored.RecoverAsync(identity, applicationId, csr, recoveredRequest).ConfigureAwait(false);
                }
                DeviceRegistrarPushEnrollment repeated = await EnrollAsync(restored,
                    _ => throw new InvalidOperationException("The device must not create another key."))
                    .ConfigureAwait(false);
                DeviceRegistrarPushEnrollment? found = await restored.FindAsync(
                    identity.ProductInstanceUri, applicationId).ConfigureAwait(false);

                if (first != null)
                {
                    Assert.That(repeated.RequestId, Is.EqualTo(first.RequestId));
                }
                Assert.That(repeated.CertificateRequest, Is.EqualTo(csr));
                Assert.That(await restored.FindRequestAsync(identity.ProductInstanceUri, applicationId)
                    .ConfigureAwait(false), Is.EqualTo(repeated.RequestId));
                Assert.That(repeated.CertificateGroupId, Is.EqualTo("DefaultApplicationGroup"));
                Assert.That(repeated.CertificateTypeId, Is.EqualTo(selectedTypeName));
                Assert.That(repeated.TrustList.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
                Assert.That(database.ReadRequest(applicationId, repeated.RequestId,
                    out _, out _, out _, out _, out _, out _, out _), Is.EqualTo(CertificateRequestState.Accepted));
                Assert.That(found!.CertificateRequest, Is.EqualTo(csr));
                Assert.That(found.Identity.IsExternalAuthentication, Is.EqualTo(externalAuthentication));
                Assert.That(found.Identity.EncodedTicket,
                    Is.EqualTo(externalAuthentication ? string.Empty : "retained-ticket"));
                ServiceResultException? completed = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await restored.RecoverAsync(identity, applicationId, csr, repeated.RequestId)
                        .ConfigureAwait(false));
                Assert.That(completed!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                group.Verify(value => value.SigningRequestAsync(
                    It.IsAny<ApplicationRecordDataType>(), selectedType, It.IsAny<string[]>(),
                    csr, It.IsAny<CancellationToken>()), Times.Once);
                CertificateRequestIssuance receipt = await receipts.GetOrCreateAsync(applicationId, repeated.RequestId,
                    _ => throw new InvalidOperationException("The issuance must already exist.")).ConfigureAwait(false);
                Assert.That(receipt.Certificate, Is.EqualTo(certificate));
                Assert.That(receipt.CertificateTypeId, Is.EqualTo(selectedTypeName));

                ServiceResultException? changedProfile = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await restored.GetOrCreateAsync(identity, applicationId,
                        useOverride
                            ? Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
                            : Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                        _ => throw new InvalidOperationException("Changing the profile must not create a key."))
                        .ConfigureAwait(false));
                Assert.That(changedProfile!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(await restored.FindRequestAsync(identity.ProductInstanceUri, applicationId)
                    .ConfigureAwait(false), Is.EqualTo(repeated.RequestId));

                if (useOverride)
                {
                    await lifecycle.RecordIssuedDcaCertificateAsync(
                        identity.ProductInstanceUri, applicationId, certificate).ConfigureAwait(false);
                    Assert.That(await lifecycle.ConfirmDcaCertificateAsync(certificate).ConfigureAwait(false), Is.True);
                    NodeId managedId = database.RegisterApplication(CreateTestApplicationRecord("DefaultProfile"));
                    await lifecycle.AssociateApplicationAsync(identity.ProductInstanceUri, managedId, string.Empty)
                        .ConfigureAwait(false);
                    ByteString managedCsr = ByteString.From(new byte[] { 4, 5, 6 });

                    DeviceRegistrarPushEnrollment defaultEnrollment = await restored.GetOrCreateAsync(
                        identity, managedId, _ => new ValueTask<ByteString>(managedCsr)).ConfigureAwait(false);

                    Assert.That(defaultEnrollment.CertificateTypeId,
                        Is.EqualTo(nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)));
                    Assert.That(defaultEnrollment.CertificateRequest, Is.EqualTo(managedCsr));
                    Assert.That(defaultEnrollment.RequestId, Is.Not.EqualTo(repeated.RequestId));
                    group.Verify(value => value.SigningRequestAsync(
                        It.IsAny<ApplicationRecordDataType>(), Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        It.IsAny<string[]>(), managedCsr, It.IsAny<CancellationToken>()), Times.Once);
                }

                ValueTask<DeviceRegistrarPushEnrollment> EnrollAsync(
                    SecretStoreDeviceRegistrarPushEnrollmentProvider provider,
                    Func<CancellationToken, ValueTask<ByteString>> create)
                {
                    return useOverride
                        ? provider.GetOrCreateAsync(identity, applicationId, selectedType, create)
                        : provider.GetOrCreateAsync(identity, applicationId, create);
                }
            }
            finally
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
        }

        /// <summary>
        /// An uncertain device CSR response is never retried automatically after journal reconstruction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PushEnrollmentDoesNotRepeatUncertainCsrAfterRestart(bool useOverride)
        {
            var database = new LinqApplicationsDatabase();
            using ApplicationsNodeManager manager = await CreateSelectionManagerAsync(database).ConfigureAwait(false);
            NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("UncertainPushCsr"));
            var secrets = new InMemorySecretStore();
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ByteString selected = ByteString.From(new byte[] { 1 });
            var identity = new OnboardingIdentitySelection(
                selected, new Opc.Ua.Onboarding.DeviceIdentityTicketType(),
                "urn:device:uncertain-push", "signed-ticket", string.Empty);
            await lifecycle.RecordCandidateAsync(identity.ProductInstanceUri, applicationId, selected, false)
                .ConfigureAwait(false);
            var failure = new IOException("The CSR response was lost.");
            NodeId selectedType = useOverride
                ? Ua.ObjectTypeIds.RsaMinApplicationCertificateType
                : Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
            using (var first = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets, "uncertain", ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry))
            {
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await first.GetOrCreateAsync(identity, applicationId, selectedType, _ => throw failure)
                        .ConfigureAwait(false));
                Assert.That(error, Is.SameAs(failure));
            }
            using var restored = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets, "uncertain", ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                useOverride
                    ? Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
                    : Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                lifecycle, lifecycle, Telemetry);
            int calls = 0;

            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restored.GetOrCreateAsync(identity, applicationId, selectedType, _ =>
                {
                    calls++;
                    return new ValueTask<ByteString>(selected);
                }).ConfigureAwait(false));

            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(calls, Is.Zero);
            ServiceResultException? changedProfile = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restored.GetOrCreateAsync(identity, applicationId,
                    useOverride
                        ? Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
                        : Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                    _ => throw new InvalidOperationException("Changing an uncertain profile must not create a key."))
                    .ConfigureAwait(false));
            Assert.That(changedProfile!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            ServiceResultException? changedDefault = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restored.GetOrCreateAsync(identity, applicationId,
                    _ => throw new InvalidOperationException("The default must not replace a retained profile."))
                    .ConfigureAwait(false));
            Assert.That(changedDefault!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(await restored.FindAsync(identity.ProductInstanceUri, applicationId).ConfigureAwait(false),
                Is.Null);
            var substituted = new OnboardingIdentitySelection(
                selected, identity.MatchingTicket, identity.ProductInstanceUri, "replacement-ticket", string.Empty);
            ServiceResultException? substitution = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restored.GetOrCreateAsync(substituted, applicationId,
                    _ => throw new InvalidOperationException("The substituted ticket must not create a key."))
                    .ConfigureAwait(false));
            Assert.That(substitution!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// An unfinished enrollment cannot move its original CSR to another configured authority after restart.
        /// </summary>
        [TestCase("find")]
        [TestCase("enroll")]
        [TestCase("recover")]
        public async Task PushEnrollmentRejectsChangedGroupAfterRestart(string operation)
        {
            var database = new LinqApplicationsDatabase();
            using ApplicationsNodeManager manager = await CreateSelectionManagerAsync(database).ConfigureAwait(false);
            NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("ChangedPushGroup"));
            var secrets = new InMemorySecretStore();
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ByteString selected = ByteString.From(new byte[] { 1 });
            var identity = new OnboardingIdentitySelection(
                selected, new Opc.Ua.Onboarding.DeviceIdentityTicketType(),
                "urn:device:changed-push-group", "signed-ticket", string.Empty);
            await lifecycle.RecordCandidateAsync(identity.ProductInstanceUri, applicationId, selected, false)
                .ConfigureAwait(false);
            int calls = 0;
            using (var first = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets, "changed-group", ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry))
            {
                await Assert.ThrowsAsync<IOException>(async () =>
                    await first.GetOrCreateAsync(identity, applicationId, _ =>
                    {
                        calls++;
                        throw new IOException("The CSR response was lost.");
                    }).ConfigureAwait(false));
            }
            using var restored = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets, "changed-group", new ExpandedNodeId("other-group", "urn:other:authority"),
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                switch (operation)
                {
                    case "find":
                        await restored.FindAsync(identity.ProductInstanceUri, applicationId).ConfigureAwait(false);
                        break;
                    case "enroll":
                        await restored.GetOrCreateAsync(identity, applicationId, _ =>
                        {
                            calls++;
                            return new ValueTask<ByteString>(selected);
                        }).ConfigureAwait(false);
                        break;
                    default:
                        await restored.RecoverAsync(identity, applicationId, selected).ConfigureAwait(false);
                        break;
                }
            });

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(database.CertificateRequests, Is.Empty);
            using var originalGroup = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets, "changed-group", ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry);
            ServiceResultException? retained = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await originalGroup.GetOrCreateAsync(identity, applicationId,
                    _ => throw new InvalidOperationException("The original uncertain CSR must remain retained."))
                    .ConfigureAwait(false));
            Assert.That(retained!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Explicit enrollment profiles must be standard non-null certificate types before any journal or key effect.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PushEnrollmentRejectsInvalidExplicitProfileBeforeJournalAccess(bool indexedType)
        {
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            var database = new LinqApplicationsDatabase();
            using ApplicationsNodeManager manager = CreateManager(database, Mock.Of<ICertificateRequest>());
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            using var provider = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets.Object, "invalid-profile",
                ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry);
            var identity = new OnboardingIdentitySelection(
                ByteString.From(new byte[] { 1 }), new Opc.Ua.Onboarding.DeviceIdentityTicketType(),
                "urn:device:invalid-profile", "signed-ticket", string.Empty);
            NodeId type = indexedType ? new NodeId(1, 2) : NodeId.Null;
            int calls = 0;

            ArgumentException? error = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await provider.GetOrCreateAsync(identity, new NodeId("application", 1), type, _ =>
                {
                    calls++;
                    return new ValueTask<ByteString>(ByteString.From(new byte[] { 2 }));
                }).ConfigureAwait(false));

            Assert.That(error!.ParamName, Is.EqualTo("certificateTypeId"));
            Assert.That(calls, Is.Zero);
            secrets.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Unsupported local issuance profiles do not create a key, uncertain journal or queue entry.
        /// </summary>
        [Test]
        public async Task PushEnrollmentRejectsUnsupportedAuthorityTypeBeforeCreatingCsr()
        {
            var database = new LinqApplicationsDatabase();
            using ApplicationsNodeManager manager = await CreateSelectionManagerAsync(database).ConfigureAwait(false);
            NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("UnsupportedPushType"));
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(value => value.StoreType).Returns("InMemory");
            secrets.Setup(value => value.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISecret?)null);
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ByteString selected = ByteString.From(new byte[] { 1 });
            var identity = new OnboardingIdentitySelection(
                selected, new Opc.Ua.Onboarding.DeviceIdentityTicketType(),
                "urn:device:unsupported-push-type", "signed-ticket", string.Empty);
            await lifecycle.RecordCandidateAsync(identity.ProductInstanceUri, applicationId, selected, false)
                .ConfigureAwait(false);
            using var provider = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets.Object, "unsupported-type",
                ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry);
            int calls = 0;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.GetOrCreateAsync(
                    identity, applicationId, Ua.ObjectTypeIds.ApplicationCertificateType, _ =>
                {
                    calls++;
                    return new ValueTask<ByteString>(selected);
                }).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(calls, Is.Zero);
            Assert.That(database.CertificateRequests, Is.Empty);
            secrets.VerifyGet(value => value.StoreType, Times.Once);
            secrets.Verify(value => value.GetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()), Times.Once);
            secrets.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Software approval is checked before journal publication, CSR creation or certificate-queue access.
        /// </summary>
        [Test]
        public async Task PushEnrollmentRejectsSoftwareBlockedCandidateBeforeCreatingCsr()
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using ApplicationsNodeManager manager = CreateManager(database, requests.Object);
            NodeId applicationId = database.RegisterApplication(CreateTestApplicationRecord("BlockedPush"));
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ByteString selected = ByteString.From(new byte[] { 1 });
            var identity = new OnboardingIdentitySelection(
                selected, new Opc.Ua.Onboarding.DeviceIdentityTicketType(),
                "urn:device:blocked-push", "signed-ticket", string.Empty);
            await lifecycle.RecordCandidateAsync(identity.ProductInstanceUri, applicationId, selected, true)
                .ConfigureAwait(false);
            using var provider = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, new InMemorySecretStore(), "blocked",
                ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry);
            int calls = 0;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.GetOrCreateAsync(identity, applicationId, _ =>
                {
                    calls++;
                    return new ValueTask<ByteString>(selected);
                }).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(calls, Is.Zero);
            Assert.That(await provider.FindAsync(identity.ProductInstanceUri, applicationId).ConfigureAwait(false),
                Is.Null);
            requests.VerifyNoOtherCalls();
        }

        /// <summary>
        /// External evidence must retain current approval for the original DCA before any journal or CSR effect.
        /// </summary>
        [TestCase("missingProvider")]
        [TestCase("untrusted")]
        [TestCase("product")]
        [TestCase("application")]
        public async Task ExternalPushEnrollmentRejectsMissingOrChangedApprovalBeforeEffects(string failure)
        {
            var database = new LinqApplicationsDatabase();
            var requests = new Mock<ICertificateRequest>(MockBehavior.Strict);
            using ApplicationsNodeManager manager = CreateManager(database, requests.Object);
            ApplicationRecordDataType original = CreateTestApplicationRecord("ExternalPush");
            NodeId applicationId = database.RegisterApplication(original);
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ByteString selected = ByteString.From(new byte[] { 1 });
            OnboardingIdentitySelection identity = OnboardingIdentitySelection.FromExternalAuthentication(
                selected, "urn:device:external-push");
            await lifecycle.RecordCandidateAsync(identity.ProductInstanceUri, applicationId, selected, false)
                .ConfigureAwait(false);
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(value => value.StoreType).Returns(new InMemorySecretStore().StoreType);
            var external = new Mock<IDeviceRegistrarExternalAuthenticationProvider>(MockBehavior.Strict);
            ApplicationRecordDataType approved = CreateTestApplicationRecord(
                failure == "application" ? "OtherExternalPush" : "ExternalPush");
            external.Setup(value => value.EvaluateAsync(selected, It.IsAny<CancellationToken>()))
                .ReturnsAsync(failure == "untrusted" ? null : new DeviceRegistrarExternalAuthenticationResult(
                    failure == "product" ? "urn:device:other-product" : identity.ProductInstanceUri,
                    new DeviceRegistrarOnboardingDecision(approved, false, null)));
            using var provider = new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                manager, secrets.Object, "external-approval",
                ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, lifecycle, lifecycle, Telemetry,
                failure == "missingProvider" ? null : external.Object);
            int calls = 0;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.GetOrCreateAsync(identity, applicationId, _ =>
                {
                    calls++;
                    return new ValueTask<ByteString>(selected);
                }).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(failure switch
            {
                "missingProvider" => StatusCodes.BadConfigurationError,
                "untrusted" => StatusCodes.BadCertificateUntrusted,
                "product" => StatusCodes.BadSecurityChecksFailed,
                _ => StatusCodes.BadRequestNotAllowed
            }));
            Assert.That(calls, Is.Zero);
            Assert.That(database.CertificateRequests, Is.Empty);
            secrets.VerifyGet(value => value.StoreType, Times.Once);
            secrets.VerifyNoOtherCalls();
            requests.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Creates an isolated handler manager while borrowing the shared fixture's server context.
        /// </summary>
        private ApplicationsNodeManager CreateManager(
            LinqApplicationsDatabase database,
            ICertificateRequest requests)
        {
            var manager = new ApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, database, requests,
                Mock.Of<ICertificateGroup>(), autoApprove: true);
            database.NamespaceIndex = manager.ApplicationsNamespaceIndex;
            return manager;
        }

        /// <summary>
        /// Initializes the real authority map with mocked groups whose store paths are never opened.
        /// </summary>
        private async Task<ApplicationsNodeManager> CreateSelectionManagerAsync(LinqApplicationsDatabase database)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "UnusedPushSelectionStore");
            Mock<ICertificateGroup> group = CreateCertificateGroup(path, default);
            group.Object.CertificateTypes =
            [
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Ua.ObjectTypeIds.RsaMinApplicationCertificateType
            ];
            group.Object.Configuration.CertificateTypes =
            [
                nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                nameof(Ua.ObjectTypeIds.RsaMinApplicationCertificateType)
            ];
            ApplicationsNodeManager manager = CreateConfiguredManager(
                database, group.Object, path, Mock.Of<IDeviceRegistrarCertificateAuthorization>(),
                Mock.Of<ICertificateRequestIssuanceStore>());
            try
            {
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                database.NamespaceIndex = manager.ApplicationsNamespaceIndex;
                return manager;
            }
            catch
            {
                manager.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Supplies a real request database and model initialization with a deterministic signing engine.
        /// </summary>
        private ApplicationsNodeManager CreateConfiguredManager(
            LinqApplicationsDatabase database,
            ICertificateGroup group,
            string path,
            IDeviceRegistrarCertificateAuthorization authorization,
            ICertificateRequestIssuanceStore receipts)
        {
            var configuration = new ApplicationConfiguration(Telemetry)
            {
                ApplicationUri = ServerFixture.Config.ApplicationUri,
                SecurityConfiguration = ServerFixture.Config.SecurityConfiguration,
                ServerConfiguration = ServerFixture.Config.ServerConfiguration,
                CertificateManager = ServerFixture.Config.CertificateManager
            };
            configuration.UpdateExtension(null, new GlobalDiscoveryServerConfiguration
            {
                ApplicationCertificatesStorePath = Path.Combine(path, "applications"),
                AuthoritiesStorePath = Path.Combine(path, "authorities"),
                CertificateGroups = [group.Configuration]
            });
            return new ApplicationsNodeManager(
                ReferenceServer.CurrentInstance, configuration, database, database, group, autoApprove: true)
            {
                DeviceRegistrarCertificateAuthorization = authorization,
                CertificateRequestIssuanceStore = receipts
            };
        }

        /// <summary>
        /// Keeps the cryptographic fixture independent of the handler's retry and persistence decisions.
        /// </summary>
        private static Mock<ICertificateGroup> CreateCertificateGroup(string path, ByteString certificate)
        {
            var group = new Mock<ICertificateGroup>();
            group.SetupAllProperties();
            group.SetupGet(g => g.Configuration).Returns(new CertificateGroupConfiguration
            {
                Id = "DefaultApplicationGroup",
                SubjectName = "CN=Receipt issuer",
                BaseStorePath = Path.Combine(path, "group"),
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)]
            });
            group.Object.CertificateTypes = [Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType];
            group.SetupGet(g => g.Certificates).Returns(new ConcurrentDictionary<NodeId, Certificate?>());
            group.Setup(g => g.InitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            group.Setup(g => g.Create(It.IsAny<string>(), It.IsAny<CertificateGroupConfiguration>(),
                It.IsAny<string?>())).Returns(group.Object);
            group.Setup(g => g.SigningRequestAsync(
                It.IsAny<ApplicationRecordDataType>(), It.IsAny<NodeId>(), It.IsAny<string[]>(),
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Certificate.FromRawData(certificate));
            return group;
        }

        /// <summary>
        /// Supplies a real encrypted-channel operation context without granting an application privilege.
        /// </summary>
        private ServerSystemContext CreateContext(Role role)
        {
            var identity = new GdsRoleBasedIdentity(
                new UserIdentity(new UserNameIdentityToken { UserName = "onboarding-test" }),
                [role], default, null, ReferenceServer.CurrentInstance.NamespaceUris);
            var channel = new SecureChannelContext(
                "onboarding-directory-tests",
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                },
                RequestEncoding.Binary,
                [1, 2, 3]);
            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity);
            return ReferenceServer.CurrentInstance.DefaultSystemContext.Copy(operation);
        }

        /// <summary>
        /// Invokes each supported certificate-request handler without issuing a real certificate.
        /// </summary>
        private static async ValueTask<ServiceResult> InvokeAsync(
            ApplicationsNodeManager manager,
            string handler,
            ISystemContext context,
            NodeId applicationId)
        {
            var method = new MethodState(null);
            return handler switch
            {
                "newKey" => (await manager.OnStartNewKeyPairRequestAsync(
                    context, method, default, applicationId, default, default,
                    "CN=Onboarding", default, "PFX", "unused", CancellationToken.None).ConfigureAwait(false))
                    .ServiceResult,
                "signing" => (await manager.OnStartSigningRequestAsync(
                    context, method, default, applicationId, default, default,
                    ByteString.From(new byte[] { 1, 2, 3 }), CancellationToken.None).ConfigureAwait(false))
                    .ServiceResult,
                "finish" => (await manager.OnFinishRequestAsync(
                    context, method, default, applicationId, new NodeId("request", 1),
                    CancellationToken.None).ConfigureAwait(false)).ServiceResult,
                "trustList" => (await manager.OnGetTrustListAsync(
                    context, method, default, applicationId, default, CancellationToken.None).ConfigureAwait(false))
                    .ServiceResult,
                "certificates" => (await manager.OnGetCertificatesAsync(
                    context, method, default, applicationId, default, CancellationToken.None).ConfigureAwait(false))
                    .ServiceResult,
                _ => throw new ArgumentOutOfRangeException(nameof(handler))
            };
        }
    }
}
