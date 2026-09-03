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

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// A materialized USD attribute — a typed, valued property of a prim
    /// (draft OPC UA — OpenUSD Scene Materialization §5.4).
    /// </summary>
    public sealed class UsdAttribute
    {
        /// <summary>
        /// Creates an attribute.
        /// </summary>
        /// <param name="name">The full property name, including any namespace prefix
        /// (for example <c>xformOp:rotateZ</c> or <c>primvars:displayColor</c>).</param>
        /// <param name="typeName">The exact <c>SdfValueTypeName</c> (for example
        /// <c>float3</c>, <c>token</c>, <c>color3f[]</c>).</param>
        public UsdAttribute(string name, string typeName)
        {
            Name = name ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            int idx = Name.LastIndexOf(':');
            Namespace = idx < 0 ? string.Empty : Name.Substring(0, idx);
            BaseName = idx < 0 ? Name : Name.Substring(idx + 1);
        }

        /// <summary>
        /// The full property name including any namespace prefix.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The exact <c>SdfValueTypeName</c>. Always retained so an export reproduces the
        /// precise spelling even where several USD types share one OPC UA DataType (§6.2).
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// The resolved attribute value. <see cref="UsdValue.Null"/> when the attribute is
        /// declared but carries no authored default.
        /// </summary>
        public UsdValue Value { get; set; }

        /// <summary>
        /// The authored USD time samples, an ordered map from time code to value kept separate
        /// from the authored default in <see cref="Value"/> (§7.1 step 3, §9). An attribute may
        /// carry a default, samples, both, or neither. The map is ordered by time code ascending
        /// (USD's composed sample order), so a materializer exposes <see cref="Value"/> as the
        /// live default and each sample as a HistoricalAccess entry (§9). A negative or fractional
        /// time code is permitted. When empty the attribute has no time samples.
        /// </summary>
        public SortedList<double, UsdValue> TimeSamples { get; } = new SortedList<double, UsdValue>();

        /// <summary>
        /// Whether the attribute may vary over time.
        /// </summary>
        public UsdVariabilityEnum Variability { get; set; } = UsdVariabilityEnum.Varying;

        /// <summary>
        /// Whether the attribute is a custom (non-schema) property.
        /// </summary>
        public bool Custom { get; set; }

        /// <summary>
        /// Interpolation for a primvar, when authored (for example <c>constant</c>, <c>vertex</c>).
        /// </summary>
        public string? Interpolation { get; set; }

        /// <summary>
        /// Authored attribute connections as SdfPath strings, materialized as
        /// <c>UsdConnection</c> references (§5.4).
        /// </summary>
        public IList<string> Connections { get; } = new List<string>();

        /// <summary>
        /// Whether the attribute is server-maintained and time-varying — Mode A of §9.
        /// A Mode B (static) attribute keeps its authored default.
        /// </summary>
        public bool Live { get; set; }

        /// <summary>
        /// The property namespace — the portion of <see cref="Name"/> before the last colon
        /// (for example <c>xformOp</c>), or an empty string when the attribute is unnamespaced.
        /// </summary>
        public string Namespace { get; }

        /// <summary>
        /// The property name without its namespace prefix.
        /// </summary>
        public string BaseName { get; }
    }
}
