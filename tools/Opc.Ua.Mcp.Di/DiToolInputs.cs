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

using System.ComponentModel;
using System.Text.Json.Serialization;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The finite set of DI topology entry points.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DiTopologyScope>))]
    public enum DiTopologyScope
    {
        /// <summary>
        /// Direct children of the standard DeviceSet.
        /// </summary>
        DeviceSet,

        /// <summary>
        /// Direct children of the standard NetworkSet.
        /// </summary>
        NetworkSet,

        /// <summary>
        /// Direct children of the standard DeviceTopology.
        /// </summary>
        DeviceTopology,

        /// <summary>
        /// Direct children of an explicitly supplied topology object.
        /// </summary>
        Children
    }

    /// <summary>
    /// Standard nameplate properties readable on a DI component or device.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DiDeviceProperty>))]
    public enum DiDeviceProperty
    {
        /// <summary>
        /// Localized manufacturer name.
        /// </summary>
        Manufacturer,

        /// <summary>
        /// Manufacturer identity URI.
        /// </summary>
        ManufacturerUri,

        /// <summary>
        /// Localized model name.
        /// </summary>
        Model,

        /// <summary>
        /// Hardware revision.
        /// </summary>
        HardwareRevision,

        /// <summary>
        /// Software revision.
        /// </summary>
        SoftwareRevision,

        /// <summary>
        /// Device revision.
        /// </summary>
        DeviceRevision,

        /// <summary>
        /// Product code.
        /// </summary>
        ProductCode,

        /// <summary>
        /// Manual reference, never fetched by the tool.
        /// </summary>
        DeviceManual,

        /// <summary>
        /// Device class.
        /// </summary>
        DeviceClass,

        /// <summary>
        /// Serial number.
        /// </summary>
        SerialNumber,

        /// <summary>
        /// Globally unique product-instance identity.
        /// </summary>
        ProductInstanceUri,

        /// <summary>
        /// Integer revision counter.
        /// </summary>
        RevisionCounter,

        /// <summary>
        /// User-assigned asset identifier.
        /// </summary>
        AssetId,

        /// <summary>
        /// User-assigned localized component name.
        /// </summary>
        ComponentName
    }

    /// <summary>
    /// The only DI nameplate fields exposed for explicit writes.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DiWritableProperty>))]
    public enum DiWritableProperty
    {
        /// <summary>
        /// User-assigned asset identifier.
        /// </summary>
        AssetId,

        /// <summary>
        /// User-assigned localized component name.
        /// </summary>
        ComponentName
    }

    /// <summary>
    /// Software-update state machines that can be read and finitely observed.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DiSoftwareStateMachine>))]
    public enum DiSoftwareStateMachine
    {
        /// <summary>
        /// Preparation for the update.
        /// </summary>
        PrepareForUpdate,

        /// <summary>
        /// Software installation.
        /// </summary>
        Installation,

        /// <summary>
        /// Confirmation of the installed software.
        /// </summary>
        Confirmation,

        /// <summary>
        /// Power-cycle state; no reboot operation is exposed.
        /// </summary>
        PowerCycle
    }

    /// <summary>
    /// Identifies the package to install independently from uploading its bytes.
    /// </summary>
    public sealed class DiInstallPackageRequest
    {
        /// <summary>
        /// Manufacturer identity passed to InstallSoftwarePackage.
        /// </summary>
        [JsonPropertyName("manufacturerUri")]
        public required string ManufacturerUri { get; init; }

        /// <summary>
        /// Requested software revision.
        /// </summary>
        [JsonPropertyName("softwareRevision")]
        public required string SoftwareRevision { get; init; }

        /// <summary>
        /// Patch identifiers, represented as a JSON array of strings.
        /// </summary>
        [JsonPropertyName("patchIdentifiers")]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        [Description("JSON array of patch identifiers, at most 500; an empty array means no patches.")]
        public ArrayOf<string> PatchIdentifiers { get; init; } = ArrayOf.Empty<string>();

        /// <summary>
        /// Base64 package hash; the empty string explicitly supplies an empty ByteString.
        /// </summary>
        [JsonPropertyName("hashBase64")]
        [Description("Base64 package hash required by the device; not a filesystem path or a signing key.")]
        public required string HashBase64 { get; init; }
    }

    /// <summary>
    /// Selects already uploaded server-side files for a separate installation operation.
    /// </summary>
    public sealed class DiInstallFilesRequest
    {
        /// <summary>
        /// Local server file NodeIds, represented as a nonempty JSON array of strings.
        /// </summary>
        [JsonPropertyName("fileNodeIds")]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        [Description("JSON array of 1..500 local File object NodeIds; these are not host filesystem paths.")]
        public required ArrayOf<string> FileNodeIds { get; init; }
    }
}
