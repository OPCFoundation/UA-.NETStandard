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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Selects how the NodeManager behind a fluent builder mints NodeIds.
    /// </summary>
    public static class NodeIdAssignmentBuilderExtensions
    {
        /// <summary>
        /// Selects the identifier type that the owning NodeManager mints
        /// NodeIds with, for this and every node created after it.
        /// </summary>
        /// <remarks>
        /// Call this before the nodes it should apply to - at the top of a
        /// <c>Configure</c> delegate, typically. Nodes created earlier keep
        /// the identifiers they were already given, so switching mode
        /// midway leaves a graph with two identifier styles.
        /// </remarks>
        /// <typeparam name="TBuilder">
        /// The builder type, so the call composes in a chain that continues
        /// with either <see cref="NodeManagerBuilder"/> or
        /// <see cref="INodeManagerBuilder"/> members.
        /// </typeparam>
        /// <param name="builder">The fluent node-manager builder.</param>
        /// <param name="mode">The identifier type to mint.</param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is null.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Raised when the builder's NodeManager does not mint its own
        /// NodeIds, so there is no mode to select.
        /// </exception>
        public static TBuilder WithNodeIdAssignment<TBuilder>(
            this TBuilder builder,
            NodeIdAssignmentMode mode)
            where TBuilder : INodeManagerBuilder
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder.NodeManager is not AsyncCustomNodeManager manager)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "NodeId assignment can only be selected on a NodeManager " +
                    "deriving from AsyncCustomNodeManager.");
            }

            manager.NodeIdFactory = manager.NodeIdFactory.WithMode(mode);
            return builder;
        }
    }
}
