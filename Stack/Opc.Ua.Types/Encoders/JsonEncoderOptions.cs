/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua
{
    /// <summary>
    /// Json encoder options
    /// </summary>
    public record class JsonEncoderOptions
    {
        /// <summary>
        /// Verbose encoding
        /// See https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.1
        /// </summary>
        public static JsonEncoderOptions Verbose { get; } = new(nameof(Verbose))
        {
            IgnoreNullValues = false,
            IgnoreDefaultValues = false,
            EnumerationAsNumber = false,
            IgnoreUnionSwitchField = true,
            IgnoreOptionalFieldEncodingMask = true,
            ForceNamespaceUri = true,
            SuppressArtifacts = false,
            OmitStatusCodeSymbol = false
        };

        /// <summary>
        /// Compact encoding
        /// See https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.1
        /// </summary>
        public static JsonEncoderOptions Compact { get; } = new(nameof(Compact))
        {
            IgnoreNullValues = true,
            IgnoreDefaultValues = true,
            EnumerationAsNumber = true,
            IgnoreUnionSwitchField = false,
            IgnoreOptionalFieldEncodingMask = false,
            ForceNamespaceUri = true,
            SuppressArtifacts = false,
            OmitStatusCodeSymbol = true
        };

        /// <summary>
        /// RawData encoding
        /// See https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.1
        /// </summary>
        public static JsonEncoderOptions RawData { get; } = new(nameof(RawData))
        {
            IgnoreNullValues = false,
            IgnoreDefaultValues = false,
            EnumerationAsNumber = false,
            IgnoreUnionSwitchField = true,
            IgnoreOptionalFieldEncodingMask = true,
            ForceNamespaceUri = true,
            SuppressArtifacts = true,
            OmitStatusCodeSymbol = false
        };

        /// <summary>
        /// Create options
        /// </summary>
        /// <param name="name"></param>
        public JsonEncoderOptions(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Write indented
        /// </summary>
        public bool Indented { get; init; }

        /// <summary>
        /// Enumerations are encoded as numbers
        /// See https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.4
        /// </summary>
        public bool EnumerationAsNumber { get; init; }

        /// <summary>
        /// Enumerations are encoded as numbers
        /// See https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.2.12
        /// </summary>
        public bool OmitStatusCodeSymbol { get; init; }

        /// <summary>
        /// Do not write union switch field
        /// See https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.8
        /// </summary>
        public bool IgnoreUnionSwitchField { get; init; }

        /// <summary>
        /// Do not write encoding mask
        /// See https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.7
        /// </summary>
        public bool IgnoreOptionalFieldEncodingMask { get; init; }

        /// <summary>
        /// Ignore null values
        /// </summary>
        public bool IgnoreNullValues { get; init; }

        /// <summary>
        /// Ignore default primitive values
        /// </summary>
        public bool IgnoreDefaultValues { get; init; }

        /// <summary>
        /// Enable RawData mode by suppressing artifacts
        /// In RawData mode, encoders shall omit the following fields:
        /// UaType (see
        /// https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.2.17
        /// and
        /// https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.2.18)
        /// UaTypeId (see
        /// https://reference.opcfoundation.org/Core/Part6/v105/docs/5.4.2.16)
        /// Decoders may not be able to process streams encoding in RawData
        /// mode unless they have access to the associated metadata. These
        /// fields are not omitted when serialization uses abstract DataTypes
        /// such as Structure (i.e. ExtensionObject) or BaseDataType
        /// (i.e. Variant).
        /// </summary>
        public bool SuppressArtifacts { get; init; }

        /// <summary>
        /// Force namespace uri
        /// </summary>
        public bool ForceNamespaceUri { get; init; }

        /// <summary>
        /// Options name
        /// </summary>
        public string? Name { get; init; }
    }
}
