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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Namespace-metadata surface of <see cref="ConfigurationNodeManager"/>: the
    /// <see cref="IConfigurationNodeManager"/> members for looking up and creating
    /// <see cref="NamespaceMetadataState"/> nodes under <c>Server/Namespaces</c> and the
    /// <see cref="DefaultPermissionsChanged"/> notification. The behaviour lives in
    /// <see cref="NamespaceMetadataRegistry"/>; the manager satisfies its
    /// <see cref="INamespaceMetadataHost"/> contract with the inherited public members plus
    /// the <c>Server/Namespaces</c> lookup below.
    /// </summary>
    public partial class ConfigurationNodeManager : INamespaceMetadataHost
    {
        /// <inheritdoc/>
        public event EventHandler? DefaultPermissionsChanged
        {
            add => m_namespaceMetadata.DefaultPermissionsChanged += value;
            remove => m_namespaceMetadata.DefaultPermissionsChanged -= value;
        }

        /// <inheritdoc/>
        public ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            return m_namespaceMetadata.GetAsync(namespaceUri, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(
            ushort namespaceIndex,
            CancellationToken cancellationToken = default)
        {
            return m_namespaceMetadata.GetAsync(namespaceIndex, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<NamespaceMetadataState> CreateNamespaceMetadataStateAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            return m_namespaceMetadata.CreateAsync(namespaceUri, cancellationToken);
        }

        /// <inheritdoc/>
        NamespacesState? INamespaceMetadataHost.FindServerNamespacesNode()
        {
            return FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces);
        }
    }
}
