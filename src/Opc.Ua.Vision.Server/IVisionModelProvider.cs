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

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Contributes compiled predefined nodes to a Vision node manager.
    /// </summary>
    /// <remarks>
    /// The built-in <see cref="VisionModelProvider"/> loads the Vision
    /// companion NodeSet. Applications can add extra providers to inject
    /// vendor extensions or pre-materialised instances that are known at
    /// startup.
    /// </remarks>
    public interface IVisionModelProvider
    {
        /// <summary>
        /// Gets the deterministic provider execution order. Providers run in
        /// ascending order; the built-in provider uses <c>int.MinValue</c> so
        /// application providers run afterwards by default.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Gets the namespace URIs contributed by this provider. Providers
        /// that replace the built-in provider must advertise the Vision
        /// namespace URI.
        /// </summary>
        ArrayOf<string> NamespaceUris { get; }

        /// <summary>
        /// Adds predefined nodes into <paramref name="nodes"/>.
        /// </summary>
        void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context);
    }

    /// <summary>
    /// Built-in Vision model provider.
    /// </summary>
    public sealed class VisionModelProvider : IVisionModelProvider
    {
        /// <inheritdoc/>
        public int Order => int.MinValue;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => new string[]
        {
            global::Opc.Ua.Vision.Namespaces.Vision
        };

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            nodes.AddOpcUaVision(context);
        }
    }
}
