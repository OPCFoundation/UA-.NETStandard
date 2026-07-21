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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Binding.Planners;

namespace Opc.Ua.WotCon.Binding.Modbus
{
    /// <summary>
    /// Executes Modbus TCP WoT binding forms compiled by the
    /// <see cref="ModbusBindingPlanner"/> by opening a per-form Modbus TCP
    /// connection.
    /// </summary>
    public sealed class ModbusWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>Initializes a new Modbus executor.</summary>
        public ModbusWotBindingExecutor(ModbusWotBindingOptions? options = null)
        {
            m_options = options ?? new ModbusWotBindingOptions();
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("w3c.modbus", "1.0-ed", ModbusBindingPlanner.BindingUri, "W3C WoT Modbus Executor");

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
            // Re-validate the addressing (and perform the ushort / byte casts) before
            // opening the socket so a hand-built or tampered compiled form fails fast
            // and never leaks a half-open connection.
            ModbusAddressing addressing = ModbusAddressing.FromForm(form);
            string host = string.IsNullOrEmpty(form.Endpoint.Host) ? "127.0.0.1" : form.Endpoint.Host!;
            int port = form.Endpoint.Port > 0 ? form.Endpoint.Port : 502;
            var client = new ModbusTcpClient(host, port, context.Bounds.DefaultTimeout);
            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                throw;
            }
            return new ModbusWotBindingChannel(client, form, context, m_options, addressing);
        }

        private readonly ModbusWotBindingOptions m_options;
    }
}
