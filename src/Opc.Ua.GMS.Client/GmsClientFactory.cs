/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// Creates GMS facades that borrow application- or container-owned managed sessions.
    /// </summary>
    public interface IGmsClientFactory
    {
        /// <summary>
        /// Acquires a managed session without taking ownership and returns a facade that owns only its workflows.
        /// </summary>
        Task<GmsClient> CreateAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Uses the existing managed-session accessor without taking ownership of the acquired session.
    /// </summary>
    public sealed class GmsClientFactory : IGmsClientFactory
    {
        /// <summary>
        /// Creates a directly constructed or injected factory using the application's managed-session infrastructure.
        /// </summary>
        /// <param name="sessionFactory">Acquires a managed session whose ownership remains with its provider.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The bounded workflow options.</param>
        /// <param name="timeProvider">The clock used for bounded observations.</param>
        public GmsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory, ITelemetryContext telemetry,
            GmsClientOptions? options = null, TimeProvider? timeProvider = null)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_options = options ?? new GmsClientOptions();
            m_options.Validate();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async Task<GmsClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new GmsClient(session, m_telemetry, m_options, m_timeProvider);
        }

        /// <summary>The existing managed-session accessor.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>The shared telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>The validated immutable workflow bounds.</summary>
        private readonly GmsClientOptions m_options;

        /// <summary>The injected observation clock.</summary>
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Adds typed GMS workflows to an existing session without taking its ownership.
    /// </summary>
    public static class GmsClientSessionExtensions
    {
        /// <summary>
        /// Creates a bounded GMS facade over the caller's session.
        /// </summary>
        public static GmsClient CreateGmsClient(
            this ISession session, ITelemetryContext telemetry, GmsClientOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            return new GmsClient(session, telemetry, options, timeProvider);
        }
    }
}
