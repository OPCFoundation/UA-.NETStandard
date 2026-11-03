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
    /// CodeTypesRWData is not bound: the existing method-report path has no selectable formatting contract.
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
