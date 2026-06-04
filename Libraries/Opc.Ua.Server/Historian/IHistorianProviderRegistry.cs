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

using System.Collections.Generic;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Server-wide registry of <see cref="IHistorianProvider"/>
    /// instances. Resolved on each HistoryRead / HistoryUpdate operation
    /// after the per-NodeManager <c>GetHistorianProvider</c> override
    /// returns <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry supports three registration scopes, in resolution
    /// precedence order:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <c>RegisterForNode(NodeId, provider)</c> — exact-NodeId binding
    /// (highest precedence). Used by the fluent <c>HistorianBuilder</c>
    /// when the user explicitly registers a single variable.
    /// </item>
    /// <item>
    /// <c>RegisterForNamespace(uri, provider)</c> — binds the provider to
    /// every historizing node whose NodeId namespace matches.
    /// </item>
    /// <item>
    /// <c>RegisterDefault(provider)</c> — fallback provider used when no
    /// more specific binding matches.
    /// </item>
    /// </list>
    /// <para>
    /// Resolution returns the first match; the registry never merges
    /// providers. The first NodeId-scoped registration for a given node
    /// wins (later registrations replace it). Namespace registrations
    /// replace each other in the same way.
    /// </para>
    /// </remarks>
    public interface IHistorianProviderRegistry
    {
        /// <summary>
        /// Adds a NodeId-scoped binding.
        /// </summary>
        void RegisterForNode(NodeId nodeId, IHistorianProvider provider);

        /// <summary>
        /// Adds a namespace-scoped binding.
        /// </summary>
        void RegisterForNamespace(string namespaceUri, IHistorianProvider provider);

        /// <summary>
        /// Sets the default fallback provider.
        /// </summary>
        void RegisterDefault(IHistorianProvider provider);

        /// <summary>
        /// Removes the NodeId-scoped binding (no-op when absent).
        /// </summary>
        bool UnregisterForNode(NodeId nodeId);

        /// <summary>
        /// Removes the namespace-scoped binding (no-op when absent).
        /// </summary>
        bool UnregisterForNamespace(string namespaceUri);

        /// <summary>
        /// Clears the default provider.
        /// </summary>
        void ClearDefault();

        /// <summary>
        /// Resolves the provider for a node. Returns <c>null</c> when no
        /// binding matches.
        /// </summary>
        IHistorianProvider? Resolve(NodeId nodeId);

        /// <summary>
        /// Returns a snapshot of every registered provider — used by the
        /// capability roll-up to compute the server-wide
        /// <c>HistoryServerCapabilities</c> flags.
        /// </summary>
        IReadOnlyCollection<IHistorianProvider> Providers { get; }
    }

    /// <summary>
    /// Optional opt-in interface that hosts a server's
    /// <see cref="IHistorianProviderRegistry"/>. <c>ServerInternalData</c>
    /// implements this so node managers can resolve the registry without
    /// any <see cref="IServerInternal"/> surface change; external/mocked
    /// <see cref="IServerInternal"/> implementations remain unaffected.
    /// </summary>
    public interface IHistorianRegistryProvider
    {
        /// <summary>
        /// Gets the server's historian provider registry; never <c>null</c>.
        /// </summary>
        IHistorianProviderRegistry HistorianRegistry { get; }
    }
}
