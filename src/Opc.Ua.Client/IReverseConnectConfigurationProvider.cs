// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Asynchronously validates and/or transforms the effective
    /// <see cref="ReverseConnectClientConfiguration"/> that a
    /// <see cref="ReverseConnectManager"/> is about to activate.
    /// </summary>
    /// <remarks>
    /// A provider participates in the async lifecycle after the candidate
    /// configuration is selected and before any listener is opened. Providers
    /// are invoked outside the manager's lifecycle gate so they may perform
    /// asynchronous work (for example reading additional endpoints from a
    /// store) without blocking concurrent lifecycle operations.
    /// The default implementation is a pass-through
    /// (<see cref="DefaultReverseConnectConfigurationProvider"/>) so
    /// direct-constructor usage keeps its original behavior.
    /// </remarks>
    public interface IReverseConnectConfigurationProvider
    {
        /// <summary>
        /// Produces the effective reverse-connect configuration to activate.
        /// </summary>
        /// <param name="applicationConfiguration">
        /// Optional application-configuration context. Present when the
        /// lifecycle was started from an
        /// <see cref="ApplicationConfiguration"/>; <c>null</c> when started
        /// from a bare <see cref="ReverseConnectClientConfiguration"/>.
        /// </param>
        /// <param name="configuration">
        /// The candidate configuration supplied to the lifecycle.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The effective configuration to activate.</returns>
        ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
            ApplicationConfiguration? applicationConfiguration,
            ReverseConnectClientConfiguration configuration,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default pass-through
    /// <see cref="IReverseConnectConfigurationProvider"/> that returns the
    /// candidate configuration unchanged.
    /// </summary>
    public sealed class DefaultReverseConnectConfigurationProvider
        : IReverseConnectConfigurationProvider
    {
        /// <summary>
        /// Shared stateless instance used as the direct-constructor fallback.
        /// </summary>
        public static DefaultReverseConnectConfigurationProvider Instance { get; } = new();

        /// <inheritdoc/>
        public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
            ApplicationConfiguration? applicationConfiguration,
            ReverseConnectClientConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ReverseConnectClientConfiguration>(
                configuration ?? new ReverseConnectClientConfiguration());
        }
    }
}
