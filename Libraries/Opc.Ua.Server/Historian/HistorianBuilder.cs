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
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Fluent helper for wiring an <see cref="IHistorianProvider"/> into
    /// the server. Sample usage:
    /// </summary>
    /// <example>
    /// <code>
    /// server
    ///     .UseHistorian()
    ///     .UseInMemory()
    ///     .Historize(variableNodeId, AccessLevels.HistoryRead | AccessLevels.HistoryWrite)
    ///     .RegisterAsDefault();
    /// </code>
    /// </example>
    public sealed class HistorianBuilder : IAsyncDisposable
    {
        /// <summary>
        /// Creates a new builder bound to the supplied server.
        /// </summary>
        public HistorianBuilder(IServerInternal server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            if (server is not IHistorianRegistryProvider)
            {
                throw new InvalidOperationException(
                    "The supplied IServerInternal does not implement IHistorianRegistryProvider; " +
                    "the fluent HistorianBuilder requires the standard ServerInternalData host.");
            }
        }

        /// <summary>
        /// The historian provider currently bound to the builder. Set by
        /// <see cref="UseInMemory"/> or <see cref="UseProvider(IHistorianProvider)"/>.
        /// </summary>
        public IHistorianProvider? Provider { get; private set; }

        /// <summary>
        /// Configures the builder to use the bundled
        /// <see cref="InMemoryHistorianProvider"/>. The returned provider
        /// is the same instance that becomes <see cref="Provider"/>; the
        /// caller may keep the reference to register more variables later.
        /// </summary>
        public InMemoryHistorianProvider UseInMemory(InMemoryHistorianOptions? options = null)
        {
            InMemoryHistorianProvider provider = options is null
                ? new InMemoryHistorianProvider()
                : new InMemoryHistorianProvider(options);
            Provider = provider;
            return provider;
        }

        /// <summary>
        /// Configures the builder to use the supplied provider.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <c>null</c>.</exception>
        public HistorianBuilder UseProvider(IHistorianProvider provider)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        /// <summary>
        /// Marks <paramref name="variable"/> as historizing and registers
        /// it with the in-memory provider when one is bound. Optionally
        /// sets the <c>HistoryRead</c> / <c>HistoryWrite</c> access level
        /// bits and the <see cref="BaseVariableState.Historizing"/> flag.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Automatic value capture (opt-out).</strong> When
        /// <paramref name="autoCapture"/> is <c>true</c> (the default),
        /// the builder attaches a handler to
        /// <see cref="NodeState.StateChanged"/> that forwards every
        /// value-mask change on the variable to a per-builder
        /// <see cref="HistorianCaptureSink"/>. The sink batches samples
        /// across all historized variables that share this builder and
        /// flushes them to <see cref="Provider"/> via
        /// <see cref="IHistorianBulkInsertProvider"/> (when supported)
        /// or per-node <see cref="IHistorianDataProvider.InsertAsync"/>.
        /// Pass <paramref name="autoCapture"/> = <c>false</c> to disable
        /// — only explicit HistoryUpdate Insert calls will populate the
        /// archive in that case.
        /// </para>
        /// <para>
        /// Auto-capture requires <paramref name="systemContext"/>
        /// because the sink stores it once at construction. When
        /// <paramref name="autoCapture"/> is <c>true</c> and
        /// <paramref name="systemContext"/> is <c>null</c>, the call
        /// throws.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="variable"/> is <c>null</c>.</exception>
        public HistorianBuilder Historize(
            BaseVariableState variable,
            byte historyAccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
            bool setHistorizing = true,
            bool installConfigurationNode = false,
            bool installConfigurationOnBrowse = false,
            ISystemContext? systemContext = null,
            HistorianNodeCapabilities? capabilities = null,
            bool autoCapture = true,
            HistorianCaptureOptions? captureOptions = null)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }

            if (setHistorizing)
            {
                variable.Historizing = true;
            }
            if (historyAccessLevel != 0)
            {
                variable.AccessLevel = (byte)(variable.AccessLevel | historyAccessLevel);
                variable.UserAccessLevel = (byte)(variable.UserAccessLevel | historyAccessLevel);
            }

            if (Provider is InMemoryHistorianProvider inMemory)
            {
                inMemory.Register(variable.NodeId, capabilities);
            }

            if (systemContext != null)
            {
                EnsureAnnotationsProperty(systemContext, variable, capabilities, historyAccessLevel);
            }

            if (installConfigurationNode && Provider != null && systemContext != null)
            {
                _ = HistoricalDataConfigurationInstaller
                    .EnsureInstalledAsync(systemContext, variable, Provider, default)
                    .AsTask().GetAwaiter().GetResult();
            }
            else if (installConfigurationOnBrowse && Provider != null)
            {
                AttachConfigurationLazyInstaller(variable, Provider);
            }

            if (autoCapture)
            {
                AttachAutoCapture(variable, systemContext, captureOptions);
            }
            return this;
        }

        /// <summary>
        /// Attaches a <see cref="NodeState.StateChanged"/> handler that
        /// pushes every value-mask change into the per-builder
        /// <see cref="HistorianCaptureSink"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no historian provider has been bound, or when <paramref name="systemContext"/>
        /// is not a <see cref="ServerSystemContext"/>.
        /// </exception>
        private void AttachAutoCapture(
            BaseVariableState variable,
            ISystemContext? systemContext,
            HistorianCaptureOptions? captureOptions)
        {
            if (Provider == null)
            {
                throw new InvalidOperationException(
                    "Cannot enable auto-capture: no historian provider has been bound. " +
                    "Call UseInMemory() or UseProvider() before Historize(autoCapture: true).");
            }
            if (systemContext is not ServerSystemContext serverContext)
            {
                throw new InvalidOperationException(
                    "Cannot enable auto-capture: a ServerSystemContext must be supplied via " +
                    "the systemContext argument. Pass the manager's SystemContext or call " +
                    "Historize(autoCapture: false) to disable.");
            }

            HistorianCaptureSink sink = GetOrCreateCaptureSink(serverContext, captureOptions);
            // The handler is a per-instance closure so the same NodeId
            // routes to its sink across multiple captures; detach when
            // the builder is disposed.
            void handler(ISystemContext ctx, NodeState node, NodeStateChangeMasks masks)
            {
                if ((masks & NodeStateChangeMasks.Value) == 0)
                {
                    return;
                }
                if (node is not BaseVariableState v)
                {
                    return;
                }
                sink.Enqueue(v.NodeId, new DataValue(
                    v.WrappedValue,
                    v.StatusCode,
                    sourceTimestamp: v.Timestamp,
                    serverTimestamp: DateTime.UtcNow));
            }
            variable.StateChanged += handler;
            lock (m_captureSinkLock)
            {
                m_captureHandlers.Add(new CaptureHandlerRegistration(variable, handler));
            }
        }

        private HistorianCaptureSink GetOrCreateCaptureSink(
            ServerSystemContext serverContext,
            HistorianCaptureOptions? options)
        {
            if (m_captureSink != null)
            {
                return m_captureSink;
            }
            lock (m_captureSinkLock)
            {
                m_captureSink ??= new HistorianCaptureSink(Provider!, serverContext, options);
                return m_captureSink;
            }
        }

        /// <summary>
        /// Flushes pending auto-captured samples and tears down the
        /// per-builder <see cref="HistorianCaptureSink"/>, detaching the
        /// <see cref="NodeState.StateChanged"/> handlers it installed.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            CaptureHandlerRegistration[] handlers;
            lock (m_captureSinkLock)
            {
                handlers = [.. m_captureHandlers];
                m_captureHandlers.Clear();
            }
            // Detach handlers first so no new samples enqueue while we drain.
            foreach (CaptureHandlerRegistration reg in handlers)
            {
                try
                {
                    reg.Variable.StateChanged -= reg.Handler;
                }
                catch (Exception)
                {
                    // ignore — variable may already be torn down
                }
            }
            HistorianCaptureSink? sink;
            lock (m_captureSinkLock)
            {
                sink = m_captureSink;
                m_captureSink = null;
            }
            if (sink != null)
            {
                await sink.DisposeAsync().ConfigureAwait(false);
            }
        }

        private readonly record struct CaptureHandlerRegistration(BaseVariableState Variable, NodeStateChangedHandler Handler);
        private readonly List<CaptureHandlerRegistration> m_captureHandlers = [];
        private HistorianCaptureSink? m_captureSink;
        private readonly object m_captureSinkLock = new();

        /// <summary>
        /// Hooks <see cref="NodeState.OnPopulateBrowser"/> on
        /// <paramref name="variable"/> so the first browse call against
        /// the variable installs (synchronously) the
        /// <c>HistoricalDataConfigurationType</c> companion object.
        /// The handler self-detaches after the install runs.
        /// </summary>
        private static void AttachConfigurationLazyInstaller(
            BaseVariableState variable,
            IHistorianProvider provider)
        {
            void handler(ISystemContext context, NodeState node, NodeBrowser browser)
            {
                try
                {
                    _ = HistoricalDataConfigurationInstaller
                        .EnsureInstalledAsync(context, variable, provider, default)
                        .AsTask().GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // Best-effort install: never break Browse on a config-install failure.
                    // TODO(historian): plumb shared telemetry to log this.
                }
                finally
                {
                    // Self-detach so subsequent browses don't repeat the work.
                    variable.OnPopulateBrowser -= handler!;
                }
            }

            variable.OnPopulateBrowser += handler;
        }

        /// <summary>
        /// Ensures that <paramref name="variable"/> has an <c>Annotations</c>
        /// property when the provider's per-node capabilities advertise
        /// annotation support. The property is created if missing and its
        /// access-level bits are set to the same value as the variable's
        /// <c>HistoryRead</c> / <c>HistoryWrite</c> access.
        /// </summary>
        private static void EnsureAnnotationsProperty(
            ISystemContext systemContext,
            BaseVariableState variable,
            HistorianNodeCapabilities? capabilities,
            byte historyAccessLevel)
        {
            var browseName = new QualifiedName(BrowseNames.Annotations);
            BaseInstanceState? existing = variable.FindChild(systemContext, browseName);

            if (existing == null)
            {
                // Only auto-create when the caller explicitly wants annotation support.
                if (capabilities == null || !capabilities.InsertAnnotation)
                {
                    return;
                }

                var annotations = new PropertyState(variable)
                {
                    SymbolicName = BrowseNames.Annotations,
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.Annotation,
                    ValueRank = ValueRanks.OneDimension,
                    BrowseName = browseName,
                    DisplayName = LocalizedText.From(BrowseNames.Annotations),
                    AccessLevel = historyAccessLevel,
                    UserAccessLevel = historyAccessLevel
                };
                annotations.NodeId = annotations.NodeId.IsNull
                    ? new NodeId(variable.NodeId + "/Annotations", variable.NodeId.NamespaceIndex)
                    : annotations.NodeId;
                variable.AddChild(annotations);
                variable.AddReference(ReferenceTypeIds.HasProperty, false, annotations.NodeId);
                annotations.AddReference(ReferenceTypeIds.HasProperty, true, variable.NodeId);
                return;
            }

            if (existing is BaseVariableState annotationsVar && historyAccessLevel != 0)
            {
                annotationsVar.AccessLevel = (byte)(annotationsVar.AccessLevel | historyAccessLevel);
                annotationsVar.UserAccessLevel = (byte)(annotationsVar.UserAccessLevel | historyAccessLevel);
            }
        }

        /// <summary>
        /// Registers <see cref="Provider"/> as the server's default
        /// historian fallback (used for any historizing node that is not
        /// covered by a NodeId-scoped or namespace-scoped binding).
        /// </summary>
        public HistorianBuilder RegisterAsDefault()
        {
            EnsureProvider();
            Registry.RegisterDefault(Provider!);
            return this;
        }

        /// <summary>
        /// Registers <see cref="Provider"/> for every historizing node
        /// whose NodeId namespace matches <paramref name="namespaceUri"/>.
        /// </summary>
        public HistorianBuilder RegisterForNamespace(string namespaceUri)
        {
            EnsureProvider();
            Registry.RegisterForNamespace(namespaceUri, Provider!);
            return this;
        }

        /// <summary>
        /// Registers <see cref="Provider"/> specifically for
        /// <paramref name="nodeId"/>.
        /// </summary>
        public HistorianBuilder RegisterForNode(NodeId nodeId)
        {
            EnsureProvider();
            Registry.RegisterForNode(nodeId, Provider!);
            return this;
        }

        private void EnsureProvider()
        {
            if (Provider == null)
            {
                throw new InvalidOperationException(
                    "No historian provider has been bound. Call UseInMemory() or UseProvider() first.");
            }
        }

        private IHistorianProviderRegistry Registry
            => ((IHistorianRegistryProvider)m_server).HistorianRegistry;

        private readonly IServerInternal m_server;
    }

    /// <summary>
    /// Extension helpers that root a <see cref="HistorianBuilder"/> on
    /// an <see cref="IServerInternal"/>.
    /// </summary>
    public static class HistorianBuilderExtensions
    {
        /// <summary>Starts a fluent registration chain for the historian.</summary>
        public static HistorianBuilder UseHistorian(this IServerInternal server)
        {
            return new HistorianBuilder(server);
        }
    }
}
