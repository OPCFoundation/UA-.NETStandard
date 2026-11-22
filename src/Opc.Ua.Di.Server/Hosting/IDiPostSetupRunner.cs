// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// Orchestrator that resolves
    /// <see cref="IDiPostSetupConfigurator"/> instances from the
    /// service provider, filters them by manager type, and invokes
    /// each in registration order with fail-fast exception semantics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as a singleton by
    /// <c>OpcUaServerBuilderExtensions.AddOpcUaDi</c>. DI-aware node
    /// manager factories (<see cref="DiNodeManagerFactory"/>,
    /// <c>Pumps.PumpNodeManagerFactory</c>) inject this service through
    /// their constructor and pass it on to the manager they produce;
    /// the manager then calls <c>RunAsync</c> at the end of its own
    /// <c>CreateAddressSpaceAsync</c> override.
    /// </para>
    /// <para>
    /// Plain DI servers can also resolve the runner manually for
    /// testing, but the production path is always through the
    /// factory + manager.
    /// </para>
    /// </remarks>
    public interface IDiPostSetupRunner
    {
        /// <summary>
        /// Runs every registered <see cref="IDiPostSetupConfigurator"/>
        /// whose <see cref="IDiPostSetupConfigurator.TargetManagerType"/>
        /// matches (via <see cref="System.Type.IsAssignableFrom"/>) the
        /// runtime type of <paramref name="manager"/>. Configurators run
        /// in registration order; the first exception aborts startup.
        /// </summary>
        /// <param name="manager">The active DI node manager.</param>
        /// <param name="cancellationToken">Cancellation token from the
        /// hosted service.</param>
        /// <exception cref="System.Exception">
        /// Re-thrown from any configurator that fails. Startup aborts.
        /// </exception>
        ValueTask RunAsync(DiNodeManager manager, CancellationToken cancellationToken);
    }
}
