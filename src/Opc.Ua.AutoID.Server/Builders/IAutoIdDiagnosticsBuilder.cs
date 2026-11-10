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

namespace Opc.Ua.AutoID.Server.Builders
{
    /// <summary>
    /// Optional reader-builder capability for standard diagnostic projections.
    /// </summary>
    public interface IAutoIdDiagnosticsBuilder : IAutoIdReaderBuilder
    {
        /// <summary>
        /// Publishes read-only Diagnostics from the existing presence, log and access reports.
        /// </summary>
        /// <remarks>
        /// Includes Presence, a single Message column in Logbook, and LastAccess. RFID readers
        /// also expose the optional RFID access fields. Unreported measurements have UncertainInitialValue;
        /// fields absent from a subsequent access report have BadNoData rather than
        /// retaining an earlier access. Access-event publication is configured independently.
        /// </remarks>
        IAutoIdReaderBuilder WithDiagnostics();
    }
}
