/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.PubSub.Schema;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency injection extensions for OPC UA PubSub schema generation.
    /// </summary>
    public static class PubSubSchemaServiceCollectionExtensions
    {
        /// <summary>
        /// Registers PubSub DataSet schema generation services.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddPubSubSchema(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddSchemaGeneration();
            builder.Services.TryAddSingleton<IPubSubSchemaProvider, PubSubSchemaProvider>();
            return builder;
        }

        /// <summary>
        /// Registers PubSub DataSet schema generation services.
        /// </summary>
        /// <param name="builder">The PubSub builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IPubSubBuilder AddSchema(this IPubSubBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.OpcUaBuilder.AddPubSubSchema();
            return builder;
        }
    }
}
