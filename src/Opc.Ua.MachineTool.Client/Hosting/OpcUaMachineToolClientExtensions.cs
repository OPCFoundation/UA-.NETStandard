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
using Opc.Ua.MachineTool.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers typed clients over the application's existing managed-session factory.
    /// </summary>
    public static class OpcUaMachineToolClientExtensions
    {
        /// <summary>
        /// Registers the injectable factory and asynchronous client-creation delegate.
        /// </summary>
        /// <param name="builder">The configured OPC UA client builder.</param>
        /// <returns>The client builder.</returns>
        public static IOpcUaClientBuilder AddMachineToolClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton(services => new MachineToolClientFactory(
                services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                services.GetRequiredService<ITelemetryContext>(),
                services.GetService<TimeProvider>()));
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<MachineToolClient>>>(services =>
                services.GetRequiredService<MachineToolClientFactory>().CreateAsync);
            return builder;
        }
    }
}
