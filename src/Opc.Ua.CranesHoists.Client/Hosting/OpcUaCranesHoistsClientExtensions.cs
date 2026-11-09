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
using Opc.Ua.CranesHoists.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers crane client workflows over the existing managed-session services.
    /// </summary>
    public static class OpcUaCranesHoistsClientExtensions
    {
        /// <summary>
        /// Adds an injectable typed client factory without constructing or owning another session.
        /// </summary>
        public static IOpcUaClientBuilder AddCranesHoistsClient(
            this IOpcUaClientBuilder builder,
            CranesHoistsClientOptions? options = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            CranesHoistsClientOptions selected = options ?? new CranesHoistsClientOptions();
            selected.Validate();
            builder.Services.TryAddSingleton(services => new CranesHoistsClientFactory(
                services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                services.GetRequiredService<ITelemetryContext>(),
                selected,
                services.GetService<TimeProvider>()));
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<CranesHoistsClient>>>(services =>
                services.GetRequiredService<CranesHoistsClientFactory>().CreateAsync);
            return builder;
        }
    }
}
