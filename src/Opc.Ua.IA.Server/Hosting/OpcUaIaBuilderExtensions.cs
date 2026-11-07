/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.IA.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers IA on the selected DI-owning server without adding a competing model producer.
    /// </summary>
    public static class OpcUaIaBuilderExtensions
    {
        /// <summary>
        /// Adds the IA model to an existing DI, Machinery or Robotics registration.
        /// </summary>
        public static IOpcUaServerBuilder AddIndustrialAutomation(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddDiModel<IaModelProvider>();
        }

        /// <summary>
        /// Configures IA bindings on the chosen owner after its models have loaded.
        /// </summary>
        /// <typeparam name="TNodeManager">The existing DI or companion namespace owner.</typeparam>
        /// <remarks>
        /// Resolve application providers through the setup context. Construct the bindings and
        /// register their nodes with the same manager that owns their parent.
        /// </remarks>
        public static IOpcUaServerBuilder ConfigureIndustrialAutomationFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IDiPostSetupContext, ValueTask> configure)
            where TNodeManager : DiNodeManager
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.AddIndustrialAutomation().ConfigureDevicesFor<TNodeManager>(configure);
        }
    }
}
