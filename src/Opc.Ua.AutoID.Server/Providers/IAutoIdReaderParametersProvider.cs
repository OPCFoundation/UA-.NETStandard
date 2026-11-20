/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Optional capability for standard reader-specific RuntimeParameters in OPC 30010 Tables 10, 12 and 15.
    /// </summary>
    /// <remarks>
    /// Values are read and committed through IAutoIdScanParametersProvider as one complete snapshot.
    /// Capability declarations remain stable for the reader lifetime. The provider validates device-specific
    /// constraints and implements the selected tag types, templates and RF behavior; exposing a value alone
    /// is not an implementation of those behaviors. Unsupported values must remain absent.
    /// CodeTypesRWData additionally requires IAutoIdRfidDiagnosticFormatter to implement the diagnostic conversion.
    /// </remarks>
    public interface IAutoIdReaderParametersProvider : IAutoIdScanParametersProvider
    {
        /// <summary>
        /// Gets the capabilities established by the actual device configuration, not inferred from diagnostics.
        /// </summary>
        AutoIdReaderParameterCapabilities ReaderParameterCapabilities { get; }
    }

    /// <summary>
    /// Declares supported standard parameters without supplying invented hardware defaults.
    /// </summary>
    public sealed record AutoIdReaderParameterCapabilities
    {
        /// <summary>
        /// Gets the diagnostic RWData format names; a null array omits CodeTypesRWData.
        /// </summary>
        public ArrayOf<string> CodeTypesRWData { get; init; }

        /// <summary>
        /// Gets the RFID tag type names; a null array omits TagTypes.
        /// </summary>
        public ArrayOf<string> TagTypes { get; init; }

        /// <summary>
        /// Gets whether the provider implements the model's scalar SByte RfPower contract.
        /// </summary>
        /// <remarks>
        /// The provider owns its mapping to configured antennas. This declaration does not define a
        /// per-antenna addressing scheme or permit the runtime to infer one.
        /// </remarks>
        public bool SupportsRfPower { get; init; }

        /// <summary>
        /// Gets whether the provider implements MinRssi.
        /// </summary>
        public bool SupportsMinRssi { get; init; }

        /// <summary>
        /// Gets the generally configured RFID antenna mask, or null to omit EnableAntennas.
        /// </summary>
        /// <remarks>
        /// Bit zero denotes antenna one. Runtime selection can only restrict this mask; zero writes
        /// have no effect. Recreate the reader if the general device configuration changes.
        /// </remarks>
        public uint? ConfiguredAntennas { get; init; }

        /// <summary>
        /// Gets the preconfigured OCR or optical template names; a null array omits TemplateName.
        /// </summary>
        public ArrayOf<string> TemplateNames { get; init; }

        /// <summary>
        /// Gets whether the provider implements the OCR or optical MatchCode parameter.
        /// </summary>
        public bool SupportsMatchCode { get; init; }
    }
}
