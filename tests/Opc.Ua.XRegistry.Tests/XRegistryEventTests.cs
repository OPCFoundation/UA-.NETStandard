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
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    public sealed class XRegistryEventTests
    {
        [TestCaseSource(nameof(EventMappings))]
        public void EmitterBuildsEveryConcreteGeneratedEventState(
            object kindValue,
            Type expectedType)
        {
            var kind = (XRegistryEventKind)kindValue;
            Mock<IServerInternal> server =
                XRegistryServerTestHarness.CreateServer(XRegistryWellKnown.XRegistryNamespaceUri);
            ServerSystemContext context = server.Object.DefaultSystemContext.Copy();
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("registry", 1),
                BrowseName = new QualifiedName("Registry", 1),
                DisplayName = new LocalizedText("Registry")
            };
            var emitter = new XRegistryEventEmitter(context, "https://registry.example.test");
            var change = new XRegistryEventChange(
                kind,
                "/groups/g/resources/r/versions/v1",
                source.NodeId,
                Epoch: RequiresEpoch(kind) ? 7u : null,
                MetaEpoch: RequiresMetaEpoch(kind) ? 3u : null,
                Changed: AllowsChanged(kind)
                    ? ImmutableArray.Create("z", "a", "a")
                    : ImmutableArray<string>.Empty);

            BaseEventState evt = emitter.BuildEvent(
                source,
                change,
                (DateTimeUtc)new DateTime(2026, 9, 3, 5, 0, 0, DateTimeKind.Utc));

            Assert.Multiple(() =>
            {
                Assert.That(evt, Is.TypeOf(expectedType));
                Assert.That(((XRegistryEventState)evt).SourceUrl!.Value,
                    Is.EqualTo("https://registry.example.test"));
                Assert.That(((XRegistryEventState)evt).Subject!.Value,
                    Is.EqualTo(change.Subject));
                Assert.That(evt.SourceNode!.Value, Is.EqualTo(source.NodeId));
            });
        }

        [Test]
        public void CoalescerAppliesPrecedenceAndCanonicalChangedOrdering()
        {
            ImmutableArray<XRegistryEventChange> result = XRegistryEventCoalescer.Coalesce(
            [
                new(XRegistryEventKind.ResourceUpdated, "/r", new NodeId(1), 2, 1,
                    ImmutableArray.Create("z", "a")),
                new(XRegistryEventKind.ResourceUpdated, "/r", new NodeId(1), 3, 1,
                    ImmutableArray.Create("a", "m")),
                new(XRegistryEventKind.ResourceCreated, "/r", new NodeId(2), 1, 1),
                new(XRegistryEventKind.ResourceDeleted, "/r", new NodeId(3)),
                new(XRegistryEventKind.ResourceDeprecated, "/r", new NodeId(1))
            ]);

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Length.EqualTo(2));
                Assert.That(result.Any(change =>
                    change.Kind == XRegistryEventKind.ResourceDeleted), Is.True);
                Assert.That(result.Any(change =>
                    change.Kind == XRegistryEventKind.ResourceDeprecated), Is.True);
            });
        }

        [Test]
        public void EnabledEventsRequireCompleteConfiguration()
        {
            var options = new XRegistryServerOptions { EventsEnabled = true };
            Assert.Throws<ArgumentException>(options.Validate);

            options.EventSourceUrl = "relative";
            Assert.Throws<ArgumentException>(options.Validate);

            options.EventSourceUrl = "https://registry.example.test";
            options.ResourcesAttributeName = string.Empty;
            Assert.Throws<ArgumentException>(options.Validate);

            options.ResourcesAttributeName = "resources";
            Assert.DoesNotThrow(options.Validate);
        }

        [Test]
        public void GeneratedRecordsAndFiltersCoverAllConcreteEventTypes()
        {
            Assembly model = typeof(XRegistryEventTypeRecord).Assembly;
            Type[] records = model.GetTypes()
                .Where(type =>
                    type.Namespace == typeof(XRegistryEventTypeRecord).Namespace &&
                    type.Name.EndsWith("EventTypeRecord", StringComparison.Ordinal) &&
                    type != typeof(XRegistryEventTypeRecord))
                .ToArray();
            Assert.That(records, Has.Length.EqualTo(19));

            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            namespaceUris.GetIndexOrAppend(Namespaces.xRegistry);
            foreach (Type record in records)
            {
                Type? filters = record.GetNestedType("EventFilters", BindingFlags.Public);
                MethodInfo? build = filters?.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
                Assert.That(build, Is.Not.Null, record.Name);
                var filter = (EventFilter?)build!.Invoke(null, [namespaceUris, null]);
                Assert.That(filter, Is.Not.Null, record.Name);
                Assert.That(filter!.WhereClause.Elements, Is.Not.Empty, record.Name);
            }
        }

        [Test]
        public void GeneratedResourceUpdatedDecoderPopulatesTypedXRegistryFields()
        {
            QualifiedName[][] layout = ResourceUpdatedEventTypeRecord.Decoder.StandardFields;
            var fields = Enumerable.Repeat(Variant.Null, layout.Length).ToArray();
            fields[IndexOf(layout, BrowseNames.SourceUrl)] =
                new Variant("https://registry.example.test");
            fields[IndexOf(layout, BrowseNames.Subject)] =
                new Variant("/groups/g/resources/r");
            fields[IndexOf(layout, BrowseNames.Changed)] =
                new Variant(new ArrayOf<string>(s_decodedChanged));
            fields[IndexOf(layout, BrowseNames.Epoch)] = new Variant(7u);
            fields[IndexOf(layout, BrowseNames.MetaEpoch)] = new Variant(3u);

            ResourceUpdatedEventTypeRecord? decoded =
                ResourceUpdatedEventTypeRecord.Decoder.Decode(fields);

            Assert.Multiple(() =>
            {
                Assert.That(decoded, Is.Not.Null);
                Assert.That(decoded!.SourceUrl, Is.EqualTo("https://registry.example.test"));
                Assert.That(decoded.Subject, Is.EqualTo("/groups/g/resources/r"));
                Assert.That(decoded.Changed, Is.EqualTo(s_decodedChanged));
                Assert.That(decoded.Epoch, Is.EqualTo(7u));
                Assert.That(decoded.MetaEpoch, Is.EqualTo(3u));
            });
        }

        private static int IndexOf(QualifiedName[][] layout, string browseName)
        {
            for (int index = 0; index < layout.Length; index++)
            {
                if (layout[index].Length > 0 &&
                    string.Equals(layout[index][^1].Name, browseName, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            Assert.Fail($"The generated event layout does not contain '{browseName}'.");
            return -1;
        }

        private static IEnumerable<TestCaseData> EventMappings()
        {
            yield return Case(XRegistryEventKind.RegistryCreated, typeof(RegistryCreatedEventState));
            yield return Case(XRegistryEventKind.RegistryUpdated, typeof(RegistryUpdatedEventState));
            yield return Case(XRegistryEventKind.RegistryDeleted, typeof(RegistryDeletedEventState));
            yield return Case(XRegistryEventKind.ModelUpdated, typeof(ModelUpdatedEventState));
            yield return Case(XRegistryEventKind.ModelSourceUpdated, typeof(ModelSourceUpdatedEventState));
            yield return Case(XRegistryEventKind.CapabilitiesUpdated, typeof(CapabilitiesUpdatedEventState));
            yield return Case(XRegistryEventKind.GroupCreated, typeof(GroupCreatedEventState));
            yield return Case(XRegistryEventKind.GroupUpdated, typeof(GroupUpdatedEventState));
            yield return Case(XRegistryEventKind.GroupDeprecated, typeof(GroupDeprecatedEventState));
            yield return Case(XRegistryEventKind.GroupUndeprecated, typeof(GroupUndeprecatedEventState));
            yield return Case(XRegistryEventKind.GroupDeleted, typeof(GroupDeletedEventState));
            yield return Case(XRegistryEventKind.ResourceCreated, typeof(ResourceCreatedEventState));
            yield return Case(XRegistryEventKind.ResourceUpdated, typeof(ResourceUpdatedEventState));
            yield return Case(XRegistryEventKind.ResourceDeprecated, typeof(ResourceDeprecatedEventState));
            yield return Case(XRegistryEventKind.ResourceUndeprecated, typeof(ResourceUndeprecatedEventState));
            yield return Case(XRegistryEventKind.ResourceDeleted, typeof(ResourceDeletedEventState));
            yield return Case(XRegistryEventKind.VersionCreated, typeof(VersionCreatedEventState));
            yield return Case(XRegistryEventKind.VersionUpdated, typeof(VersionUpdatedEventState));
            yield return Case(XRegistryEventKind.VersionDeleted, typeof(VersionDeletedEventState));

            static TestCaseData Case(XRegistryEventKind kind, Type type)
                => new TestCaseData(kind, type).SetName($"Emitter_{kind}");
        }

        private static bool RequiresEpoch(XRegistryEventKind kind)
        {
            return kind is XRegistryEventKind.RegistryCreated or
                XRegistryEventKind.RegistryUpdated or
                XRegistryEventKind.GroupCreated or
                XRegistryEventKind.GroupUpdated or
                XRegistryEventKind.ResourceCreated or
                XRegistryEventKind.ResourceUpdated or
                XRegistryEventKind.VersionCreated or
                XRegistryEventKind.VersionUpdated;
        }

        private static bool RequiresMetaEpoch(XRegistryEventKind kind)
        {
            return kind is XRegistryEventKind.ResourceCreated or
                XRegistryEventKind.ResourceUpdated;
        }

        private static bool AllowsChanged(XRegistryEventKind kind)
        {
            return kind is XRegistryEventKind.RegistryUpdated or
                XRegistryEventKind.CapabilitiesUpdated or
                XRegistryEventKind.GroupUpdated or
                XRegistryEventKind.ResourceUpdated or
                XRegistryEventKind.VersionUpdated;
        }

        private static readonly string[] s_decodedChanged = ["meta.epoch", "versions"];
    }
}
