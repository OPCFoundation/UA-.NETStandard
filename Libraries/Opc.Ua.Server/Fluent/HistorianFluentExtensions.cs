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
using System.Runtime.CompilerServices;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Bridges <see cref="INodeManagerBuilder"/> /
    /// <see cref="IVariableBuilder{TValue}"/> with the Part 11 historian
    /// surface so historization can be wired in the same fluent chain as
    /// <c>OnRead</c>/<c>OnWrite</c>/<c>Publish</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Caching.</strong> Per-<see cref="INodeManagerBuilder"/>
    /// state is held in a <see cref="ConditionalWeakTable{TKey,TValue}"/>
    /// keyed by the builder instance, so a single
    /// <c>UseHistorian().UseInMemory().RegisterAsDefault()</c> at the
    /// start of <c>Configure</c> is reused by every later
    /// <c>Historize()</c> call on the same builder. The table is
    /// weak-keyed; entries vanish when the user's
    /// <c>Configure</c> delegate completes and the builder is dropped.
    /// </para>
    /// <para>
    /// <strong>"Just works" path.</strong> A bare
    /// <c>Variable&lt;double&gt;("X").Historize()</c> — without a prior
    /// <c>UseHistorian()</c> call and without a per-call
    /// <c>provider</c> argument — lazily creates an
    /// <see cref="InMemoryHistorianProvider"/> and registers it as the
    /// server-wide default. Subsequent <c>Historize()</c> calls reuse it.
    /// </para>
    /// <para>
    /// <strong>Dispatch.</strong> The extensions register variables
    /// against the server-wide <see cref="IHistorianProviderRegistry"/>;
    /// no <c>GetHistorianProvider</c> override is required on the node
    /// manager because <c>HistorianDispatcher.ResolveProvider</c> falls
    /// through from the per-manager override to the registry.
    /// </para>
    /// </remarks>
    public static class HistorianFluentExtensions
    {
        /// <summary>
        /// Returns the <see cref="HistorianBuilder"/> associated with this
        /// <see cref="INodeManagerBuilder"/>. The first call creates and
        /// caches a new builder bound to the manager's server; subsequent
        /// calls on the same <paramref name="builder"/> return the same
        /// instance so the fluent chain can stack
        /// <c>UseInMemory()</c>, <c>UseProvider(...)</c>,
        /// <c>RegisterAsDefault()</c>, <c>RegisterForNamespace(...)</c>,
        /// and <c>RegisterForNode(...)</c> calls in the usual way.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Raised when <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Raised when the underlying server does not implement
        /// <see cref="IHistorianRegistryProvider"/> (i.e., when the host
        /// is not the standard <c>ServerInternalData</c>).
        /// </exception>
        public static HistorianBuilder UseHistorian(this INodeManagerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return GetOrCreateBuilder(builder);
        }

        /// <summary>
        /// Marks the wrapped variable as historizing and registers it
        /// with the per-manager <see cref="HistorianBuilder"/>. When no
        /// builder has been bound yet (and no <paramref name="provider"/>
        /// is supplied) the call lazily creates an
        /// <see cref="InMemoryHistorianProvider"/> and installs it as the
        /// server-wide default.
        /// </summary>
        /// <typeparam name="TValue">CLR value type carried by the variable.</typeparam>
        /// <param name="variable">The variable builder to historize.</param>
        /// <param name="historyAccessLevel">
        /// Access-level bits OR-ed onto <c>AccessLevel</c> and
        /// <c>UserAccessLevel</c>. Defaults to
        /// <c>HistoryRead | HistoryWrite</c>.
        /// </param>
        /// <param name="installConfigurationOnBrowse">
        /// When <c>true</c>, attaches an
        /// <see cref="NodeState.OnPopulateBrowser"/> handler that installs
        /// the <c>HistoricalDataConfigurationType</c> companion lazily on
        /// the first browse against the variable.
        /// </param>
        /// <param name="capabilities">
        /// Optional per-node capability set passed to
        /// <c>HistorianBuilder.Historize</c>. Defaults to the underlying
        /// provider's default (typically <c>ReadWrite</c> for the
        /// in-memory engine).
        /// </param>
        /// <param name="provider">
        /// Optional per-call override. When supplied, the call binds the
        /// variable to <paramref name="provider"/> exclusively via
        /// <c>RegisterForNode</c>; the cached per-manager builder is left
        /// untouched.
        /// </param>
        public static IVariableBuilder<TValue> Historize<TValue>(
            this IVariableBuilder<TValue> variable,
            byte historyAccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
            bool installConfigurationOnBrowse = false,
            HistorianNodeCapabilities? capabilities = null,
            IHistorianProvider? provider = null)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            ApplyHistorization(
                variable.Builder,
                variable.Node,
                historyAccessLevel,
                installConfigurationOnBrowse,
                capabilities,
                provider);
            return variable;
        }

        /// <summary>
        /// Untyped overload of
        /// <see cref="Historize{TValue}(IVariableBuilder{TValue}, byte, bool, HistorianNodeCapabilities, IHistorianProvider)"/>
        /// for callers that already hold an
        /// <see cref="INodeBuilder{TState}"/> view of a
        /// <see cref="BaseVariableState"/>.
        /// </summary>
        public static INodeBuilder<BaseVariableState> Historize(
            this INodeBuilder<BaseVariableState> variable,
            byte historyAccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
            bool installConfigurationOnBrowse = false,
            HistorianNodeCapabilities? capabilities = null,
            IHistorianProvider? provider = null)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            ApplyHistorization(
                variable.Builder,
                variable.Node,
                historyAccessLevel,
                installConfigurationOnBrowse,
                capabilities,
                provider);
            return variable;
        }

        /// <summary>
        /// Binds <paramref name="provider"/> to the wrapped variable's
        /// <c>NodeId</c> in the server-wide registry. The next
        /// <see cref="Historize{TValue}(IVariableBuilder{TValue}, byte, bool, HistorianNodeCapabilities, IHistorianProvider)"/>
        /// call without an explicit <c>provider</c> argument will dispatch
        /// through this binding instead of the per-manager default.
        /// </summary>
        /// <remarks>
        /// Useful when a single node manager spans many historized
        /// variables that each live in a different time-series store
        /// (e.g., one provider per signal vendor).
        /// </remarks>
        public static IVariableBuilder<TValue> WithHistorian<TValue>(
            this IVariableBuilder<TValue> variable,
            IHistorianProvider provider)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            IServerInternal server = GetServer(variable.Builder);
            IHistorianRegistryProvider registryHost = RequireRegistryHost(server);
            registryHost.HistorianRegistry.RegisterForNode(variable.Node.NodeId, provider);
            return variable;
        }

        private static void ApplyHistorization(
            INodeManagerBuilder nodeManagerBuilder,
            BaseVariableState variable,
            byte historyAccessLevel,
            bool installConfigurationOnBrowse,
            HistorianNodeCapabilities? capabilities,
            IHistorianProvider? perCallProvider)
        {
            if (perCallProvider != null)
            {
                // Per-call override path — bypass the cached per-manager
                // builder so the override doesn't leak into other variables.
                IServerInternal serverForBinding = GetServer(nodeManagerBuilder);
                IHistorianRegistryProvider registryHost = RequireRegistryHost(serverForBinding);

                variable.Historizing = true;
                if (historyAccessLevel != 0)
                {
                    variable.AccessLevel = (byte)(variable.AccessLevel | historyAccessLevel);
                    variable.UserAccessLevel = (byte)(variable.UserAccessLevel | historyAccessLevel);
                }
                RegisterVariableOnProvider(perCallProvider, variable.NodeId, capabilities);
                registryHost.HistorianRegistry.RegisterForNode(variable.NodeId, perCallProvider);

                if (installConfigurationOnBrowse)
                {
                    // Reuse the lazy-install hook on the cached per-manager
                    // builder (creating one if necessary) — but bind it to
                    // the per-call provider, not the manager default.
                    HistorianBuilder manager = GetOrCreateBuilder(nodeManagerBuilder);
                    HistorianBuilder scope = manager.Provider != null ? manager : manager.UseProvider(perCallProvider);
                    scope.Historize(
                        variable,
                        historyAccessLevel: 0,
                        setHistorizing: false,
                        installConfigurationNode: false,
                        installConfigurationOnBrowse: true,
                        systemContext: nodeManagerBuilder.Context,
                        capabilities: capabilities);
                }
                return;
            }

            // Default path — share the per-manager HistorianBuilder.
            HistorianBuilder managerBuilder = GetOrCreateBuilder(nodeManagerBuilder);
            if (managerBuilder.Provider == null)
            {
                // Lazy default — install an in-memory engine and make it
                // the server-wide default so dispatch picks it up.
                managerBuilder.UseInMemory();
                managerBuilder.RegisterAsDefault();
            }

            managerBuilder.Historize(
                variable,
                historyAccessLevel: historyAccessLevel,
                setHistorizing: true,
                installConfigurationNode: false,
                installConfigurationOnBrowse: installConfigurationOnBrowse,
                systemContext: nodeManagerBuilder.Context,
                capabilities: capabilities);
        }

        private static void RegisterVariableOnProvider(
            IHistorianProvider provider,
            NodeId nodeId,
            HistorianNodeCapabilities? capabilities)
        {
            // The bundled InMemory engine exposes a typed Register(NodeId, caps);
            // other providers are responsible for their own internal mapping
            // when they observe HistoryRead/HistoryUpdate for the node.
            if (provider is InMemoryHistorianProvider inMemory)
            {
                inMemory.Register(nodeId, capabilities);
            }
        }

        private static HistorianBuilder GetOrCreateBuilder(INodeManagerBuilder builder)
        {
            return s_builders.GetValue(
                builder,
                static b =>
                {
                    IServerInternal server = GetServer(b);
                    return new HistorianBuilder(server);
                });
        }

        private static IServerInternal GetServer(INodeManagerBuilder builder)
        {
            if (builder.Context is ServerSystemContext serverContext && serverContext.Server != null)
            {
                return serverContext.Server;
            }
            throw new InvalidOperationException(
                "INodeManagerBuilder.Context does not expose an IServerInternal — " +
                "the fluent historian extensions require the standard ServerSystemContext host.");
        }

        private static IHistorianRegistryProvider RequireRegistryHost(IServerInternal server)
        {
            if (server is IHistorianRegistryProvider registryHost)
            {
                return registryHost;
            }
            throw new InvalidOperationException(
                "The server does not implement IHistorianRegistryProvider; " +
                "the fluent historian extensions require the standard ServerInternalData host.");
        }

        private static readonly ConditionalWeakTable<INodeManagerBuilder, HistorianBuilder> s_builders
            = new();
    }
}
