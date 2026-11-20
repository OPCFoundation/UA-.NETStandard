// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    public sealed class UnionAttribute : Attribute;
}
