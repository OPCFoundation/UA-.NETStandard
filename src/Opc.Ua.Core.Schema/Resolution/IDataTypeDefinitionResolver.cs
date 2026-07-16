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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Resolves the runtime structure definition for an OPC UA data type id.
    /// Implementations may aggregate multiple sources (registered generated
    /// types, dynamically built complex types, or a server address space).
    /// </summary>
    public interface IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Resolves the type description for the supplied data type id.
        /// </summary>
        /// <param name="typeId">The data type id to resolve.</param>
        /// <param name="description">The resolved type description.</param>
        /// <returns><c>true</c> when the type was resolved.</returns>
        bool TryResolve(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description);

        /// <summary>
        /// Resolves the type description for the supplied data type id.
        /// </summary>
        /// <param name="typeId">The data type id to resolve.</param>
        /// <param name="description">The resolved type description.</param>
        /// <returns><c>true</c> when the type was resolved.</returns>
        bool TryResolve(
            NodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description);

        /// <summary>
        /// Returns all resolvable data types of a namespace.
        /// </summary>
        /// <param name="namespaceUri">The namespace uri.</param>
        /// <returns>The data types in the namespace.</returns>
        IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri);
    }
}
