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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Device Integration owner for the OPC 30010 AutoID Devices model.
    /// </summary>
    /// <remarks>
    /// The owner for a server whose only Device Integration based companion is AutoID: it loads DI
    /// and, through <see cref="AutoIdMachineryModelProvider"/>, AutoID, owns the
    /// <see cref="AutoIdRuntime"/> that creates the readers below the <c>DeviceSet</c>, and disposes
    /// it on shutdown. To combine AutoID with other companions, compose it into the Machinery owner
    /// instead (<c>AddAutoIdToMachinery</c>).
    /// </remarks>
    public sealed class AutoIdNodeManager : DiNodeManager
    {
        /// <summary>
        /// Creates a manager with default options.
        /// </summary>
        public AutoIdNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Creates a manager with explicit options.
        /// </summary>
        public AutoIdNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            AutoIdServerOptions options)
            : this(server, configuration, null, new OptionsWrapper<AutoIdServerOptions>(
                options ?? throw new ArgumentNullException(nameof(options))))
        {
        }

        /// <summary>
        /// Creates a manager participating in the DI post-setup pipeline.
        /// </summary>
        public AutoIdNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<AutoIdServerOptions>? options = null)
            : base(server, configuration, postSetupRunner, ManagerNamespaceUris(options?.Value))
        {
            Runtime = new AutoIdRuntime(this, options?.Value ?? new AutoIdServerOptions());
        }

        /// <summary>
        /// Gets the runtime that creates and owns the readers.
        /// </summary>
        public AutoIdRuntime Runtime { get; }

        /// <inheritdoc cref="AutoIdRuntime.Options"/>
        public AutoIdServerOptions Options => Runtime.Options;

        /// <inheritdoc cref="AutoIdRuntime.Readers"/>
        public ArrayOf<AutoIdReader> Readers => Runtime.Readers;

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // The stand-alone owner is a Device Integration owner that loads DI and then the same
            // AutoID model provider a composed Machinery owner uses.
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            new AutoIdMachineryModelProvider(Runtime.Options).AddPredefinedNodes(nodes, context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await Runtime.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gets the model and additional namespaces; DI is appended by the base manager.
        /// </summary>
        internal static string[] ManagerNamespaceUris(AutoIdServerOptions? options)
        {
            options ??= new AutoIdServerOptions();
            var uris = new List<string> { AutoIdModel.NamespaceUri };
            foreach (string uri in options.AdditionalNamespaceUris)
            {
                uris.Add(uri);
            }
            return [.. uris];
        }
    }
}
