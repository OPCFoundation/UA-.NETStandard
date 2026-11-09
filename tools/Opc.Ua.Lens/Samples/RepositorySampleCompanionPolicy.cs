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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Samples
{
    /// <summary>
    /// Authorizes only explicit, finite operations on live owned domain simulations.
    /// Other deployment tools retain their configured, default-deny rule policy.
    /// </summary>
    internal sealed class RepositorySampleCompanionPolicy :
        ICompanionDeploymentPolicy, IWeighingSimulationPolicy, IRoboticsSimulationPolicy
    {
        /// <summary>
        /// Creates a policy without launching or trusting any sample.
        /// </summary>
        public RepositorySampleCompanionPolicy(
            IRepositorySampleService? samples,
            ArrayOf<CompanionDeploymentRule> rules = default,
            TimeProvider? timeProvider = null)
        {
            m_samples = samples;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_configured = new ConfiguredCompanionDeploymentPolicy(rules, m_timeProvider);
        }

        /// <inheritdoc/>
        public ValueTask<CompanionDeploymentGrant> AuthorizeAsync(
            CompanionContext context, CompanionOperationDraft draft, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(draft);
            bool machineryJob = draft.Target.ProviderId == "isa95" &&
                draft.TaskInput is Isa95PreparedTask { Binding.MachineryOwner.IsNull: false };
            if (machineryJob &&
                draft.TaskInput is Isa95PreparedTask job &&
                (!job.Version2 || job.Target != draft.Target || job.OperationId != draft.Operation.Id))
            {
                throw new UnauthorizedAccessException("The Machinery job preparation does not match this task.");
            }
            if (draft.Target.ProviderId == "machinery" &&
                (draft.TaskInput is not MachineryZeroPointTask adjustment || !adjustment.MatchesAuthorization(draft)))
            {
                throw new UnauthorizedAccessException("Machinery requires an exact prepared zero-point adjustment.");
            }
            if (!machineryJob &&
                draft.Target.ProviderId is not (
                    "weighing" or "amb" or "packml" or "machinery" or "di" or "robotics" or "robot-intent"))
            {
                return m_configured.AuthorizeAsync(context, draft, cancellationToken);
            }
            if (draft.Operation.Safety != CompanionOperationSafety.DeploymentMutation ||
                !draft.Matches(context.Session, m_timeProvider.GetUtcNow()))
            {
                throw new UnauthorizedAccessException("The prepared simulation request is no longer current.");
            }
            OwnedSimulationGrant grant = AuthorizeOwnedRun(
                context, draft.Target, draft.Operation.Id, cancellationToken);
            DateTimeOffset expires = grant.ExpiresAt < draft.ExpiresAt ? grant.ExpiresAt : draft.ExpiresAt;
            string prefix = "owned-" + draft.Target.ProviderId + "-";
            return new ValueTask<CompanionDeploymentGrant>(new CompanionDeploymentGrant(
                prefix + grant.RunId.ToString("N"), grant.Revision, expires));
        }

        /// <inheritdoc/>
        public ValueTask<CompanionDeploymentGrant> AuthorizeRoboticsAsync(
            CompanionContext context, CompanionOperationDraft draft, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(draft);
            if (draft.Target.ProviderId is not ("robotics" or "robot-intent"))
            {
                throw new UnauthorizedAccessException("A Robotics grant requires a Robotics simulation target.");
            }
            return AuthorizeAsync(context, draft, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WeighingSimulationGrant> AuthorizeAsync(
            CompanionContext context, CompanionTarget target, string operationId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.ProviderId != "weighing")
            {
                throw new UnauthorizedAccessException("A weighing grant requires a weighing target.");
            }
            OwnedSimulationGrant grant = AuthorizeOwnedRun(context, target, operationId, cancellationToken);
            return new ValueTask<WeighingSimulationGrant>(
                new WeighingSimulationGrant(grant.RunId, grant.Revision, grant.ExpiresAt));
        }

        private OwnedSimulationGrant AuthorizeOwnedRun(
            CompanionContext context, CompanionTarget target, string operationId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(target);
            cancellationToken.ThrowIfCancellationRequested();
            RepositorySampleSnapshot snapshot = m_samples?.Snapshot ??
                throw new UnauthorizedAccessException("No owned domain simulation is configured.");
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            RepositorySampleId requiredSample = target.ProviderId switch
            {
                "weighing" => RepositorySampleId.ScalesServer,
                "amb" => RepositorySampleId.AmbServer,
                "packml" => RepositorySampleId.ScalesServer,
                "isa95" => RepositorySampleId.MachineryServer,
                "machinery" => RepositorySampleId.MachineryServer,
                "di" => RepositorySampleId.PumpSoftwareUpdateSimulator,
                "robotics" => RepositorySampleId.MinimalRobotServer,
                "robot-intent" => RepositorySampleId.IntentEnabledRobot,
                _ => throw new UnauthorizedAccessException("This provider has no owned-simulation policy.")
            };
            if (
                target.NodeId.IsNull ||
                !IsSupportedOperation(target.ProviderId, operationId) ||
                snapshot.Selection != requiredSample ||
                snapshot.Phase != RepositorySamplePhase.AdvertisedReady ||
                !snapshot.OwnsResources ||
                !snapshot.CompanionOperationsAuthorized ||
                snapshot.RunId == Guid.Empty ||
                snapshot.CompanionOperationsExpireAt is not { } expires ||
                now >= expires ||
                snapshot.Evidence is not { } evidence)
            {
                throw new UnauthorizedAccessException(
                    "Start and explicitly opt the matching owned simulator into operations before preparing a task.");
            }
            EndpointDescription endpoint = context.Session.Endpoint;
            if (!context.Session.Connected ||
                (target.ProviderId is "robotics" or "robot-intent" &&
                    context.Session.Identity.TokenType is not (UserTokenType.UserName or UserTokenType.Certificate)) ||
                endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                endpoint.SecurityPolicyUri is not (SecurityPolicies.Basic256Sha256 or
                    SecurityPolicies.Aes128_Sha256_RsaOaep or SecurityPolicies.Aes256_Sha256_RsaPss) ||
                !string.Equals(endpoint.EndpointUrl, evidence.Endpoint.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(endpoint.Server?.ApplicationUri, evidence.ApplicationUri, StringComparison.Ordinal) ||
                endpoint.ServerCertificate.IsNull ||
                endpoint.ServerCertificate.Length is 0 or > 65536 ||
                evidence.CertificateSha256.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(endpoint.ServerCertificate.Span), evidence.CertificateSha256.Span))
            {
                throw new UnauthorizedAccessException(
                    "The primary connection does not match the verified, signed and encrypted owned simulation.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            RepositorySampleSnapshot current = m_samples!.Snapshot;
            if (current.RunId != snapshot.RunId ||
                current.Selection != requiredSample ||
                current.Phase != RepositorySamplePhase.AdvertisedReady ||
                !current.OwnsResources ||
                !current.CompanionOperationsAuthorized ||
                current.CompanionOperationsExpireAt != expires ||
                current.Evidence != evidence ||
                m_timeProvider.GetUtcNow() >= expires)
            {
                throw new UnauthorizedAccessException("The owned simulation stopped or its authorization changed.");
            }
            return new OwnedSimulationGrant(snapshot.RunId, "owned-" + target.ProviderId + "-v1", expires);
        }

        private static bool IsSupportedOperation(string providerId, string operationId)
        {
            if (providerId == "robotics")
            {
                return operationId is "get-ready" or "start-system" or "stop-system" or "stand-down" or
                    "load-program" or "start-program" or "stop-program" or "unload-program";
            }
            if (providerId == "robot-intent")
            {
                return operationId is "submit-wait" or "submit-wait-mission" or "cancel-intent" or
                    "cancel-mission" or "request-control" or "release-control" or "open-channel" or "close-channel";
            }
            if (providerId == "di")
            {
                return operationId is "prepare-sample" or "upload-package-sample" or "install-package-sample" or
                    "abort-prepare-sample" or "resume-prepare-sample" or "resume-installation-sample" or
                    "confirm-sample";
            }
            if (providerId == "amb")
            {
                return operationId is "set-asset-id" or "add-link" or "write-link" or "remove-link" or
                    "acknowledge-health" or "acknowledge-maintenance";
            }
            if (providerId == "packml")
            {
                return operationId is "abort" or "clear" or "stop" or "reset-machine" or "reset-execute" or
                    "start" or "hold" or "unhold" or "suspend" or "unsuspend" or "complete";
            }
            if (providerId == "isa95")
            {
                return operationId is "store-job" or "update-job" or "start-job" or "cancel-job" or
                    "clear-job" or "pause-job" or "resume-job" or "abort-job";
            }
            if (providerId == "machinery")
            {
                return operationId == "zero-point-adjustment";
            }
            return operationId is
                "set-zero" or "set-tare" or "clear-tare" or "preset-tare" or "register-weight" or
                "select-product" or "deselect-product" or "switch-product" or "remove-product" or "add-product" or
                "add-recipe" or "remove-recipe" or
                "start-recipe" or "stop-recipe" or "continue-recipe" or "abort-recipe" or "skip-recipe-element" or
                "reference-piece-weight" or "reference-piece-count" or "start-reference" or
                "inbound-weighing" or "outbound-weighing" or "one-pass-weighing" or
                "reset-statistics";
        }

        private readonly IRepositorySampleService? m_samples;
        private readonly TimeProvider m_timeProvider;
        private readonly ConfiguredCompanionDeploymentPolicy m_configured;

        /// <summary>
        /// One immutable host grant, adapted to the domain's existing task interface.
        /// </summary>
        private sealed record OwnedSimulationGrant(Guid RunId, string Revision, DateTimeOffset ExpiresAt);
    }
}
