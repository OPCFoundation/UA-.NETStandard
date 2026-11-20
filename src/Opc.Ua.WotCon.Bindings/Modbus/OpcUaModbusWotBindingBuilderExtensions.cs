// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;
using Opc.Ua.WotCon.Bindings.Modbus;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that register the Modbus TCP WoT
    /// binding executor alongside the shipped planner binders.
    /// </summary>
    public static class OpcUaModbusWotBindingBuilderExtensions
    {
        /// <summary>
        /// Registers the eight shipped planner binders and the Modbus TCP executor,
        /// so Modbus binding forms are validated, compiled and executable.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddModbusWotBinding(
            this IOpcUaBuilder builder, Action<ModbusWotBindingOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new ModbusWotBindingOptions();
            configure?.Invoke(options);
            return builder
                .AddWotProtocolBinders()
                .AddWotBindingExecutor(new ModbusWotBindingExecutor(options));
        }
    }
}
