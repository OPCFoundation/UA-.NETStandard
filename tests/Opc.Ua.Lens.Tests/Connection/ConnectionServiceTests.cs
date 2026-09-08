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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ConnectionServiceTests
{
    [Test]
    public async Task ChangingUserUpdatesTheProfileAndFutureEngineCredentials()
    {
        m_fixture.Backend.RequireTrust = false;
        ConnectionProfile profile = m_fixture.UseUserNameProfile();
        var provider = new TrackingIdentityProvider(profile);
        await m_fixture.Service.ConnectAsync(profile, provider).ConfigureAwait(false);
        Mock.Get(m_fixture.Service.CurrentSession!).SetupGet(session => session.ConfiguredEndpoint)
            .Returns(new ConfiguredEndpoint(null, m_fixture.Endpoint, null));

        await m_fixture.Service.ChangeIdentityAsync(
            new UserIdentity("replacement-user", Guid.NewGuid().ToByteArray())).ConfigureAwait(false);
        Assert.That(m_fixture.Service.Profile!.IdentityName, Is.EqualTo("replacement-user"));
        Assert.That(m_fixture.Service.Profile.SecurityPolicyUri, Is.EqualTo(profile.SecurityPolicyUri));
        await m_fixture.Service.ReconnectAsync(SubscriptionEngineKind.Classic).ConfigureAwait(false);

        UserIdentityToken token = m_fixture.Backend.Identities[^1].TokenHandler.Token;
        Assert.That(token, Is.InstanceOf<UserNameIdentityToken>());
        Assert.That(((UserNameIdentityToken)token).UserName, Is.EqualTo("replacement-user"));
        Assert.That(m_fixture.Service.Profile!.IdentityName, Is.EqualTo("replacement-user"));
        Assert.That(m_fixture.Service.Profile.Engine, Is.EqualTo(SubscriptionEngineKind.Classic));
        Assert.That(provider.Identities, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ProfilelessImportCannotReuseThePreviousServerProfile()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        var workspace = new DocumentWorkspace<IPlugin>(
            NullLogger.Instance, InlineWorkspaceDispatcher.Instance, (_, _) => Task.CompletedTask);
        var model = new MainViewModel(telemetry, m_fixture.Service, workspace,
            dispatcher: InlineWorkspaceDispatcher.Instance);
        await using (model.ConfigureAwait(false))
        {
            await workspace.OpenAsync(() => new AvailabilityDocument()).ConfigureAwait(false);
            await m_fixture.ConnectAcceptOnceAsync().ConfigureAwait(false);
            await model.LoadSessionAsync(new SessionFile
            {
                EndpointUrl = "opc.tcp://localhost:4960/imported"
            }).ConfigureAwait(false);

            SessionFile saved = model.SnapshotSession();
            Assert.That(saved.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4960/imported"));
            Assert.That(saved.Profile, Is.Null);
            Assert.That(m_fixture.Service.Profile, Is.Not.Null, "The service may retain its separate resume intent.");
        }
    }

    [Test]
    public async Task CommittedRestoreKeepsNewConnectionMetadataWhenOldCleanupFails()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        var workspace = new DocumentWorkspace<IPlugin>(
            NullLogger.Instance, InlineWorkspaceDispatcher.Instance, (_, _) => Task.CompletedTask);
        var model = new MainViewModel(telemetry, m_fixture.Service, workspace,
            dispatcher: InlineWorkspaceDispatcher.Instance);
        await using (model.ConfigureAwait(false))
        {
            await workspace.OpenAsync(() => new AvailabilityDocument
            {
                DisposeFailure = new InvalidOperationException("Old document cleanup failed.")
            }).ConfigureAwait(false);
            ConnectionProfile imported = m_fixture.Profile with { EndpointUrl = "opc.tcp://localhost:4961/new" };
            await Assert.ThatAsync(() => model.LoadSessionAsync(new SessionFile
            {
                Version = "2",
                EndpointUrl = imported.EndpointUrl,
                Profile = imported
            }), Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(model.Tabs, Is.Empty);
            Assert.That(model.EndpointUrl, Is.EqualTo(imported.EndpointUrl));
            Assert.That(model.SnapshotSession().Profile, Is.EqualTo(imported));
        }
    }

    [Test]
    public async Task ConnectCommandReportsAggregatedLifecycleFailureInsteadOfThrowingOnTheDispatcher()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        var model = new MainViewModel(telemetry, m_fixture.Service, dispatcher: InlineWorkspaceDispatcher.Instance);
        await using (model.ConfigureAwait(false))
        {
            model.ConnectRequestedAsync = _ =>
                Task.FromException(new AggregateException(new InvalidOperationException("Attachment failed.")));
            await model.ConnectCommand.ExecuteAsync(null).ConfigureAwait(false);

            Assert.That(model.OperationError, Does.Contain("Attachment failed."));
            Assert.That(model.IsConnected, Is.False);
        }
    }

    [Test]
    public async Task QueuedInitialBindingSurvivesAnAvailabilityChangeWithinTheSameSession()
    {
        await m_fixture.ConnectAcceptOnceAsync().ConfigureAwait(false);
        var dispatcher = new PausableDispatcher();
        var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        int deliveries = 0;
        var workspace = new DocumentWorkspace<IPlugin>(NullLogger.Instance, dispatcher, (_, _) =>
        {
            deliveries++;
            return Task.CompletedTask;
        });
        var model = new MainViewModel(telemetry, m_fixture.Service, workspace, dispatcher: dispatcher);
        await using (model.ConfigureAwait(false))
        {
            await workspace.OpenAsync(() => new AvailabilityDocument()).ConfigureAwait(false);
            int initial = deliveries;
            dispatcher.Pause = true;
            Task binding = model.SynchronizeConnectionAsync();
            try
            {
                await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                m_fixture.Backend.Sessions[0].Transition(ConnectionPhase.Reconnecting);
            }
            finally
            {
                dispatcher.Pause = false;
                dispatcher.Release.TrySetResult();
            }
            await binding.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(deliveries, Is.EqualTo(initial + 1));
        }
    }

    private sealed class PausableDispatcher : IWorkspaceDispatcher
    {
        public bool Pause { get; set; }
        public TaskCompletionSource Entered { get; } = NewSignal();
        public TaskCompletionSource Release { get; } = NewSignal();
        public void VerifyAccess() { }
        public async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            if (Pause)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await action().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task MainViewModelRestoresAvailabilityAfterSameGenerationReconnect()
    {
        var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
        int lifecycleDeliveries = 0;
        var workspace = new DocumentWorkspace<IPlugin>(
            NullLogger.Instance, InlineWorkspaceDispatcher.Instance, (_, _) =>
            {
                lifecycleDeliveries++;
                return Task.CompletedTask;
            });
        var viewModel = new MainViewModel(
            telemetry, m_fixture.Service, workspace, dispatcher: InlineWorkspaceDispatcher.Instance);
        await using (viewModel.ConfigureAwait(false))
        {
            var document = new AvailabilityDocument();
            await workspace.OpenAsync(() => document).ConfigureAwait(false);
            await m_fixture.ConnectAcceptOnceAsync().ConfigureAwait(false);
            long generation = m_fixture.Service.Snapshot.Generation;
            int deliveries = lifecycleDeliveries;
            var disconnected = NewSignal();
            var reconnected = NewSignal();
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(MainViewModel.ConnectionStatus))
                {
                    return;
                }
                if (viewModel.ConnectionStatus.StartsWith("Reconnecting", StringComparison.Ordinal))
                {
                    disconnected.TrySetResult();
                }
                if (disconnected.Task.IsCompleted && viewModel.IsConnected)
                {
                    reconnected.TrySetResult();
                }
            };

            m_fixture.Backend.Sessions[0].Transition(ConnectionPhase.Reconnecting);
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(viewModel.IsConnected, Is.False);
            m_fixture.Backend.Sessions[0].Transition(ConnectionPhase.Connected);
            await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(viewModel.ConnectionStatus, Does.StartWith("Connected"));
            Assert.That(m_fixture.Service.Snapshot.Generation, Is.EqualTo(generation));
            Assert.That(viewModel.SelectedTab, Is.SameAs(document));
            Assert.That(lifecycleDeliveries, Is.EqualTo(deliveries));
        }
    }

    private sealed class AvailabilityDocument : ObservableObject, IPlugin
    {
        public PluginKind Kind => PluginKind.Historian;
        public string Title { get; set; } = "Retained document";
        public bool IsRenaming { get; set; }
        public Control? View => null;
        public Control? HeaderToolbar => null;
        public string Status => string.Empty;
        public bool SupportsDuplicate => false;
        public IReadOnlyList<MenuItem> ContributeMenuItems() => Array.Empty<MenuItem>();
        public void OnActivated() { }
        public void OnDeactivated() { }
        public Exception? DisposeFailure { get; set; }
        public ValueTask DisposeAsync() => DisposeFailure is { } error
            ? ValueTask.FromException(error) : ValueTask.CompletedTask;
    }

    [SetUp]
    public void SetUp()
    {
        m_fixture = new ConnectionFixture();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task MissingPromptRejectsAndRestoresExistingValidatorHook()
    {
        Func<Certificate, ServiceResult, bool> previous = (_, _) => true;
        m_fixture.Backend.PreviousCallback = previous;

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Identities, Is.Empty);
        Assert.That(m_fixture.Backend.Configurations[0].CertificateManager.AcceptError, Is.SameAs(previous));
        Assert.That(
            m_fixture.Backend.Configurations[0].SecurityConfiguration.AutoAcceptUntrustedCertificates,
            Is.False);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Failed));
        Assert.That(m_fixture.Workspace.Snapshot.Error, Is.Not.Null.And.Not.Empty);
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
        Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
    }

    [Test]
    public async Task ExplicitRejectionDoesNotRetryOrAcquireAnIdentity()
    {
        int prompts = 0;
        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
            {
                prompts++;
                return Task.FromResult(TrustChoice.Reject);
            }),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(prompts, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Identities, Is.Empty);
        Assert.That(m_fixture.Backend.CommitCount, Is.Zero);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Failed));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task LegacySecureAnonymousConnectRetriesWithoutReusingItsReleasedIdentity(bool useV2)
    {
        SubscriptionEngineKind engine = useV2
            ? SubscriptionEngineKind.ChannelV2
            : SubscriptionEngineKind.Classic;
        int released = 0;
        int prompts = 0;
        var suppliedIdentity = new Mock<IUserIdentity>();
        suppliedIdentity.SetupGet(value => value.TokenType).Returns(UserTokenType.Anonymous);
        suppliedIdentity.SetupGet(value => value.PolicyId).Returns(string.Empty);
        suppliedIdentity.SetupGet(value => value.DisplayName).Returns("Anonymous");
        suppliedIdentity.SetupGet(value => value.TokenHandler).Returns(() =>
        {
            ObjectDisposedException.ThrowIf(released != 0, suppliedIdentity);
            return new AnonymousIdentityTokenHandler();
        });
        suppliedIdentity.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(() =>
        {
            released++;
            return ValueTask.CompletedTask;
        });

        await m_fixture.Service.ConnectAsync(
            new ConnectionOptions
            {
                EndpointUrl = m_fixture.Endpoint.EndpointUrl!,
                UseSecurity = true,
                Engine = engine
            },
            m_fixture.Endpoint,
            suppliedIdentity.Object,
            (certificate, error) =>
            {
                Assert.That(m_fixture.Backend.InValidation, Is.False);
                Assert.That(certificate.Subject, Is.EqualTo(m_fixture.Certificate.Subject));
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
                Assert.That(released, Is.EqualTo(1));
                prompts++;
                return Task.FromResult(TrustChoice.AcceptOnce);
            },
            CancellationToken.None).ConfigureAwait(false);

        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
        Assert.That(m_fixture.Workspace.Snapshot.Profile!.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        Assert.That(
            m_fixture.Workspace.Snapshot.Profile.SecurityPolicyUri,
            Is.EqualTo(SecurityPolicies.Basic256Sha256));
        Assert.That(m_fixture.Workspace.Snapshot.Profile.IdentityType, Is.EqualTo(UserTokenType.Anonymous));
        Assert.That(m_fixture.Workspace.Snapshot.Profile.Engine, Is.EqualTo(engine));
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(2));
        Assert.That(prompts, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Identities, Has.Count.EqualTo(1));
        Assert.That(m_fixture.Backend.Identities[0], Is.Not.SameAs(suppliedIdentity.Object));
        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);
        Assert.That(released, Is.EqualTo(1));
    }

    [Test]
    public async Task AcceptOncePromptsOutsideValidationAndRetriesWithAnOwnedCertificate()
    {
        var entered = NewSignal();
        var answer = new TaskCompletionSource<TrustChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task connecting = m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: async (request, _) =>
        {
            Assert.That(m_fixture.Backend.InValidation, Is.False);
            using var certificate = new Certificate(request.CertificateData.Span);
            Assert.That(certificate.Subject, Is.EqualTo(m_fixture.Certificate.Subject));
            Assert.That(request.Profile, Is.EqualTo(m_fixture.Profile));
            entered.SetResult();
            return await answer.Task.ConfigureAwait(false);
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        Assert.That(connecting.IsCompleted, Is.False);
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Connecting));
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
        answer.SetResult(TrustChoice.AcceptOnce);
        await connecting.ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(2));
        Assert.That(m_fixture.Backend.CommitCount, Is.Zero);
        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(m_fixture.Profile));
        Assert.That(m_fixture.Workspace.Snapshot.Generation, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Identities, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AcceptOnceRejectsOtherCertificatesAndErrorsAndIsClearedOnDisconnect()
    {
        await m_fixture.ConnectAcceptOnceAsync().ConfigureAwait(false);
        Func<Certificate, ServiceResult, bool> callback =
            m_fixture.Backend.Configurations[0].CertificateManager.AcceptError!;
        using Certificate other = CreateCertificate("CN=Other server");
        Assert.That(
            callback(m_fixture.Certificate, new ServiceResult(StatusCodes.BadCertificateUntrusted)),
            Is.True);
        Assert.That(callback(other, new ServiceResult(StatusCodes.BadCertificateUntrusted)), Is.False);
        Assert.That(
            callback(m_fixture.Certificate, new ServiceResult(StatusCodes.BadCertificateTimeInvalid)),
            Is.False);
        var nested = new ServiceResult(
            null,
            StatusCodes.BadCertificateUntrusted,
            default,
            null,
            new ServiceResult(StatusCodes.BadCertificateRevoked));
        Assert.That(callback(m_fixture.Certificate, nested), Is.False);

        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);

        Assert.That(callback(m_fixture.Certificate, new ServiceResult(StatusCodes.BadCertificateUntrusted)), Is.False);
        Assert.That(m_fixture.Backend.Configurations[0].CertificateManager.AcceptError, Is.Null);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(m_fixture.Profile));
        Assert.That(m_fixture.Backend.Sessions[0].DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ReplacingTheTargetDoesNotReuseItsAcceptOnceDecision()
    {
        await m_fixture.ConnectAcceptOnceAsync().ConfigureAwait(false);
        EndpointDescription replacement = (EndpointDescription)m_fixture.Endpoint.Clone();
        replacement.EndpointUrl = "opc.tcp://localhost:4851/other";
        ConnectionProfile profile = ConnectionProfile.Create(
            replacement, replacement.UserIdentityTokens[0], SubscriptionEngineKind.Classic);
        m_fixture.Backend.Endpoints = [replacement];

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(profile),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(3));
        Assert.That(m_fixture.Backend.Sessions[0].DisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(profile));
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
    }

    [Test]
    public async Task PermanentTrustCommitsBeforeRetryAndDoesNotBecomeAcceptOnce()
    {
        var committing = NewSignal();
        var allowCommit = NewSignal();
        m_fixture.Backend.CommitAsync = async ct =>
        {
            committing.SetResult();
            await allowCommit.Task.WaitAsync(ct).ConfigureAwait(false);
        };
        Task connecting = m_fixture.Workspace.ConnectAsync(
            m_fixture.Profile,
            certificatePrompt: (_, _) => Task.FromResult(TrustChoice.TrustPermanently));
        await committing.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Persisted, Is.False);
        Assert.That(connecting.IsCompleted, Is.False);
        allowCommit.SetResult();
        await connecting.ConfigureAwait(false);

        Assert.That(m_fixture.Backend.CommitCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Persisted, Is.True);
        Assert.That(m_fixture.Backend.TransactionDisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(2));
        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
        Assert.That(m_fixture.Backend.Configurations[0].CertificateManager.AcceptError!(
            m_fixture.Certificate, new ServiceResult(StatusCodes.BadCertificateUntrusted)), Is.False);
    }

    [Test]
    public async Task FailedPersistenceFailsTheConnectionWithoutRetryOrTemporaryAcceptance()
    {
        m_fixture.Backend.CommitAsync = _ => Task.FromException(new IOException("Trust store is read-only."));

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(
                m_fixture.Profile,
                certificatePrompt: (_, _) => Task.FromResult(TrustChoice.TrustPermanently)),
            Throws.InstanceOf<IOException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Persisted, Is.False);
        Assert.That(m_fixture.Backend.TransactionDisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Configurations[0].CertificateManager.AcceptError, Is.Null);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Failed));
        Assert.That(m_fixture.Workspace.Snapshot.Error, Does.Contain("read-only"));
    }

    private static StatusCode[] InvalidCertificateStatusCodes =>
    [
        StatusCodes.BadCertificateInvalid,
        StatusCodes.BadCertificateTimeInvalid,
        StatusCodes.BadCertificateIssuerTimeInvalid,
        StatusCodes.BadCertificateRevoked,
        StatusCodes.BadCertificateIssuerRevoked,
        StatusCodes.BadCertificateHostNameInvalid,
        StatusCodes.BadCertificateUriInvalid,
        StatusCodes.BadCertificateChainIncomplete,
        StatusCodes.BadCertificateRevocationUnknown
    ];

    [TestCaseSource(nameof(InvalidCertificateStatusCodes))]
    public async Task InvalidCertificateErrorsCannotBeOverridden(StatusCode statusCode)
    {
        m_fixture.Backend.ValidationError = new ServiceResult(statusCode);
        int prompts = 0;
        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
            {
                prompts++;
                return Task.FromResult(TrustChoice.AcceptOnce);
            }),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(prompts, Is.Zero);
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Persisted, Is.False);
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
    }

    [Test]
    public async Task DiscoveryCertificateMismatchDoesNotPromptOrAccept()
    {
        using Certificate replacement = CreateCertificate("CN=Changed since discovery");
        m_fixture.Backend.PresentedCertificate = replacement;
        int prompts = 0;
        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
            {
                prompts++;
                return Task.FromResult(TrustChoice.AcceptOnce);
            }),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(prompts, Is.Zero);
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AChangedCertificateOnRetryFailsInsteadOfPromptingIndefinitely()
    {
        using Certificate replacement = CreateCertificate("CN=Changed during prompt");
        int prompts = 0;
        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
            {
                prompts++;
                m_fixture.Backend.PresentedCertificate = replacement;
                return Task.FromResult(TrustChoice.AcceptOnce);
            }),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(prompts, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(2));
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Failed));
    }

    [TestCase((int)TrustChoice.AcceptOnce)]
    [TestCase((int)TrustChoice.TrustPermanently)]
    public async Task CancellingAPromptIgnoresLateAcceptance(int lateAnswer)
    {
        var entered = NewSignal();
        var answer = new TaskCompletionSource<TrustChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task connecting = m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
        {
            entered.SetResult();
            return answer.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await m_fixture.Workspace.CancelAsync().ConfigureAwait(false);
        await Assert.ThatAsync(() => connecting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        answer.SetResult((TrustChoice)lateAnswer);

        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.CommitCount, Is.Zero);
        Assert.That(m_fixture.Backend.Configurations[0].CertificateManager.AcceptError, Is.Null);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
        Assert.That(m_fixture.Workspace.Snapshot.Error, Is.Null);
    }

    [Test]
    public async Task CancellingPersistenceRollsBackAndDoesNotRetry()
    {
        var entered = NewSignal();
        m_fixture.Backend.CommitAsync = async ct =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        };
        Task connecting = m_fixture.Workspace.ConnectAsync(
            m_fixture.Profile,
            certificatePrompt: (_, _) => Task.FromResult(TrustChoice.TrustPermanently));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await m_fixture.Workspace.CancelAsync().ConfigureAwait(false);
        await Assert.ThatAsync(() => connecting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.Persisted, Is.False);
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.TransactionDisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ASessionReturningAfterCancellationIsDisposedAndNeverPublished()
    {
        m_fixture.Backend.RequireTrust = false;
        var entered = NewSignal();
        var returned = new TaskCompletionSource<IConnectionSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        m_fixture.Backend.OpenAsync = (_, _, _) =>
        {
            entered.SetResult();
            return returned.Task;
        };
        Task connecting = m_fixture.Workspace.ConnectAsync(m_fixture.Profile);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await m_fixture.Workspace.CancelAsync().ConfigureAwait(false);
        var abandoned = new FakeConnectionSession(new UserIdentity());
        returned.SetResult(abandoned);
        await Assert.ThatAsync(() => connecting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(abandoned.DisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
        Assert.That(m_fixture.Workspace.Snapshot.Generation, Is.Zero);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
    }

    [Test]
    public async Task ASecondConnectIsRejectedWithoutDisturbingTheInFlightAttempt()
    {
        var entered = NewSignal();
        var answer = new TaskCompletionSource<TrustChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
        {
            entered.SetResult();
            return answer.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile),
            Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Connecting));

        answer.SetResult(TrustChoice.AcceptOnce);
        await first.ConfigureAwait(false);
        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DisconnectCancelsAnInFlightAttemptAndWaitsForCleanup()
    {
        var entered = NewSignal();
        var answer = new TaskCompletionSource<TrustChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task connecting = m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
        {
            entered.SetResult();
            return answer.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);
        await Assert.ThatAsync(() => connecting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        answer.SetResult(TrustChoice.Reject);

        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
        Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
    }

    [Test]
    public async Task DisposeCancelsPendingTrustAndIsIdempotent()
    {
        var entered = NewSignal();
        var answer = new TaskCompletionSource<TrustChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task connecting = m_fixture.Workspace.ConnectAsync(m_fixture.Profile, certificatePrompt: (_, _) =>
        {
            entered.SetResult();
            return answer.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Task firstDispose = m_fixture.Workspace.DisposeAsync().AsTask();
        Task secondDispose = m_fixture.Workspace.DisposeAsync().AsTask();
        await Task.WhenAll(firstDispose, secondDispose).ConfigureAwait(false);
        await Assert.ThatAsync(() => connecting, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        answer.SetResult(TrustChoice.Reject);

        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile),
            Throws.InstanceOf<ObjectDisposedException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task ManagedReconnectKeepsTheSessionButChangesAvailability()
    {
        m_fixture.Backend.RequireTrust = false;
        int referenceChanges = 0;
        m_fixture.Workspace.ConnectionChangedAsync += _ =>
        {
            referenceChanges++;
            return Task.CompletedTask;
        };
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        FakeConnectionSession session = m_fixture.Backend.Sessions[0];
        ISession? original = m_fixture.Workspace.CurrentSession;
        long generation = m_fixture.Workspace.Snapshot.Generation;

        session.Transition(ConnectionPhase.Reconnecting, "Transport unavailable");
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Reconnecting));
        Assert.That(m_fixture.Workspace.CurrentSession, Is.SameAs(original));
        Assert.That(m_fixture.Workspace.Snapshot.Generation, Is.EqualTo(generation));
        Assert.That(m_fixture.Workspace.Snapshot.Error, Does.Contain("unavailable"));
        session.Transition(ConnectionPhase.Connected);
        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
        Assert.That(m_fixture.Workspace.Snapshot.Error, Is.Null);
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(referenceChanges, Is.EqualTo(1));
    }

    [Test]
    public async Task DisconnectWaitsForDocumentOwnershipToBeReleasedBeforeDisposingTheSession()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        FakeConnectionSession session = m_fixture.Backend.Sessions[0];
        var entered = NewSignal();
        var released = NewSignal();
        m_fixture.Workspace.ConnectionChangedAsync += async ct =>
        {
            ConnectionSnapshot snapshot = m_fixture.Workspace.Snapshot;
            Assert.That(snapshot.Profile, Is.EqualTo(m_fixture.Profile));
            Assert.That(snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
            Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
            Assert.That(ct.IsCancellationRequested, Is.False);
            entered.SetResult();
            await released.Task.ConfigureAwait(false);
        };

        Task disconnecting = m_fixture.Workspace.DisconnectAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(session.DisposeCount, Is.Zero);
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.Zero);
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
        Assert.That(disconnecting.IsCompleted, Is.False);
        session.Transition(ConnectionPhase.Connected);
        Assert.That(m_fixture.Workspace.IsConnected, Is.False, "Late keep-alive must not revive a closing session.");
        released.SetResult();
        await disconnecting.ConfigureAwait(false);

        Assert.That(session.DisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
    }

    [Test]
    public async Task EngineReplacementAwaitsTheOldGenerationBeforeOpeningTheNewSession()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        var entered = NewSignal();
        var released = NewSignal();
        Func<CancellationToken, Task> changed = async _ =>
        {
            if (m_fixture.Workspace.CurrentSession is not null)
            {
                return;
            }
            ConnectionSnapshot snapshot = m_fixture.Workspace.Snapshot;
            Assert.That(snapshot.Generation, Is.EqualTo(1));
            Assert.That(snapshot.Profile!.Engine, Is.EqualTo(SubscriptionEngineKind.ChannelV2));
            entered.SetResult();
            await released.Task.ConfigureAwait(false);
        };
        m_fixture.Workspace.ConnectionChangedAsync += changed;

        Task replacing = m_fixture.Workspace.ReconnectAsync(SubscriptionEngineKind.Classic);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(m_fixture.Backend.ConnectCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Sessions[0].DisposeCount, Is.Zero);
        released.SetResult();
        await replacing.ConfigureAwait(false);
        m_fixture.Workspace.ConnectionChangedAsync -= changed;

        Assert.That(m_fixture.Backend.Sessions[0].DisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.Snapshot.Generation, Is.EqualTo(2));
        Assert.That(m_fixture.Workspace.Snapshot.Profile!.Engine, Is.EqualTo(SubscriptionEngineKind.Classic));
    }

    [Test]
    public async Task LifecycleObserverFailuresDoNotSkipOtherObserversOrConnectionCleanup()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        int remainingObserverCalls = 0;
        m_fixture.Workspace.ConnectionChangedAsync += _ =>
            Task.FromException(new InvalidOperationException("Document detach failed."));
        m_fixture.Workspace.ConnectionChangedAsync += _ =>
        {
            remainingObserverCalls++;
            return Task.CompletedTask;
        };

        await Assert.ThatAsync(
            () => m_fixture.Workspace.DisconnectAsync(),
            Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);

        Assert.That(remainingObserverCalls, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Sessions[0].DisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.Snapshot.Error, Does.Contain("Document detach failed."));
        Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
    }

    [Test]
    public async Task ConnectDoesNotReturnUntilConnectionObserversFinishAttachingDocuments()
    {
        m_fixture.Backend.RequireTrust = false;
        var entered = NewSignal();
        var attached = NewSignal();
        Func<CancellationToken, Task> changed = async ct =>
        {
            Assert.That(m_fixture.Workspace.CurrentSession, Is.Not.Null);
            Assert.That(m_fixture.Workspace.IsConnected, Is.True);
            entered.SetResult();
            await attached.Task.WaitAsync(ct).ConfigureAwait(false);
        };
        m_fixture.Workspace.ConnectionChangedAsync += changed;

        Task connecting = m_fixture.Workspace.ConnectAsync(m_fixture.Profile);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(connecting.IsCompleted, Is.False);
        attached.SetResult();
        await connecting.ConfigureAwait(false);
        m_fixture.Workspace.ConnectionChangedAsync -= changed;

        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
        Assert.That(m_fixture.Workspace.Snapshot.Generation, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangedObserverSeesNullSessionBeforeTheOldSessionIsDisposed()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        FakeConnectionSession session = m_fixture.Backend.Sessions[0];
        var entered = NewSignal();
        var detached = NewSignal();
        m_fixture.Workspace.ConnectionChangedAsync += async ct =>
        {
            Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
            Assert.That(ct.IsCancellationRequested, Is.False);
            entered.SetResult();
            await detached.Task.ConfigureAwait(false);
        };

        Task disconnecting = m_fixture.Workspace.DisconnectAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(session.DisposeCount, Is.Zero);
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.Zero);
        Assert.That(disconnecting.IsCompleted, Is.False);
        detached.SetResult();
        await disconnecting.ConfigureAwait(false);

        Assert.That(session.DisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedDocumentAttachmentClosesTheNewSessionAndNotifiesDetachment()
    {
        m_fixture.Backend.RequireTrust = false;
        int notifications = 0;
        m_fixture.Workspace.ConnectionChangedAsync += _ =>
        {
            notifications++;
            return m_fixture.Workspace.CurrentSession is null
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException("Document attach failed."));
        };

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile),
            Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);

        Assert.That(notifications, Is.EqualTo(2));
        Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Failed));
        Assert.That(m_fixture.Workspace.Snapshot.Error, Does.Contain("Document attach failed."));
        Assert.That(m_fixture.Backend.Sessions[0].DisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AwaitedDocumentDeliveryCanResolveToolConfigurationWithoutReenteringTheTransition()
    {
        m_fixture.Backend.RequireTrust = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ApplicationConfiguration? first = null;
        int deliveries = 0;
        m_fixture.Workspace.ConnectionChangedAsync += async _ =>
        {
            ApplicationConfiguration configuration = await m_fixture.Service
                .GetConfigAsync(deadline.Token)
                .ConfigureAwait(false);
            if (first is null)
            {
                first = configuration;
            }
            else
            {
                Assert.That(configuration, Is.SameAs(first));
            }
            deliveries++;
        };

        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile, ct: deadline.Token).ConfigureAwait(false);
        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);

        Assert.That(deliveries, Is.EqualTo(2));
        Assert.That(m_fixture.Backend.Configurations, Has.Count.EqualTo(2));
        Assert.That(first!.CertificateManager.AcceptError, Is.Null);
    }

    [Test]
    public async Task ShutdownObserversCanFinishUsingToolConfigurationBeforeItIsDisposed()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int deliveries = 0;
        m_fixture.Workspace.ConnectionChangedAsync += async _ =>
        {
            ApplicationConfiguration configuration = await m_fixture.Service
                .GetConfigAsync(deadline.Token)
                .ConfigureAwait(false);
            Assert.That(configuration.CertificateManager.AcceptError, Is.Null);
            Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.Zero);
            deliveries++;
        };

        await m_fixture.Workspace.DisposeAsync().ConfigureAwait(false);

        Assert.That(deliveries, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(2));
        await Assert.ThatAsync(
            () => m_fixture.Service.GetConfigAsync(),
            Throws.InstanceOf<ObjectDisposedException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task LateStateFromAReplacedSessionCannotChangeTheNewConnection()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        FakeConnectionSession first = m_fixture.Backend.Sessions[0];
        Action<IConnectionSession, ConnectionSessionState> late = first.CaptureHandlers();
        await m_fixture.Workspace.ReconnectAsync(SubscriptionEngineKind.Classic).ConfigureAwait(false);
        ConnectionSnapshot replacement = m_fixture.Workspace.Snapshot;

        late(first, new ConnectionSessionState(ConnectionPhase.Failed, "Late old-session failure"));

        Assert.That(m_fixture.Workspace.Snapshot, Is.EqualTo(replacement));
        Assert.That(m_fixture.Workspace.Snapshot.Generation, Is.EqualTo(2));
        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
        Assert.That(first.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EngineReplacementPreservesCredentialsAndCreatesFreshIdentities()
    {
        m_fixture.Backend.RequireTrust = false;
        ConnectionProfile profile = m_fixture.UseUserNameProfile();
        var provider = new TrackingIdentityProvider(profile);
        await m_fixture.Workspace.ConnectAsync(profile, provider).ConfigureAwait(false);
        await m_fixture.Workspace.ReconnectAsync(SubscriptionEngineKind.Classic).ConfigureAwait(false);

        Assert.That(provider.Identities, Has.Count.EqualTo(2));
        Assert.That(provider.Identities[0].Disposed, Is.True);
        Assert.That(provider.Identities[1].Disposed, Is.False);
        Assert.That(provider.Identities[1], Is.Not.SameAs(provider.Identities[0]));
        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(profile with
        {
            Engine = SubscriptionEngineKind.Classic
        }));
        Assert.That(m_fixture.Backend.Identities[1].TokenType, Is.EqualTo(UserTokenType.UserName));
    }

    [Test]
    public async Task ReusingADisposedIdentityRequiresReacquisitionRatherThanAnonymousFallback()
    {
        m_fixture.Backend.RequireTrust = false;
        ConnectionProfile profile = m_fixture.UseUserNameProfile();
        var provider = new TrackingIdentityProvider(profile) { ReuseIdentity = true };
        await m_fixture.Workspace.ConnectAsync(profile, provider).ConfigureAwait(false);

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ReconnectAsync(SubscriptionEngineKind.Classic),
            Throws.InstanceOf<CredentialsRequiredException>()).ConfigureAwait(false);

        Assert.That(provider.Identities[0].Disposed, Is.True);
        Assert.That(m_fixture.Workspace.Snapshot.Profile!.IdentityType, Is.EqualTo(UserTokenType.UserName));
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
        Assert.That(m_fixture.Backend.Sessions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SupplyingTheSameProviderAfterDisconnectCannotReuseAnOldIdentity()
    {
        m_fixture.Backend.RequireTrust = false;
        ConnectionProfile profile = m_fixture.UseUserNameProfile();
        var provider = new TrackingIdentityProvider(profile) { ReuseIdentity = true };
        await m_fixture.Workspace.ConnectAsync(profile, provider).ConfigureAwait(false);
        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(profile, provider),
            Throws.InstanceOf<CredentialsRequiredException>()).ConfigureAwait(false);

        Assert.That(provider.Identities[0].Disposed, Is.True);
        Assert.That(m_fixture.Backend.Sessions, Has.Count.EqualTo(1));
        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(profile));
    }

    [Test]
    public async Task ExplicitDisconnectRequiresCredentialsWithoutLosingTheSecurityProfile()
    {
        m_fixture.Backend.RequireTrust = false;
        ConnectionProfile profile = m_fixture.UseUserNameProfile();
        await m_fixture.Workspace.ConnectAsync(profile, new TrackingIdentityProvider(profile)).ConfigureAwait(false);
        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);

        await Assert.ThatAsync(
            () => m_fixture.Service.ConnectAsync(new ConnectionOptions
            {
                EndpointUrl = profile.EndpointUrl,
                UseSecurity = false,
                Engine = SubscriptionEngineKind.Classic
            }, CancellationToken.None),
            Throws.InstanceOf<CredentialsRequiredException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Workspace.Snapshot.Profile!.SecurityMode, Is.EqualTo(profile.SecurityMode));
        Assert.That(m_fixture.Workspace.Snapshot.Profile.IdentityType, Is.EqualTo(UserTokenType.UserName));
        Assert.That(m_fixture.Backend.Sessions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LegacyEngineHelperCannotDowngradeAnExistingSecureProfile()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);
        await m_fixture.Service.ConnectAsync(new ConnectionOptions
        {
            EndpointUrl = m_fixture.Profile.EndpointUrl,
            UseSecurity = false,
            Engine = SubscriptionEngineKind.Classic
        }, CancellationToken.None).ConfigureAwait(false);

        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(m_fixture.Profile with
        {
            Engine = SubscriptionEngineKind.Classic
        }));
        Assert.That(m_fixture.Backend.Sessions, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AProfileRestoreRejectsEndpointsThatWouldDowngradeSecurityOrIdentity()
    {
        m_fixture.Backend.RequireTrust = false;
        ConnectionProfile profile = m_fixture.UseUserNameProfile();
        var weaker = new EndpointDescription(profile.EndpointUrl)
        {
            SecurityMode = MessageSecurityMode.None,
            SecurityPolicyUri = SecurityPolicies.None,
            TransportProfileUri = profile.TransportProfileUri,
            UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
        };
        m_fixture.Backend.Endpoints = [weaker];

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(profile, new TrackingIdentityProvider(profile)),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.Zero);
        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(profile));
    }

    [TestCase("mode")]
    [TestCase("channelPolicy")]
    [TestCase("transport")]
    [TestCase("applicationUri")]
    [TestCase("tokenPolicyId")]
    [TestCase("tokenPolicySecurity")]
    public async Task RestoringAProfileRequiresEveryPinnedEndpointField(string changedField)
    {
        m_fixture.Backend.RequireTrust = false;
        ConnectionProfile profile = m_fixture.UseUserNameProfile();
        EndpointDescription endpoint = m_fixture.Endpoint;
        switch (changedField)
        {
            case "mode":
                endpoint.SecurityMode = MessageSecurityMode.Sign;
                break;
            case "channelPolicy":
                endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                break;
            case "transport":
                endpoint.TransportProfileUri = "urn:ualens:test:other-transport";
                break;
            case "applicationUri":
                endpoint.Server.ApplicationUri += ":other";
                break;
            case "tokenPolicyId":
                endpoint.UserIdentityTokens[0].PolicyId += "-other";
                break;
            case "tokenPolicySecurity":
                endpoint.UserIdentityTokens[0].SecurityPolicyUri = SecurityPolicies.None;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(changedField));
        }

        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(profile, new TrackingIdentityProvider(profile)),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.Zero);
        Assert.That(m_fixture.Workspace.Snapshot.Profile, Is.EqualTo(profile));
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
    }

    [Test]
    public async Task SessionCleanupFailureIsSurfacedAfterOtherResourcesAreReleased()
    {
        m_fixture.Backend.RequireTrust = false;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);
        m_fixture.Backend.Sessions[0].DisposeFailure = new IOException("Session cleanup failed.");

        await Assert.ThatAsync(
            () => m_fixture.Workspace.DisconnectAsync(),
            Throws.InstanceOf<AggregateException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Workspace.CurrentSession, Is.Null);
        Assert.That(m_fixture.Workspace.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Failed));
        Assert.That(m_fixture.Workspace.Snapshot.Error, Does.Contain("Session cleanup failed"));
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Backend.Configurations[0].CertificateManager.AcceptError, Is.Null);
    }

    [Test]
    public async Task LocalToolsNeverShareThePrimaryAcceptOnceValidator()
    {
        ApplicationConfiguration tools = await m_fixture.Service.GetConfigAsync().ConfigureAwait(false);
        await m_fixture.ConnectAcceptOnceAsync().ConfigureAwait(false);

        Assert.That(m_fixture.Backend.Configurations, Has.Count.EqualTo(2));
        Assert.That(tools.CertificateManager, Is.Not.SameAs(m_fixture.Backend.Configurations[1].CertificateManager));
        Assert.That(tools.CertificateManager.AcceptError, Is.Null);
        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
    }

    [Test]
    public async Task TrustDoesNotOverwriteAValidatorHookInstalledByAnotherOwner()
    {
        await m_fixture.ConnectAcceptOnceAsync().ConfigureAwait(false);
        Func<Certificate, ServiceResult, bool> replacement = (_, _) => false;
        m_fixture.Backend.Configurations[0].CertificateManager.AcceptError = replacement;
        await m_fixture.Workspace.DisconnectAsync().ConfigureAwait(false);

        Assert.That(m_fixture.Backend.Configurations[0].CertificateManager.AcceptError, Is.SameAs(replacement));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task UnsafeInjectedConfigurationIsRejectedBeforeConnecting(bool autoAccept, bool cacheAccepted)
    {
        m_fixture.Backend.AutoAccept = autoAccept;
        m_fixture.Backend.CacheAcceptedCertificates = cacheAccepted;
        await Assert.ThatAsync(
            () => m_fixture.Workspace.ConnectAsync(m_fixture.Profile),
            Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);

        Assert.That(m_fixture.Backend.ConnectCount, Is.Zero);
        Assert.That(m_fixture.Backend.ConfigurationDisposeCount, Is.EqualTo(1));
        Assert.That(m_fixture.Workspace.IsConnected, Is.False);
    }

    [Test]
    public async Task SubscriberFailureCannotHideStateFromOtherSubscribers()
    {
        m_fixture.Backend.RequireTrust = false;
        int observed = 0;
        m_fixture.Workspace.StateChanged += () => throw new InvalidOperationException("Subscriber failed.");
        m_fixture.Workspace.StateChanged += () => observed++;
        await m_fixture.Workspace.ConnectAsync(m_fixture.Profile).ConfigureAwait(false);

        Assert.That(observed, Is.GreaterThanOrEqualTo(2));
        Assert.That(m_fixture.Workspace.IsConnected, Is.True);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Certificate CreateCertificate(string subject)
    {
        return CertificateBuilder.Create(subject).SetRSAKeySize(2048).CreateForRSA();
    }

    private sealed class ConnectionFixture : IAsyncDisposable
    {
        public ConnectionFixture()
        {
            Certificate = CreateCertificate("CN=UaLens connection test");
            Endpoint = new EndpointDescription("opc.tcp://localhost:4850/primary")
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.UaTcpTransport,
                ServerCertificate = ByteString.From(Certificate.RawData),
                UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
            };
            Profile = ConnectionProfile.Create(
                Endpoint, Endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2);
            Backend = new FakeBackend(Telemetry, Certificate) { Endpoints = [Endpoint] };
            Service = new ConnectionService(Telemetry, null, Backend, new ProfileCredentialProvider());
        }

        public TestTelemetry Telemetry { get; } = new();

        public Certificate Certificate { get; }

        public EndpointDescription Endpoint { get; }

        public ConnectionProfile Profile { get; }

        public FakeBackend Backend { get; }

        public ConnectionService Service { get; }

        public ConnectionService Workspace => Service;

        public Task ConnectAcceptOnceAsync()
        {
            return Workspace.ConnectAsync(
                Profile, certificatePrompt: (_, _) => Task.FromResult(TrustChoice.AcceptOnce));
        }

        public ConnectionProfile UseUserNameProfile()
        {
            var policy = new UserTokenPolicy(UserTokenType.UserName)
            {
                PolicyId = "username",
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            Endpoint.UserIdentityTokens = [policy];
            return ConnectionProfile.Create(Endpoint, policy, SubscriptionEngineKind.ChannelV2, "engineer");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Service.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Certificate.Dispose();
                Telemetry.Dispose();
            }
        }
    }

    private sealed class FakeBackend : IConnectionBackend
    {
        public FakeBackend(ITelemetryContext telemetry, Certificate certificate)
        {
            m_telemetry = telemetry;
            m_certificate = certificate;
        }

        public ArrayOf<EndpointDescription> Endpoints { get; set; }

        public List<ApplicationConfiguration> Configurations { get; } = [];

        public List<FakeConnectionSession> Sessions { get; } = [];

        public List<IUserIdentity> Identities { get; } = [];

        public Func<Certificate, ServiceResult, bool>? PreviousCallback { get; set; }

        public ServiceResult ValidationError { get; set; } = new(StatusCodes.BadCertificateUntrusted);

        public Certificate? PresentedCertificate { get; set; }

        public Func<CancellationToken, Task>? CommitAsync { get; set; }

        public Func<ApplicationConfiguration, IUserIdentity, CancellationToken, Task<IConnectionSession>>?
            OpenAsync { get; set; }

        public bool RequireTrust { get; set; } = true;

        public bool AutoAccept { get; set; }

        public bool CacheAcceptedCertificates { get; set; }

        public bool InValidation { get; private set; }

        public bool Persisted { get; private set; }

        public int ConnectCount { get; private set; }

        public int CommitCount { get; private set; }

        public int TransactionDisposeCount { get; private set; }

        public int ConfigurationDisposeCount { get; private set; }

        public Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var manager = new Mock<ICertificateManager>();
            manager.SetupProperty(value => value.AcceptError, PreviousCallback);
            manager.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(() =>
            {
                ConfigurationDisposeCount++;
                return ValueTask.CompletedTask;
            });
            manager.Setup(value => value.BeginUpdateAsync(TrustListIdentifier.Peers, It.IsAny<CancellationToken>()))
                .Returns((TrustListIdentifier _, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var transaction = new Mock<ITrustListTransaction>();
                    transaction.Setup(value => value.AddTrustedCertificateAsync(
                        It.IsAny<Certificate>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                    transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
                        .Returns(async (CancellationToken commitToken) =>
                        {
                            CommitCount++;
                            if (CommitAsync is not null)
                            {
                                await CommitAsync(commitToken).ConfigureAwait(false);
                            }
                            commitToken.ThrowIfCancellationRequested();
                            Persisted = true;
                        });
                    transaction.Setup(value => value.DisposeAsync()).Returns(() =>
                    {
                        TransactionDisposeCount++;
                        return ValueTask.CompletedTask;
                    });
                    return Task.FromResult(transaction.Object);
                });
            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                CertificateManager = manager.Object,
                SecurityConfiguration = new SecurityConfiguration
                {
                    AutoAcceptUntrustedCertificates = AutoAccept,
                    UseValidatedCertificates = CacheAcceptedCertificates
                }
            };
            Configurations.Add(configuration);
            return Task.FromResult(configuration);
        }

        public Task<ArrayOf<EndpointDescription>> DiscoverAsync(
            ApplicationConfiguration configuration,
            string endpointUrl,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Endpoints);
        }

        public async Task<IConnectionSession> ConnectAsync(
            ApplicationConfiguration configuration,
            EndpointDescription endpoint,
            ConnectionProfile profile,
            IClientIdentityProvider identityProvider,
            CancellationToken ct)
        {
            ConnectCount++;
            if (RequireTrust && !Persisted)
            {
                bool accepted;
                using (var presented = new Certificate((PresentedCertificate ?? m_certificate).RawData))
                {
                    InValidation = true;
                    try
                    {
                        accepted = configuration.CertificateManager.AcceptError?.Invoke(presented, ValidationError) ??
                            false;
                    }
                    finally
                    {
                        InValidation = false;
                    }
                }
                if (!accepted)
                {
                    throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
                }
            }
            IUserIdentity identity = await identityProvider.AcquireIdentityAsync(
                endpoint, configuration.CreateMessageContext(), ct).ConfigureAwait(false);
            Identities.Add(identity);
            if (OpenAsync is not null)
            {
                return await OpenAsync(configuration, identity, ct).ConfigureAwait(false);
            }
            var session = new FakeConnectionSession(identity);
            Sessions.Add(session);
            return session;
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly Certificate m_certificate;
    }

    private sealed class FakeConnectionSession : IConnectionSession
    {
        public FakeConnectionSession(IUserIdentity identity)
        {
            m_identity = identity;
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Connected).Returns(true);
            session.SetupGet(value => value.Identity).Returns(identity);
            Session = session.Object;
        }

        public ISession Session { get; }

        public ConnectionSessionState State { get; private set; } = new(ConnectionPhase.Connected);

        public int DisposeCount { get; private set; }

        public Exception? DisposeFailure { get; set; }

        public event Action<IConnectionSession, ConnectionSessionState>? StateChanged;

        public void Transition(ConnectionPhase phase, string? error = null)
        {
            State = new ConnectionSessionState(phase, error);
            StateChanged?.Invoke(this, State);
        }

        public Action<IConnectionSession, ConnectionSessionState> CaptureHandlers()
        {
            return StateChanged!;
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            await ConnectionCredentials.ReleaseIdentityAsync(m_identity).ConfigureAwait(false);
            if (DisposeFailure is not null)
            {
                throw DisposeFailure;
            }
        }

        private readonly IUserIdentity m_identity;
    }

    private sealed class TrackingIdentityProvider : IClientIdentityProvider
    {
        public TrackingIdentityProvider(ConnectionProfile profile)
        {
            m_profile = profile;
        }

        public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; } = [UserTokenType.UserName];

        public IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; } = [];

        public DateTime ExpiresAt => DateTime.MaxValue;

        public List<TrackingIdentity> Identities { get; } = [];

        public bool ReuseIdentity { get; init; }

        public ValueTask<CanSatisfyResult> CanSatisfyAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            return ValueTask.FromResult(CanSatisfyResult.Yes);
        }

        public ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (ReuseIdentity && Identities.Count > 0)
            {
                return ValueTask.FromResult<IUserIdentity>(Identities[0]);
            }
            var identity = new TrackingIdentity(m_profile.IdentityName!);
            Identities.Add(identity);
            return ValueTask.FromResult<IUserIdentity>(identity);
        }

        private readonly ConnectionProfile m_profile;
    }

    private sealed class TrackingIdentity : IUserIdentity, IAsyncDisposable
    {
        public TrackingIdentity(string name)
        {
            m_inner = new UserIdentity(name, Guid.NewGuid().ToByteArray());
        }

        public bool Disposed { get; private set; }

        public string DisplayName => m_inner.DisplayName;

        public string PolicyId => m_inner.PolicyId;

        public UserTokenType TokenType => m_inner.TokenType;

        public System.Xml.XmlQualifiedName IssuedTokenType => m_inner.IssuedTokenType;

        public bool SupportsSignatures => m_inner.SupportsSignatures;

        public ArrayOf<NodeId> GrantedRoleIds => m_inner.GrantedRoleIds;

        public IUserIdentityTokenHandler TokenHandler
        {
            get
            {
                ObjectDisposedException.ThrowIf(Disposed, this);
                return m_inner.TokenHandler;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        private readonly UserIdentity m_inner;
    }

    private sealed class TestTelemetry : ITelemetryContext, IDisposable
    {
        public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

        public ActivitySource ActivitySource { get; } = new(nameof(ConnectionServiceTests));

        public Meter CreateMeter()
        {
            return new Meter(nameof(ConnectionServiceTests));
        }

        public void Dispose()
        {
            ActivitySource.Dispose();
        }
    }

    private ConnectionFixture m_fixture = null!;
}
