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
using Opc.Ua.Client;
using Opc.Ua.WotCon.Binding.Planners;

namespace Opc.Ua.WotCon.Binding.OpcUa
{
    /// <summary>
    /// Executes OPC UA WoT binding forms compiled by the
    /// <see cref="OpcUaBindingPlanner"/> by connecting an <see cref="ISession"/> to
    /// the target endpoint through the injectable session factory.
    /// </summary>
    public sealed class OpcUaWotBindingExecutor : IWotBindingExecutor
    {
        /// <summary>Initializes a new OPC UA executor.</summary>
        public OpcUaWotBindingExecutor(OpcUaWotBindingOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public WotBindingIdentity Identity { get; } =
            new WotBindingIdentity("opc.opcua", "10101", OpcUaBindingPlanner.BindingUri, "OPC UA WoT Executor");

        /// <inheritdoc/>
        public bool CanExecute(WotCompiledForm form)
            => form is not null && string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);

        /// <inheritdoc/>
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
            if (m_options.SessionFactory is null)
            {
                throw new InvalidOperationException(
                    "No OPC UA session factory is configured on the executor options.");
            }
            string endpoint = string.IsNullOrEmpty(form.Endpoint.BaseUri)
                ? form.Endpoint.Scheme + "://" + (form.Endpoint.Host ?? string.Empty)
                : form.Endpoint.BaseUri;
            ISession session = await m_options.SessionFactory(endpoint, cancellationToken).ConfigureAwait(false);
            return new OpcUaWotBindingChannel(session, m_options.DisposeSession, form, context, m_options);
        }

        private readonly OpcUaWotBindingOptions m_options;
    }
}
