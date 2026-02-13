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

using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Model compiler options
    /// </summary>
    internal sealed record class ModelCompilationOptions
    {
        /// <summary>
        /// Model options
        /// </summary>
        public DesignFileOptions Options { get; set; }

        /// <summary>
        /// -useAllowSubtypes
        /// </summary>
        public bool UseAllowSubtypes { get; set; }

        /// <summary>
        /// -exclude [id;id;id]
        /// </summary>
        public IReadOnlyList<string> Exclude { get; set; }

        /// <summary>
        /// Get options from options provider
        /// </summary>
        /// <param name="provider"></param>
        /// <returns></returns>
        public static ModelCompilationOptions From(AnalyzerConfigOptionsProvider provider)
        {
            return new ModelCompilationOptions
            {
                Options = new DesignFileOptions
                {
                    Version = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.Version)) ??
                        "v105",
                    StartId = (uint)provider.GlobalOptions.GetInteger(
                        nameof(DesignFileOptions.StartId)),
                    ModelVersion = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.ModelVersion)),
                    ModelPublicationDate = provider.GlobalOptions.GetString(
                        nameof(DesignFileOptions.ModelPublicationDate)),
                    ReleaseCandidate = provider.GlobalOptions.GetBool(
                        nameof(DesignFileOptions.ReleaseCandidate))
                },
                Exclude = provider.GlobalOptions.GetStrings(nameof(Exclude)),
                UseAllowSubtypes = provider.GlobalOptions.GetBool(nameof(UseAllowSubtypes))
            };
        }
    }
}
