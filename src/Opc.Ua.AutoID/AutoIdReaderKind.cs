// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
