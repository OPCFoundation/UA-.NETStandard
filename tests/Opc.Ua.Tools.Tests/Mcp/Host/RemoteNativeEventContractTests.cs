/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

#if NET10_0
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon;
using Opc.Ua.XRegistry;
using WotTypes = Opc.Ua.WotCon.ObjectTypeIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Pins native generated field contracts and the tools' notifier/boundary refusals.
    /// Actual notification delivery and owned-item cleanup require a ManagedSession integration fixture.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RemoteNativeEventContractTests
    {
        [Test]
        public async Task RegistryGeneratedFilterContainsNativeIdentityAndBothConcurrencyCountersAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            EventRecordDecoderRegistry decoders = EventRecordDecoderRegistry.Default.CreateChildScope();
            decoders.RegisterxRegistryDecoders(fixture.Context.NamespaceUris);

            EventFilter filter = XRegistryEventTypeRecord.EventFilters.Build(fixture.Context.NamespaceUris, decoders);

            AssertFields(filter, fixture, XRegistryWellKnown.XRegistryNamespaceUri,
                ["SourceUrl", "Subject", "CorrelationId", "Epoch", "MetaEpoch"]);
            Assert.That(filter.SelectClauses.ToArray()!.Any(clause =>
                clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == new QualifiedName("EventType")), Is.True);
            Assert.That(filter.SelectClauses.ToArray()!.Any(clause =>
                clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == new QualifiedName("SourceNode")), Is.True);
            Assert.That(filter.WhereClause.Elements.ToArray()!.Select(element => element.FilterOperator),
                Does.Contain(FilterOperator.OfType));
            Assert.That(fixture.Operations, Is.Empty);
        }

        [Test]
        public async Task WotGeneratedFilterContainsResourceFailureAndRefreshSummaryFieldsAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            EventRecordDecoderRegistry decoders = EventRecordDecoderRegistry.Default.CreateChildScope();
            decoders.RegisterWotConDecoders(fixture.Context.NamespaceUris);

            EventFilter filter = WoTResourceEventTypeRecord.EventFilters.Build(fixture.Context.NamespaceUris, decoders);

            AssertFields(filter, fixture, WotCon.Namespaces.WotCon,
                ["Xid", "ResourceId", "VersionId", "DocumentKind", "Generation", "Phase", "Outcome",
                    "ValidationOutcome", "LoadState", "FailedNodeId", "Reason", "BindingUri", "Summary", "RequestId"]);
            Assert.That(filter.SelectClauses.ToArray()!.Any(clause =>
                clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == new QualifiedName("Time")), Is.True);
            Assert.That(fixture.Operations, Is.Empty);
        }

        [TestCase(false, 0, 1)]
        [TestCase(false, 30001, 1)]
        [TestCase(false, 1, 0)]
        [TestCase(false, 1, 501)]
        [TestCase(true, 0, 1)]
        [TestCase(true, 30001, 1)]
        [TestCase(true, 1, 0)]
        [TestCase(true, 1, 501)]
        public async Task NativeObservationBoundsRefuseBeforeAnyStreamingSubscriptionAsync(
            bool wot, int duration, int count)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            CallToolResult result = wot
                ? await fixture.Wot.ObserveEventsAsync(
                    fixture.Id("wot-registry").ToString(), duration, count, "selected")
                    .ConfigureAwait(false)
                : await fixture.Registry.ObserveEventsAsync(
                    fixture.Id("registry").ToString(), duration, count, "selected")
                    .ConfigureAwait(false);

            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Calls, Is.Empty);
            fixture.Session.Verify(value => value.Dispose(), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeObservationRejectsWrongNotifierTypeInsteadOfGenericServerSubscriptionAsync(bool wot)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.Type(fixture.Id("notifier"), ObjectTypeIds.FolderType);
            CallToolResult result = wot
                ? await fixture.Wot.ObserveEventsAsync(fixture.Id("notifier").ToString(), sessionName: "selected")
                    .ConfigureAwait(false)
                : await fixture.Registry.ObserveEventsAsync(fixture.Id("notifier").ToString(), sessionName: "selected")
                    .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeMismatch);
            Assert.That(fixture.Browses, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls, Is.Empty);
            fixture.Session.Verify(value => value.Dispose(), Times.Never);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task NativeObservationDoesNotFabricateEventsWhenRegisteredSessionHasNoManagedStreamingAsync(
            bool wot, bool document)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            string node = wot ? document ? "document" : "wot-registry" : "registry";
            if (document)
            {
                NodeId vendor = fixture.Id("vendor-document-type");
                fixture.Type(fixture.Id("document"), vendor);
                fixture.Subtype(vendor, WotTypes.WoTDocumentType);
            }
            CallToolResult result = wot
                ? await fixture.Wot.ObserveEventsAsync(fixture.Id(node).ToString(), 30000, 500, "selected")
                    .ConfigureAwait(false)
                : await fixture.Registry.ObserveEventsAsync(fixture.Id(node).ToString(), 30000, 500, "selected")
                    .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("errorType").GetString(), Is.EqualTo(nameof(NotSupportedException)));
            Assert.That(json.GetProperty("message").GetString(), Does.Contain("ManagedSession"));
            Assert.That(json.TryGetProperty("items", out _), Is.False);
            Assert.That(fixture.Calls, Is.Empty);
            fixture.Session.Verify(value => value.Dispose(), Times.Never);
        }

        private static void AssertFields(
            EventFilter filter, RemoteModelTestSession fixture, string namespaceUri, string[] expected)
        {
            foreach (string name in expected)
            {
                SimpleAttributeOperand[] clauses = [.. filter.SelectClauses.ToArray()!.Where(clause =>
                    clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == fixture.Name(namespaceUri, name))];
                Assert.That(clauses, Has.Length.EqualTo(1), $"Generated field {name} must be selected exactly once.");
                Assert.That(clauses[0].AttributeId, Is.EqualTo(Attributes.Value), name);
                Assert.That(clauses[0].TypeDefinitionId.IsNull, Is.False, name);
            }
        }
    }
}
#endif
