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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;

namespace OnboardingDevice
{
    /// <summary>
    /// Validates a lost-Finish assignment using the active attempt's session and retained public evidence.
    /// </summary>
    internal sealed class DevicePullSigningRecovery :
        IPullDcaSigningRecoveryProvider,
        IPullDcaSigningAssignmentsObserver
    {
        /// <summary>
        /// Borrows the local journal, staged-key owner, and current attempt session.
        /// </summary>
        public DevicePullSigningRecovery(
            DevicePullJournal journal,
            DevicePullSigningKeyStore keys,
            Func<CancellationToken, Task<ManagedSession>> sessionAccessor)
        {
            m_journal = journal ?? throw new ArgumentNullException(nameof(journal));
            m_keys = keys ?? throw new ArgumentNullException(nameof(keys));
            m_sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        }

        /// <inheritdoc/>
        public ValueTask RecordAssignmentsAsync(
            PullDcaSigningBinding binding,
            ArrayOf<NodeId> certificateTypes,
            ArrayOf<ByteString> certificates,
            CancellationToken cancellationToken = default)
        {
            return m_keys.RecordAssignmentsAsync(binding, certificateTypes, certificates, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask ValidateAsync(
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (registrar == null)
            {
                throw new ArgumentNullException(nameof(registrar));
            }
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            registrar = registrar with
            {
                RegistrarId = DevicePullSigningKeyStore.CopyNodeId(registrar.RegistrarId),
                CertificateDirectoryId = DevicePullSigningKeyStore.CopyNodeId(registrar.CertificateDirectoryId)
            };
            applicationId = DevicePullSigningKeyStore.CopyNodeId(applicationId);
            requestId = DevicePullSigningKeyStore.CopyNodeId(requestId);
            profile = DevicePullSigningKeyStore.CopyProfile(profile);
            credentials = DevicePullJournal.SnapshotCredentials(credentials);
            ManagedSession session = await m_sessionAccessor(cancellationToken).ConfigureAwait(false);
            SessionRequestContext context = await SessionRequestContext.CaptureConfidentialAsync(
                session, cancellationToken).ConfigureAwait(false);
            await using var lifetime = context.ConfigureAwait(false);
            DevicePullJournalCheckpoint original = await RequireRecoveryAsync(
                context, registrar, applicationId, requestId, profile, cancellationToken).ConfigureAwait(false);
            ByteString digest = m_journal.DigestDispatchBinding(original.Binding);
            await m_keys.ValidateAssignedCertificateAsync(original.Binding, credentials, cancellationToken)
                .ConfigureAwait(false);
            DevicePullJournalCheckpoint current = await RequireRecoveryAsync(
                context, registrar, applicationId, requestId, profile, cancellationToken).ConfigureAwait(false);
            if (current.Binding.AttemptId != original.Binding.AttemptId ||
                m_journal.DigestDispatchBinding(current.Binding) != digest)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
        }

        /// <summary>
        /// Requires the original request, current journal stage, and exact confidential channel.
        /// </summary>
        private async ValueTask<DevicePullJournalCheckpoint> RequireRecoveryAsync(
            SessionRequestContext context,
            PullDcaRegistrar registrar,
            NodeId applicationId,
            NodeId requestId,
            PullDcaCertificateProfile profile,
            CancellationToken ct)
        {
            DevicePullJournalCheckpoint original = await m_journal.ReadAsync(ct).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadInvalidState);
            DevicePullJournalCheckpoint current = await m_journal.ReadForSessionAsync(
                context.Session.NamespaceUris.ToArrayOf(), ct).ConfigureAwait(false);
            PullDcaSigningBinding binding = current.Binding;
            if (original.Stage != DevicePullJournalStage.Receiving ||
                current.Stage != DevicePullJournalStage.Receiving ||
                original.Binding.AttemptId != binding.AttemptId ||
                binding.Registrar != registrar || binding.ApplicationId != applicationId ||
                binding.RequestId != requestId || binding.Profile != profile)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            RequireChannel(context, binding);
            _ = await m_journal.GetReceivedAsync(binding, ct).ConfigureAwait(false);
            RequireChannel(context, binding);
            return original;
        }

        /// <summary>
        /// Compares the physical channel with the original signing identity.
        /// </summary>
        private static void RequireChannel(SessionRequestContext context, PullDcaSigningBinding binding)
        {
            ITransportChannel channel = context.Session.TransportChannel;
            if (context.Invalidated.IsCompleted ||
                !binding.ClientChannelCertificate.Span.SequenceEqual(channel.ClientChannelCertificate) ||
                !binding.RegistrarChannelCertificate.Span.SequenceEqual(channel.ServerChannelCertificate) ||
                !string.Equals(
                    binding.RegistrarApplicationUri, channel.EndpointDescription.Server?.ApplicationUri,
                    StringComparison.Ordinal) ||
                !Uri.TryCreate(channel.EndpointDescription.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                !endpoint.Equals(binding.Registrar.EndpointUrl))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The recovered request belongs to a different channel.");
            }
        }

        /// <summary>The request journal retaining the original dispatch binding.</summary>
        private readonly DevicePullJournal m_journal;

        /// <summary>The owner of the retained private key and assignment checkpoint.</summary>
        private readonly DevicePullSigningKeyStore m_keys;

        /// <summary>The active attempt's borrowed session accessor.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionAccessor;
    }
}
