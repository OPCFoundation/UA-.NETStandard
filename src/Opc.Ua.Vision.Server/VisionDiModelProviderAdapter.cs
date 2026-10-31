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

using Opc.Ua.Di.Server;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Adapts a neutral model provider to the Vision provider pipeline.
    /// </summary>
    internal sealed class VisionDiModelProviderAdapter : IVisionModelProvider
    {
        /// <summary>
        /// Creates an adapter for the registered provider instance.
        /// </summary>
        public VisionDiModelProviderAdapter(IDiModelProvider provider)
        {
            m_provider = provider;
        }

        /// <summary>
        /// Gets the wrapped provider's model loading order.
        /// </summary>
        public int Order => m_provider.Order;

        /// <summary>
        /// Gets the namespaces contributed by the wrapped provider.
        /// </summary>
        public ArrayOf<string> NamespaceUris => m_provider.NamespaceUris;

        /// <summary>
        /// Gets the original provider type used to break model ordering ties.
        /// </summary>
        internal System.Type ProviderType => m_provider.GetType();

        /// <summary>
        /// Loads predefined nodes through the wrapped provider.
        /// </summary>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            m_provider.AddPredefinedNodes(nodes, context);
        }

        /// <summary>The registered provider instance.</summary>
        private readonly IDiModelProvider m_provider;
    }
}
