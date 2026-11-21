/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace OnboardingDevice
{
    /// <summary>
    /// Initiates pull onboarding after recovery and listener activation. Candidate metadata selects the
    /// connection; existing peer trust and the actual channel prove its identity.
    /// </summary>
    internal sealed class DevicePullTask : IServerStartupTask
    {
        /// <summary>
        /// Borrows the same configuration, security state and session ownership used by the hosted device.
        /// </summary>
        public DevicePullTask(
            DeviceSampleOptions options,
            IProvisionableDeviceSecurity security,
            IProvisionableDeviceRegistrarStore registrars,
            IProvisionableDeviceRuntime runtime,
            IOpcUaApplicationConfigurationProvider configuration,
            PullDcaWorkflow workflow,
            DevicePullSession sessions,
            DevicePullJournal? journal = null,
            DevicePullCredentials? credentials = null,
            DiscoveryConnectOptions? discovery = null,
            DevicePullSigningKeyStore? signingKeys = null,
            Func<ManagedSession, CancellationToken, ValueTask>? reconnectInstalled = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_security = security ?? throw new ArgumentNullException(nameof(security));
            m_registrars = registrars ?? throw new ArgumentNullException(nameof(registrars));
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_journal = journal;
            m_credentials = credentials;
            m_discovery = discovery;
            m_signingKeys = signingKeys;
            m_reconnectInstalled = reconnectInstalled ?? ReloadAndReconnectInstalledAsync;
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            DevicePullInstalledManagerOperation? managerOperation = m_journal == null
                ? null
                : new DevicePullInstalledManagerOperation(m_security.CreateApplicationOperations(), m_journal);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            CancellationToken ct = timeout.Token;
            ProvisionableDeviceSecurityStage stage = await m_security.GetStageAsync(ct).ConfigureAwait(false);
            if (m_signingKeys != null)
            {
                await m_signingKeys.ReconcileStagedAsync(ct).ConfigureAwait(false);
            }
            DevicePullJournalCheckpoint? checkpoint = m_journal == null
                ? null
                : await m_journal.ReadAsync(ct).ConfigureAwait(false);
            if (stage == ProvisionableDeviceSecurityStage.Installed)
            {
                if (checkpoint != null)
                {
                    DevicePullInstalledManagerOperation operation = managerOperation ??
                        throw new ServiceResultException(StatusCodes.BadConfigurationError);
                    await CompleteInstalledAsync(checkpoint, ct).ConfigureAwait(false);
                    await ContinueInstalledAsync(checkpoint, operation, ct).ConfigureAwait(false);
                }
                Console.WriteLine("ONBOARDING_DEVICE_PULL_ALREADY_INSTALLED");
                return;
            }
            if (stage != ProvisionableDeviceSecurityStage.Unconfigured)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Complete or reset the pending device transition before outbound pull.");
            }
            if (checkpoint != null)
            {
                DevicePullInstalledManagerOperation operation = managerOperation ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                await ResumeAsync(checkpoint, operation, ct).ConfigureAwait(false);
                return;
            }
            using IDisposable? dispatch = m_journal == null
                ? null
                : await m_journal.ReserveDispatchAsync(ct).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                await RunFreshAsync(managerOperation, ct).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                if (m_signingKeys != null)
                {
                    try
                    {
                        await m_signingKeys.RetireUndispatchedAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupError) when (failure != null)
                    {
                        throw new AggregateException(
                            "The pull attempt and staged-key cleanup both failed.", failure, cleanupError);
                    }
                }
            }
        }

        /// <summary>
        /// Attempts each configured registrar under the reserved dispatch generation.
        /// </summary>
        private async ValueTask RunFreshAsync(
            DevicePullInstalledManagerOperation? managerOperation,
            CancellationToken ct)
        {
            Uri endpoint = m_options.PullRegistrarEndpoint ??
                throw new InvalidOperationException("The pull registrar endpoint is not configured.");
            string expectedApplicationUri = m_options.PullRegistrarApplicationUri ??
                throw new InvalidOperationException("The pull registrar application URI is not configured.");
            ArrayOf<ApplicationDescription> candidates = await m_registrars.ReadAsync(ct).ConfigureAwait(false);
            PullDcaRegistrarSelection.RequireConfiguredEndpoint(candidates, expectedApplicationUri, endpoint);

            var failures = new List<Exception>();
            foreach ((string applicationUri, Uri candidateEndpoint) in GetCandidates(
                candidates, expectedApplicationUri, endpoint))
            {
                ct.ThrowIfCancellationRequested();
                if (failures.Count != 0 && (m_journal == null || m_discovery == null))
                {
                    break;
                }
                CandidateOutcome outcome = await TryCandidateAsync(
                    applicationUri, candidateEndpoint, failures.Count != 0, managerOperation, ct)
                    .ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (outcome.Failure == null)
                {
                    PullDcaResult result = outcome.Result ??
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                    Console.WriteLine($"ONBOARDING_DEVICE_PULL_INSTALLED {result.Identity.ApplicationId}");
                    return;
                }
                failures.Add(outcome.Failure);
                Console.Error.WriteLine(
                    $"ONBOARDING_DEVICE_PULL_CANDIDATE_FAILED {candidateEndpoint} {applicationUri}: " +
                    outcome.Failure);
            }
            ct.ThrowIfCancellationRequested();
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            throw new AggregateException("No configured pull registrar succeeded.", failures);
        }

        /// <summary>
        /// Visits the operator's endpoint first, then each distinct stored URI and endpoint association.
        /// </summary>
        private static IEnumerable<(string ApplicationUri, Uri Endpoint)> GetCandidates(
            ArrayOf<ApplicationDescription> candidates,
            string primaryApplicationUri,
            Uri primaryEndpoint)
        {
            yield return (primaryApplicationUri, primaryEndpoint);
            for (int i = 0; i < candidates.Count; i++)
            {
                ApplicationDescription candidate = candidates[i] ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                if (!Uri.TryCreate(candidate.ApplicationUri, UriKind.Absolute, out _) ||
                    candidate.ApplicationType is not (ApplicationType.Server or ApplicationType.ClientAndServer) ||
                    candidate.DiscoveryUrls.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A configured pull registrar is invalid.");
                }
                for (int j = 0; j < candidate.DiscoveryUrls.Count; j++)
                {
                    if (!Uri.TryCreate(candidate.DiscoveryUrls[j], UriKind.Absolute, out Uri? endpoint) ||
                        endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.Query.Length != 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError, "A configured pull registrar endpoint is invalid.");
                    }
                    if (endpoint.Equals(primaryEndpoint) &&
                        string.Equals(candidate.ApplicationUri, primaryApplicationUri, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    PullDcaRegistrarSelection.RequireConfiguredEndpoint(candidates, candidate.ApplicationUri, endpoint);
                    yield return (candidate.ApplicationUri, endpoint);
                }
            }
        }

        /// <summary>
        /// Reserves the attempt before changing discovery options, then retires its one returned session.
        /// </summary>
        private async ValueTask<CandidateOutcome> TryCandidateAsync(
            string applicationUri,
            Uri endpoint,
            bool isFallback,
            DevicePullInstalledManagerOperation? managerOperation,
            CancellationToken ct)
        {
            bool connecting = false;
            DevicePullSession.Attempt attempt;
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectionTimeout.CancelAfter(s_connectionTimeout);
            try
            {
                attempt = await m_sessions.BeginAsync(async token =>
                {
                    if (isFallback)
                    {
                        await RequireSafeSwitchAsync(applicationUri, endpoint, token).ConfigureAwait(false);
                    }
                    SelectEndpoint(endpoint);
                    token.ThrowIfCancellationRequested();
                    connecting = true;
                }, connectionTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (
                connecting && !ct.IsCancellationRequested && connectionTimeout.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "The registrar connection timed out before its failure could be classified.", error);
            }
            catch (ServiceResultException error) when (
                connecting && !ct.IsCancellationRequested && !connectionTimeout.IsCancellationRequested &&
                IsConnectionFailure(error.StatusCode))
            {
                return new CandidateOutcome(null, error);
            }
            catch (SocketException error) when (
                connecting && !ct.IsCancellationRequested && !connectionTimeout.IsCancellationRequested)
            {
                return new CandidateOutcome(null, error);
            }
            bool timedOut = connectionTimeout.IsCancellationRequested && !ct.IsCancellationRequested;
            connectionTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            if (timedOut)
            {
                return await UseAttemptAsync<CandidateOutcome>(
                    attempt,
                    () => throw new TimeoutException(
                        "The registrar connection timed out before its failure could be classified."))
                    .ConfigureAwait(false);
            }
            return await UseAttemptAsync(
                attempt,
                () => RunConnectedCandidateAsync(applicationUri, endpoint, attempt.Session, managerOperation, ct),
                static outcome => outcome.Failure).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks current durable evidence and candidate membership before selecting another endpoint.
        /// </summary>
        private async ValueTask RequireSafeSwitchAsync(string applicationUri, Uri endpoint, CancellationToken ct)
        {
            DevicePullJournal journal = m_journal ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            if (await journal.ReadAsync(ct).ConfigureAwait(false) != null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A retained signing attempt forbids switching registrars.");
            }
            if (await m_security.GetStageAsync(ct).ConfigureAwait(false) !=
                ProvisionableDeviceSecurityStage.Unconfigured)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A device security transition forbids switching registrars.");
            }
            ArrayOf<ApplicationDescription> current = await m_registrars.ReadAsync(ct).ConfigureAwait(false);
            PullDcaRegistrarSelection.RequireConfiguredEndpoint(current, applicationUri, endpoint);
            if (await journal.ReadAsync(ct).ConfigureAwait(false) != null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "A retained signing attempt forbids switching registrars.");
            }
        }

        /// <summary>
        /// Selects the discovery URL only while the session owner has reserved the attempt.
        /// </summary>
        private void SelectEndpoint(Uri endpoint)
        {
            if (m_discovery == null)
            {
                if (!endpoint.Equals(m_options.PullRegistrarEndpoint))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "The selected discovery endpoint cannot be changed.");
                }
                return;
            }
            m_discovery.DiscoveryUrl = endpoint.AbsoluteUri;
        }

        /// <summary>
        /// Authenticates the channel before providing tickets and retries only candidate-scoped admission failures.
        /// </summary>
        private async ValueTask<CandidateOutcome> RunConnectedCandidateAsync(
            string applicationUri,
            Uri endpoint,
            ManagedSession session,
            DevicePullInstalledManagerOperation? managerOperation,
            CancellationToken ct)
        {
            ServiceResultException? admission = await CheckRegistrarIdentityAsync(
                session, endpoint, applicationUri, null, ct).ConfigureAwait(false);
            if (admission != null)
            {
                return new CandidateOutcome(null, admission);
            }
            ArrayOf<ApplicationDescription> candidates = await m_registrars.ReadAsync(ct).ConfigureAwait(false);
            var registrar = PullDcaRegistrarSelection.SelectConfigured(
                candidates, applicationUri, endpoint,
                ExpandedNodeId.ToNodeId(Opc.Ua.Onboarding.ObjectIds.DeviceRegistrar, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, session.NamespaceUris));
            var profile = new PullDcaCertificateProfile(
                ExpandedNodeId.ToNodeId(
                    Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup, session.NamespaceUris),
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            ApplicationConfiguration configuration = await m_configuration.GetAsync(ct).ConfigureAwait(false);
            ICertificateManager certificates = configuration.CertificateManager ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            using CertificateEntry identity = certificates.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            var issuers = new ByteString[identity.IssuerChain.Count];
            for (int i = 0; i < issuers.Length; i++)
            {
                issuers[i] = ByteString.From(identity.IssuerChain[i].RawData.AsSpan());
            }
            ArrayOf<string> tickets = await m_runtime.RequestTicketsAsync(ct).ConfigureAwait(false);
            var request = new PullDcaRequest(
                new PullDcaIdentityOffer([ByteString.From(identity.Certificate.RawData.AsSpan())], issuers, tickets),
                profile, []);
            var progress = new CandidateProgress();
            try
            {
                PullDcaResult result = managerOperation == null
                    ? await m_workflow.RunAsync(registrar, request, progress, ct).ConfigureAwait(false)
                    : await m_workflow.RunAsync(registrar, request, progress, managerOperation, ct)
                        .ConfigureAwait(false);
                return new CandidateOutcome(result, null);
            }
            catch (ServiceResultException error) when (
                progress.Stage == PullDcaStage.ProvidingIdentities && !ct.IsCancellationRequested &&
                IsAdmissionFailure(error.StatusCode))
            {
                return new CandidateOutcome(null, error);
            }
        }

        /// <summary>
        /// Preserves an operation failure when disposing its attempt also fails.
        /// </summary>
        private static async ValueTask<T> UseAttemptAsync<T>(
            DevicePullSession.Attempt attempt,
            Func<ValueTask<T>> action,
            Func<T, Exception?>? deferredFailure = null)
        {
            Exception? operationFailure = null;
            try
            {
                await using var attemptLifetime = attempt.ConfigureAwait(false);
                try
                {
                    T result = await action().ConfigureAwait(false);
                    operationFailure = deferredFailure?.Invoke(result);
                    return result;
                }
                catch (Exception error)
                {
                    operationFailure = error;
                    throw;
                }
            }
            catch (Exception disposalError) when (
                operationFailure != null && !ReferenceEquals(operationFailure, disposalError))
            {
                throw new AggregateException(
                    "The pull attempt and its session disposal both failed.", operationFailure, disposalError);
            }
        }

        /// <summary>
        /// Reconciles an installed security checkpoint without starting or installing the request again.
        /// </summary>
        private async ValueTask CompleteInstalledAsync(DevicePullJournalCheckpoint checkpoint, CancellationToken ct)
        {
            if (checkpoint.Stage is not (DevicePullJournalStage.Received or DevicePullJournalStage.Installed) ||
                checkpoint.Credentials == null ||
                m_security is not IProvisionableDevicePullInstallationRecovery recovery)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The installed security cannot be reconciled with the pull journal.");
            }
            using CertificateCollection registrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                checkpoint.Binding.RegistrarChannelCertificate.Memory);
            if (registrar.Count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            await recovery.CompleteInterruptedPullAsync(
                checkpoint.Binding.AttemptId,
                ByteString.From(registrar[0].RawData.AsSpan()),
                new ProvisionableDeviceCredentials(
                    checkpoint.Credentials.Certificate,
                    checkpoint.Credentials.IssuerCertificates,
                    checkpoint.Credentials.TrustList),
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reopens one owned session to continue an installation completed before startup.
        /// </summary>
        private async ValueTask ContinueInstalledAsync(
            DevicePullJournalCheckpoint checkpoint,
            DevicePullInstalledManagerOperation managerOperation,
            CancellationToken ct)
        {
            DevicePullJournal journal = m_journal ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            DevicePullCredentials credentials = m_credentials ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            PullDcaCredentials installed = checkpoint.Credentials ??
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            PullDcaSigningBinding retained = checkpoint.Binding;
            DevicePullSession.Attempt attempt = await m_sessions.BeginAsync(async token =>
            {
                DevicePullJournalCheckpoint? current = await journal.ReadAsync(token).ConfigureAwait(false);
                if (current?.Stage != DevicePullJournalStage.Installed || current.Credentials == null ||
                    !SameRetainedChannel(retained, current.Binding) ||
                    !installed.Certificate.Span.SequenceEqual(current.Credentials.Certificate.Span) ||
                    await m_security.GetStageAsync(token).ConfigureAwait(false) !=
                        ProvisionableDeviceSecurityStage.Installed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The installed pull attempt changed before reconnecting.");
                }
                ArrayOf<ApplicationDescription> candidates = await m_registrars.ReadAsync(token)
                    .ConfigureAwait(false);
                PullDcaRegistrarSelection.RequireConfiguredEndpoint(
                    candidates, retained.RegistrarApplicationUri, retained.Registrar.EndpointUrl);
                SelectEndpoint(retained.Registrar.EndpointUrl);
                await credentials.ActivateAsync(installed.Certificate, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            await UseAttemptAsync(attempt, async () =>
            {
                await ContinueInstalledOnSessionAsync(
                    attempt.Session, retained, installed.Certificate, managerOperation, ct)
                    .ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Restores the original channel and request before continuing an unfinished certificate request.
        /// </summary>
        private async ValueTask ResumeAsync(
            DevicePullJournalCheckpoint checkpoint,
            DevicePullInstalledManagerOperation managerOperation,
            CancellationToken ct)
        {
            if (checkpoint.Stage is DevicePullJournalStage.DispatchUnknown or DevicePullJournalStage.Installed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The retained pull attempt requires recovery or an explicit reset.");
            }
            DevicePullJournal journal = m_journal ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            DevicePullCredentials credentials = m_credentials ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            _ = m_options.PullRegistrarEndpoint ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            _ = m_options.PullRegistrarApplicationUri ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            PullDcaSigningBinding retained = checkpoint.Binding;
            Uri endpoint = retained.Registrar.EndpointUrl;
            string expectedApplicationUri = retained.RegistrarApplicationUri;
            ArrayOf<ApplicationDescription> candidates = await m_registrars.ReadAsync(ct).ConfigureAwait(false);
            PullDcaRegistrarSelection.RequireConfiguredEndpoint(candidates, expectedApplicationUri, endpoint);
            DevicePullSession.Attempt attempt = await m_sessions.BeginAsync(async token =>
            {
                DevicePullJournalCheckpoint? current = await journal.ReadAsync(token).ConfigureAwait(false);
                if (current == null || !SameRetainedChannel(retained, current.Binding) ||
                    current.Stage is DevicePullJournalStage.DispatchUnknown or DevicePullJournalStage.Installed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The retained pull attempt changed before reconnecting.");
                }
                if (await m_security.GetStageAsync(token).ConfigureAwait(false) !=
                    ProvisionableDeviceSecurityStage.Unconfigured)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A device security transition forbids pull recovery.");
                }
                ArrayOf<ApplicationDescription> currentCandidates = await m_registrars.ReadAsync(token)
                    .ConfigureAwait(false);
                PullDcaRegistrarSelection.RequireConfiguredEndpoint(
                    currentCandidates, expectedApplicationUri, endpoint);
                current = await journal.ReadAsync(token).ConfigureAwait(false);
                if (current == null || !SameRetainedChannel(retained, current.Binding) ||
                    current.Stage is DevicePullJournalStage.DispatchUnknown or DevicePullJournalStage.Installed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The retained pull attempt changed before reconnecting.");
                }
                SelectEndpoint(endpoint);
                await credentials.ActivateAsync(retained.ClientChannelCertificate, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            NodeId applicationId = await UseAttemptAsync(attempt, async () =>
            {
                ManagedSession session = attempt.Session;
                ServiceResultException? admission = await CheckRegistrarIdentityAsync(
                    session, endpoint, expectedApplicationUri, retained, ct).ConfigureAwait(false);
                if (admission != null)
                {
                    ExceptionDispatchInfo.Capture(admission).Throw();
                }
                ArrayOf<ApplicationDescription> currentCandidates = await m_registrars.ReadAsync(ct)
                    .ConfigureAwait(false);
                PullDcaRegistrarSelection.RequireConfiguredEndpoint(
                    currentCandidates, expectedApplicationUri, endpoint);
                DevicePullJournalCheckpoint resolved = await journal.ReadForSessionAsync(
                    session.NamespaceUris.ToArrayOf(), ct).ConfigureAwait(false);
                if (!SameRetainedChannel(retained, resolved.Binding) ||
                    resolved.Stage is DevicePullJournalStage.DispatchUnknown or DevicePullJournalStage.Installed)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The pull attempt changed while the session was connecting.");
                }
                await credentials.RestoreSigningRequestAsync(resolved.Binding, ct).ConfigureAwait(false);
                PullDcaCredentials installed = await m_workflow.CompleteCertificateRequestAsync(
                    resolved.Binding, cancellationToken: ct).ConfigureAwait(false);
                await m_reconnectInstalled(session, ct).ConfigureAwait(false);
                await ContinueInstalledOnSessionAsync(
                    session, retained, installed.Certificate, managerOperation, ct)
                    .ConfigureAwait(false);
                return resolved.Binding.ApplicationId;
            }).ConfigureAwait(false);
            Console.WriteLine($"ONBOARDING_DEVICE_PULL_INSTALLED {applicationId}");
        }

        /// <summary>
        /// Retrieves managers only after the installed certificate is active on the selected registrar channel.
        /// </summary>
        private async ValueTask ContinueInstalledOnSessionAsync(
            ManagedSession session,
            PullDcaSigningBinding retained,
            ByteString installedCertificate,
            DevicePullInstalledManagerOperation managerOperation,
            CancellationToken ct)
        {
            DevicePullJournal journal = m_journal ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            DevicePullJournalCheckpoint current = await journal.ReadForSessionAsync(
                session.NamespaceUris.ToArrayOf(), ct).ConfigureAwait(false);
            if (installedCertificate.IsEmpty || current.Stage != DevicePullJournalStage.Installed ||
                current.Credentials == null || !SameRetainedChannel(retained, current.Binding) ||
                !installedCertificate.Span.SequenceEqual(current.Credentials.Certificate.Span) ||
                await m_security.GetStageAsync(ct).ConfigureAwait(false) !=
                    ProvisionableDeviceSecurityStage.Installed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The installed pull attempt changed before retrieving managers.");
            }
            ArrayOf<ApplicationDescription> candidates = await m_registrars.ReadAsync(ct).ConfigureAwait(false);
            PullDcaRegistrarSelection.RequireConfiguredEndpoint(
                candidates, retained.RegistrarApplicationUri, retained.Registrar.EndpointUrl);
            ServiceResultException? admission = await CheckRegistrarIdentityAsync(
                session, retained.Registrar.EndpointUrl, retained.RegistrarApplicationUri,
                retained, ct, installedCertificate).ConfigureAwait(false);
            if (admission != null)
            {
                ExceptionDispatchInfo.Capture(admission).Throw();
            }
            await managerOperation.ExecuteAsync(
                current.Binding,
                installedCertificate,
                token => m_workflow.ResumeInstalledAsync(
                    current.Binding, installedCertificate, cancellationToken: token),
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reloads the installed application certificate before reconnecting the owned session.
        /// </summary>
        private static async ValueTask ReloadAndReconnectInstalledAsync(ManagedSession session, CancellationToken ct)
        {
            await session.ReloadInstanceCertificateAsync(ct).ConfigureAwait(false);
            await session.ReconnectAsync(null, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Requires the retained endpoint and both original channel certificates on recovery.
        /// </summary>
        private static bool SameRetainedChannel(PullDcaSigningBinding expected, PullDcaSigningBinding current)
        {
            return expected.AttemptId == current.AttemptId &&
                expected.Registrar.EndpointUrl.Equals(current.Registrar.EndpointUrl) &&
                string.Equals(expected.RegistrarApplicationUri, current.RegistrarApplicationUri,
                    StringComparison.Ordinal) &&
                expected.ClientChannelCertificate.Span.SequenceEqual(current.ClientChannelCertificate.Span) &&
                expected.RegistrarChannelCertificate.Span.SequenceEqual(current.RegistrarChannelCertificate.Span);
        }

        /// <summary>
        /// Authenticates the actual confidential channel independently of stored discovery metadata.
        /// </summary>
        private static async ValueTask<ServiceResultException?> CheckRegistrarIdentityAsync(
            ManagedSession session,
            Uri endpoint,
            string expectedApplicationUri,
            PullDcaSigningBinding? retained,
            CancellationToken ct,
            ByteString installedIdentity = default)
        {
            SessionRequestContext context;
            try
            {
                context = await SessionRequestContext.CaptureConfidentialAsync(session, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException error) when (
                !ct.IsCancellationRequested && IsChannelAdmissionFailure(error.StatusCode))
            {
                return error;
            }
            ServiceResultException? rejection = null;
            Exception? operationFailure = null;
            try
            {
                await using var lifetime = context.ConfigureAwait(false);
                try
                {
                    ITransportChannel channel = context.Session.TransportChannel;
                    using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(
                        channel.ServerChannelCertificate.AsMemory());
                    if (chain.Count == 0 ||
                        !string.Equals(channel.EndpointDescription.Server?.ApplicationUri,
                            expectedApplicationUri, StringComparison.Ordinal) ||
                        !X509Utils.GetApplicationUrisFromCertificate(chain[0]).Contains(expectedApplicationUri))
                    {
                        rejection = new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
                    }
                    else if (!Uri.TryCreate(
                        channel.EndpointDescription.EndpointUrl, UriKind.Absolute, out Uri? channelEndpoint) ||
                        !endpoint.Equals(channelEndpoint))
                    {
                        rejection = new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed, "The channel endpoint differs from the candidate.");
                    }
                    else if (retained != null &&
                        !MatchesRetainedChannel(channel, chain[0], retained, installedIdentity))
                    {
                        rejection = new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed, "The channel differs from the retained pull attempt.");
                    }
                    else if (context.Invalidated.IsCompleted)
                    {
                        rejection = new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                    }
                }
                catch (Exception error)
                {
                    if (rejection != null)
                    {
                        operationFailure = new AggregateException(
                            "Registrar admission and certificate disposal both failed.", rejection, error);
                        throw operationFailure;
                    }
                    operationFailure = error;
                    throw;
                }
            }
            catch (Exception disposalError) when (
                (operationFailure ?? rejection) != null &&
                !ReferenceEquals(operationFailure ?? rejection, disposalError))
            {
                throw new AggregateException(
                    "Registrar admission and channel-context disposal both failed.",
                    (operationFailure ?? rejection)!, disposalError);
            }
            ct.ThrowIfCancellationRequested();
            return rejection;
        }

        /// <summary>
        /// Compares the original channel, or the installed identity and original registrar after reconnection.
        /// </summary>
        private static bool MatchesRetainedChannel(
            ITransportChannel channel,
            Certificate registrar,
            PullDcaSigningBinding retained,
            ByteString installedIdentity)
        {
            if (installedIdentity.IsEmpty)
            {
                return retained.ClientChannelCertificate.Span.SequenceEqual(channel.ClientChannelCertificate) &&
                    retained.RegistrarChannelCertificate.Span.SequenceEqual(channel.ServerChannelCertificate);
            }
            using CertificateCollection originalRegistrar = DefaultCertificateFactory.Instance.ParseChainBlob(
                retained.RegistrarChannelCertificate.Memory);
            using CertificateCollection actualClient = DefaultCertificateFactory.Instance.ParseChainBlob(
                channel.ClientChannelCertificate.AsMemory());
            return originalRegistrar.Count > 0 && actualClient.Count > 0 &&
                originalRegistrar[0].RawData.AsSpan().SequenceEqual(registrar.RawData) &&
                installedIdentity.Span.SequenceEqual(actualClient[0].RawData);
        }

        /// <summary>
        /// Identifies connection and channel failures without treating arbitrary local exceptions as retryable.
        /// </summary>
        private static bool IsConnectionFailure(StatusCode status)
        {
            return IsTransportFailure(status) ||
                status == StatusCodes.BadCertificateUntrusted ||
                status == StatusCodes.BadCertificateHostNameInvalid;
        }

        /// <summary>
        /// Limits transport retries to errors attributable to the selected connection.
        /// </summary>
        private static bool IsTransportFailure(StatusCode status)
        {
            return status == StatusCodes.BadTimeout ||
                status == StatusCodes.BadCommunicationError ||
                status == StatusCodes.BadConnectionClosed ||
                status == StatusCodes.BadConnectionRejected ||
                status == StatusCodes.BadSecureChannelClosed ||
                status == StatusCodes.BadSecureChannelIdInvalid ||
                status == StatusCodes.BadNoCommunication ||
                status == StatusCodes.BadNotConnected ||
                status == StatusCodes.BadServerNotConnected ||
                status == StatusCodes.BadTcpServerTooBusy;
        }

        /// <summary>
        /// Recognizes failures while capturing the actual confidential channel.
        /// </summary>
        private static bool IsChannelAdmissionFailure(StatusCode status)
        {
            return IsTransportFailure(status) ||
                status == StatusCodes.BadSecurityModeInsufficient ||
                status == StatusCodes.BadSecurityChecksFailed ||
                status == StatusCodes.BadCertificateUriInvalid;
        }

        /// <summary>
        /// Recognizes registrar-specific identity admission responses before any identity mutation or signing.
        /// </summary>
        private static bool IsAdmissionFailure(StatusCode status)
        {
            return IsConnectionFailure(status) || IsChannelAdmissionFailure(status) ||
                status == StatusCodes.BadUserAccessDenied ||
                status == StatusCodes.BadIdentityTokenInvalid ||
                status == StatusCodes.BadIdentityTokenRejected ||
                status == StatusCodes.BadTicketInvalid ||
                status == StatusCodes.BadRequestNotAllowed ||
                status == StatusCodes.BadNotSupported ||
                status == StatusCodes.BadNodeIdUnknown ||
                status == StatusCodes.BadMethodInvalid;
        }

        /// <summary>
        /// Captures the synchronous workflow phase without scheduling progress callbacks.
        /// </summary>
        private sealed class CandidateProgress : IProgress<PullDcaProgress>
        {
            /// <summary>
            /// Gets the last phase entered by the workflow.
            /// </summary>
            public PullDcaStage Stage { get; private set; }

            /// <inheritdoc/>
            public void Report(PullDcaProgress value)
            {
                ArgumentNullException.ThrowIfNull(value);
                Stage = value.Stage;
            }
        }

        /// <summary>
        /// Holds either a completed workflow result or a candidate-scoped pre-dispatch failure.
        /// </summary>
        private sealed record CandidateOutcome(PullDcaResult? Result, Exception? Failure);

        /// <summary>The explicit operator-selected connection inputs.</summary>
        private readonly DeviceSampleOptions m_options;

        /// <summary>The shared device transition owner.</summary>
        private readonly IProvisionableDeviceSecurity m_security;

        /// <summary>The persisted candidate metadata, which does not grant peer trust.</summary>
        private readonly IProvisionableDeviceRegistrarStore m_registrars;

        /// <summary>The verified signed ticket source.</summary>
        private readonly IProvisionableDeviceRuntime m_runtime;

        /// <summary>The host-owned shared application configuration.</summary>
        private readonly IOpcUaApplicationConfigurationProvider m_configuration;

        /// <summary>The typed workflow over generated method clients.</summary>
        private readonly PullDcaWorkflow m_workflow;

        /// <summary>The active attempt's session owner.</summary>
        private readonly DevicePullSession m_sessions;

        /// <summary>The durable local signing evidence, when configured.</summary>
        private readonly DevicePullJournal? m_journal;

        /// <summary>The sample's channel identity and signing-key owner, when configured.</summary>
        private readonly DevicePullCredentials? m_credentials;

        /// <summary>The one-shot factory's mutable discovery options, changed only under attempt ownership.</summary>
        private readonly DiscoveryConnectOptions? m_discovery;

        /// <summary>The optional dedicated signing-key store for restart recovery.</summary>
        private readonly DevicePullSigningKeyStore? m_signingKeys;

        /// <summary>Reloads the installed identity and reconnects the attempt's session.</summary>
        private readonly Func<ManagedSession, CancellationToken, ValueTask> m_reconnectInstalled;

        /// <summary>The maximum time allowed to connect to one candidate.</summary>
        private static readonly TimeSpan s_connectionTimeout = TimeSpan.FromSeconds(20);
    }
}
