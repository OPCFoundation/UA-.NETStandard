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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Explicit result-subtree capabilities; these options do not select equipment or production facets.
    /// </summary>
    public sealed record GmsResultBindingOptions
    {
        /// <summary>
        /// Gets the provider and response bounds for this instance.
        /// </summary>
        public GmsResultLimits Limits { get; init; } = new();

        /// <summary>
        /// Gets the number of stable derived GMS result-variable slots; zero selects method-only access.
        /// </summary>
        public int PublishedResults { get; init; } = 4;

        /// <summary>
        /// Gets whether result variables expose the optional UsedTools property.
        /// </summary>
        public bool IncludeUsedTools { get; init; }

        /// <summary>
        /// Gets whether the shared Machinery temporary-file transfer path is enabled.
        /// </summary>
        public bool EnableFileTransfer { get; init; } = true;

        /// <summary>
        /// Gets whether intermediate results may emit their own concrete IntermediateResultEventType subtype.
        /// </summary>
        public bool EnableIntermediateEvents { get; init; }

        /// <summary>
        /// Gets the initially configured corrections; null omits the optional folder, empty exposes an empty folder.
        /// </summary>
        public ArrayOf<GmsCorrection> Corrections { get; init; }

        /// <summary>
        /// Validates the requested subtree before any instance is published.
        /// </summary>
        internal void Validate()
        {
            if (Limits == null)
            {
                throw new ArgumentNullException(nameof(Limits));
            }
            Limits.Validate();
            if (PublishedResults < 0 || PublishedResults > Limits.MaximumResults ||
                Corrections.Count > Limits.MaximumCorrections)
            {
                throw new ArgumentOutOfRangeException(nameof(PublishedResults));
            }
        }
    }
}
