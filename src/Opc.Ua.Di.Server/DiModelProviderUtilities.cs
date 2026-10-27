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
using System.Collections.Generic;

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// Ordering and namespace helpers for <see cref="IDiModelProvider"/> lists.
    /// </summary>
    internal static class DiModelProviderUtilities
    {
        /// <summary>
        /// Returns the providers ordered by <see cref="IDiModelProvider.Order"/>,
        /// then type name, then registration order.
        /// </summary>
        public static ArrayOf<IDiModelProvider> Normalize(ArrayOf<IDiModelProvider> providers)
        {
            var indexed = new List<(IDiModelProvider Provider, int Index)>();
            for (int ii = 0; ii < providers.Count; ii++)
            {
                IDiModelProvider provider = providers[ii] ??
                    throw new ArgumentException("Model providers must not contain null.", nameof(providers));
                indexed.Add((provider, ii));
            }
            indexed.Sort(static (x, y) =>
            {
                int result = x.Provider.Order.CompareTo(y.Provider.Order);
                if (result == 0)
                {
                    result = string.CompareOrdinal(
                        x.Provider.GetType().FullName,
                        y.Provider.GetType().FullName);
                }
                return result != 0 ? result : x.Index.CompareTo(y.Index);
            });
            var sorted = new IDiModelProvider[indexed.Count];
            for (int ii = 0; ii < sorted.Length; ii++)
            {
                sorted[ii] = indexed[ii].Provider;
            }
            return sorted;
        }

        /// <summary>
        /// Returns the namespace URIs of all providers in provider order.
        /// </summary>
        public static IEnumerable<string> GetNamespaceUris(
            ArrayOf<IDiModelProvider> providers,
            ArrayOf<string> additionalNamespaceUris = default)
        {
            for (int ii = 0; ii < additionalNamespaceUris.Count; ii++)
            {
                yield return additionalNamespaceUris[ii];
            }
            ArrayOf<IDiModelProvider> normalized = Normalize(providers);
            for (int ii = 0; ii < normalized.Count; ii++)
            {
                ArrayOf<string> uris = normalized[ii].NamespaceUris;
                for (int jj = 0; jj < uris.Count; jj++)
                {
                    yield return uris[jj];
                }
            }
        }
    }
}
