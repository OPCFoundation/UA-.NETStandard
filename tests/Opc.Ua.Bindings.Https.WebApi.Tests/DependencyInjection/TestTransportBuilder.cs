// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.DependencyInjection
{
    internal sealed class TestTransportBuilder : IOpcUaTransportBuilder
    {
        public TestTransportBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }
    }
}
