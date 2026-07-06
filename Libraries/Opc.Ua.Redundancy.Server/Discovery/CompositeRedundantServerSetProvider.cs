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
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="IRedundantServerSetProvider"/> that prefers a dynamic
    /// discovery provider and falls back to a statically-configured provider when discovery has not (yet) found
    /// any peers. This realises "static configuration is the fallback for dynamic discovery": <c>FindServers</c>
    /// returns the discovered replica set once discovery yields peers, and the configured set until then.
    /// </summary>
    public sealed class CompositeRedundantServerSetProvider : IRedundantServerSetProvider
    {
        /// <summary>
        /// Creates a composite provider.
        /// </summary>
        /// <param name="primary">The dynamic discovery provider consulted first.</param>
        /// <param name="fallback">The statically-configured provider used when discovery is empty.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="primary"/> or <paramref name="fallback"/> is <c>null</c>.
        /// </exception>
        public CompositeRedundantServerSetProvider(
            IRedundantServerSetProvider primary,
            IRedundantServerSetProvider fallback)
        {
            m_primary = primary ?? throw new ArgumentNullException(nameof(primary));
            m_fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        /// <inheritdoc/>
        public ArrayOf<ApplicationDescription> GetRedundantServerSet()
        {
            ArrayOf<ApplicationDescription> discovered = m_primary.GetRedundantServerSet();
            return discovered.Count > 0 ? discovered : m_fallback.GetRedundantServerSet();
        }

        private readonly IRedundantServerSetProvider m_primary;
        private readonly IRedundantServerSetProvider m_fallback;
    }
}
