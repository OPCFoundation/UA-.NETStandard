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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Redundancy;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Redundancy.K8s
{
    internal sealed class KubernetesPeerDiscoveryStartupTask : IServerStartupTask, IAsyncDisposable
    {
        public KubernetesPeerDiscoveryStartupTask(
            IKubernetesPeerDiscovery discovery,
            KubernetesPeerDiscoveryOptions options)
        {
            m_discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            m_loop = Task.Run(() => RefreshLoopAsync(m_cts.Token), CancellationToken.None);
            return default;
        }

        public async ValueTask DisposeAsync()
        {
            m_cts.Cancel();
            if (m_loop != null)
            {
                try
                {
                    await m_loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }
            m_cts.Dispose();
        }

        private async Task RefreshLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await m_discovery.RefreshAsync(ct).ConfigureAwait(false);
                await Task.Delay(m_options.RefreshInterval, ct).ConfigureAwait(false);
            }
        }

        private readonly IKubernetesPeerDiscovery m_discovery;
        private readonly KubernetesPeerDiscoveryOptions m_options;
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_loop;
    }
}
