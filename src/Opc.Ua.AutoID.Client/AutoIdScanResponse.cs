// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// The response of a <c>Scan</c> call.
    /// </summary>
    /// <param name="Results">
    /// The scan results, of the result type of the reader kind (for example
    /// <see cref="RfidScanResult"/> for an RFID reader).
    /// </param>
    /// <param name="Status">The status the device reports for the scan.</param>
    public sealed record AutoIdScanResponse(
        ArrayOf<ScanResult> Results,
        AutoIdOperationStatusEnumeration Status);

    /// <summary>
    /// The response of a <c>ReadTag</c> call.
    /// </summary>
    /// <param name="Data">The data read; empty unless <paramref name="Status"/> reports success.</param>
    /// <param name="Status">The status the device reports for the read.</param>
    public sealed record AutoIdTagReadResponse(
        ByteString Data,
        AutoIdOperationStatusEnumeration Status);
}
