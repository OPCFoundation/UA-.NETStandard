// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;
using Opc.Ua.Di.Client.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaClientBuilder"/> extensions that register the
    /// OPC UA Device Integration (DI, OPC 10000-100) client services
    /// in the unified <c>AddOpcUa()</c> Microsoft.Extensions DI
    /// hosting model.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <code>
    /// services.AddOpcUa()
    ///         .AddClient(o =&gt; { ... })
    ///         .AddOpcUaDi();
    ///
    /// // Then inject IDiDiscoveryService or
    /// // Func&lt;NodeId, CancellationToken, ValueTask&lt;DiDeviceClient&gt;&gt;
    /// // into your application services.
    /// </code>
    /// <para>
    /// Requires <c>AddClient(...)</c> to be called first — the
    /// extension throws if the managed-session factory is missing.
    /// </para>
    /// </remarks>
    public static class OpcUaClientDiBuilderExtensions
    {
        /// <summary>
        /// Registers DI client services:
        /// <list type="bullet">
        ///   <item>
        ///     <description>
        ///       <see cref="IDiDiscoveryService"/> — enumerates
        ///       DeviceType instances on the connected server.
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <description>
        ///       <see cref="Func{T1, T2, TResult}"/> for
        ///       (<see cref="NodeId"/>,
        ///        <see cref="CancellationToken"/>) returning
        ///       <see cref="ValueTask{TResult}"/> of
        ///       <see cref="DiDeviceClient"/> — opens the lazy
        ///       session and creates a verified DiDeviceClient
        ///       rooted at the supplied NodeId.
        ///     </description>
        ///   </item>
        /// </list>
        /// </summary>
        /// <param name="builder">The client builder.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is null.
        /// </exception>
        public static IOpcUaClientBuilder AddOpcUaDi(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<IDiDiscoveryService>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires AddClient() to be called first. " +
                        "The managed-session factory was not registered.");
                ITelemetryContext telemetry = sp.GetService<ITelemetryContext>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires an ITelemetryContext. " +
                        "AddClient() registers one by default.");

                return new DiDiscoveryService(accessor, telemetry);
            });

            // Lazy factory: callers ask for a DiDeviceClient rooted at a NodeId;
            // we open the managed session (or reuse it) and call ForDeviceAsync
            // to validate the node exists.
            builder.Services.TryAddSingleton<
                Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires AddClient() to be called first. " +
                        "The managed-session factory was not registered.");
                ITelemetryContext telemetry = sp.GetService<ITelemetryContext>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires an ITelemetryContext.");

                return async (deviceNodeId, ct) =>
                {
                    ManagedSession session = await accessor(ct).ConfigureAwait(false);
                    return await DiDeviceClient
                        .ForDeviceAsync(session, deviceNodeId, telemetry, ct)
                        .ConfigureAwait(false);
                };
            });

            // Lock client factory: callers supply the NodeId of a
            // LockingServicesType instance; we open the session and
            // wrap it in a DiLockClient.
            builder.Services.TryAddSingleton<
                Func<NodeId, CancellationToken, ValueTask<DiLockClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires AddClient() to be called first.");
                ITelemetryContext telemetry = sp.GetService<ITelemetryContext>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires an ITelemetryContext.");

                return async (lockNodeId, ct) =>
                {
                    ManagedSession session = await accessor(ct).ConfigureAwait(false);
                    return new DiLockClient(session, lockNodeId, telemetry);
                };
            });

            // Topology client: enumerates DeviceSet / NetworkSet /
            // DeviceTopology folders. Constructed lazily on first use.
            builder.Services.TryAddSingleton<
                Func<CancellationToken, ValueTask<DiTopologyClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires AddClient() to be called first.");
                ITelemetryContext telemetry = sp.GetService<ITelemetryContext>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires an ITelemetryContext.");

                return async ct =>
                {
                    ManagedSession session = await accessor(ct).ConfigureAwait(false);
                    return new DiTopologyClient(session, telemetry);
                };
            });

            // SoftwareUpdate client factory: callers supply a NodeId of
            // a SoftwareUpdateType instance on the server.
            builder.Services.TryAddSingleton<
                Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires AddClient() to be called first.");
                ITelemetryContext telemetry = sp.GetService<ITelemetryContext>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires an ITelemetryContext.");

                return async (softwareUpdateNodeId, ct) =>
                {
                    ManagedSession session = await accessor(ct).ConfigureAwait(false);
                    return new SoftwareUpdateClient(session, softwareUpdateNodeId, telemetry);
                };
            });

            // Transfer client factory: callers supply the NodeId of a
            // TransferServicesType instance on the server.
            builder.Services.TryAddSingleton<
                Func<NodeId, CancellationToken, ValueTask<DiTransferClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires AddClient() to be called first.");
                ITelemetryContext telemetry = sp.GetService<ITelemetryContext>()
                    ?? throw new InvalidOperationException(
                        "AddOpcUaDi() requires an ITelemetryContext.");

                return async (transferNodeId, ct) =>
                {
                    ManagedSession session = await accessor(ct).ConfigureAwait(false);
                    return new DiTransferClient(session, transferNodeId, telemetry);
                };
            });

            return builder;
        }
    }
}
