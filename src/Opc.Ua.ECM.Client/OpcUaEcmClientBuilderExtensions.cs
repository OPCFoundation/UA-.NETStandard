// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.ECM.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers ECM clients over the existing container-owned session.
    /// </summary>
    public static class OpcUaEcmClientBuilderExtensions
    {
        /// <summary>
        /// Adds the typed ECM client factory and optional DI exclusive-access client services.
        /// </summary>
        public static IOpcUaClientBuilder AddEcmClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.AddOpcUaDi();
            builder.Services.TryAddSingleton(static services => new EcmClientFactory(
                services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                services.GetRequiredService<ITelemetryContext>()));
            return builder;
        }
    }
}
