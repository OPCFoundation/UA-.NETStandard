// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;
using Opc.Ua.WotCon.Bindings.OpcUa;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that register the OPC UA WoT binding
    /// executor (OPC UA-to-OPC UA translation) alongside the shipped planner binders.
    /// </summary>
    public static class OpcUaTargetWotBindingBuilderExtensions
    {
        /// <summary>
        /// Registers the eight shipped planner binders and the OPC UA executor, so
        /// OPC UA binding forms are validated, compiled and executable.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddOpcUaWotBinding(
            this IOpcUaBuilder builder, Action<OpcUaWotBindingOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var options = new OpcUaWotBindingOptions();
            configure(options);
            return builder
                .AddWotProtocolBinders()
                .AddWotBindingExecutor(new OpcUaWotBindingExecutor(options));
        }
    }
}
