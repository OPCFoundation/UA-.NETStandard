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
 *
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

using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The alias policy the WoT Binding conversion applies: the standard
    /// base-namespace names a converted NodeSet2 document writes, and the
    /// identifiers they stand for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A conversion from a WoT document states a ReferenceType or a DataType
    /// of the base namespace by the name OPC 10000-5 gives it -
    /// <c>HasComponent</c>, <c>Double</c> - because that is what the WoT
    /// Binding's mappings are written in and what a restored document has to
    /// read as. Two things then need to know what those names stand for: the
    /// pass that declares them in the document's <c>&lt;Aliases&gt;</c> table
    /// so a Server can load it, and the comparison that asks whether a
    /// restored document says what the source said. Stating the policy once,
    /// here, is what keeps the two answering the same question.
    /// </para>
    /// <para>
    /// The names themselves are not repeated: they are the ones NodeSet2
    /// already knows, so this only says that the WoT Binding writes them. A
    /// name outside that set - a vendor alias a source document declared for
    /// itself - is not resolved here, and stays the document's own to declare.
    /// </para>
    /// </remarks>
    public sealed class WotNodeSetAliases : INodeSetAliasResolver
    {
        private WotNodeSetAliases()
        {
        }

        /// <summary>
        /// Gets the policy, which holds no state and is safe to share.
        /// </summary>
        public static WotNodeSetAliases Instance { get; } = new();

        /// <inheritdoc/>
        public bool TryResolve(string alias, out string nodeId)
        {
            return NodeSetStandardAliases.TryResolve(alias, out nodeId);
        }
    }
}
