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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// A multi-store dispatcher for <see cref="SecretIdentifier"/>
    /// lookups. Routes each identifier to the registered
    /// <see cref="ISecretStore"/> whose
    /// <see cref="ISecretStore.StoreType"/> matches.
    /// </summary>
    public interface ISecretRegistry
    {
        /// <summary>
        /// Registers a store. If a store with the same
        /// <see cref="ISecretStore.StoreType"/> is already registered,
        /// it is replaced.
        /// </summary>
        void RegisterStore(ISecretStore store);

        /// <summary>
        /// Synchronous fast-path lookup. Returns a fresh
        /// <see cref="ISecret"/> when the matching store can answer
        /// without I/O, otherwise <see langword="null"/>.
        /// </summary>
        ISecret? TryGet(SecretIdentifier id);

        /// <summary>
        /// Resolves a secret via the matching store. Returns
        /// <see langword="null"/> when no store is registered for the
        /// identifier's <see cref="SecretIdentifier.StoreType"/>, or
        /// when the store has no entry for the identifier.
        /// </summary>
        ValueTask<ISecret?> GetAsync(
            SecretIdentifier id,
            CancellationToken ct = default);
    }
}
