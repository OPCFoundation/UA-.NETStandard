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

namespace Opc.Ua.Server.Historian.InMemory
{
    /// <summary>
    /// Configuration knobs for <see cref="InMemoryHistorianProvider"/>.
    /// </summary>
    public sealed record InMemoryHistorianOptions
    {
        /// <summary>
        /// Maximum number of raw samples retained per variable. Zero = unbounded.
        /// When the cap is reached the oldest samples are evicted on insert.
        /// </summary>
        public uint MaxSamplesPerNode { get; init; }

        /// <summary>
        /// Maximum number of modified-history entries retained per variable.
        /// Zero = unbounded.
        /// </summary>
        public uint MaxModifiedEntriesPerNode { get; init; }

        /// <summary>
        /// Maximum number of annotations retained per variable. Zero = unbounded.
        /// </summary>
        public uint MaxAnnotationsPerNode { get; init; }

        /// <summary>
        /// Default per-node capabilities advertised by the provider.
        /// </summary>
        public HistorianNodeCapabilities DefaultCapabilities { get; init; }
            = HistorianNodeCapabilities.ReadWrite;
    }
}
