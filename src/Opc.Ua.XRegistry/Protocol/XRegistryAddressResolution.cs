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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Protocol
{
    /// <summary>
    /// Optional caller-bound address resolution before interpreting a request body.
    /// Resolution grants no mutation permission. Execution must revalidate the original
    /// address atomically and preserve its operation digest instead of following redirects.
    /// </summary>
    public interface IXRegistryAddressResolver
    {
        /// <summary>
        /// Resolves the canonical model address or returns an explicit rejection.
        /// No request payload or registry state is changed.
        /// </summary>
        ValueTask<XRegistryAddressResolution> ResolveAddressAsync(
            XRegistryRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The complete request at its canonical address, or a failed address-resolution response.
    /// </summary>
    public sealed record XRegistryAddressResolution
    {
        /// <summary>
        /// Creates a successful resolution or retains an unsuccessful response.
        /// </summary>
        public XRegistryAddressResolution(XRegistryRequest request, XRegistryResponse? rejection = null)
        {
            Request = request.ThrowIfNull(nameof(request));
            if (rejection?.IsSuccess == true)
            {
                throw new ArgumentException("A resolution rejection cannot be successful.", nameof(rejection));
            }
            Rejection = rejection;
        }

        /// <summary>
        /// Gets the request retaining its original address, guards, caller, parameters and content.
        /// </summary>
        public XRegistryRequest Request { get; }

        /// <summary>
        /// Gets an explicit failure, or null when the canonical request can be processed.
        /// </summary>
        public XRegistryResponse? Rejection { get; }
    }
}
