/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// Orchestrator that resolves
    /// <see cref="IDiPostSetupConfigurator"/> instances from the
    /// service provider, filters them by manager type, and invokes
    /// each in registration order with fail-fast exception semantics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered as a singleton by
    /// <c>OpcUaServerBuilderExtensions.AddOpcUaDi</c>. DI-aware node
    /// manager factories (<see cref="DiNodeManagerFactory"/>,
    /// <c>Pumps.PumpNodeManagerFactory</c>) inject this service through
    /// their constructor and pass it on to the manager they produce;
    /// the manager then calls <c>RunAsync</c> at the end of its own
    /// <c>CreateAddressSpaceAsync</c> override.
    /// </para>
    /// <para>
    /// Plain DI servers can also resolve the runner manually for
    /// testing, but the production path is always through the
    /// factory + manager.
    /// </para>
    /// </remarks>
    public interface IDiPostSetupRunner
    {
        /// <summary>
        /// Runs every registered <see cref="IDiPostSetupConfigurator"/>
        /// whose <see cref="IDiPostSetupConfigurator.TargetManagerType"/>
        /// matches (via <see cref="System.Type.IsAssignableFrom"/>) the
        /// runtime type of <paramref name="manager"/>. Configurators run
        /// in registration order; the first exception aborts startup.
        /// </summary>
        /// <param name="manager">The active DI node manager.</param>
        /// <param name="cancellationToken">Cancellation token from the
        /// hosted service.</param>
        /// <exception cref="System.Exception">
        /// Re-thrown from any configurator that fails. Startup aborts.
        /// </exception>
        ValueTask RunAsync(DiNodeManager manager, CancellationToken cancellationToken);
    }
}
