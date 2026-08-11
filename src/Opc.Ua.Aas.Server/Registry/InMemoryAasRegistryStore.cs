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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Server.Registry
{
    /// <summary>
    /// Process-local AAS registry store that atomically switches immutable snapshots.
    /// </summary>
    public sealed class InMemoryAasRegistryStore : IAasRegistryStore, IAasRegistryResourceStoreProvider
    {
        /// <inheritdoc/>
        public IXRegistryResourceStore ResourceStore { get; } = new InMemoryResourceStore();

        /// <inheritdoc/>
        public ValueTask<AasRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<AasRegistrySnapshot>(Volatile.Read(ref m_committed));
        }

        /// <inheritdoc/>
        public ValueTask CommitAsync(AasRegistrySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref m_committed, snapshot ?? AasRegistrySnapshot.Empty);
            return default;
        }
        private AasRegistrySnapshot m_committed = AasRegistrySnapshot.Empty;
    }
}
