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

namespace Opc.Ua
{
    /// <summary>
    /// Marks an API member as a 1.5.378 → 1.6 migration shim. Used by the
    /// <c>Opc.Ua.CodeFixers</c> analyzer to map calls that bind to a shim
    /// extension back to the underlying <c>UA00xx</c> diagnostic rule, so
    /// consumers get the same migration guidance whether they call the
    /// shim directly or the legacy API in source.
    /// </summary>
    /// <remarks>
    /// Apply this in addition to <see cref="ObsoleteAttribute"/>. The shim
    /// keeps the call compilable; the analyzer fires an <c>Info</c>
    /// diagnostic that points at the matching migration-guide section.
    /// </remarks>
    [AttributeUsage(
        AttributeTargets.Method |
        AttributeTargets.Property |
        AttributeTargets.Constructor |
        AttributeTargets.Class,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class OpcUaShimAttribute : Attribute
    {
        /// <summary>
        /// The diagnostic rule identifier the shim corresponds to,
        /// e.g. <c>"UA0008"</c>.
        /// </summary>
        public string RuleId { get; }

        /// <summary>
        /// Creates a new <see cref="OpcUaShimAttribute"/>.
        /// </summary>
        public OpcUaShimAttribute(string ruleId)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        }
    }
}
