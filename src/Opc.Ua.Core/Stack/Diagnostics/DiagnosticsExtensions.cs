/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Service collection extensions
    /// </summary>
    public static class DiagnosticsExtensions
    {
        /// <summary>
        /// Add logging to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddLogging(
            this IDependencyInjectionBuilder builder)
        {
            builder.Services.AddLogging(logging => logging.EnableOpcUaRedaction());
            return builder;
        }

        /// <summary>
        /// Add logging to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddLogging(
            this IDependencyInjectionBuilder builder, Action<ILoggingBuilder> configure)
        {
            builder.Services.AddLogging(logging =>
            {
                logging.EnableOpcUaRedaction();
                configure(logging);
            });
            return builder;
        }

        /// <summary>
        /// Add metrics support to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddMetrics(
            this IDependencyInjectionBuilder builder)
        {
            builder.Services.AddMetrics();
            return builder;
        }

        /// <summary>
        /// Add metrics support to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddMetrics(
            this IDependencyInjectionBuilder builder, Action<IMetricsBuilder> configure)
        {
            builder.Services.AddMetrics(configure);
            return builder;
        }
    }
}
