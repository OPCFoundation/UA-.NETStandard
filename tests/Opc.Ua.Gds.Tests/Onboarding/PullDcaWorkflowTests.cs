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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Onboarding;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Deterministic tests of the independent pull-DCA workflow and fluent registration.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PullDcaWorkflowTests
    {
        /// <summary>
        /// An external DCA proceeds directly to managers and ordinary managed-application signing and installation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalHandoffSkipsTicketSelectionAndReusesManagedApplicationProvisioning(bool managed)
        {
            var fixture = new Fixture();
            PullDcaManagedApplication application = CreateApplication();
            PullDcaExternalAuthenticationResult result = await fixture.Create(includeSoftware: false)
                .RunExternalAuthenticationAsync(kRegistrar, managed ? [application] : []).ConfigureAwait(false);

            Assert.That(fixture.Calls.Take(2), Is.EqualTo(s_connectManagersCalls));
            Assert.That(fixture.Calls, Does.Not.Contain("provide"));
            Assert.That(fixture.Calls, Does.Not.Contain("identity"));
            Assert.That(fixture.Calls, Does.Not.Contain("software"));
            Assert.That(fixture.Calls, Does.Not.Contain("reconnect"));
            Assert.That(result.Managers, Is.EqualTo(fixture.Connection.Managers));
            Assert.That(result.ManagedApplications.Count, Is.EqualTo(managed ? 1 : 0));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(managed ? new[] { kManagedId } : []));
            if (managed)
            {
                Assert.That(result.ManagedApplications[0].ApplicationId, Is.EqualTo(kManagedId));
                Assert.That(result.ManagedApplications[0].Credentials.Certificate, Is.EqualTo(kCertificate));
                Assert.That(fixture.Connection.RegisteredApplication!.Application,
                    Is.Not.SameAs(application.Application));
            }
        }

        /// <summary>
        /// A failed registrar admission cannot reach registration or installation.
        /// </summary>
        [Test]
        public async Task ExternalHandoffPropagatesManagerAdmissionFailureBeforeAnyCertificateRequest()
        {
            var fixture = new Fixture();
            fixture.FailingOperation = "managers";

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunExternalAuthenticationAsync(kRegistrar, [CreateApplication()])
                    .ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Is.EqualTo(s_connectManagersCalls));
        }

        [Test]
        public async Task PullManagementProvesIdentityCompletesGateAndProvisionsEveryApplicationInOrder()
        {
            var fixture = new Fixture();
            var manager = new ManagerDescription
            {
                IsRequired = true,
                PurposeUri = "urn:software:update",
                EndpointUrls = ["opc.tcp://update:4840"]
            };
            fixture.Connection.Selection = fixture.Connection.Selection with { SoftwareUpdateManager = manager };
            var progress = new ProgressRecorder();
            PullDcaManagedApplication application = CreateApplication();
            PullDcaRequest request = CreateRequest() with { ManagedApplications = [application] };

            PullDcaResult result = await fixture.Create().RunAsync(kRegistrar, request, progress)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls, Is.EqualTo(s_fullWorkflowCalls));
            Assert.That(fixture.Host.ActivatedIdentity, Is.EqualTo(kIdentity));
            Assert.That(fixture.Host.SoftwareIdentity, Is.SameAs(result.Identity));
            Assert.That(fixture.Host.SoftwareManager, Is.SameAs(manager));
            Assert.That(fixture.Host.SoftwareRegistrar, Is.SameAs(kRegistrar));
            Assert.That(fixture.Connection.Offer, Is.Not.SameAs(request.IdentityOffer));
            Assert.That(fixture.Connection.Offer, Is.EqualTo(request.IdentityOffer));
            Assert.That(fixture.Connection.RegisteredApplication, Is.Not.SameAs(application));
            Assert.That(fixture.Connection.RegisteredApplication!.Application, Is.Not.SameAs(application.Application));
            Assert.That(fixture.Connection.RegisteredApplication.Application.ApplicationUri,
                Is.EqualTo(application.Application.ApplicationUri));
            Assert.That(fixture.Connection.RegisteredApplication.ProtocolUri, Is.EqualTo(application.ProtocolUri));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(result.DcaCredentials.TrustList, Is.SameAs(fixture.Connection.TrustList));
            Assert.That(result.Managers, Is.EqualTo(fixture.Connection.Managers));
            Assert.That(result.ManagedApplications.Count, Is.EqualTo(1));
            Assert.That(result.ManagedApplications[0].ApplicationId, Is.EqualTo(kManagedId));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId, kManagedId }));
            Assert.That(progress.Items[^1].Stage, Is.EqualTo(PullDcaStage.Completed));
            PullDcaProgress pending = progress.Items.First(p => p.Stage == PullDcaStage.FinishingCertificate);
            Assert.That(pending.ApplicationId, Is.EqualTo(kDcaId));
            Assert.That(pending.RequestId, Is.EqualTo(kRequestId));
        }

        [Test]
        public async Task NoSoftwareGateOrManagedApplicationsRequiresNoSoftwareProvider()
        {
            var fixture = new Fixture();
            PullDcaResult result = await fixture.Create(includeSoftware: false).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            Assert.That(result.ManagedApplications.IsEmpty, Is.True);
            Assert.That(fixture.Calls.Count(c => c == "reconnect"), Is.EqualTo(2));
            Assert.That(fixture.Calls, Does.Not.Contain("software"));
            Assert.That(fixture.Calls, Does.Not.Contain("register"));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
        }

        /// <summary>
        /// Caller mutations while identity selection is pending cannot change the approved request or registrations.
        /// </summary>
        [Test]
        public async Task InFlightRequestRetainsOfferedBytesTicketsAndManagedApplicationSnapshot()
        {
            var fixture = new Fixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Connection.IdentityWait = async ct =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            };
            byte[] identity = [1, 2, 3];
            byte[] issuer = [4, 5, 6];
            string[] tickets = ["ticket"];
            string[] urls = ["opc.tcp://managed:4840"];
            string[] capabilities = ["DA"];
            LocalizedText[] names = [new LocalizedText("Managed application")];
            PullDcaManagedApplication application = CreateApplication();
            application.Application.ApplicationId = kManagedId;
            application.Application.ApplicationType = ApplicationType.ClientAndServer;
            application.Application.ApplicationNames = names;
            application.Application.DiscoveryUrls = urls;
            application.Application.ServerCapabilities = capabilities;
            application.Application.ProductUri = "urn:original:product";
            PullDcaManagedApplication[] applications = [application];
            PullDcaRequest request = CreateRequest() with
            {
                IdentityOffer = new PullDcaIdentityOffer(
                    [ByteString.From(identity)], [ByteString.From(issuer)], tickets),
                ManagedApplications = applications
            };
            Task<PullDcaResult> running = fixture.Create().RunAsync(kRegistrar, request).AsTask();
            await entered.Task.ConfigureAwait(false);
            try
            {
                identity[0] = 99;
                issuer[0] = 99;
                tickets[0] = "changed";
                urls[0] = "opc.tcp://changed:4840";
                capabilities[0] = "changed";
                names[0] = new LocalizedText("Changed");
                application.Application.ApplicationId = NodeId.Null;
                application.Application.ApplicationUri = "urn:changed:application";
                application.Application.ApplicationType = ApplicationType.DiscoveryServer;
                application.Application.ProductUri = "urn:changed:product";
                applications[0] = application with { ProtocolUri = "urn:changed:protocol" };
            }
            finally
            {
                release.SetResult(true);
            }
            PullDcaResult result = await running.ConfigureAwait(false);

            Assert.That(fixture.Connection.Offer!.Identities[0], Is.EqualTo(kIdentity));
            Assert.That(fixture.Connection.Offer.Issuers[0], Is.EqualTo(kIssuer));
            Assert.That(fixture.Connection.Offer.Tickets, Is.EqualTo(s_expectedTickets));
            PullDcaManagedApplication registered = fixture.Connection.RegisteredApplication!;
            Assert.That(registered.Application.ApplicationId, Is.EqualTo(kManagedId));
            Assert.That(registered.Application.ApplicationUri, Is.EqualTo("urn:managed:application"));
            Assert.That(registered.Application.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
            Assert.That(registered.Application.ProductUri, Is.EqualTo("urn:original:product"));
            Assert.That(registered.Application.ApplicationNames,
                Is.EqualTo(new[] { new LocalizedText("Managed application") }.ToArrayOf()));
            Assert.That(registered.Application.DiscoveryUrls,
                Is.EqualTo(s_expectedDiscoveryUrls));
            Assert.That(registered.Application.ServerCapabilities, Is.EqualTo(s_expectedCapabilities));
            Assert.That(registered.ProtocolUri, Is.EqualTo("urn:opcua"));
            Assert.That(result.ManagedApplications.Count, Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId, kManagedId }));
        }

        [Test]
        public async Task RequiredSoftwareProviderCannotBeReplacedByFabricatedSuccess()
        {
            var fixture = new Fixture();
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                SoftwareUpdateManager = new ManagerDescription { IsRequired = true }
            };
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(includeSoftware: false).RunAsync(kRegistrar, CreateRequest())
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls, Is.EqualTo(s_identityCalls));
        }

        [TestCase("identity")]
        [TestCase("reconnect")]
        [TestCase("software")]
        [TestCase("csr:dca")]
        [TestCase("start:dca")]
        [TestCase("finish:dca")]
        [TestCase("trust:dca")]
        [TestCase("install:dca")]
        [TestCase("managers")]
        [TestCase("register")]
        [TestCase("install:managed")]
        public async Task ProviderOrServiceFailurePropagatesWithoutCompletionOrRetries(string failingOperation)
        {
            var fixture = new Fixture { FailingOperation = failingOperation };
            fixture.Connection.Selection = fixture.Connection.Selection with
            {
                SoftwareUpdateManager = new ManagerDescription { IsRequired = true }
            };
            var progress = new ProgressRecorder();
            PullDcaRequest request = CreateRequest() with { ManagedApplications = [CreateApplication()] };

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, request, progress).ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Calls[^1], Is.EqualTo(failingOperation));
            Assert.That(fixture.Calls.Count(c => c == failingOperation), Is.EqualTo(1));
            Assert.That(progress.Items.Any(p => p.Stage == PullDcaStage.Completed), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task InvalidIdentitySelectionNeverActivatesCertificateOrRequestsCredentials(int invalidSelection)
        {
            var fixture = new Fixture();
            fixture.Connection.Selection = invalidSelection switch
            {
                0 => fixture.Connection.Selection with { SelectedIdentity = ByteString.From(new byte[] { 99 }) },
                1 => fixture.Connection.Selection with { ApplicationId = NodeId.Null },
                _ => fixture.Connection.Selection with { MatchingTicket = null! }
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.Calls, Is.EqualTo(s_provideCalls));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingCertificateIsBoundedAndPreservesApplicationAndRequestIds(bool emptyResponse)
        {
            var fixture = new Fixture();
            fixture.Connection.PendingResponses = 10;
            fixture.Connection.EmptyPendingResponse = emptyResponse;
            var options = new PullDcaOptions { MaximumFinishAttempts = 2, FinishRetryDelay = TimeSpan.Zero };

            PullDcaCertificatePendingException? error =
                await Assert.ThrowsAsync<PullDcaCertificatePendingException>(async () =>
                    await fixture.Create(options).RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.ApplicationId, Is.EqualTo(kDcaId));
            Assert.That(error.RequestId, Is.EqualTo(kRequestId));
            Assert.That(fixture.Calls.Count(c => c == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(2));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Does.Not.Contain("trust:dca"));
        }

        [Test]
        public async Task PendingCertificateThenApprovalReusesRequestAndInstallsOnlyAfterApproval()
        {
            var fixture = new Fixture();
            fixture.Connection.PendingResponses = 1;
            var options = new PullDcaOptions { FinishRetryDelay = TimeSpan.Zero };

            await fixture.Create(options).RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(fixture.Calls.Count(c => c == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(2));
            Assert.That(fixture.Calls.Count(c => c == "install:dca"), Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
        }

        /// <summary>
        /// Transport failure retries finish against the existing signing request without regenerating its key.
        /// </summary>
        [TestCase("timeout")]
        [TestCase("communication")]
        [TestCase("connection")]
        [TestCase("channel")]
        [TestCase("no-communication")]
        public async Task FinishTransportFailureRetriesOnlyTheOriginalSigningRequest(string scenario)
        {
            StatusCode status = scenario switch
            {
                "timeout" => StatusCodes.BadTimeout,
                "communication" => StatusCodes.BadCommunicationError,
                "connection" => StatusCodes.BadConnectionClosed,
                "channel" => StatusCodes.BadSecureChannelClosed,
                "no-communication" => StatusCodes.BadNoCommunication,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var fixture = new Fixture();
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(status));
            var options = new PullDcaOptions { MaximumFinishAttempts = 2, FinishRetryDelay = TimeSpan.Zero };

            PullDcaResult result = await fixture.Create(options).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Calls.Count(call => call == "csr:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
        }

        /// <summary>
        /// Exhausting the shared finish budget preserves the final transport diagnostic rather than reporting pending.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public async Task ExhaustedFinishTransportBudgetPreservesLastErrorWithoutInstallation(int maximumAttempts)
        {
            var fixture = new Fixture();
            for (int i = 1; i < maximumAttempts; i++)
            {
                fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadTimeout));
            }
            var last = new ServiceResultException(StatusCodes.BadConnectionClosed, "Final transport diagnostic.");
            fixture.Connection.FinishFailures.Enqueue(last);
            var options = new PullDcaOptions
            {
                MaximumFinishAttempts = maximumAttempts,
                FinishRetryDelay = TimeSpan.Zero
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(options).RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(last));
            Assert.That(fixture.Connection.FinishedRequestIds,
                Is.EqualTo(Enumerable.Repeat(kRequestId, maximumAttempts)));
            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Calls, Does.Not.Contain("trust:dca"));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Cancellation after a transport failure prevents a second finish call without waiting out the retry delay.
        /// </summary>
        [Test]
        public async Task FinishTransportRetryHonorsCancellationBeforeAnotherDispatch()
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            fixture.Connection.AfterFinish = cancellation.Cancel;
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadTimeout));
            var options = new PullDcaOptions { FinishRetryDelay = TimeSpan.FromSeconds(30) };

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await fixture.Create(options).RunAsync(
                    kRegistrar, CreateRequest(), cancellationToken: cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// A lost response followed by a consumed request cannot bypass request-bound recovery validation.
        /// </summary>
        [Test]
        public async Task LostFinishResponseFollowedByConsumedRequestRequiresExplicitRequestValidation()
        {
            var fixture = new Fixture();
            fixture.Connection.FinishFailures.Enqueue(new ServiceResultException(StatusCodes.BadTimeout));
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            fixture.Connection.TrustList.IssuerCertificates = [kIssuer];
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.Setup(value => value.ValidateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile,
                    It.Is<PullDcaCredentials>(credentials => credentials.Certificate == kCertificate),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var options = new PullDcaOptions { MaximumFinishAttempts = 2, FinishRetryDelay = TimeSpan.Zero };

            await fixture.Create(options, signingRecovery: recovery.Object).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            Assert.That(fixture.Calls.Count(call => call == "start:dca"), Is.EqualTo(1));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
            Assert.That(fixture.Calls.Count(call => call == "certificates:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            recovery.VerifyAll();
            recovery.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A journaling recovery provider learns the exact started request before the first FinishRequest.
        /// </summary>
        [Test]
        public async Task SigningRequestObserverRecordsStartedRequestBeforeFirstFinish()
        {
            var fixture = new Fixture();
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            Mock<IPullDcaSigningRequestObserver> observer = recovery.As<IPullDcaSigningRequestObserver>();
            observer.Setup(value => value.OnSigningRequestStartedAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, kCsr, It.IsAny<CancellationToken>()))
                .Callback(() => fixture.Calls.Add("observe:dca"))
                .Returns(default(ValueTask));

            await fixture.Create(signingRecovery: recovery.Object).RunAsync(kRegistrar, CreateRequest())
                .ConfigureAwait(false);

            int started = fixture.Calls.IndexOf("start:dca");
            Assert.That(fixture.Calls.IndexOf("observe:dca"), Is.EqualTo(started + 1));
            Assert.That(fixture.Calls.IndexOf("finish:dca"), Is.EqualTo(started + 2));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            observer.Verify(value => value.OnSigningRequestStartedAsync(
                kRegistrar, kDcaId, kRequestId, kProfile, kCsr, It.IsAny<CancellationToken>()), Times.Once);
            recovery.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A request that could not be journaled is never finished, so its certificate is never left unrecoverable.
        /// </summary>
        [Test]
        public async Task SigningRequestObserverFailurePreventsFinish()
        {
            var fixture = new Fixture();
            var failure = new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.As<IPullDcaSigningRequestObserver>()
                .Setup(value => value.OnSigningRequestStartedAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, kCsr, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(failure)));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(signingRecovery: recovery.Object).RunAsync(kRegistrar, CreateRequest())
                    .ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(fixture.Calls, Does.Contain("start:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("finish:dca"));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.Empty);
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// A host can resume a persisted request without repeating identity selection, key generation or submission.
        /// </summary>
        [Test]
        public async Task CompletePersistedRequestDoesNotCreateAnotherKeyOrCertificateRequest()
        {
            var fixture = new Fixture();
            PullDcaWorkflow workflow = fixture.Create(new PullDcaOptions
            {
                MaximumFinishAttempts = 1,
                FinishRetryDelay = TimeSpan.Zero
            });
            fixture.Connection.PendingResponses = 1;
            PullDcaCertificatePendingException? pending =
                await Assert.ThrowsAsync<PullDcaCertificatePendingException>(async () =>
                    await workflow.RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));
            fixture.Calls.Clear();
            var progress = new ProgressRecorder();

            PullDcaCredentials credentials = await workflow.CompleteCertificateRequestAsync(
                kRegistrar, pending!.ApplicationId, pending.RequestId, CreateRequest().DcaCertificateProfile, progress)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls,
                Is.EqualTo(s_finishAndInstallCalls));
            Assert.That(credentials.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId, kRequestId }));
            Assert.That(progress.Items.Any(p => p.Stage is PullDcaStage.RequestingCertificate or
                PullDcaStage.ProvidingIdentities or PullDcaStage.Completed), Is.False);
        }

        /// <summary>
        /// Resumption remains bounded and retains the original request when approval is still pending.
        /// </summary>
        [Test]
        public async Task CompletePersistedRequestRetainsIdentifiersWhenStillPending()
        {
            var fixture = new Fixture();
            fixture.Connection.PendingResponses = 10;
            PullDcaWorkflow workflow = fixture.Create(new PullDcaOptions
            {
                MaximumFinishAttempts = 2,
                FinishRetryDelay = TimeSpan.Zero
            });

            PullDcaCertificatePendingException? pending =
                await Assert.ThrowsAsync<PullDcaCertificatePendingException>(async () =>
                    await workflow.CompleteCertificateRequestAsync(
                        kRegistrar, kDcaId, kRequestId, CreateRequest().DcaCertificateProfile).ConfigureAwait(false));

            Assert.That(pending!.ApplicationId, Is.EqualTo(kDcaId));
            Assert.That(pending.RequestId, Is.EqualTo(kRequestId));
            Assert.That(fixture.Calls, Is.EqualTo(s_pendingFinishCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Missing persisted identifiers are rejected before accessing a session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompletePersistedRequestRequiresBothIdentifiers(bool missingApplication)
        {
            var fixture = new Fixture();
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await fixture.Create().CompleteCertificateRequestAsync(
                    kRegistrar, missingApplication ? NodeId.Null : kDcaId,
                    missingApplication ? kRequestId : NodeId.Null,
                    CreateRequest().DcaCertificateProfile).ConfigureAwait(false));
            Assert.That(fixture.Calls, Is.Empty);
        }

        /// <summary>
        /// Provider failures stop resumption and do not produce a workflow completion notification.
        /// </summary>
        [TestCase("finish:dca")]
        [TestCase("trust:dca")]
        [TestCase("install:dca")]
        public async Task CompletePersistedRequestPropagatesServiceAndInstallationFailures(string operation)
        {
            var fixture = new Fixture { FailingOperation = operation };
            var progress = new ProgressRecorder();
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create().CompleteCertificateRequestAsync(
                    kRegistrar, kDcaId, kRequestId, CreateRequest().DcaCertificateProfile, progress)
                    .ConfigureAwait(false));
            Assert.That(failure!.Message, Is.EqualTo("host failure"));
            Assert.That(fixture.Calls[^1], Is.EqualTo(operation));
            Assert.That(fixture.Calls, Does.Not.Contain("csr:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("start:dca"));
            Assert.That(progress.Items.Any(p => p.Stage == PullDcaStage.Completed), Is.False);
        }

        /// <summary>
        /// A received certificate survives later failures and a new workflow never repeats accepted FinishRequest.
        /// This recreates adapters over a retained in-memory backend, not a power-loss durability test.
        /// </summary>
        [TestCase("trust:dca")]
        [TestCase("install:dca")]
        public async Task RetainedSigningOutputResumesAfterTrustOrInstallationFailure(string operation)
        {
            var fixture = new Fixture { FailingOperation = operation };
            var secrets = new InMemorySecretStore();
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:unused");
            namespaces.Append("urn:registrar:model");
            using (var first = new SecretStorePullDcaCertificateReceiptStore(
                secrets, namespaces, "device", NUnitTelemetryContext.Create()))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await fixture.Create(receipts: first).CompleteCertificateRequestAsync(
                        kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));
            }
            fixture.Calls.Clear();
            fixture.FailingOperation = null;
            fixture.Connection.CertificateError = StatusCodes.BadRequestNotAllowed;
            using var restarted = new SecretStorePullDcaCertificateReceiptStore(
                secrets, namespaces, "device", NUnitTelemetryContext.Create());

            PullDcaCredentials result = await fixture.Create(receipts: restarted).CompleteCertificateRequestAsync(
                kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false);

            Assert.That(result.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Calls, Is.EqualTo(s_installCalls));
            Assert.That(fixture.Connection.FinishedRequestIds, Is.EqualTo(new[] { kRequestId }));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
        }

        /// <summary>
        /// Failure to retain output stops the workflow before trust download or installation.
        /// </summary>
        [Test]
        public async Task ReceiptPublicationFailureDoesNotInstallCredentials()
        {
            var fixture = new Fixture();
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            var failure = new InvalidOperationException("Receipt publication failed.");
            receipts.Setup(s => s.GetOrCreateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile,
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (PullDcaRegistrar _, NodeId _, NodeId _, PullDcaCertificateProfile _,
                    Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>> receive, CancellationToken ct) =>
                {
                    await receive(ct).ConfigureAwait(false);
                    throw failure;
                });

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Create(receipts: receipts.Object).CompleteCertificateRequestAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(fixture.Calls, Is.EqualTo(s_finishCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Invalid retained output is not interpreted as installed credentials or permission to finish again.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidReceiptDoesNotFinishDownloadTrustOrInstall(bool missingReceipt)
        {
            var fixture = new Fixture();
            var receipts = new Mock<IPullDcaCertificateReceiptStore>(MockBehavior.Strict);
            receipts.Setup(s => s.GetOrCreateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile,
                    It.IsAny<Func<CancellationToken, ValueTask<PullDcaCertificateReceipt>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(missingReceipt ? null! : new PullDcaCertificateReceipt(default, []));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(receipts: receipts.Object).CompleteCertificateRequestAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls, Is.EqualTo(s_connectCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        [Test]
        public async Task EmptySigningRequestFailsBeforeCallingCertificateDirectory()
        {
            var fixture = new Fixture();
            fixture.Host.SigningRequest = default;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Calls[^1], Is.EqualTo("csr:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("start:dca"));
        }

        /// <summary>
        /// Authorization, certificate validation, and invalid-request errors do not enter transport retry.
        /// </summary>
        [TestCase("authorization")]
        [TestCase("certificate")]
        [TestCase("invalid-request")]
        public async Task NonTransientCertificateFailureIsNeverRetried(string scenario)
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = scenario switch
            {
                "authorization" => StatusCodes.BadRequestNotAllowed,
                "certificate" => StatusCodes.BadCertificateInvalid,
                "invalid-request" => StatusCodes.BadInvalidArgument,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(fixture.Connection.CertificateError));
            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Does.Not.Contain("certificates:dca"));
        }

        /// <summary>
        /// Part 12 signing recovery requires the original binding and explicit host validation before installation.
        /// </summary>
        [Test]
        public async Task ConsumedSigningRequestRecoversOnlyAfterRequestBoundValidation()
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            fixture.Connection.TrustList.IssuerCertificates = [kIssuer];
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            recovery.Setup(r => r.ValidateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, It.IsAny<PullDcaCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Callback<PullDcaRegistrar, NodeId, NodeId, PullDcaCertificateProfile,
                    PullDcaCredentials, CancellationToken>((_, _, _, _, credentials, ct) =>
                {
                    fixture.Record("validate-recovery", ct);
                    Assert.That(credentials.Certificate, Is.EqualTo(kCertificate));
                    Assert.That(credentials.IssuerCertificates, Is.EqualTo(new[] { kIssuer }.ToArrayOf()));
                    Assert.That(credentials.TrustList, Is.SameAs(fixture.Connection.TrustList));
                    Assert.That(fixture.Host.InstalledIds, Is.Empty);
                })
                .Returns(default(ValueTask));

            PullDcaCredentials result = await fixture.Create(signingRecovery: recovery.Object)
                .CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false);

            Assert.That(result.Certificate, Is.EqualTo(kCertificate));
            Assert.That(fixture.Calls, Is.EqualTo(s_recoveryCalls));
            Assert.That(fixture.Host.InstalledIds, Is.EqualTo(new[] { kDcaId }));
            recovery.VerifyAll();
        }

        /// <summary>
        /// Missing validation is never replaced with an implicit acceptance of an assigned certificate.
        /// </summary>
        [Test]
        public async Task ConsumedSigningRequestWithoutRecoveryProviderDoesNotReadAssignments()
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create().CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(fixture.Calls, Is.EqualTo(s_finishCalls));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        /// <summary>
        /// Assignment shape, type and trust checks stop before the host may validate or install recovered output.
        /// </summary>
        [TestCase("length")]
        [TestCase("empty")]
        [TestCase("type")]
        [TestCase("duplicate")]
        [TestCase("trust")]
        public async Task SigningRecoveryRejectsInvalidAssignmentsAndIncompleteTrust(string failure)
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            switch (failure)
            {
                case "length":
                    fixture.Connection.AssignedTypes = [];
                    break;
                case "empty":
                    fixture.Connection.AssignedCertificates = [default];
                    break;
                case "type":
                    fixture.Connection.AssignedTypes = [new NodeId("other-type", 0)];
                    break;
                case "duplicate":
                    fixture.Connection.AssignedTypes = [kProfile.CertificateTypeId, kProfile.CertificateTypeId];
                    fixture.Connection.AssignedCertificates = [kCertificate, kCertificate];
                    break;
                case "trust":
                    fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.TrustedCertificates;
                    break;
            }
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(signingRecovery: recovery.Object)
                    .CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            StatusCode expected = failure switch
            {
                "length" => StatusCodes.BadUnexpectedError,
                "type" => StatusCodes.BadNotFound,
                _ => StatusCodes.BadSecurityChecksFailed
            };
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            Assert.That(fixture.Calls, Does.Not.Contain("csr:dca"));
            Assert.That(fixture.Calls, Does.Not.Contain("start:dca"));
            recovery.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Host rejection of stale material, a wrong key, or mismatched retained binding cannot publish a receipt.
        /// </summary>
        [Test]
        public async Task RejectedSigningRecoveryDoesNotPublishReceiptOrInstall()
        {
            var fixture = new Fixture();
            fixture.Connection.CertificateError = StatusCodes.BadInvalidArgument;
            fixture.Connection.TrustList.SpecifiedLists = (uint)TrustListMasks.All;
            var recovery = new Mock<IPullDcaSigningRecoveryProvider>(MockBehavior.Strict);
            var failure = new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            recovery.Setup(r => r.ValidateAsync(
                    kRegistrar, kDcaId, kRequestId, kProfile, It.IsAny<PullDcaCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Throws(failure);
            var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
            secrets.SetupGet(s => s.StoreType).Returns("test");
            secrets.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ISecret?>((ISecret?)null));
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:unused");
            namespaces.Append("urn:registrar:model");
            using var receipts = new SecretStorePullDcaCertificateReceiptStore(
                secrets.Object, namespaces, "device", NUnitTelemetryContext.Create());

            ServiceResultException? actual = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Create(receipts: receipts, signingRecovery: recovery.Object)
                    .CompleteCertificateRequestAsync(kRegistrar, kDcaId, kRequestId, kProfile).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
            secrets.Verify(s => s.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            recovery.VerifyAll();
        }

        [Test]
        public async Task PreCancelledRunDoesNotBorrowSession()
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await fixture.Create().RunAsync(kRegistrar, CreateRequest(), cancellationToken: cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task CancellationDuringApprovalWaitPreventsTrustDownloadAndInstallation()
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            fixture.Connection.AfterFinish = cancellation.Cancel;
            fixture.Connection.PendingResponses = 10;
            var options = new PullDcaOptions { FinishRetryDelay = TimeSpan.FromSeconds(1) };

            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await fixture.Create(options).RunAsync(
                    kRegistrar, CreateRequest(), cancellationToken: cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(fixture.Calls.Count(c => c == "finish:dca"), Is.EqualTo(1));
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        [Test]
        public async Task ConcurrentRunIsRejectedAndFailureReleasesWorkflowForNextRun()
        {
            var fixture = new Fixture();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Connection.IdentityWait = async ct =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("first run");
            };
            PullDcaWorkflow workflow = fixture.Create();
            Task<PullDcaResult> first = workflow.RunAsync(kRegistrar, CreateRequest()).AsTask();
            await entered.Task.ConfigureAwait(false);
            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await workflow.RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                ServiceResultException? resume = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await workflow.CompleteCertificateRequestAsync(
                        kRegistrar, kDcaId, kRequestId, CreateRequest().DcaCertificateProfile).ConfigureAwait(false));
                Assert.That(resume!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            finally
            {
                release.SetResult(true);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await first.ConfigureAwait(false));
            fixture.Connection.IdentityWait = null;

            PullDcaResult result = await workflow.RunAsync(kRegistrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(result.Identity.ApplicationId, Is.EqualTo(kDcaId));
        }

        [Test]
        public async Task DiscoveryReturnsConfiguredCandidatesWithoutConnecting()
        {
            var fixture = new Fixture();
            var options = new PullDcaOptions { Registrars = [kRegistrar] };

            ArrayOf<PullDcaRegistrar> registrars = await fixture.Create(options).DiscoverRegistrarsAsync()
                .ConfigureAwait(false);

            Assert.That(registrars.Count, Is.EqualTo(1));
            Assert.That(registrars[0], Is.SameAs(kRegistrar));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task DiscoveryUsesExplicitHostProviderWithoutConnectingToUnselectedCandidates()
        {
            var fixture = new Fixture();
            fixture.Host.DiscoveredRegistrars = [kRegistrar];

            ArrayOf<PullDcaRegistrar> registrars = await fixture.Create(includeDiscovery: true)
                .DiscoverRegistrarsAsync()
                .ConfigureAwait(false);

            Assert.That(registrars[0], Is.SameAs(kRegistrar));
            Assert.That(fixture.Calls, Is.EqualTo(s_discoveryCalls));
            Assert.That(fixture.Host.DiscoveryToken.CanBeCanceled, Is.True);
        }

        [Test]
        public async Task OperationDeadlineCancelsCooperativeProviderWithoutClaimingCompletion()
        {
            var fixture = new Fixture();
            fixture.Connection.IdentityWait = ct => Task.Delay(Timeout.Infinite, ct);
            var options = new PullDcaOptions { OperationTimeout = TimeSpan.FromMilliseconds(20) };
            var progress = new ProgressRecorder();

            await Assert.ThatAsync(async () =>
                await fixture.Create(options).RunAsync(kRegistrar, CreateRequest(), progress).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(fixture.Calls, Is.SubsetOf(s_provideCalls));
            Assert.That(progress.Items.Any(p => p.Stage == PullDcaStage.Completed), Is.False);
            Assert.That(fixture.Host.InstalledIds, Is.Empty);
        }

        [Test]
        public void DirectConstructionRequiresSessionAccessorTelemetryAndRealHostProviders()
        {
            var fixture = new Fixture();
            var options = new PullDcaOptions();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> accessor = (_, _) => default;

            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, null!, fixture.Host, fixture.Host, telemetry));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, accessor, fixture.Host, fixture.Host, null!));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, accessor, null!, fixture.Host, telemetry));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(options, accessor, fixture.Host, null!, telemetry));
            Assert.Throws<ArgumentNullException>(() =>
                new PullDcaWorkflow(null!, accessor, fixture.Host, fixture.Host, telemetry));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ConstructorRejectsInvalidTimeAndTrustListBudgets(int invalidBudget)
        {
            var fixture = new Fixture();
            var options = new PullDcaOptions();
            switch (invalidBudget)
            {
                case 0:
                    options.OperationTimeout = TimeSpan.Zero;
                    break;
                case 1:
                    options.FinishRetryDelay = TimeSpan.FromMilliseconds(-1);
                    break;
                case 2:
                    options.FinishRetryDelay = TimeSpan.FromMinutes(2);
                    break;
                default:
                    options.MaximumTrustListSize = 0;
                    break;
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Create(options));
        }

        [TestCase(0)]
        [TestCase(101)]
        public void ConstructorRejectsUnboundedOrEmptyAttemptBudget(int attempts)
        {
            var fixture = new Fixture();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                fixture.Create(new PullDcaOptions { MaximumFinishAttempts = attempts }));
        }

        [Test]
        public async Task InvalidRequestAndRegistrarFailBeforeBorrowingSession()
        {
            var fixture = new Fixture();
            PullDcaWorkflow workflow = fixture.Create();
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await workflow.RunAsync(null!, CreateRequest()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await workflow.RunAsync(kRegistrar with { RegistrarId = NodeId.Null }, CreateRequest())
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await workflow.RunAsync(kRegistrar, CreateRequest() with
                {
                    IdentityOffer = new PullDcaIdentityOffer(default, default, default)
                }).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await workflow.RunAsync(kRegistrar, CreateRequest() with
                {
                    DcaCertificateProfile = kProfile with { CertificateTypeId = NodeId.Null }
                }).ConfigureAwait(false));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task FluentRegistrationRetainsHostAccessorAndUsesExplicitProvidersWithoutConnecting()
        {
            var fixture = new Fixture();
            var services = new ServiceCollection();
            Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>> accessor =
                (_, _) => throw new InvalidOperationException("must not connect during registration or discovery");
            services.AddSingleton(accessor);
            services.AddSingleton<IPullDcaIdentityProvider>(fixture.Host);
            services.AddSingleton<IPullDcaCredentialProvider>(fixture.Host);
            services.AddSingleton(NUnitTelemetryContext.Create());
            IGdsClientBuilder builder = services.AddOpcUa().AddGdsClient();
            Assert.That(builder.AddPullDcaWorkflow(o => o.Registrars = [kRegistrar]), Is.SameAs(builder));
            builder.AddPullDcaWorkflow();
            using ServiceProvider provider = services.BuildServiceProvider();

            PullDcaWorkflow workflow = provider.GetRequiredService<PullDcaWorkflow>();
            ArrayOf<PullDcaRegistrar> registrars = await workflow.DiscoverRegistrarsAsync().ConfigureAwait(false);

            Assert.That(registrars[0], Is.SameAs(kRegistrar));
            Assert.That(provider.GetRequiredService<
                Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(), Is.SameAs(accessor));
            Assert.That(provider.GetRequiredService<PullDcaWorkflow>(), Is.SameAs(workflow));
            Assert.That(services.Count(s => s.ServiceType == typeof(PullDcaWorkflow)), Is.EqualTo(1));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public void FluentRegistrationDoesNotInventMissingHostProviders()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(
                (_, _) => throw new InvalidOperationException("unused"));
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddOpcUa().AddGdsClient().AddPullDcaWorkflow();
            using ServiceProvider provider = services.BuildServiceProvider();

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<PullDcaWorkflow>());

            Assert.That(error!.Message, Does.Contain(nameof(IPullDcaIdentityProvider)));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaGdsClientBuilderExtensions.AddPullDcaWorkflow(null!));
        }

        private static PullDcaRequest CreateRequest()
        {
            return new PullDcaRequest(new PullDcaIdentityOffer([kIdentity], [kIssuer], ["ticket"]), kProfile, default);
        }

        private static PullDcaManagedApplication CreateApplication()
        {
            return new PullDcaManagedApplication(
                new ApplicationRecordDataType { ApplicationUri = "urn:managed:application" }, "urn:opcua", kProfile);
        }

        private sealed class ProgressRecorder : IProgress<PullDcaProgress>
        {
            public List<PullDcaProgress> Items { get; } = [];

            public void Report(PullDcaProgress value)
            {
                Items.Add(value);
            }
        }

        private sealed class Fixture
        {
            public Fixture()
            {
                Connection = new FakeConnection(this);
                Host = new FakeHost(this);
            }

            public List<string> Calls { get; } = [];
            public FakeConnection Connection { get; }
            public FakeHost Host { get; }
            public string? FailingOperation { get; set; }

            public PullDcaWorkflow Create(
                PullDcaOptions? options = null, bool includeSoftware = true, bool includeDiscovery = false,
                IPullDcaCertificateReceiptStore? receipts = null,
                IPullDcaSigningRecoveryProvider? signingRecovery = null)
            {
                return new PullDcaWorkflow(
                    options ?? new PullDcaOptions(),
                    (_, ct) =>
                    {
                        Record("connect", ct);
                        return new ValueTask<IPullDcaConnection>(Connection);
                    },
                    Host, Host, includeSoftware ? Host : null, includeDiscovery ? Host : null,
                    receipts, signingRecovery);
            }

            public void Record(string operation, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Calls.Add(operation);
                if (operation == FailingOperation)
                {
                    throw new InvalidOperationException("host failure");
                }
            }
        }

        private sealed class FakeConnection : IPullDcaConnection
        {
            public FakeConnection(Fixture fixture)
            {
                m_fixture = fixture;
            }

            public PullDcaIdentitySelection Selection { get; set; } =
                new(kIdentity, new DeviceIdentityTicketType(), kDcaId, null);
            public PullDcaIdentityOffer? Offer { get; private set; }
            public PullDcaManagedApplication? RegisteredApplication { get; private set; }
            public TrustListDataType TrustList { get; } = new();
            public ArrayOf<ManagerDescription> Managers { get; } =
                [new ManagerDescription { Name = new LocalizedText("manager") }];
            public int PendingResponses { get; set; }
            public bool EmptyPendingResponse { get; set; }
            public StatusCode CertificateError { get; set; } = StatusCodes.Good;
            public Queue<Exception> FinishFailures { get; } = new();
            public List<NodeId> FinishedRequestIds { get; } = [];
            public Action? AfterFinish { get; set; }
            public Func<CancellationToken, Task>? IdentityWait { get; set; }
            public ArrayOf<NodeId> AssignedTypes { get; set; } = [kProfile.CertificateTypeId];
            public ArrayOf<ByteString> AssignedCertificates { get; set; } = [kCertificate];

            public async ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(
                PullDcaIdentityOffer offer, CancellationToken ct)
            {
                m_fixture.Record("provide", ct);
                Offer = offer;
                if (IdentityWait != null)
                {
                    await IdentityWait(ct).ConfigureAwait(false);
                }
                return Selection;
            }

            public ValueTask ReloadAndReconnectAsync(CancellationToken ct)
            {
                m_fixture.Record("reconnect", ct);
                return default;
            }

            public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct)
            {
                m_fixture.Record("managers", ct);
                return new ValueTask<ArrayOf<ManagerDescription>>(Managers);
            }

            public ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct)
            {
                m_fixture.Record("register", ct);
                RegisteredApplication = application;
                return new ValueTask<NodeId>(kManagedId);
            }

            public ValueTask<NodeId> StartAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct)
            {
                m_fixture.Record("start:" + applicationId.IdentifierAsString, ct);
                Assert.That(profile, Is.SameAs(kProfile));
                Assert.That(csr, Is.EqualTo(kCsr));
                return new ValueTask<NodeId>(kRequestId);
            }

            public ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
                NodeId applicationId, NodeId requestId, CancellationToken ct)
            {
                m_fixture.Record("finish:" + applicationId.IdentifierAsString, ct);
                FinishedRequestIds.Add(requestId);
                AfterFinish?.Invoke();
                if (FinishFailures.Count != 0)
                {
                    throw FinishFailures.Dequeue();
                }
                if (StatusCode.IsBad(CertificateError))
                {
                    throw new ServiceResultException(CertificateError);
                }
                if (PendingResponses-- > 0)
                {
                    if (!EmptyPendingResponse)
                    {
                        throw new ServiceResultException(StatusCodes.BadNothingToDo);
                    }
                    return new ValueTask<(ByteString, ArrayOf<ByteString>)>(
                        (default, ArrayOf<ByteString>.Null));
                }
                return new ValueTask<(ByteString, ArrayOf<ByteString>)>((kCertificate, new[] { kIssuer }.ToArrayOf()));
            }

            public ValueTask<TrustListDataType> ReadTrustListAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                m_fixture.Record("trust:" + applicationId.IdentifierAsString, ct);
                Assert.That(certificateGroupId, Is.EqualTo(kProfile.CertificateGroupId));
                return new ValueTask<TrustListDataType>(TrustList);
            }

            public ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                m_fixture.Record("certificates:" + applicationId.IdentifierAsString, ct);
                Assert.That(certificateGroupId, Is.EqualTo(kProfile.CertificateGroupId));
                return new ValueTask<(ArrayOf<NodeId>, ArrayOf<ByteString>)>(
                    (AssignedTypes, AssignedCertificates));
            }

            private readonly Fixture m_fixture;
        }

        private sealed class FakeHost :
            IPullDcaIdentityProvider, IPullDcaCredentialProvider,
            IPullDcaSoftwareUpdateProvider, IPullDcaRegistrarDiscovery
        {
            public FakeHost(Fixture fixture)
            {
                m_fixture = fixture;
            }

            public ByteString ActivatedIdentity { get; private set; }
            public PullDcaIdentitySelection? SoftwareIdentity { get; private set; }
            public ManagerDescription? SoftwareManager { get; private set; }
            public PullDcaRegistrar? SoftwareRegistrar { get; private set; }
            public ByteString SigningRequest { get; set; } = kCsr;
            public List<NodeId> InstalledIds { get; } = [];
            public ArrayOf<PullDcaRegistrar> DiscoveredRegistrars { get; set; }
            public CancellationToken DiscoveryToken { get; private set; }

            public ValueTask ActivateAsync(ByteString selectedIdentity, CancellationToken cancellationToken)
            {
                m_fixture.Record("identity", cancellationToken);
                ActivatedIdentity = selectedIdentity;
                return default;
            }

            public ValueTask CompleteAsync(
                PullDcaRegistrar registrar, PullDcaIdentitySelection identity,
                ManagerDescription manager, CancellationToken cancellationToken)
            {
                m_fixture.Record("software", cancellationToken);
                SoftwareIdentity = identity;
                SoftwareManager = manager;
                SoftwareRegistrar = registrar;
                return default;
            }

            public ValueTask<ByteString> CreateSigningRequestAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, CancellationToken cancellationToken)
            {
                m_fixture.Record("csr:" + applicationId.IdentifierAsString, cancellationToken);
                Assert.That(profile, Is.SameAs(kProfile));
                return new ValueTask<ByteString>(SigningRequest);
            }

            public ValueTask InstallAsync(
                NodeId applicationId, PullDcaCredentials credentials, CancellationToken cancellationToken)
            {
                m_fixture.Record("install:" + applicationId.IdentifierAsString, cancellationToken);
                Assert.That(credentials.Certificate, Is.EqualTo(kCertificate));
                Assert.That(credentials.IssuerCertificates, Is.EqualTo(new[] { kIssuer }.ToArrayOf()));
                Assert.That(credentials.TrustList, Is.SameAs(m_fixture.Connection.TrustList));
                InstalledIds.Add(applicationId);
                return default;
            }

            public ValueTask<ArrayOf<PullDcaRegistrar>> DiscoverAsync(CancellationToken cancellationToken)
            {
                m_fixture.Record("discover", cancellationToken);
                DiscoveryToken = cancellationToken;
                return new ValueTask<ArrayOf<PullDcaRegistrar>>(DiscoveredRegistrars);
            }

            private readonly Fixture m_fixture;
        }

        private static readonly string[] s_fullWorkflowCalls =
        [
            "connect", "provide", "identity", "reconnect", "software",
            "csr:dca", "start:dca", "finish:dca", "trust:dca", "install:dca",
            "reconnect", "managers", "register", "csr:managed", "start:managed",
            "finish:managed", "trust:managed", "install:managed"
        ];
        private static readonly string[] s_identityCalls = ["connect", "provide", "identity", "reconnect"];
        private static readonly string[] s_provideCalls = ["connect", "provide"];
        private static readonly string[] s_finishAndInstallCalls =
            ["connect", "finish:dca", "trust:dca", "install:dca"];
        private static readonly string[] s_pendingFinishCalls = ["connect", "finish:dca", "finish:dca"];
        private static readonly string[] s_installCalls = ["connect", "trust:dca", "install:dca"];
        private static readonly string[] s_finishCalls = ["connect", "finish:dca"];
        private static readonly string[] s_connectCalls = ["connect"];
        private static readonly string[] s_connectManagersCalls = ["connect", "managers"];
        private static readonly string[] s_recoveryCalls =
        [
            "connect", "finish:dca", "certificates:dca", "trust:dca", "validate-recovery",
            "trust:dca", "install:dca"
        ];
        private static readonly string[] s_discoveryCalls = ["discover"];
        private static readonly ArrayOf<string> s_expectedTickets = ["ticket"];
        private static readonly ArrayOf<string> s_expectedDiscoveryUrls = ["opc.tcp://managed:4840"];
        private static readonly ArrayOf<string> s_expectedCapabilities = ["DA"];
        private static readonly PullDcaRegistrar kRegistrar = new(
            new Uri("opc.tcp://registrar:4840"), new NodeId("registrar", 2), new NodeId("directory", 2));
        private static readonly NodeId kDcaId = new("dca", 2);
        private static readonly NodeId kManagedId = new("managed", 2);
        private static readonly NodeId kRequestId = new("request", 2);
        private static readonly PullDcaCertificateProfile kProfile = new(
            new NodeId("group", 2), Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
        private static readonly ByteString kIdentity = ByteString.From(new byte[] { 1, 2, 3 });
        private static readonly ByteString kIssuer = ByteString.From(new byte[] { 4, 5, 6 });
        private static readonly ByteString kCsr = ByteString.From(new byte[] { 7, 8, 9 });
        private static readonly ByteString kCertificate = ByteString.From(new byte[] { 10, 11, 12 });
    }

    /// <summary>
    /// Verifies the production transport uses generated native method proxies and bounded file transfer.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class PullDcaConnectionTests
    {
        [Test]
        public async Task ProductionAdapterUsesGeneratedRegistrarAndCertificateDirectoryMethods()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var requests = new List<CallMethodRequest>();
            var manager = new ManagerDescription
            {
                PurposeUri = "urn:manager",
                EndpointUrls = ["opc.tcp://host:4840"]
            };
            var ticket = new DeviceIdentityTicketType { ProductInstanceUri = "urn:device" };
            var applicationId = new NodeId("app", 2);
            var requestId = new NodeId("request", 2);
            ByteString certificate = ByteString.From(new byte[] { 1, 2, 3 });
            ArrayOf<ByteString> issuers = [certificate];
            ArrayOf<ManagerDescription> managers = [manager];
            Mock<ISession> session = CreateSession(context, request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(
                        certificate, Variant.FromStructure(ticket), applicationId, Variant.FromStructure(manager)),
                    2 => Good(Variant.FromStructure(managers)),
                    3 => Good(applicationId),
                    4 => Good(requestId),
                    5 => Good(certificate, default(ByteString), Variant.From(issuers)),
                    _ => Good(Variant.From(new[] { kProfile.CertificateTypeId }.ToArrayOf()), Variant.From(issuers))
                };
            });
            int reconnects = 0;
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 1024, _ =>
            {
                reconnects++;
                return default;
            });
            var offer = new PullDcaIdentityOffer([certificate], issuers, ["ticket"]);
            var application = new PullDcaManagedApplication(
                new ApplicationRecordDataType { ApplicationUri = "urn:managed" },
                "urn:opcua", kProfile);

            PullDcaIdentitySelection identity = await adapter.ProvideIdentitiesAsync(offer, CancellationToken.None)
                .ConfigureAwait(false);
            await adapter.ReloadAndReconnectAsync(CancellationToken.None).ConfigureAwait(false);
            ArrayOf<ManagerDescription> actualManagers = await adapter.GetManagersAsync(CancellationToken.None)
                .ConfigureAwait(false);
            NodeId actualApplicationId = await adapter.RegisterAsync(application, CancellationToken.None)
                .ConfigureAwait(false);
            NodeId actualRequestId = await adapter.StartAsync(
                applicationId, kProfile, certificate, CancellationToken.None)
                .ConfigureAwait(false);
            var (issued, actualIssuers) = await adapter.FinishAsync(applicationId, requestId, CancellationToken.None)
                .ConfigureAwait(false);
            var (assignedTypes, assignedCertificates) = await adapter.GetCertificatesAsync(
                applicationId, kProfile.CertificateGroupId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(identity.SelectedIdentity, Is.EqualTo(certificate));
            Assert.That(identity.MatchingTicket, Is.SameAs(ticket));
            Assert.That(identity.SoftwareUpdateManager, Is.SameAs(manager));
            Assert.That(actualManagers, Is.EqualTo(managers));
            Assert.That(actualApplicationId, Is.EqualTo(applicationId));
            Assert.That(actualRequestId, Is.EqualTo(requestId));
            Assert.That(issued, Is.EqualTo(certificate));
            Assert.That(actualIssuers, Is.EqualTo(issuers));
            Assert.That(assignedTypes, Is.EqualTo(new[] { kProfile.CertificateTypeId }.ToArrayOf()));
            Assert.That(assignedCertificates, Is.EqualTo(issuers));
            Assert.That(reconnects, Is.EqualTo(1));
            Assert.That(requests.Select(r => r.MethodId), Is.EqualTo(new[]
            {
                Resolve(Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_ProvideIdentities, context),
                Resolve(Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_GetManagers, context),
                Resolve(Opc.Ua.Onboarding.MethodIds.DeviceRegistrarType_RegisterManagedApplication, context),
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_StartSigningRequest, context),
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_FinishRequest, context),
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetCertificates, context)
            }));
            Assert.That(requests.Take(3).All(r => r.ObjectId == kRegistrar.RegistrarId), Is.True);
            Assert.That(requests.Skip(3).All(r => r.ObjectId == kRegistrar.CertificateDirectoryId), Is.True);
            Assert.That(requests[0].InputArguments[0].TryGetValue(out ArrayOf<ByteString> sentIdentities), Is.True);
            Assert.That(sentIdentities, Is.EqualTo(offer.Identities));
            Assert.That(requests[0].InputArguments[2].TryGetValue(out ArrayOf<string> sentTickets), Is.True);
            Assert.That(sentTickets, Is.EqualTo(offer.Tickets));
            Assert.That(requests[3].InputArguments[0].TryGetValue(out NodeId sentApplicationId), Is.True);
            Assert.That(sentApplicationId, Is.EqualTo(applicationId));
            Assert.That(requests[3].InputArguments[3].TryGetValue(out ByteString sentCsr), Is.True);
            Assert.That(sentCsr, Is.EqualTo(certificate));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        [Test]
        public async Task SigningResponseCannotUnexpectedlySupplyPrivateKeyMaterial()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            ByteString certificate = ByteString.From(new byte[] { 1 });
            Mock<ISession> session = CreateSession(context, _ =>
                Good(certificate, certificate, Variant.From(ArrayOf<ByteString>.Null)));
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 1024, _ => default);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.FinishAsync(new NodeId(1), new NodeId(2), CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task TrustDownloadUsesGeneratedFileMethodsEnforcesBoundAndClosesHandleWithoutDisposingSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            var requests = new List<CallMethodRequest>();
            var trustListId = new NodeId("trust", 2);
            Mock<ISession> session = CreateSession(context, request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(trustListId),
                    2 => Good(42u),
                    3 => Good(ByteString.From(new byte[] { 1, 2, 3, 4, 5 })),
                    _ => Good()
                };
            });
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 4, _ => default);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await adapter.ReadTrustListAsync(
                    new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(requests.Select(r => r.MethodId), Is.EqualTo(new NodeId[]
            {
                Resolve(Opc.Ua.Gds.MethodIds.CertificateDirectoryType_GetTrustList, context),
                Opc.Ua.MethodIds.FileType_Open,
                Opc.Ua.MethodIds.FileType_Read,
                Opc.Ua.MethodIds.FileType_Close
            }));
            Assert.That(requests.Skip(1).All(r => r.ObjectId == trustListId), Is.True);
            Assert.That(requests[^1].InputArguments[0].TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.EqualTo(42u));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// File cleanup failure cannot hide the original trust-list transfer rejection.
        /// </summary>
        [Test]
        public async Task TrustDownloadAndHandleCleanupFailuresAreBothReported()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            var requests = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(context, request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(new NodeId("trust", 2)),
                    2 => Good(42u),
                    3 => Good(ByteString.From(new byte[] { 1, 2, 3, 4, 5 })),
                    _ => new CallMethodResult { StatusCode = StatusCodes.BadUserAccessDenied }
                };
            });
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 4, _ => default);

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await adapter.ReadTrustListAsync(
                    new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(error.InnerExceptions[1], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(requests[^1].MethodId, Is.EqualTo(Opc.Ua.MethodIds.FileType_Close));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        /// <summary>
        /// Transfer cancellation and transport errors retain their original exception when cleanup succeeds,
        /// and remain observable together with a cleanup error when both operations fail.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TrustDownloadPreservesTransferExceptionAndIndependentCleanupFailure(
            bool canceled, bool cleanupFails)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            context.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            Exception transfer = canceled
                ? new OperationCanceledException("Transfer canceled.")
                : new ServiceResultException(StatusCodes.BadCommunicationError);
            var cleanup = new ServiceResultException(StatusCodes.BadConnectionClosed);
            var requests = new List<CallMethodRequest>();
            Mock<ISession> session = CreateSession(context, request =>
            {
                requests.Add(request);
                return requests.Count switch
                {
                    1 => Good(new NodeId("trust", 2)),
                    2 => Good(42u),
                    3 => throw transfer,
                    _ when cleanupFails => throw cleanup,
                    _ => Good()
                };
            });
            var adapter = new PullDcaConnection(session.Object, kRegistrar, telemetry, 1024, _ => default);

            if (cleanupFails)
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await adapter.ReadTrustListAsync(
                        new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(error!.InnerExceptions, Is.EqualTo(new[] { transfer, cleanup }));
            }
            else if (canceled)
            {
                OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await adapter.ReadTrustListAsync(
                        new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(error, Is.SameAs(transfer));
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await adapter.ReadTrustListAsync(
                        new NodeId("app", 2), kProfile.CertificateGroupId, CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(error, Is.SameAs(transfer));
            }
            Assert.That(requests, Has.Count.EqualTo(4));
            Assert.That(requests[3].MethodId, Is.EqualTo(Opc.Ua.MethodIds.FileType_Close));
            session.Verify(s => s.Dispose(), Times.Never);
        }

        private static Mock<ISession> CreateSession(
            ServiceMessageContext context,
            Func<CallMethodRequest, CallMethodResult> handler)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.MessageContext).Returns(context);
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session.Setup(s => s.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) => new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [handler(requests[0])]
                    }));
            return session;
        }

        private static CallMethodResult Good(params Variant[] outputs)
        {
            return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs };
        }

        private static NodeId Resolve(ExpandedNodeId methodId, ServiceMessageContext context)
        {
            return ExpandedNodeId.ToNodeId(methodId, context.NamespaceUris);
        }

        private static readonly PullDcaRegistrar kRegistrar = new(
            new Uri("opc.tcp://registrar:4840"), new NodeId("registrar", 2), new NodeId("directory", 2));
        private static readonly PullDcaCertificateProfile kProfile = new(
            new NodeId("group", 2), Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
    }
}
