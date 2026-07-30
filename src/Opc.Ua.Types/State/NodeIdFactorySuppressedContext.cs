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

using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Forwards every member of a system context except its
    /// <see cref="ISystemContext.NodeIdFactory"/>, which is reported as absent.
    /// </summary>
    /// <remarks>
    /// A node copy materialises its children through
    /// <see cref="NodeState.CreateChild"/>, and the
    /// <c>CreateOrReplace&lt;Child&gt;</c> plumbing behind it assigns a
    /// per-instance NodeId whenever the context carries a factory. In a copy
    /// every child is initialised from its source immediately afterwards, which
    /// overwrites that NodeId, so the assignment only consumes identifiers -
    /// and permanently leaks them for factories that track outstanding
    /// allocations. Hiding the factory for the duration of the copy leaves
    /// those identifiers unused; the <c>assignInstanceNodeIds</c> flag cannot
    /// serve here because it is not part of the virtual <c>FindChild</c>
    /// contract the copy goes through.
    /// </remarks>
    internal sealed class NodeIdFactorySuppressedContext : ISystemContext
    {
        /// <summary>
        /// Wraps the supplied context.
        /// </summary>
        /// <param name="context">The context to forward to.</param>
        public NodeIdFactorySuppressedContext(ISystemContext context)
        {
            m_context = context;
        }

        /// <inheritdoc/>
        public object? SystemHandle => m_context.SystemHandle;

        /// <inheritdoc/>
        public string? UserId => m_context.UserId;

        /// <inheritdoc/>
        public ArrayOf<string> PreferredLocales => m_context.PreferredLocales;

        /// <inheritdoc/>
        public string? AuditEntryId => m_context.AuditEntryId;

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_context.NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris => m_context.ServerUris;

        /// <inheritdoc/>
        public ITypeTable TypeTable => m_context.TypeTable;

        /// <inheritdoc/>
        public IEncodeableFactory EncodeableFactory => m_context.EncodeableFactory;

        /// <inheritdoc/>
        public INodeIdFactory? NodeIdFactory => null;

        /// <inheritdoc/>
        public NodeStateFactory NodeStateFactory => m_context.NodeStateFactory;

        /// <inheritdoc/>
        public ITelemetryContext Telemetry => m_context.Telemetry;

        private readonly ISystemContext m_context;
    }
}
