/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using Opc.Ua.WotCon.Binding.Planners;

namespace Opc.Ua.WotCon.Binding.Mqtt
{
    /// <summary>
    /// Executes MQTT WoT binding forms compiled by the
    /// <see cref="MqttBindingPlanner"/> by opening a per-form MQTT connection using
    /// the repository's MQTTnet infrastructure.
    /// </summary>
    public sealed class MqttWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>Initializes a new MQTT executor.</summary>
        public MqttWotBindingExecutor(MqttWotBindingOptions? options = null)
        {
            m_options = options ?? new MqttWotBindingOptions();
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.mqtt", "1.0-ed", MqttBindingPlanner.BindingUri, "W3C WoT MQTT Executor");

        /// <inheritdoc/>
        public bool CanExecute(WotCompiledForm form)
            => form is not null && string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);

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
            string clientId = string.Concat(m_options.ClientIdPrefix, "-", suffix.AsSpan(0, 12));
            // Resolve credentials / trust and build the options first, so a
            // fail-closed rejection throws before any client is created.
            MqttWotConnection.MqttWotConnectPlan plan = await MqttWotConnection
                .PrepareAsync(form, context, m_options, clientId, cancellationToken).ConfigureAwait(false);
            IMqttClient client = m_options.ClientFactory?.Invoke() as IMqttClient
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
