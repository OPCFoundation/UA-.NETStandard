/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry.Federation
{
    /// <summary>
    /// An explicit optional schema adapter. The server owns its authorization and format interpretation.
    /// Installing it never requires a hosted SchemaRegistry root.
    /// </summary>
    public interface IFederationSchemaProvider
    {
        /// <summary>Resolves a schema for the selected Message origin, without guessing URI-fragment associations.</summary>
        ValueTask<SchemaDocumentDataType?> ResolveAsync(
            MessageDefinitionDataType definition,
            RegistryEntityReferenceDataType messageOrigin,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Immutable validated observations. Resolution reads copies only; it never opens a Session or an HTTP URL.
    /// Replace EndpointRegistryResolutionOptions.Provider explicitly after authorized preload completes.
    /// </summary>
    public sealed class FederationResolutionCache : IEndpointRegistryResolutionProvider
    {
        /// <summary>Creates an empty cache with an optional separately authorized schema adapter.</summary>
        public FederationResolutionCache(IFederationSchemaProvider? schemaProvider = null)
        {
            m_schema = schemaProvider;
            m_entries = [];
        }

        private FederationResolutionCache(
            Dictionary<FederationSourceKey, Entry> entries, IFederationSchemaProvider? schema)
        {
            m_entries = entries;
            m_schema = schema;
        }

        /// <summary>Gets the number of origin/role/Xid observations.</summary>
        public int Count => m_entries.Count;

        /// <summary>
        /// Creates a new cache after validating all independent evidence. A route update compares historical
        /// pins, not historical locator authorization, and must supply fresh evidence for the new route.
        /// </summary>
        public FederationResolutionCache WithObservation(
            FederationTrustBinding binding,
            FederationMetadataObservation evidence,
            EndpointRegistryMessageObservation observation)
        {
            if (observation is null)
            {
                throw new ArgumentNullException(nameof(observation));
            }
            var key = new FederationSourceKey(observation.Source);
            m_entries.TryGetValue(key, out Entry? previous);
            RegistryEntityReferenceDataType selected = new FederationMetadataSelector().Select(
                observation.Source, binding, evidence, previous?.Snapshot);
            if (observation.VersionId != evidence.VersionId)
            {
                throw new ArgumentException("Metadata VersionId contradicts independent provider evidence.");
            }
            ValidateMetadata(observation.Metadata, key, observation.Epoch, evidence.VersionId);
            var entries = new Dictionary<FederationSourceKey, Entry>(m_entries)
            {
                [key] = new Entry(new FederationReferenceSnapshot(selected, binding, evidence), new EndpointRegistryMessageObservation
                {
                    Source = selected,
                    Metadata = (RegistryObjectValueDataType)observation.Metadata.Clone(),
                    Epoch = observation.Epoch,
                    VersionId = observation.VersionId
                })
            };
            return new FederationResolutionCache(entries, m_schema);
        }

        /// <summary>Reads a copy of a previously validated observation; no network access occurs.</summary>
        public ValueTask<EndpointRegistryMessageObservation?> ReadMessageAsync(
            RegistryEntityReferenceDataType reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new FederationSourceKey(reference);
            if (!m_entries.TryGetValue(key, out Entry? entry))
            {
                return new ValueTask<EndpointRegistryMessageObservation?>((EndpointRegistryMessageObservation?)null);
            }
            EndpointRegistryMessageObservation value = entry.Observation;
            if (reference.HasNativeTarget && (!value.Source.HasNativeTarget ||
                value.Source.NativeTarget != reference.NativeTarget))
            {
                throw new ArgumentException("The selected native target differs from the authenticated cache pin.");
            }
            if (!reference.HasNativeTarget && !reference.NativeTarget.IsNull)
            {
                throw new ArgumentException("An absent native target must be null.");
            }
            return new ValueTask<EndpointRegistryMessageObservation?>(new EndpointRegistryMessageObservation
            {
                Source = (RegistryEntityReferenceDataType)value.Source.Clone(),
                Metadata = (RegistryObjectValueDataType)value.Metadata.Clone(),
                Epoch = value.Epoch,
                VersionId = value.VersionId
            });
        }

        /// <summary>Calls only the explicitly supplied schema adapter with cloned inputs.</summary>
        public ValueTask<SchemaDocumentDataType?> ResolveSchemaAsync(
            MessageDefinitionDataType definition,
            RegistryEntityReferenceDataType origin,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return m_schema is null
                ? new ValueTask<SchemaDocumentDataType?>((SchemaDocumentDataType?)null)
                : m_schema.ResolveAsync((MessageDefinitionDataType)definition.Clone(),
                    (RegistryEntityReferenceDataType)origin.Clone(), cancellationToken);
        }

        private static void ValidateMetadata(
            RegistryObjectValueDataType metadata, FederationSourceKey key, uint epoch, string versionId)
        {
            EndpointRegistryRules.ValidateMessage(metadata);
            if (epoch == 0)
            {
                throw new RegistryRuleException("E_EPOCH", "epoch", "a committed UInt32 epoch is required");
            }
            foreach (RegistryMemberDataType member in metadata.Members)
            {
                if (member.Name == "epoch")
                {
                    RegistryValueDataType expected = RegistryValues.Parse(
                        System.Text.Encoding.UTF8.GetBytes(epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    if (!RegistryValues.Identical(expected, member.Value))
                    {
                        throw new RegistryRuleException("E_SNAPSHOT", "epoch", "raw and committed epochs disagree");
                    }
                }
                if (member.Name is "xid" or "messageid" or "versionid")
                {
                    string expected = member.Name == "messageid" ? key.LogicalXid[(key.LogicalXid.LastIndexOf('/') + 1)..]
                        : member.Name == "versionid" ? versionId : key.Xid;
                    if (member.Value is not RegistryStringValueDataType text ||
                        text.Value != expected && !(member.Name == "xid" && text.Value == key.LogicalXid))
                    {
                        throw new RegistryRuleException("E_REFERENCE_IDENTITY", "Reference",
                            "raw Message and sole-Version identity differ from the authorized observation");
                    }
                }
                if (member.Name == "versions" && member.Value is RegistryObjectValueDataType versions)
                {
                    foreach (RegistryMemberDataType version in versions.Members)
                    {
                        if (version.Name != versionId)
                        {
                            throw new RegistryRuleException("E_REFERENCE_IDENTITY", "Reference",
                                "raw Message and sole-Version identity differ from the authorized observation");
                        }
                    }
                }
            }
        }

        private sealed record Entry(FederationReferenceSnapshot Snapshot, EndpointRegistryMessageObservation Observation);
        private readonly Dictionary<FederationSourceKey, Entry> m_entries;
        private readonly IFederationSchemaProvider? m_schema;
    }
}
