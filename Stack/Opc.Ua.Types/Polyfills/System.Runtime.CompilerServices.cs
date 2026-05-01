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

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Marks a type as a union type, in preparation for C# 15 union type
    /// support. A type annotated with this attribute represents a discriminated
    /// union whose state is one of a known set of "cases" exposed through
    /// non-boxing access members (typically a set of <c>TryGetValue</c>
    /// overloads and a <c>HasValue</c> indicator).
    /// </summary>
    /// <remarks>
    /// This is a forward-compatible polyfill of the proposed
    /// <c>System.Runtime.CompilerServices.UnionAttribute</c> from the C# 15
    /// union types language proposal. See
    /// <see href="https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/unions"/>
    /// and
    /// <see href="https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-15#union-types"/>.
    /// The attribute is currently a marker only; once the language and BCL
    /// ship the attribute, this polyfill can be removed.
    /// </remarks>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct,
        Inherited = false,
        AllowMultiple = false)]
    public sealed class UnionAttribute : Attribute
    {
    }
}
