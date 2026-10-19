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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// The channel a reader provider uses to push asynchronous results and
    /// diagnostics into the address space of its reader.
    /// </summary>
    /// <remarks>
    /// The runtime hands the sink to the provider in
    /// <see cref="IAutoIdReaderProvider.AttachAsync"/> and withdraws it in
    /// <see cref="IAutoIdReaderProvider.DetachAsync"/>. Every member may be
    /// called from any thread, and none of them is called under a lock held by
    /// the runtime. After the reader has been disposed the members throw
    /// <see cref="System.ObjectDisposedException"/>.
    /// </remarks>
    public interface IAutoIdReaderSink
    {
        /// <summary>
        /// Reports scan results: updates <c>LastScanData</c> and <c>LastScanTimestamp</c> and
        /// raises the reader's scan event (OPC 30010 7.2 to 7.7). Use it for results of an
        /// asynchronous scan started by <c>ScanStart</c> and for scans the device triggers itself.
        /// </summary>
        /// <param name="results">
        /// One or more results of the reader's result type. More than
        /// <see cref="AutoIdServerOptions.MaximumScanResults"/> fail with <c>Bad_ResponseTooLarge</c>.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask ReportScanAsync(ArrayOf<ScanResult> results, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports that the asynchronous scan started by <c>ScanStart</c> ended because one of its
        /// termination conditions was fulfilled. The reader returns to <c>Idle_0</c> and accepts
        /// <c>ScanStart</c> again. Has no effect when no asynchronous scan is active.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask CompleteScanAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports that the number of identifiers the device currently sees changed
        /// (<c>AutoIdPresenceEventType</c>, OPC 30010 7.12).
        /// </summary>
        /// <param name="presence">The number of identifiers in range.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask ReportPresenceAsync(ushort presence, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports a new logbook entry (<c>AutoIdLogEntryEventType</c>, OPC 30010 7.9). The entry
        /// is carried in the event's <c>Message</c> field.
        /// </summary>
        /// <param name="entry">The logbook entry text.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask ReportLogEntryAsync(string entry, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports that identifiers were accessed by a command the device executed on its own
        /// (<c>AutoIdAccessEventType</c> or <c>RfidAccessEventType</c>, OPC 30010 7.10 and 7.11).
        /// </summary>
        /// <param name="command">
        /// The command name; for an OPC UA client the names of OPC 30010, for example <c>ReadTag</c>.
        /// </param>
        /// <param name="client">The originator of the command.</param>
        /// <param name="results">
        /// The access results, of type <see cref="RfidAccessResult"/> for an RFID reader.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask ReportAccessAsync(
            string command,
            string client,
            ArrayOf<AccessResult> results,
            CancellationToken cancellationToken = default);
    }
}
