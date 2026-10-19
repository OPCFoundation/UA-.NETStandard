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
    /// The reader hardware behind one OPC 30010 reader device.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base contract is enough for a device that scans on its own (OPC 30010 6.1.1 option 3):
    /// the provider reports results through the <see cref="IAutoIdReaderSink"/> it receives in
    /// <see cref="AttachAsync"/>. A provider adds capabilities by implementing more interfaces;
    /// the runtime materialises exactly the methods the provider supports:
    /// <see cref="IAutoIdScanProvider"/> for <c>Scan</c>, <see cref="IAutoIdAsyncScanProvider"/>
    /// for <c>ScanStart</c> and <c>ScanStop</c>, <see cref="IAutoIdRfidProvider"/> for the RFID tag
    /// access methods and <see cref="IAutoIdRtlsProvider"/> for the RTLS location methods.
    /// </para>
    /// <para>
    /// The runtime serialises <c>ScanStart</c> and <c>ScanStop</c> and never runs two scans
    /// concurrently on one reader. Tag access and location calls from different sessions can run
    /// concurrently; the provider decides how to order them against the hardware.
    /// </para>
    /// </remarks>
    public interface IAutoIdReaderProvider
    {
        /// <summary>
        /// Gets the reader kind. It selects the device type and must not change.
        /// </summary>
        AutoIdReaderKind Kind { get; }

        /// <summary>
        /// Called once when the reader has been registered in the address space. The provider
        /// keeps the sink until <see cref="DetachAsync"/>.
        /// </summary>
        /// <param name="sink">The channel for asynchronous results and diagnostics.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default);

        /// <summary>
        /// Called once when the reader is shut down. The provider stops any scan it runs and
        /// stops using the sink before it returns.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        ValueTask DetachAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A provider that executes the synchronous <c>Scan</c> method (OPC 30010 6.1.3.4).
    /// </summary>
    public interface IAutoIdScanProvider : IAutoIdReaderProvider
    {
        /// <summary>
        /// Scans until a termination condition of <paramref name="settings"/> is fulfilled and
        /// returns the results.
        /// </summary>
        /// <remarks>
        /// The runtime has validated the settings: at least one termination condition is finite,
        /// the duration does not exceed <see cref="AutoIdServerOptions.MaximumScanDuration"/> and
        /// no other scan is active. The token is cancelled when the client abandons the call, the
        /// maximum duration passes or the reader shuts down.
        /// </remarks>
        /// <param name="settings">The scan settings of the call.</param>
        /// <param name="cancellationToken">Cancels the scan.</param>
        ValueTask<AutoIdScanOutcome> ScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A provider that executes the asynchronous <c>ScanStart</c> and <c>ScanStop</c> methods
    /// (OPC 30010 6.1.3.5 and 6.1.3.6). Results are delivered through
    /// <see cref="IAutoIdReaderSink"/>.
    /// </summary>
    public interface IAutoIdAsyncScanProvider : IAutoIdReaderProvider
    {
        /// <summary>
        /// Starts scanning. Returns once scanning has started; results follow through the sink.
        /// </summary>
        /// <remarks>
        /// When a termination condition of <paramref name="settings"/> is fulfilled the provider
        /// calls <see cref="IAutoIdReaderSink.CompleteScanAsync"/>. A status other than
        /// <c>SUCCESS_0</c> means scanning did not start.
        /// </remarks>
        /// <param name="settings">The scan settings of the call.</param>
        /// <param name="cancellationToken">Cancels the start.</param>
        /// <returns>The status of the scan start operation.</returns>
        ValueTask<AutoIdOperationStatusEnumeration> StartScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the scan started by <see cref="StartScanAsync"/>. Called when a client calls
        /// <c>ScanStop</c>, when the session that started the scan closes and when the reader
        /// shuts down. After it returns the provider reports no further results of that scan.
        /// </summary>
        /// <param name="cancellationToken">Cancels the stop.</param>
        ValueTask StopScanAsync(CancellationToken cancellationToken = default);
    }
}
