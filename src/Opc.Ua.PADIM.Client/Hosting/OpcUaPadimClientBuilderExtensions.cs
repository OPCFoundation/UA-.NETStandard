/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PADIM.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers standalone PA-DIM clients over managed sessions.
    /// </summary>
    public static class OpcUaPadimClientBuilderExtensions
    {
        /// <summary>
        /// Adds the injectable PA-DIM client factory without taking ownership of its session.
        /// </summary>
        public static IOpcUaClientBuilder AddPadimClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<PadimClientFactory>();
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<PadimClient>>>(services =>
                services.GetRequiredService<PadimClientFactory>().CreateAsync);
            return builder;
        }
    }
}
