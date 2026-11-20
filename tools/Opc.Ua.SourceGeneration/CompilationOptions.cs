/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Captures compilation options.
    /// </summary>
    internal record struct CompilationOptions(
        LanguageVersion LanguageVersion,
        string AssemblyName,
        OptimizationLevel OptimizationLevel,
        Platform Platform,
        OutputKind OutputKind)
    {
        /// <summary>
        /// True when the compilation targets C# 13 or newer. The underlying
        /// value is compared because the <c>LanguageVersion.CSharp13</c> member
        /// only exists in Roslyn 4.12 and later, while the generator is also
        /// built against the Visual Studio 2022 baseline.
        /// </summary>
        public readonly bool IsCSharp13OrLater => (int)LanguageVersion >= 1300;

        /// <summary>
        /// Create options from a compilation.
        /// </summary>
        internal static CompilationOptions From(Compilation c)
        {
            return new CompilationOptions(
                c is CSharpCompilation cs ? cs.LanguageVersion : LanguageVersion.CSharp1,
                c.AssemblyName,
                c.Options.OptimizationLevel,
                c.Options.Platform,
                c.Options.OutputKind);
        }
    }
}
