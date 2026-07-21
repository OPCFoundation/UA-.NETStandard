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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The production <see cref="IWotProjectionHost"/> that projects WoT closures
    /// onto the live server through the public NodeManager lifecycle. First
    /// activation uses
    /// <see cref="RuntimeNodeSetLifecycleExtensions.AddRuntimeNodeSetAsync"/>;
    /// updates use
    /// <see cref="RuntimeNodeSetLifecycleExtensions.ShadowReloadRuntimeNodeSetAsync"/>,
    /// so the previous generation keeps serving its existing monitored items
    /// until they drain. The stable WoT registry NodeManager is never touched.
    /// </summary>
    public sealed class LifecycleWotProjectionHost : IWotProjectionHost
    {
        /// <summary>Initializes a new host over the supplied lifecycle.</summary>
        public LifecycleWotProjectionHost(INodeManagerLifecycle lifecycle)
        {
            m_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        }

        /// <inheritdoc/>
        public async ValueTask<WotProjectionHandle> AddAsync(
            WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            RuntimeNodeSetOptions options = BuildOptions(document);
            NodeManagerRegistration registration = await m_lifecycle
                .AddRuntimeNodeSetAsync(options, cancellationToken)
                .ConfigureAwait(false);
            return new WotProjectionHandle(
                document.ClosureKey,
                registration.Generation,
                registration,
                ImmutableArray<NodeId>.Empty,
                0);
        }

        /// <inheritdoc/>
        public async ValueTask<WotProjectionHandle> ShadowReloadAsync(
            WotProjectionHandle current,
            WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            if (current?.Registration is not NodeManagerRegistration registration)
            {
                // No live registration to reload; fall back to a fresh add.
                return await AddAsync(document, cancellationToken).ConfigureAwait(false);
            }
            RuntimeNodeSetOptions options = BuildOptions(document);
            NodeManagerRegistration next = await m_lifecycle
                .ShadowReloadRuntimeNodeSetAsync(registration, options, cancellationToken)
                .ConfigureAwait(false);
            return new WotProjectionHandle(
                document.ClosureKey,
                next.Generation,
                next,
                ImmutableArray<NodeId>.Empty,
                0);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            WotProjectionHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (handle?.Registration is NodeManagerRegistration registration)
            {
                await m_lifecycle.RemoveAsync(registration, cancellationToken).ConfigureAwait(false);
            }
        }

        private static RuntimeNodeSetOptions BuildOptions(WotProjectionDocument document)
        {
            var sources = new RuntimeNodeSetSource[document.Sources.Length];
            for (int i = 0; i < document.Sources.Length; i++)
            {
                WotProjectionSource source = document.Sources[i];
                byte[] xml = source.NodeSetXml;
                var uris = new ArrayOf<string>(source.ModelNamespaceUris.ToArray());
                sources[i] = RuntimeNodeSetSource.FromStream(
                    source.Name,
                    _ => new ValueTask<Stream>(new MemoryStream(xml, writable: false)),
                    uris);
            }
            return new RuntimeNodeSetOptions
            {
                Sources = new ArrayOf<RuntimeNodeSetSource>(sources)
            };
        }

        private readonly INodeManagerLifecycle m_lifecycle;
    }
}
