/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.PubSub.Configuration;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent configuration authoring extensions for <see cref="IPubSubBuilder"/>.
    /// </summary>
    public static class PubSubConfigurationBuilderExtensions
    {
        /// <summary>
        /// Builds a PubSub configuration inline and applies it to the PubSub application.
        /// </summary>
        /// <param name="builder">PubSub builder.</param>
        /// <param name="configure">Configuration builder callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IPubSubBuilder ConfigureConfiguration(
            this IPubSubBuilder builder,
            Action<PubSubConfigurationBuilder> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var configurationBuilder = PubSubConfigurationBuilder.Create();
            configure(configurationBuilder);
            return builder.UseConfiguration(configurationBuilder.Build());
        }
    }
}
