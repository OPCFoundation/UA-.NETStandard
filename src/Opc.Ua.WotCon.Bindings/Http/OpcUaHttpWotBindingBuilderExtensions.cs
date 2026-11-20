// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;
using Opc.Ua.WotCon.Bindings.Http;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that register the HTTP WoT binding
    /// executor alongside the shipped planner binders.
    /// </summary>
    public static class OpcUaHttpWotBindingBuilderExtensions
    {
        /// <summary>
        /// Registers the eight shipped planner binders and the HTTP executor, so
        /// HTTP binding forms are validated, compiled and executable.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddHttpWotBinding(
            this IOpcUaBuilder builder, Action<HttpWotBindingOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new HttpWotBindingOptions();
            configure?.Invoke(options);
            return builder
                .AddWotProtocolBinders()
                .AddWotBindingExecutor(new HttpWotBindingExecutor(options));
        }
    }
}
