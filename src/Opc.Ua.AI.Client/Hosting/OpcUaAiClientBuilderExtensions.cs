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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.AI.Client;
using Opc.Ua.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the AI Model Management client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaAIClientBuilderExtensions
    {
        /// <summary>
        /// Registers an <see cref="AIClientFactory"/> and a
        /// <c>Func&lt;CancellationToken, Task&lt;AIClient&gt;&gt;</c> so
        /// downstream services can request AI clients.
        /// </summary>
        /// <param name="builder">The client builder returned by <c>AddClient</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddAIClient(
            this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<AIClientFactory>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                    ?? throw new InvalidOperationException(
                        "AddAIClient requires AddClient to be called first.");
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                return new AIClientFactory(sessionFactory, telemetry);
            });

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<AIClient>>>(sp =>
            {
                AIClientFactory factory =
                    sp.GetRequiredService<AIClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }
    }
}
