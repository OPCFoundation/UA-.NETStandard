// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Mqtt
{
    /// <summary>
    /// Executes MQTT WoT binding forms compiled by the
    /// <see cref="MqttBindingPlanner"/> by opening a per-form MQTT connection using
    /// the repository's MQTTnet infrastructure.
    /// </summary>
    public sealed class MqttWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>
        /// Initializes a new MQTT executor.
        /// </summary>
        public MqttWotBindingExecutor(MqttWotBindingOptions? options = null)
        {
            m_options = options ?? new MqttWotBindingOptions();
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.mqtt", "1.0-ed", MqttBindingPlanner.BindingUri, "W3C WoT MQTT Executor");

        /// <inheritdoc/>
        public bool CanExecute(WotCompiledForm form)
        {
            return form is not null && string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The client is owned by the returned channel, which disposes it.")]
        public async ValueTask<IWotBindingChannel> ActivateAsync(
            WotCompiledForm form, WotExecutorContext context, CancellationToken cancellationToken = default)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            string suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            string clientId = $"{m_options.ClientIdPrefix}-{suffix.AsSpan(0, 12)}";
            // Resolve credentials / trust and build the options first, so a
            // fail-closed rejection throws before any client is created.
            MqttWotConnection.MqttWotConnectPlan plan = await MqttWotConnection
                .PrepareAsync(form, context, m_options, clientId, cancellationToken).ConfigureAwait(false);
            IMqttClient client = m_options.ClientFactory?.Invoke()
                ?? new MqttClientFactory().CreateMqttClient();
            try
            {
                await client.ConnectAsync(plan.Options, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                throw;
            }
            return new MqttWotBindingChannel(client, form, context, m_options);
        }

        private readonly MqttWotBindingOptions m_options;
    }
}
