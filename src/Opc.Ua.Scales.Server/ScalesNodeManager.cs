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
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.PackML;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Standalone Device Integration owner for the OPC 40200 Weighing Technology model.
    /// </summary>
    /// <remarks>
    /// Loads DI, IA, Machinery, PackML and Scales in dependency order. Construction, runtime
    /// controllers and lifetime are shared with composed hosts through <see cref="ScalesRuntime"/>.
    /// </remarks>
    public sealed class ScalesNodeManager : DiNodeManager
    {
        /// <summary>
        /// Creates a standalone manager with default options.
        /// </summary>
        public ScalesNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Creates a standalone manager with explicit options.
        /// </summary>
        public ScalesNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ScalesServerOptions options)
            : this(server, configuration, null, new OptionsWrapper<ScalesServerOptions>(
                options ?? throw new ArgumentNullException(nameof(options))))
        {
        }

        /// <summary>
        /// Creates a manager participating in the DI post-setup pipeline.
        /// </summary>
        public ScalesNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<ScalesServerOptions>? options = null)
            : this(server, configuration, postSetupRunner, options, default)
        {
        }

        /// <summary>
        /// Creates a Scales owner with additional DI companion models.
        /// </summary>
        public ScalesNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<ScalesServerOptions>? options,
            ArrayOf<IDiModelProvider> providers)
            : base(server, configuration, postSetupRunner, providers, ManagerNamespaceUris(options?.Value))
        {
            Runtime = new ScalesRuntime(this, options?.Value ?? new ScalesServerOptions());
            RegisterConformanceContributor(Runtime);
        }

        /// <summary>
        /// Gets the shared runtime for all scales and systems of this owner.
        /// </summary>
        public ScalesRuntime Runtime { get; }

        /// <inheritdoc cref="ScalesRuntime.Options"/>
        public ScalesServerOptions Options => Runtime.Options;

        /// <inheritdoc cref="ScalesRuntime.Scales"/>
        public ArrayOf<ScaleHandle> Scales => Runtime.Scales;

        /// <inheritdoc cref="ScalesRuntime.ScaleSystems"/>
        public ArrayOf<ScaleSystemHandle> ScaleSystems => Runtime.ScaleSystems;

        /// <inheritdoc cref="ScalesRuntime.LockService"/>
        public ILockService LockService => Runtime.LockService;

        /// <inheritdoc cref="ScalesRuntime.NamespaceIndices"/>
        public ScaleNamespaceIndices NamespaceIndices => Runtime.NamespaceIndices;

        /// <inheritdoc/>
        public override ArrayOf<string> ServerProfiles => base.ServerProfiles;

        /// <inheritdoc/>
        public override ArrayOf<QualifiedName> ConformanceUnits => base.ConformanceUnits;

        /// <inheritdoc cref="ScalesRuntime.PublishedForTest"/>
        internal Func<BaseObjectState, Task>? PublishedForTest
        {
            get => Runtime.PublishedForTest;
            set => Runtime.PublishedForTest = value;
        }

        /// <inheritdoc cref="ScalesRuntime.CreateScaleAsync(
        /// QualifiedName, ScaleKind, Action{IScaleBuilder}, CancellationToken)"/>
        public ValueTask<ScaleHandle> CreateScaleAsync(
            QualifiedName browseName,
            ScaleKind kind,
            Action<IScaleBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            return Runtime.CreateScaleAsync(browseName, kind, configure, cancellationToken);
        }

        /// <inheritdoc cref="ScalesRuntime.CreateScaleAsync(
        /// QualifiedName, ScaleKind, Action{IScaleBuilder}, NodeState?, CancellationToken)"/>
        public ValueTask<ScaleHandle> CreateScaleAsync(
            QualifiedName browseName,
            ScaleKind kind,
            Action<IScaleBuilder> configure,
            NodeState? parent,
            CancellationToken cancellationToken = default)
        {
            return Runtime.CreateScaleAsync(browseName, kind, configure, parent, cancellationToken);
        }

        /// <inheritdoc cref="ScalesRuntime.CreateScaleSystemAsync"/>
        public ValueTask<ScaleSystemHandle> CreateScaleSystemAsync(
            QualifiedName browseName,
            Action<IScaleSystemBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            return Runtime.CreateScaleSystemAsync(browseName, configure, cancellationToken);
        }

        /// <inheritdoc cref="ScalesRuntime.FindScale"/>
        public ScaleHandle? FindScale(NodeId nodeId)
        {
            return Runtime.FindScale(nodeId);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaPackML(context);
            nodes.AddOpcUaScales(context);
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
        private static string[] ManagerNamespaceUris(ScalesServerOptions? options)
        {
            options ??= new ScalesServerOptions();
            var uris = new List<string>
            {
                Namespaces.Scales,
                PackML.Namespaces.PackML,
                Machinery.Namespaces.Machinery,
                IA.Namespaces.IA
            };
            foreach (string uri in options.AdditionalNamespaceUris)
            {
                uris.Add(uri);
            }
            return [.. uris];
        }
    }
}
