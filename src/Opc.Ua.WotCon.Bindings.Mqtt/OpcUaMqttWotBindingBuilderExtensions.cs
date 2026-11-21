// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua;
using Opc.Ua.WotCon.Bindings.Mqtt;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that register the MQTT WoT binding
    /// executor alongside the shipped planner binders.
    /// </summary>
    public static class OpcUaMqttWotBindingBuilderExtensions
    {
        /// <summary>
        /// Registers the eight shipped planner binders and the MQTT executor, so
        /// MQTT binding forms are validated, compiled and executable.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddMqttWotBinding(
            this IOpcUaBuilder builder, Action<MqttWotBindingOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new MqttWotBindingOptions();
            configure?.Invoke(options);
            return builder
                .AddWotProtocolBinders()
                .AddWotBindingExecutor(new MqttWotBindingExecutor(options));
        }
    }
}
