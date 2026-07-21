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

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// A volatile registry store that keeps no durable state. Every restart
    /// starts from an empty registry. Useful for tests and for servers whose
    /// documents are re-populated programmatically at start-up.
    /// </summary>
    public sealed class InMemoryWotRegistryStore : IWotRegistryStore
    {
        /// <inheritdoc/>
        public ValueTask<WotRegistrySnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<WotRegistrySnapshot>(WotRegistrySnapshot.Empty);
        }

        /// <inheritdoc/>
        public ValueTask UpsertGroupAsync(
            WotResourceGroup group,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask UpsertResourceAsync(
            WotResource resource,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask UpsertRegistryLabelsAsync(
            System.Collections.Immutable.ImmutableSortedDictionary<string, string> labels,
            long epoch,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RemoveResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RemoveGroupAsync(
            string groupId,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
