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
    public sealed class HistorianBuilder
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
            var provider = options is null
                ? new InMemoryHistorianProvider()
                : new InMemoryHistorianProvider(options);
            Provider = provider;
            return provider;
        }

        /// <summary>
        /// Configures the builder to use the supplied provider.
        /// </summary>
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
        public HistorianBuilder Historize(
            BaseVariableState variable,
            byte historyAccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
            bool setHistorizing = true,
            bool installConfigurationNode = false,
            bool installConfigurationOnBrowse = false,
            ISystemContext? systemContext = null,
            HistorianNodeCapabilities? capabilities = null)
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
            return this;
        }

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
            NodeStatePopulateBrowserEventHandler? handler = null;
            handler = (context, node, browser) =>
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
            };
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
                    UserAccessLevel = historyAccessLevel,
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
