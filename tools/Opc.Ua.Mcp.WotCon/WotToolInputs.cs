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
using System.Collections.Generic;
using System.Text.Json;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.WotCon;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// An explicit WoT resource selector, preserving the server's version and structural identity.
    /// </summary>
    public sealed class WotResourceSelection
    {
        /// <summary>
        /// Gets or sets the document kind.
        /// </summary>
        public WoTDocumentKindEnum Kind { get; set; }

        /// <summary>
        /// Gets or sets the group id when selecting by group/resource.
        /// </summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource id.
        /// </summary>
        public string ResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the explicit version id, or empty for the model's selected version.
        /// </summary>
        public string VersionId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an alternative structural Xid.
        /// </summary>
        public string Xid { get; set; } = string.Empty;
    }

    /// <summary>
    /// Explicit registry refresh scope and execution options.
    /// </summary>
    public sealed class WotRefreshInput
    {
        /// <summary>
        /// Gets or sets the selected resources.
        /// </summary>
        [JsonConverter(typeof(WotInputArrayJsonConverter<WotResourceSelection>))]
        public ArrayOf<WotResourceSelection> Selection { get; set; } = [];

        /// <summary>
        /// Gets or sets explicit permission to use an empty selection for a registry-wide refresh.
        /// </summary>
        public bool AllResources { get; set; }

        /// <summary>
        /// Gets or sets the model's requested atomicity.
        /// </summary>
        public WoTAtomicityEnum Atomicity { get; set; }

        /// <summary>
        /// Gets or sets whether unchanged resources should be rebuilt.
        /// </summary>
        public bool ForceRebuild { get; set; }

        /// <summary>
        /// Gets or sets the server-side dry-run flag.
        /// </summary>
        public bool DryRun { get; set; }

        /// <summary>
        /// Gets or sets whether dependent resources are included.
        /// </summary>
        public bool IncludeDependents { get; set; }

        /// <summary>
        /// Gets or sets the server's deletion policy.
        /// </summary>
        public WoTDeletePolicyEnum DeletePolicy { get; set; }

        /// <summary>
        /// Gets or sets the requested server parallelism, 1..32.
        /// </summary>
        public uint MaxParallelism { get; set; } = 1;

        /// <summary>
        /// Gets or sets the requested server timeout, 1..120000 milliseconds.
        /// </summary>
        public uint TimeoutMilliseconds { get; set; } = 30_000;

        /// <summary>
        /// Gets or sets the caller's correlation identifier.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Array binding for the closed, source-generated WoT request contracts.
    /// </summary>
    /// <typeparam name="T">A registered request element.</typeparam>
    public sealed class WotInputArrayJsonConverter<T> : McpInputArrayJsonConverter<T>
        where T : class
    {
        /// <inheritdoc/>
        protected override JsonTypeInfo<T> ElementType =>
            WotInputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ??
                throw new NotSupportedException("The type is not a registered WoT input contract.");
    }

    /// <summary>
    /// Source-generated metadata for bounded WoT input arrays.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(WotResourceSelection))]
    [JsonSerializable(typeof(WotRefreshInput))]
    [JsonSerializable(typeof(WotDocumentSource))]
    [JsonSerializable(typeof(WotDocumentInput))]
    [JsonSerializable(typeof(WotArchiveManifest))]
    [JsonSerializable(typeof(WotArchiveEntry))]
    [JsonSerializable(typeof(Dictionary<string, JsonElement>))]
    internal sealed partial class WotInputJsonContext : JsonSerializerContext
    {
    }
}
