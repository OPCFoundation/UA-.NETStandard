// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Companion binding seam for one process view. Implementations own only local protocol/runtime handles.
    /// They must apply absolute committed counters, not replay transfers or start their own causal engine.
    /// </summary>
    public interface IRecyclingProcessViewAdapter
    {
        /// <summary>
        /// True when the selected read-only equipment instances and committed-value bindings
        /// are implemented and verified.
        /// This does not claim source-schema parity, optional remote controls, or companion-profile certification.
        /// </summary>
        bool CompanionBindingsComplete { get; }

        /// <summary>
        /// Builds instances in the existing single DI/Machinery owner's address space.
        /// </summary>
        ValueTask InitializeAsync(IMachineryBuildContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Reprojects an immutable absolute image, preserving its source timestamps.
        /// Must tolerate repeated revisions without increasing counters or repeating physical effects.
        /// Availability recovery can reapply the same revision to restore native-node data quality.
        /// </summary>
        ValueTask ApplyAsync(CommittedSnapshot snapshot, CancellationToken cancellationToken);

        /// <summary>
        /// Marks material data unavailable while retaining the last committed source timestamp and values.
        /// </summary>
        ValueTask MarkUnavailableAsync(DateTime sourceTimestampUtc, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Optional end-user forwarding boundary. The input principal was resolved from the real view session.
    /// Implementations must use an independently authenticated coordinator session for that same end user.
    /// </summary>
    public interface IRecyclingUserCommandForwarder
    {
        /// <summary>
        /// Forwards a production command without inventing an actor field
        /// or granting the process service extra rights.
        /// </summary>
        ValueTask<RecyclingCommandOutcome> ForwardAsync(
            RecyclingPrincipal authenticatedUser,
            RecyclingCommand command,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Forwards through an application-provided, same-certificate end-user managed client.
    /// The provider owns returned clients and their certificate/secret-store lifetimes.
    /// </summary>
    public sealed class RecyclingUserCommandForwarder : IRecyclingUserCommandForwarder
    {
        /// <summary>
        /// Creates a forwarding seam without storing or accepting wire-supplied user credentials.
        /// </summary>
        public RecyclingUserCommandForwarder(
            Func<RecyclingPrincipal, CancellationToken, ValueTask<RecyclingCoordinatorClient>> clientProvider)
        {
            m_clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        }

        /// <inheritdoc/>
        public async ValueTask<RecyclingCommandOutcome> ForwardAsync(
            RecyclingPrincipal authenticatedUser,
            RecyclingCommand command,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(authenticatedUser);
            ArgumentNullException.ThrowIfNull(command);
            RecyclingCoordinatorClient client = await m_clientProvider(authenticatedUser, cancellationToken)
                .ConfigureAwait(false);
            if (authenticatedUser.CertificateSha256.Length == 0 ||
                !string.Equals(
                    authenticatedUser.CertificateSha256, client.CertificateSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "The forwarder must authenticate as the originating end user.");
            }
            return await client.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        private readonly Func<RecyclingPrincipal, CancellationToken,
            ValueTask<RecyclingCoordinatorClient>> m_clientProvider;
    }

    /// <summary>
    /// Default secure refusal: a process service identity is not an operator credential.
    /// </summary>
    internal sealed class UnconfiguredUserCommandForwarder : IRecyclingUserCommandForwarder
    {
        /// <inheritdoc/>
        public ValueTask<RecyclingCommandOutcome> ForwardAsync(
            RecyclingPrincipal authenticatedUser,
            RecyclingCommand command,
            CancellationToken cancellationToken)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported,
                "Configure same-user authenticated forwarding or call the coordinator directly.");
        }
    }
}
