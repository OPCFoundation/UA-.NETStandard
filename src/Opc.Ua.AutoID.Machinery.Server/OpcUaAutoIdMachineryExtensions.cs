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
using Opc.Ua.AutoID.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Composes OPC 30010 AutoID readers into the Machinery node manager.
    /// </summary>
    public static class OpcUaAutoIdMachineryExtensions
    {
        /// <summary>
        /// Adds the AutoID model to an existing <c>AddMachinery</c> registration, so readers and
        /// machines share one <c>DeviceSet</c>.
        /// </summary>
        /// <remarks>
        /// The Machinery owner loads Device Integration; this adds only the AutoID model and the
        /// configured additional namespaces, and makes sure the Machinery configuration pipeline
        /// is registered before any <c>ConfigureAutoIdFor</c> delegate, so the readers are created
        /// after the Machinery configuration has run. Repeated calls register the model once.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Configures the AutoID options.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException">
        /// <c>AddMachinery</c> was not registered before.
        /// </exception>
        public static IOpcUaServerBuilder AddAutoIdToMachinery(
            this IOpcUaServerBuilder builder,
            Action<AutoIdServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            bool owner = false;
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                owner |= descriptor.ServiceType == typeof(MachineryNodeManagerFactory);
            }
            if (!owner)
            {
                throw new InvalidOperationException("Register AddMachinery before AddAutoIdToMachinery.");
            }
            Func<IMachineryBuildContext, CancellationToken, ValueTask> pipeline = static (_, _) => default;
            builder.ConfigureMachineryFor<MachineryNodeManager>(pipeline);
            return builder.AddAutoIdModel(configure);
        }
    }
}
