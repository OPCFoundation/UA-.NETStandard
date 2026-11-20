/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Preserves element identity while hashing positions within live document buffers.
    /// The default element hash can collide for every element of the same document.
    /// </summary>
    internal sealed class WotJsonElementComparer : IEqualityComparer<JsonElement>
    {
        /// <summary>
        /// Initializes identity hashing over the supplied live document roots.
        /// </summary>
        /// <param name="roots">The roots used to initialize the instance.</param>
        public WotJsonElementComparer(ArrayOf<JsonElement> roots)
        {
            m_roots = roots.ConvertAll(root => (Root: root, Hash: root.GetHashCode()));
        }

        /// <inheritdoc/>
        /// <param name="x">The JSON element.</param>
        /// <param name="y">The JSON element.</param>
        public bool Equals(JsonElement x, JsonElement y)
        {
            return x.Equals(y);
        }

        /// <inheritdoc/>
        /// <param name="element">The JSON element.</param>
        public int GetHashCode(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Undefined)
            {
                return 0;
            }
            ReadOnlySpan<byte> value = JsonMarshal.GetRawUtf8Value(element);
            foreach ((JsonElement root, int hash) in m_roots)
            {
                if (JsonMarshal.GetRawUtf8Value(root).Overlaps(value, out int offset))
                {
                    return HashCode.Combine(hash, offset);
                }
            }
            return element.GetHashCode();
        }

        private readonly ArrayOf<(JsonElement Root, int Hash)> m_roots;
    }
}
