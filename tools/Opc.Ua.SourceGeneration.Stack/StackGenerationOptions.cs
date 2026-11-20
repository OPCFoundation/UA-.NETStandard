// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Model compiler options
    /// </summary>
    internal sealed record class StackGenerationOptions
    {
        /// <summary>
        /// -exclude [id;id;id]
        /// </summary>
        public IReadOnlyList<string> Exclude { get; set; }

        /// <summary>
        /// Which part of the stack the compilation opts in to generating.
        /// </summary>
        /// <remarks>
        /// Empty when the project did not opt in, which is the case for every
        /// compilation the generator is merely loaded into - a
        /// BenchmarkDotNet host project, for instance. Those must compile
        /// without the generator contributing anything, so the absence of the
        /// property is not an error.
        /// </remarks>
        public string Mode { get; set; }

        /// <summary>
        /// Get options from options provider
        /// </summary>
        /// <param name="provider"></param>
        /// <returns></returns>
        public static StackGenerationOptions FromProvider(AnalyzerConfigOptionsProvider provider)
        {
            return new StackGenerationOptions
            {
                Exclude = provider.GlobalOptions.GetStrings(nameof(Exclude)),
                Mode = provider.GlobalOptions.GetString(nameof(Mode))
            };
        }
    }
}
