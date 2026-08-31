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

using System;
using System.Collections.Generic;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Holds the ReferenceTypes a conversion resolved against the WoT Binding
    /// Section 5.1.5 local context, keyed by the compact model name a link
    /// relation used.
    /// </summary>
    /// <remarks>
    /// Resolving a name against the local context is asynchronous, but the
    /// synthesis that consumes the result is not. The names are therefore
    /// resolved once, up front, exactly as the Thing and parent references
    /// already are, and the synthesis reads the answers from here. An entry is
    /// kept even when the local context did not hold the name, so a second
    /// lookup for the same name never re-enters the resolver.
    /// </remarks>
    internal sealed class WotReferenceTypeCatalog
    {
        /// <summary>
        /// Records what the local context answered for a compact model name.
        /// </summary>
        /// <param name="modelName">The compact model name used as the key.</param>
        /// <param name="resolved">
        /// The ReferenceType and the direction its matched name expressed, or
        /// <c>null</c> when the local context did not hold the name.
        /// </param>
        public void Add(string modelName, WotResolvedReferenceType? resolved)
        {
            m_entries[modelName] = resolved;
        }

        /// <summary>
        /// Gets whether the local context resolved a compact model name.
        /// </summary>
        /// <param name="modelName">The compact model name.</param>
        /// <param name="resolved">The ReferenceType and its direction.</param>
        /// <returns><c>true</c> when the name resolved.</returns>
        public bool TryGet(string modelName, out WotResolvedReferenceType resolved)
        {
            if (m_entries.TryGetValue(modelName, out WotResolvedReferenceType? entry) &&
                entry is { } found)
            {
                resolved = found;
                return true;
            }
            resolved = default;
            return false;
        }

        /// <summary>
        /// Gets whether the catalog already holds an answer, resolved or not.
        /// </summary>
        /// <param name="modelName">The compact model name.</param>
        /// <returns><c>true</c> when the name was already looked up.</returns>
        public bool Contains(string modelName)
        {
            return m_entries.ContainsKey(modelName);
        }

        private readonly Dictionary<string, WotResolvedReferenceType?> m_entries =
            new(StringComparer.Ordinal);
    }
}
