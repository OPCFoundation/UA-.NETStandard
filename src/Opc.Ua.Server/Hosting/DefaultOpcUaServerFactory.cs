/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class DefaultOpcUaServerFactory : IOpcUaServerFactory
    {
        public DefaultOpcUaServerFactory(IServiceProvider services)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            return new DependencyInjectionStandardServer(m_services, telemetry, timeProvider);
        }

        private readonly IServiceProvider m_services;
    }
}
