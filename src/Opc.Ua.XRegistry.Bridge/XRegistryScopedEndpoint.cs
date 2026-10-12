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
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge
{
    /// <summary>
    /// Resolves isolated caller-specific endpoints without caching sessions, models or credentials.
    /// A prepared operation retains its acquisition lease through candidate reads and commit.
    /// </summary>
    public sealed class XRegistryScopedEndpoint :
        IXRegistryPreparedEndpoint, IXRegistryOperationJournalEndpoint, IXRegistryChangeFeed, IXRegistryAddressResolver
    {
        /// <summary>
        /// Borrows the application's resolver. Resolved leases remain owned by their individual operations.
        /// </summary>
        public XRegistryScopedEndpoint(IXRegistryEndpointResolver resolver, ITelemetryContext telemetry)
        {
            m_resolver = resolver.ThrowIfNull(nameof(resolver));
            m_logger = telemetry.ThrowIfNull(nameof(telemetry)).CreateLogger<XRegistryScopedEndpoint>();
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            IXRegistryEndpointLease lease = await AcquireAsync(context, cancellationToken).ConfigureAwait(false);
            await using (lease.ConfigureAwait(false))
            {
                using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Revoked);
                XRegistryEndpointDescription description = await lease.Endpoint.InspectAsync(context, token.Token)
                    .ConfigureAwait(false);
                return description with
                {
                    SupportsPreparedMutations =
                        description.SupportsPreparedMutations && lease.Endpoint is IXRegistryPreparedEndpoint,
                    SupportsPreparedSnapshots =
                        description.SupportsPreparedSnapshots && lease.Endpoint is IXRegistryPreparedEndpoint,
                    SupportsOperationReplay =
                        description.SupportsOperationReplay && lease.Endpoint is IXRegistryOperationJournalEndpoint
                };
            }
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            if (request.IsMutation)
            {
                IXRegistryPreparedOperation operation =
                    await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
                await using (operation.ConfigureAwait(false))
                {
                    return await operation.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            IXRegistryEndpointLease lease =
                await AcquireAsync(request.Context, cancellationToken).ConfigureAwait(false);
            await using (lease.ConfigureAwait(false))
            {
                using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Revoked);
                return await lease.Endpoint.ExecuteAsync(request, token.Token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryAddressResolution> ResolveAddressAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            IXRegistryEndpointLease lease = await AcquireAsync(request.Context, cancellationToken)
                .ConfigureAwait(false);
            await using (lease.ConfigureAwait(false))
            {
                using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Revoked);
                return lease.Endpoint is IXRegistryAddressResolver resolver
                    ? await resolver.ResolveAddressAsync(request, token.Token).ConfigureAwait(false)
                    : new XRegistryAddressResolution(request);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IXRegistryPreparedOperation> PrepareAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            IXRegistryEndpointLease lease =
                await AcquireAsync(request.Context, cancellationToken).ConfigureAwait(false);
            bool transferred = false;
            try
            {
                if (lease.Endpoint is not IXRegistryPreparedEndpoint endpoint)
                {
                    return new UnsupportedPreparation();
                }
                using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Revoked);
                IXRegistryPreparedOperation operation =
                    await endpoint.PrepareAsync(request, token.Token).ConfigureAwait(false);
                var result = new LeasedPreparation(operation, lease, m_logger);
                transferred = true;
                return result;
            }
            finally
            {
                if (!transferred)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<XRegistryOperationOutcome> GetOperationOutcomeAsync(
            string operationId, XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            IXRegistryEndpointLease lease = await AcquireAsync(context, cancellationToken).ConfigureAwait(false);
            await using (lease.ConfigureAwait(false))
            {
                if (lease.Endpoint is not IXRegistryOperationJournalEndpoint journal)
                {
                    throw new NotSupportedException("The selected caller endpoint has no operation journal.");
                }
                using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Revoked);
                return await journal.GetOperationOutcomeAsync(operationId, context, token.Token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<XRegistryChangeHint> WatchAsync(
            XRegistryCallContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IXRegistryEndpointLease lease = await AcquireAsync(context, cancellationToken).ConfigureAwait(false);
            await using (lease.ConfigureAwait(false))
            {
                if (lease.Endpoint is not IXRegistryChangeFeed feed)
                {
                    throw new NotSupportedException("The selected caller endpoint has no change feed.");
                }
                using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Revoked);
                await foreach (XRegistryChangeHint hint in feed.WatchAsync(context, token.Token).ConfigureAwait(false))
                {
                    await lease.EnsureCurrentAsync(token.Token).ConfigureAwait(false);
                    yield return hint;
                }
            }
        }

        private async ValueTask<IXRegistryEndpointLease> AcquireAsync(
            XRegistryCallContext context, CancellationToken ct)
        {
            context.ThrowIfNull(nameof(context));
            IXRegistryEndpointLease lease = await m_resolver.AcquireAsync(context, ct).ConfigureAwait(false);
            bool accepted = false;
            try
            {
                if (!XRegistryEndpointLease.SameScope(context, lease.Context))
                {
                    throw new UnauthorizedAccessException("The resolver returned another caller's endpoint lease.");
                }
                await lease.EnsureCurrentAsync(ct).ConfigureAwait(false);
                accepted = true;
                return lease;
            }
            finally
            {
                if (!accepted)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private sealed class UnsupportedPreparation : IXRegistryPreparedOperation
        {
            public XRegistryResponse Response { get; } = new(405)
            {
                Error = new XRegistryError("action_not_supported", "The caller endpoint cannot prepare this mutation.")
            };

            public ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<XRegistryResponse>(Response);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }

        private sealed class LeasedPreparation(
            IXRegistryPreparedOperation operation, IXRegistryEndpointLease lease,
                ILogger logger) : IXRegistryPreparedSnapshot
        {
            public XRegistryResponse Response => operation.Response;

            public bool HasCandidate => !m_consumed &&
                !lease.Revoked.IsCancellationRequested &&
                operation is IXRegistryPreparedSnapshot { HasCandidate: true };

            public async ValueTask<XRegistryResponse> CommitAsync(CancellationToken cancellationToken = default)
            {
                await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RequireActive();
                    m_consumed = true;
                    await lease.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
                    using var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Revoked);
                    XRegistryResponse response = await operation.CommitAsync(token.Token).ConfigureAwait(false);
                    m_committed = response.IsSuccess;
                    return response;
                }
                finally
                {
                    m_serial.Release();
                }
            }

            public async ValueTask<XRegistryEndpointDescription> InspectCandidateAsync(
                CancellationToken cancellationToken = default)
            {
                await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RequireActive();
                    await lease.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
                    return await Candidate().InspectCandidateAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    m_serial.Release();
                }
            }

            public async ValueTask<XRegistryResponse> ReadCandidateAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                request.ThrowIfNull(nameof(request));
                await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    RequireActive();
                    await lease.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
                    return await Candidate().ReadCandidateAsync(
                        request with { Context = lease.Context }, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    m_serial.Release();
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposalStarted, 1) != 0)
                {
                    return;
                }
                await m_serial.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    m_consumed = true;
                    try
                    {
                        await operation.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await lease.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (m_committed &&
                    exception is IOException or InvalidOperationException or ServiceResultException
                        or OperationCanceledException)
                {
                    logger.CommittedLeaseCleanupFailed(exception);
                }
                finally
                {
                    m_serial.Release();
                    m_serial.Dispose();
                }
            }

            private IXRegistryPreparedSnapshot Candidate()
            {
                return operation as IXRegistryPreparedSnapshot
                    ?? throw new NotSupportedException("The selected endpoint does not expose a candidate snapshot.");
            }

            private void RequireActive()
            {
                if (m_consumed || m_disposed)
                {
                    throw new InvalidOperationException("The prepared caller lease has already been consumed.");
                }
            }

            private readonly SemaphoreSlim m_serial = new(1, 1);
            private volatile bool m_consumed;
            private bool m_committed;
            private bool m_disposed;
            private int m_disposalStarted;
        }

        private readonly IXRegistryEndpointResolver m_resolver;
        private readonly ILogger m_logger;
    }

    /// <summary>
    /// Registers caller-specific endpoint resolution for native gateways and reconciliation jobs.
    /// HTTP hosts can pass the resolver directly to MapXRegistry to hold one lease across wire preflight.
    /// </summary>
    public static class XRegistryScopedEndpointServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a resolver and its prepared, journal and ordinary endpoint surfaces.
        /// </summary>
        public static IServiceCollection AddXRegistryCallerEndpoints(
            this IServiceCollection services, IXRegistryEndpointResolver resolver)
        {
            services.ThrowIfNull(nameof(services));
            resolver.ThrowIfNull(nameof(resolver));
            services.AddSingleton(resolver);
            services.AddSingleton<XRegistryScopedEndpoint>();
            services.AddSingleton<IXRegistryEndpoint>(
                provider => provider.GetRequiredService<XRegistryScopedEndpoint>());
            services.AddSingleton<IXRegistryPreparedEndpoint>(
                provider => provider.GetRequiredService<XRegistryScopedEndpoint>());
            services.AddSingleton<IXRegistryOperationJournalEndpoint>(
                provider => provider.GetRequiredService<XRegistryScopedEndpoint>());
            return services;
        }
    }

    internal static partial class XRegistryScopedEndpointLog
    {
        [LoggerMessage(EventId = XRegistryBridgeEventIds.ScopedEndpoint, Level = LogLevel.Error,
            Message = "xRegistry caller lease cleanup failed after a known commit; the committed outcome is retained.")]
        public static partial void CommittedLeaseCleanupFailed(this ILogger logger, Exception exception);
    }
}
