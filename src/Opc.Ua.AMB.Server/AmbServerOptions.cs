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

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Configures the OPC 10000-110 Asset Management Basics node manager and
    /// its asset registry.
    /// </summary>
    public sealed class AmbServerOptions
    {
        /// <summary>
        /// The default application-owned namespace of the nodes the manager
        /// creates at runtime. It is deliberately not the application URI, so
        /// it cannot collide with the instances a Device Integration manager
        /// creates there.
        /// </summary>
        public const string DefaultInstanceNamespaceUri = "urn:opcua-netstandard:amb:instances";

        /// <summary>
        /// The default namespace of the server-specific types the manager
        /// creates at runtime, kept apart from the instances as OPC 10000-110
        /// Table 59 recommends.
        /// </summary>
        public const string DefaultTypeNamespaceUri = "urn:opcua-netstandard:amb:types";

        /// <summary>
        /// The namespace of dictionary entries identified by an IRDI
        /// (OPC 10000-19): the identifier of their NodeId is the IRDI, for
        /// example an ECLASS class or an IEC CDD property.
        /// </summary>
        public const string IrdiNamespaceUri = "http://opcfoundation.org/UA/Dictionary/IRDI";

        /// <summary>
        /// The number of Unicode characters a configurable <c>AssetId</c> has
        /// to accept at least (OPC 10000-110 Table 54, "AMB Configurable Asset
        /// Identification").
        /// </summary>
        public const int MinimumAssetIdLength = 40;

        /// <summary>
        /// The default maximum length of a configurable <c>AssetId</c>.
        /// </summary>
        public const int DefaultMaxAssetIdLength = 255;

        /// <summary>
        /// The number of characters an editable documentation link has to
        /// accept at least (OPC 10000-110 Table 54, "AMB DocumentationLinks
        /// Edit Base").
        /// </summary>
        public const int MinimumDocumentationLinkLength = 255;

        /// <summary>
        /// The number of links users can add to an asset at least, so
        /// <c>AddLink</c> and <c>RemoveLink</c> are of use.
        /// </summary>
        public const int MinimumUserLinksPerAsset = 2;

        /// <summary>
        /// Gets or sets the application-owned namespace of the nodes the
        /// manager creates at runtime.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = DefaultInstanceNamespaceUri;

        /// <summary>
        /// Gets or sets the namespace of the server-specific types the
        /// manager creates at runtime.
        /// </summary>
        public string TypeNamespaceUri { get; set; } = DefaultTypeNamespaceUri;

        /// <summary>
        /// Gets or sets whether the OPC 10000-110 interfaces of health alarms
        /// are applied on server-specific alarm types; <see langword="true"/>
        /// by default.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 10000-110 applies <c>IRootCauseIndicationType</c> to alarm
        /// types (§9.4.2). By default the manager therefore creates a subtype
        /// of each Device Integration alarm type in
        /// <see cref="TypeNamespaceUri"/> - <c>AssetFailureAlarmType</c>
        /// below <c>2:FailureAlarmType</c>, and so on - that implements the
        /// interface, and the alarms are instances of it. A client filtering
        /// for the Device Integration type still receives them.
        /// </para>
        /// <para>
        /// Set to <see langword="false"/> to keep the Device Integration
        /// types and reference the interface from each alarm instead.
        /// </para>
        /// </remarks>
        public bool UseServerDefinedAlarmTypes { get; set; } = true;

        /// <summary>
        /// Gets or sets whether registering an asset without a non-empty
        /// <c>ProductInstanceUri</c> fails.
        /// </summary>
        /// <remarks>
        /// <c>ProductInstanceUri</c> is what identifies a manageable asset
        /// (OPC 10000-110 §7), and the server facet requires it on every
        /// asset. Turning the check off lets an application register an asset
        /// whose property it fills in later: once the new value is reported
        /// through <c>ClearChangeMasks</c>, the asset is listed in
        /// <c>AssetsByProductInstanceUri</c> and the conformance units are
        /// published again. The property has to exist when the asset is
        /// registered, and what clients configure is persisted only for an
        /// asset registered with its <c>ProductInstanceUri</c>.
        /// </remarks>
        public bool RequireProductInstanceUri { get; set; } = true;

        /// <summary>
        /// Gets or sets the alias categories through which clients discover
        /// the assets (OPC 10000-110 §8.2); both by default.
        /// </summary>
        public AssetDiscovery Discovery { get; set; } = AssetDiscovery.All;

        /// <summary>
        /// Gets or sets the maximum number of Unicode characters a client may
        /// write to a configurable <c>AssetId</c>. Must be at least
        /// <see cref="MinimumAssetIdLength"/>.
        /// </summary>
        public int MaxAssetIdLength { get; set; } = DefaultMaxAssetIdLength;

        /// <summary>
        /// Gets or sets the maximum number of characters of a documentation
        /// link a client writes or adds; at least
        /// <see cref="MinimumDocumentationLinkLength"/>, 2048 by default.
        /// </summary>
        public int MaxDocumentationLinkLength { get; set; } = 2048;

        /// <summary>
        /// Gets or sets how many links users can add to one asset through
        /// <c>AddLink</c>; at least <see cref="MinimumUserLinksPerAsset"/>,
        /// 16 by default.
        /// </summary>
        public int MaxUserLinksPerAsset { get; set; } = 16;

        /// <summary>
        /// Gets or sets the maximum number of Unicode characters of the browse
        /// name and of the display name of a link a user adds; 128 by default.
        /// </summary>
        public int MaxDocumentationLinkNameLength { get; set; } = 128;

        /// <summary>
        /// Gets or sets the maximum number of Unicode characters of the
        /// description of a link a user adds; 1024 by default.
        /// </summary>
        public int MaxDocumentationLinkDescriptionLength { get; set; } = 1024;

        /// <summary>
        /// Gets or sets the maximum number of Unicode characters a client may
        /// write to a writable location Property; 1024 by default.
        /// </summary>
        public int MaxLocationLength { get; set; } = 1024;

        /// <summary>
        /// Gets or sets who may change documentation links: write an
        /// editable link, or call <c>AddLink</c> and <c>RemoveLink</c>
        /// (OPC 10000-110 §10.5.3 lets the server restrict them).
        /// </summary>
        /// <remarks>
        /// The delegate receives the identity of the session, which is
        /// <see langword="null"/> outside one. When it is not set, every
        /// authenticated user may and an anonymous one may not.
        /// </remarks>
        public Func<IUserIdentity?, bool>? AuthorizeLinkEdit { get; set; }

        /// <summary>
        /// Gets or sets the directory in which the configuration clients write
        /// to assets is persisted, or <see langword="null"/> to keep it in
        /// memory. Set through <see cref="UseFileSystemStores"/>.
        /// </summary>
        public string? StateDirectory { get; set; }

        /// <summary>
        /// Gets the namespace URIs the manager registers in addition to the
        /// AMB model and the instance namespace.
        /// </summary>
        public IList<string> AdditionalNamespaceUris { get; } = [];

        /// <summary>
        /// Persists the configuration clients write to assets below a
        /// directory of the local file system, so it survives a restart.
        /// </summary>
        /// <param name="directory">The directory; created when missing.</param>
        /// <returns>The same options, for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
        public AmbServerOptions UseFileSystemStores(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("The directory must not be empty.", nameof(directory));
            }
            StateDirectory = directory;
            return this;
        }

        /// <summary>
        /// Gets the namespaces the node manager registers: the instance
        /// namespace first, so it is the manager's default, then the AMB model
        /// and the server-specific types.
        /// </summary>
        internal string[] GetNamespaceUris()
        {
            var uris = new List<string> { InstanceNamespaceUri, Namespaces.AMB, TypeNamespaceUri, IrdiNamespaceUri };
            foreach (string uri in AdditionalNamespaceUris)
            {
                if (!uris.Contains(uri))
                {
                    uris.Add(uri);
                }
            }
            return [.. uris];
        }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(InstanceNamespaceUri) ||
                !Uri.IsWellFormedUriString(InstanceNamespaceUri, UriKind.Absolute))
            {
                throw new ArgumentException(
                    "AmbServerOptions.InstanceNamespaceUri must be an absolute URI or URN.",
                    nameof(InstanceNamespaceUri));
            }
            if (string.Equals(InstanceNamespaceUri, Namespaces.AMB, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "AmbServerOptions.InstanceNamespaceUri must not be the AMB model namespace.",
                    nameof(InstanceNamespaceUri));
            }
            if (string.IsNullOrWhiteSpace(TypeNamespaceUri) ||
                !Uri.IsWellFormedUriString(TypeNamespaceUri, UriKind.Absolute) ||
                string.Equals(TypeNamespaceUri, Namespaces.AMB, StringComparison.Ordinal) ||
                string.Equals(TypeNamespaceUri, InstanceNamespaceUri, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "AmbServerOptions.TypeNamespaceUri must be an absolute URI or URN other than the " +
                    "AMB model and the instance namespace.",
                    nameof(TypeNamespaceUri));
            }
            if (MaxDocumentationLinkLength < MinimumDocumentationLinkLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxDocumentationLinkLength),
                    MaxDocumentationLinkLength,
                    "OPC 10000-110 requires an editable documentation link to accept at least " +
                    $"{MinimumDocumentationLinkLength} characters.");
            }
            if (MaxUserLinksPerAsset < MinimumUserLinksPerAsset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxUserLinksPerAsset),
                    MaxUserLinksPerAsset,
                    $"Users have to be able to add at least {MinimumUserLinksPerAsset} links to an asset.");
            }
            foreach ((string name, int value) in new[]
            {
                (nameof(MaxDocumentationLinkNameLength), MaxDocumentationLinkNameLength),
                (nameof(MaxDocumentationLinkDescriptionLength), MaxDocumentationLinkDescriptionLength),
                (nameof(MaxLocationLength), MaxLocationLength)
            })
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(name, value, "The length must be at least 1.");
                }
            }
            if (MaxAssetIdLength < MinimumAssetIdLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxAssetIdLength),
                    MaxAssetIdLength,
                    "OPC 10000-110 requires a configurable AssetId to accept at least " +
                    $"{MinimumAssetIdLength} characters.");
            }
        }
    }
}
