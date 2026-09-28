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
using Opc.Ua.Client;
using Opc.Ua.Machinery.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the Machinery client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaMachineryClientBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="MachineryClient"/> factory. The Machinery
        /// client composes the Device Integration client, so the DI client
        /// services are registered as well.
        /// </summary>
        /// <param name="builder">The client builder returned by <c>AddClient</c>.</param>
        public static IOpcUaClientBuilder AddMachineryClient(
            this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.AddOpcUaDi();
            builder.Services.TryAddSingleton(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException(
                        "AddMachineryClient requires AddClient to be called first.");
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new MachineryClientFactory(sessionFactory, telemetry);
            });

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<MachineryClient>>>(sp =>
            {
                MachineryClientFactory factory =
                    sp.GetRequiredService<MachineryClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }
    }
}
