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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Validates that <see cref="HistorianProfileCatalog"/> gates every
    /// Historical Access Server profile on both the static
    /// <see cref="HistoricalAccessProfileDescriptor.IsAdvertised"/> flag
    /// and the concrete provider's actual interface support / rolled-up
    /// capability flags.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianProfileCatalogTests
    {
        private const ushort NamespaceIndex = 1;

        [Test]
        public void FullyCapableProviderWithoutNotifierConfigurationReportsNonEventProfilesOnly()
        {
            // HistorianNodeCapabilities.ReadWrite is a generic,
            // provider-wide preset: it carries no EventTypes or
            // MandatoryEventFields, so the 5 Events-family Server
            // profiles must not be reported supported even though every
            // Event read/update flag is true. Regression coverage for
            // "gate on more than the bare global flags".
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("fully-capable", NamespaceIndex);
            provider.Register(nodeId, HistorianNodeCapabilities.ReadWrite);

            ArrayOf<HistoricalAccessProfileDescriptor> supported =
                HistorianProfileCatalog.GetSupportedProfiles(provider, HistorianNodeCapabilities.ReadWrite);

            Assert.That(supported.Count, Is.EqualTo(10));
            Assert.That(
                supported.ToArray()!.Any(profile => profile.Family == HistoricalAccessProfileFamily.Events),
                Is.False,
                "No Events-family profile may be reported supported without notifier EventTypes/MandatoryEventFields.");
        }

        [Test]
        public void FullyCapableProviderWithNotifierConfigurationReportsAllFifteenServerProfiles()
        {
            using var provider = new InMemoryHistorianProvider();
            var dataNodeId = new NodeId("fully-capable-data", NamespaceIndex);
            provider.Register(dataNodeId, HistorianNodeCapabilities.ReadWrite);

            HistorianNodeCapabilities notifierCapabilities = MakeHistorizedNotifierCapabilities(
                HistorianNodeCapabilities.ReadWrite);

            // Non-event families are evaluated against the provider-wide
            // rollup; Events-family profiles are evaluated against one
            // specific historized notifier's own capabilities, per
            // IsSupportedByProvider's documented contract.
            var supported = new List<HistoricalAccessProfileDescriptor>();
            foreach (HistoricalAccessProfileDescriptor profile in
                HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileSide.Server))
            {
                HistorianNodeCapabilities caps = profile.Family == HistoricalAccessProfileFamily.Events
                    ? notifierCapabilities
                    : HistorianNodeCapabilities.ReadWrite;
                if (HistorianProfileCatalog.IsSupportedByProvider(profile, provider, caps))
                {
                    supported.Add(profile);
                }
            }

            Assert.That(supported, Has.Count.EqualTo(15));
        }

        [Test]
        public void CombinedRollupReportsAllFifteenServerProfiles()
        {
            using var provider = new InMemoryHistorianProvider();
            ArrayOf<HistoricalAccessProfileDescriptor> supported =
                HistorianProfileCatalog.GetSupportedProfiles(
                    provider,
                    HistorianNodeCapabilities.ReadWrite,
                    MakeHistorizedNotifierCapabilities(
                        HistorianNodeCapabilities.ReadWrite));

            Assert.That(supported, Has.Count.EqualTo(15));
        }

        [Test]
        public void EveryServerProfileDropsWhenItsRequiredCapabilityIsRemoved()
        {
            using var provider = new InMemoryHistorianProvider();
            HistorianNodeCapabilities full =
                MakeHistorizedNotifierCapabilities(
                    HistorianNodeCapabilities.ReadWrite);

            foreach (HistoricalAccessProfileDescriptor profile in
                HistoricalAccessProfileCatalog.GetProfiles(
                    HistoricalAccessProfileSide.Server))
            {
                Assert.That(
                    HistorianProfileCatalog.IsSupportedByProvider(
                        profile,
                        provider,
                        full),
                    Is.True,
                    profile.Name);
                Assert.That(
                    HistorianProfileCatalog.IsSupportedByProvider(
                        profile,
                        provider,
                        RemoveRequiredCapability(
                            profile.ProfileUri,
                            full)),
                    Is.False,
                    profile.Name);
            }
        }

        [Test]
        public void EventFacetIsNotSupportedWhenEventTypesAreNotConfigured()
        {
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile =
                GetCatalogProfile("http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022");

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                new HistorianNodeCapabilities
                {
                    ReadEventHistory = true,
                    MandatoryEventFields = [MakeBaseEventTypeField()]
                    // EventTypes intentionally left empty.
                });

            Assert.That(supported, Is.False);
        }

        [Test]
        public void EventFacetIsNotSupportedWhenMandatoryEventFieldsAreNotConfigured()
        {
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile =
                GetCatalogProfile("http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022");

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                new HistorianNodeCapabilities
                {
                    ReadEventHistory = true,
                    EventTypes = [ObjectTypeIds.BaseEventType]
                    // MandatoryEventFields intentionally left empty.
                });

            Assert.That(supported, Is.False);
        }

        [Test]
        public void EventFacetIsSupportedOnlyWithFlagEventTypesAndMandatoryFieldsAllConfigured()
        {
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile =
                GetCatalogProfile("http://opcfoundation.org/UA-Profile/Server/HistoricalEventInsert2022");

            HistorianNodeCapabilities incomplete = new()
            {
                InsertEvent = true,
                EventTypes = [ObjectTypeIds.BaseEventType],
                MandatoryEventFields = [MakeBaseEventTypeField()]
            };
            Assert.That(HistorianProfileCatalog.IsSupportedByProvider(profile, provider, incomplete), Is.True);

            // Flip the specific flag off: even with EventTypes and
            // MandatoryEventFields configured, the Insert profile must
            // not be reported supported.
            HistorianNodeCapabilities noInsert = incomplete with { InsertEvent = false };
            Assert.That(HistorianProfileCatalog.IsSupportedByProvider(profile, provider, noInsert), Is.False);
        }

        [Test]
        public async Task ProviderWideNullNodeRollupNeverSatisfiesEventFacetsAsync()
        {
            // Regression test: the NodeId.Null capability rollup unions
            // only the boolean/uint capability fields across registered
            // nodes (see InMemoryHistorianProvider's aggregate rollup);
            // it never carries per-notifier EventTypes / MandatoryEventFields.
            // Passing that rollup for an Events-family profile must
            // therefore always fail, however many event-capable nodes are
            // registered.
            using var provider = new InMemoryHistorianProvider();
            var notifierId = new NodeId("event-notifier", NamespaceIndex);
            provider.Register(notifierId, HistorianNodeCapabilities.EventReadWrite);

            HistorianNodeCapabilities rollup = await provider
                .GetCapabilitiesAsync(NodeId.Null, CancellationToken.None)
                .ConfigureAwait(false);

            HistoricalAccessProfileDescriptor profile =
                GetCatalogProfile("http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022");

            Assert.That(HistorianProfileCatalog.IsSupportedByProvider(profile, provider, rollup), Is.False);
        }

        [Test]
        public void IsSupportedByProviderRejectsProfilesThatAreNotAdvertised()
        {
            HistoricalAccessProfileDescriptor profile = MakeProfile(
                HistoricalAccessProfileFamily.RawAndServerTimestamp,
                isAdvertised: false);
            using var provider = new InMemoryHistorianProvider();

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                HistorianNodeCapabilities.ReadWrite);

            Assert.That(supported, Is.False);
        }

        [Test]
        public void IsSupportedByProviderRejectsClientSideProfiles()
        {
            HistoricalAccessProfileDescriptor profile = MakeProfile(
                HistoricalAccessProfileFamily.RawAndServerTimestamp,
                isAdvertised: true,
                side: HistoricalAccessProfileSide.Client);
            using var provider = new InMemoryHistorianProvider();

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                HistorianNodeCapabilities.ReadWrite);

            Assert.That(supported, Is.False);
        }

        [Test]
        public void IsSupportedByProviderRequiresTheFamilysInterface()
        {
            // The fake provider below only implements the base
            // IHistorianProvider contract, not IHistorianAnnotationProvider,
            // so the Annotation family must not be reported supported even
            // though the capability flag is set.
            HistoricalAccessProfileDescriptor profile = MakeProfile(
                HistoricalAccessProfileFamily.Annotation,
                isAdvertised: true);
            var provider = new DataOnlyProvider();

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                new HistorianNodeCapabilities { InsertAnnotation = true });

            Assert.That(supported, Is.False);
        }

        [Test]
        public void IsSupportedByProviderRequiresTheCapabilityFlagEvenWhenInterfaceIsImplemented()
        {
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile = MakeProfile(
                HistoricalAccessProfileFamily.RawUpdates,
                isAdvertised: true);

            // InMemoryHistorianProvider implements IHistorianDataProvider,
            // but the supplied rolled-up capabilities carry no update
            // flags at all (as if every registered node were read-only).
            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                HistorianNodeCapabilities.ReadOnly);

            Assert.That(supported, Is.False);
        }

        [Test]
        public void IsSupportedByProviderAcceptsAggregateFamilyViaBaseDataProviderFallback()
        {
            // Aggregate (processed) reads fall back to the framework's
            // central calculator over raw reads, so only the base
            // IHistorianDataProvider interface is required — the
            // specialised IHistorianProcessedProvider is optional.
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile = MakeProfile(
                HistoricalAccessProfileFamily.Aggregate,
                isAdvertised: true);

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                new HistorianNodeCapabilities { ReadProcessedData = true });

            Assert.That(supported, Is.True);
        }

        [Test]
        public void IsSupportedByProviderAcceptsWhenBothInterfaceAndFlagAreSatisfied()
        {
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile = MakeProfile(
                HistoricalAccessProfileFamily.RawUpdates,
                isAdvertised: true);

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                new HistorianNodeCapabilities { InsertData = true });

            Assert.That(supported, Is.True);
        }

        [Test]
        public void InsertCapabilityDoesNotClaimReplaceProfile()
        {
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile =
                HistoricalAccessProfileCatalog.AllProfiles
                    .ToArray()!
                    .Single(value => value.ProfileUri ==
                        "http://opcfoundation.org/UA-Profile/Server/HistoricalDataReplace2022");

            bool supported = HistorianProfileCatalog.IsSupportedByProvider(
                profile,
                provider,
                new HistorianNodeCapabilities
                {
                    InsertData = true
                });

            Assert.That(supported, Is.False);
        }

        [Test]
        public void IsSupportedByProviderThrowsOnNullArguments()
        {
            using var provider = new InMemoryHistorianProvider();
            HistoricalAccessProfileDescriptor profile = MakeProfile(
                HistoricalAccessProfileFamily.RawAndServerTimestamp,
                isAdvertised: true);

            Assert.That(
                () => HistorianProfileCatalog.IsSupportedByProvider(null!, provider, HistorianNodeCapabilities.ReadOnly),
                Throws.ArgumentNullException);
            Assert.That(
                () => HistorianProfileCatalog.IsSupportedByProvider(profile, null!, HistorianNodeCapabilities.ReadOnly),
                Throws.ArgumentNullException);
            Assert.That(
                () => HistorianProfileCatalog.IsSupportedByProvider(profile, provider, null!),
                Throws.ArgumentNullException);
        }

        private static HistoricalAccessProfileDescriptor MakeProfile(
            HistoricalAccessProfileFamily family,
            bool isAdvertised,
            HistoricalAccessProfileSide side = HistoricalAccessProfileSide.Server)
        {
            return new HistoricalAccessProfileDescriptor(
                "Synthetic Test Profile",
                GetProfileUri(family),
                side,
                family,
                ["Synthetic Conformance Unit"],
                isAdvertised,
                isAdvertised ? string.Empty : "Not yet verified.");
        }

        private static string GetProfileUri(
            HistoricalAccessProfileFamily family)
        {
            return family switch
            {
                HistoricalAccessProfileFamily.RawAndServerTimestamp =>
                    "http://opcfoundation.org/UA-Profile/Server/HistoricalRawData2022",
                HistoricalAccessProfileFamily.Modified =>
                    "http://opcfoundation.org/UA-Profile/Server/HistoricalModifiedData2022",
                HistoricalAccessProfileFamily.AtTime =>
                    "http://opcfoundation.org/UA-Profile/Server/HistoricalDataAtTime2022",
                HistoricalAccessProfileFamily.Aggregate =>
                    "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022",
                HistoricalAccessProfileFamily.Annotation =>
                    "http://opcfoundation.org/UA-Profile/Server/HistoricalAnnotation2022",
                HistoricalAccessProfileFamily.Structured =>
                    "http://opcfoundation.org/UA-Profile/Server/HistoricalStructuredData2022",
                HistoricalAccessProfileFamily.RawUpdates =>
                    "http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022",
                HistoricalAccessProfileFamily.Events =>
                    "http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(family))
            };
        }

        /// <summary>
        /// Looks up a real profile from the shipped catalog by URI (as
        /// opposed to <see cref="MakeProfile"/>'s synthetic descriptors),
        /// so tests exercise the actual <c>IsAdvertised</c> /
        /// <c>Prerequisite</c> values <see cref="HistoricalAccessProfileCatalog"/>
        /// ships with.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static HistoricalAccessProfileDescriptor GetCatalogProfile(string profileUri)
        {
            bool found = HistoricalAccessProfileCatalog.TryGetProfile(profileUri, out HistoricalAccessProfileDescriptor? profile);
            if (!found || profile == null)
            {
                throw new ArgumentException($"Unknown profile URI: {profileUri}", nameof(profileUri));
            }
            return profile;
        }

        /// <summary>
        /// Builds a set of capabilities for a fully-capable historized
        /// event notifier: every Event read/update flag from
        /// <paramref name="template"/> plus a configured
        /// <see cref="HistorianNodeCapabilities.EventTypes"/> and
        /// <see cref="HistorianNodeCapabilities.MandatoryEventFields"/> —
        /// the two properties a bare provider-wide rollup never carries.
        /// </summary>
        private static HistorianNodeCapabilities MakeHistorizedNotifierCapabilities(
            HistorianNodeCapabilities template)
        {
            return template with
            {
                EventTypes = [ObjectTypeIds.BaseEventType],
                MandatoryEventFields = [MakeBaseEventTypeField()]
            };
        }

        private static SimpleAttributeOperand MakeBaseEventTypeField()
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(BrowseNames.EventType)],
                AttributeId = Attributes.Value
            };
        }

        private static HistorianNodeCapabilities RemoveRequiredCapability(
            string profileUri,
            HistorianNodeCapabilities capabilities)
        {
            return profileUri switch
            {
                "http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022" =>
                    capabilities with { ReadEventHistory = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalModifiedData2022" =>
                    capabilities with { ReadModifiedData = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalStructuredData2022" =>
                    capabilities with { ReadStructuredData = false },
                "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022" =>
                    capabilities with { ReadProcessedData = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalAnnotation2022" =>
                    capabilities with { InsertAnnotation = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataAtTime2022" =>
                    capabilities with { ReadAtTime = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataDelete2022" =>
                    capabilities with { DeleteRaw = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022" =>
                    capabilities with { InsertData = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataReplace2022" =>
                    capabilities with { ReplaceData = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataUpdate2022" =>
                    capabilities with { UpdateData = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventDelete2022" =>
                    capabilities with { DeleteEvent = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventInsert2022" =>
                    capabilities with { InsertEvent = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventReplace2022" =>
                    capabilities with { ReplaceEvent = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventUpdate2022" =>
                    capabilities with { UpdateEvent = false },
                "http://opcfoundation.org/UA-Profile/Server/HistoricalRawData2022" =>
                    capabilities with { ReadRawData = false },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(profileUri))
            };
        }

        /// <summary>
        /// Minimal provider that only implements the base
        /// <see cref="IHistorianProvider"/> contract, used to verify that
        /// the interface gate rejects families whose narrower interface
        /// is absent.
        /// </summary>
        private sealed class DataOnlyProvider : IHistorianProvider, IHistorianDataProvider
        {
            public ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct)
            {
                return new(true);
            }

            public ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct)
            {
                return new(HistorianNodeCapabilities.ReadOnly);
            }

            public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
                HistorianOperationContext context,
                HistorianRawReadRequest request,
                HistorianResumeToken resumeToken,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> InsertAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> UpdateAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DataValue> values,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> DeleteRawAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                DateTimeUtc startTime,
                DateTimeUtc endTime,
                bool isDeleteModified,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<HistorianUpdateOutcome<DataValue>> DeleteAtTimeAsync(
                HistorianOperationContext context,
                NodeId nodeId,
                ArrayOf<DateTimeUtc> timestamps,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }
    }
}
