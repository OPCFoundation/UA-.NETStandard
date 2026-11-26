// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.AutoID
{
    /// <summary>
    /// The runtime parameters for variable-triggered scanning (OPC 30010 6.1.3.1).
    /// </summary>
    /// <remarks>
    /// This SDK snapshot groups values from standard variables. It is not a wire DataType
    /// and does not replace the generated ScanSettings method-argument structure.
    /// </remarks>
    public sealed record AutoIdScanParameters
    {
        /// <summary>
        /// Gets the scan duration in milliseconds; zero means unlimited.
        /// </summary>
        public double Duration { get; init; }

        /// <summary>
        /// Gets the number of cycles; zero means unlimited.
        /// </summary>
        public int Cycles { get; init; }

        /// <summary>
        /// Gets whether scanning stops when data becomes available.
        /// </summary>
        public bool DataAvailable { get; init; }

        /// <summary>
        /// Gets the format of LastScanData.
        /// </summary>
        public string CodeType { get; init; } = string.Empty;

        /// <summary>
        /// Gets the selected code indexes in the CodeTypes EnumStrings property.
        /// </summary>
        public ArrayOf<uint> CodeTypes { get; init; }

        /// <summary>
        /// Gets the selected RFID tag types; a null array means unsupported.
        /// </summary>
        public ArrayOf<uint> TagTypes { get; init; }

        /// <summary>
        /// Gets the selected diagnostic RWData format indexes; null means unsupported and empty disables RWData.
        /// </summary>
        public ArrayOf<uint> CodeTypesRWData { get; init; }

        /// <summary>
        /// Gets the provider's scalar RFID power setting, or null when unsupported.
        /// </summary>
        public sbyte? RfPower { get; init; }

        /// <summary>
        /// Gets the lowest acceptable RFID signal strength, or null when unsupported.
        /// </summary>
        public int? MinRssi { get; init; }

        /// <summary>
        /// Gets the RFID antenna selection mask, or null when unsupported. A zero write has no effect.
        /// </summary>
        public uint? EnableAntennas { get; init; }

        /// <summary>
        /// Gets the active configured OCR or optical template, or null when unsupported.
        /// </summary>
        public string? TemplateName { get; init; }

        /// <summary>
        /// Gets the target OCR or optical decoding value, or null when unsupported.
        /// </summary>
        public string? MatchCode { get; init; }
    }
}
