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

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Optional federation verification at the registry's configured provider boundary.
    /// Verification must not read file content or substitute a content lookup for a logical Resource.
    /// </summary>
    public interface IXRegistryFederationProvider
    {
        /// <summary>
        /// Verifies an authorized locator, authenticated application, origin registry, logical
        /// Resource ownership/Xid, FileType capability and Versions hierarchy against the trusted binding.
        /// </summary>
        /// <param name="target">The immutable, independently configured trusted binding.</param>
        /// <param name="endpointUrl">A locator to authorize before connecting, never identity authority.</param>
        /// <param name="cancellationToken">Cancels verification before publication.</param>
        /// <exception cref="ServiceResultException">
        /// Verification fails. A provider without logical Resource verification reports <c>BadNotSupported</c>
        /// before any connection or content operation.
        /// </exception>
        ValueTask VerifyLogicalResourceAsync(
            XRegistryFederationTarget target,
            string endpointUrl,
            CancellationToken cancellationToken = default);
    }
}
