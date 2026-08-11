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
using Opc.Ua.Export;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// The result of materializing an AAS Environment into a NodeSet.
    /// </summary>
    public sealed class AasMaterializationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AasMaterializationResult"/> class.
        /// </summary>
        /// <param name="nodeSet">The produced NodeSet.</param>
        /// <param name="diagnostics">The diagnostics produced while materializing.</param>
        public AasMaterializationResult(
            UANodeSet nodeSet,
            IReadOnlyList<AasMaterializationDiagnostic> diagnostics)
        {
            NodeSet = nodeSet ?? throw new ArgumentNullException(nameof(nodeSet));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Gets the produced NodeSet.
        /// </summary>
        public UANodeSet NodeSet { get; }

        /// <summary>
        /// Gets the diagnostics produced while materializing.
        /// </summary>
        public IReadOnlyList<AasMaterializationDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets a value indicating whether any error diagnostic was produced.
        /// </summary>
        public bool HasErrors
        {
            get
            {
                for (int ii = 0; ii < Diagnostics.Count; ii++)
                {
                    if (Diagnostics[ii].Severity == AasMaterializationDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
