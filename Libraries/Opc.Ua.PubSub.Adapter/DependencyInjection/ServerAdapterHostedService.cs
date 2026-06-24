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
using Microsoft.Extensions.Hosting;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Generic-host adapter that drives the
    /// <see cref="ServerAdapterRuntime"/> through the host lifetime. It
    /// depends on <see cref="IPubSubApplication"/> so resolving it forces the
    /// deferred adapter composition steps (which populate the runtime) to run
    /// before <see cref="StartAsync"/> starts the subscription coordinators. On
    /// stop the runtime is disposed, closing every external-server session.
    /// </summary>
    internal sealed class ServerAdapterHostedService : IHostedService
    {
        /// <summary>
        /// Initializes a new <see cref="ServerAdapterHostedService"/>.
        /// </summary>
        /// <param name="application">
        /// The PubSub application whose resolution forces the adapter
        /// composition steps to run.
        /// </param>
        /// <param name="runtime">
        /// The runtime owning the adapter sessions and coordinators.
        /// </param>
        public ServerAdapterHostedService(
            IPubSubApplication application,
            ServerAdapterRuntime runtime)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return m_runtime.StartAsync(cancellationToken).AsTask();
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return m_runtime.DisposeAsync().AsTask();
        }

        private readonly ServerAdapterRuntime m_runtime;
    }
}
