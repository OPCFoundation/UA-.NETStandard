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

namespace Opc.Ua.AutoID
{
    /// <summary>
    /// The concrete OPC 30010 reader device type a reader is materialised as.
    /// </summary>
    public enum AutoIdReaderKind
    {
        /// <summary>
        /// An <c>RfidReaderDeviceType</c>, including NFC readers.
        /// </summary>
        Rfid,

        /// <summary>
        /// An <c>OcrReaderDeviceType</c>.
        /// </summary>
        Ocr,

        /// <summary>
        /// An <c>OpticalReaderDeviceType</c>, for barcode and matrix code readers.
        /// </summary>
        Optical,

        /// <summary>
        /// An <c>OpticalVerifierDeviceType</c>, which reports print quality grades.
        /// </summary>
        OpticalVerifier,

        /// <summary>
        /// An <c>RtlsDeviceType</c>, a real-time locating system.
        /// </summary>
        Rtls
    }

    /// <summary>
    /// Maps the five concrete reader device types of OPC 30010 to <see cref="AutoIdReaderKind"/>.
    /// </summary>
    public static class AutoIdReaderKinds
    {
        /// <summary>
        /// Gets the ObjectType id of the concrete device type of a reader kind.
        /// </summary>
        /// <param name="kind">The reader kind.</param>
        /// <returns>The expanded NodeId of the device type in the AutoID namespace.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The kind is not defined.</exception>
        public static ExpandedNodeId TypeIdOf(AutoIdReaderKind kind)
        {
            return kind switch
            {
                AutoIdReaderKind.Rfid => ObjectTypeIds.RfidReaderDeviceType,
                AutoIdReaderKind.Ocr => ObjectTypeIds.OcrReaderDeviceType,
                AutoIdReaderKind.Optical => ObjectTypeIds.OpticalReaderDeviceType,
                AutoIdReaderKind.OpticalVerifier => ObjectTypeIds.OpticalVerifierDeviceType,
                AutoIdReaderKind.Rtls => ObjectTypeIds.RtlsDeviceType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown reader kind.")
            };
        }
    }
}
