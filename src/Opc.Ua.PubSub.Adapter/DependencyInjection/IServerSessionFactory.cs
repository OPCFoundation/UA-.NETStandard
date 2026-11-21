// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Creates <see cref="IServerSession"/> instances that connect to an
    /// external OPC UA server. Implementations are registered with the adapter
    /// dependency-injection container so that publisher, subscriber and action
    /// components can resolve sessions without taking a direct dependency on the
    /// concrete session type.
    /// </summary>
    public interface IServerSessionFactory
    {
        /// <summary>
        /// Creates a new, not-yet-connected external server session for the
        /// supplied connection options and telemetry context. The returned
        /// session connects lazily on the first call to
        /// <see cref="IServerSession.ConnectAsync"/> or any service
        /// method.
        /// </summary>
        IServerSession Create(
            ServerConnectionOptions options,
            ITelemetryContext telemetry);
    }
}
