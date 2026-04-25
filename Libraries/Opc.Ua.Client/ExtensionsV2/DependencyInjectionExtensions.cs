#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Extensions.DependencyInjection
{
    using Microsoft.Extensions.Diagnostics.Metrics;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Client;
    using System;

    /// <summary>
    /// Service collection extensions
    /// </summary>
    public static class DependencyInjectionExtensions
    {
        /// <summary>
        /// Add logging to the builder
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IDependencyInjectionBuilder AddLogging(
            this IDependencyInjectionBuilder builder)
        {
            builder.Services.AddLogging();
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
            builder.Services.AddLogging(configure);
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
#endif
