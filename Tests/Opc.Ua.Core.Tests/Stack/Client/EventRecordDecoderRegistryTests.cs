/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/
using System.Collections.Generic;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Unit tests for <see cref="EventRecordDecoderRegistry"/> covering
    /// the registration semantics, dispatch routing, composed
    /// <c>StandardFields</c>, child-scope inheritance, and super-type
    /// fallback walk.
    /// </summary>
    [TestFixture]
    [Category("Core")]
    [Category("EventRecord")]
    [Parallelizable]
    public sealed class EventRecordDecoderRegistryTests
    {
        [Test]
        public void TryRegisterReturnsTrueForNewEntry()
        {
            var registry = new EventRecordDecoderRegistry();

            bool added = registry.TryRegister(
                eventTypeId: ObjectTypeIds.BaseEventType,
                standardFields: BaseEventTypeRecord.Decoder.StandardFields,
                decode: BaseEventTypeRecord.Decoder.Decode);

            Assert.That(added, Is.True);
        }

        [Test]
        public void TryRegisterReturnsFalseForDuplicate()
        {
            var registry = new EventRecordDecoderRegistry();
            registry.TryRegister(
                ObjectTypeIds.BaseEventType,
                BaseEventTypeRecord.Decoder.StandardFields,
                BaseEventTypeRecord.Decoder.Decode);

            bool added = registry.TryRegister(
                ObjectTypeIds.BaseEventType,
                BaseEventTypeRecord.Decoder.StandardFields,
                BaseEventTypeRecord.Decoder.Decode);

            Assert.That(added, Is.False);
        }

        [Test]
        public void RegisterThrowsForDuplicate()
        {
            var registry = new EventRecordDecoderRegistry();
            registry.Register(
                ObjectTypeIds.BaseEventType,
                BaseEventTypeRecord.Decoder.StandardFields,
                BaseEventTypeRecord.Decoder.Decode);

            Assert.That(() => registry.Register(
                ObjectTypeIds.BaseEventType,
                BaseEventTypeRecord.Decoder.StandardFields,
                BaseEventTypeRecord.Decoder.Decode),
                Throws.InvalidOperationException);
        }

        [Test]
        public void DefaultRegistryDecodesBaseEventTypeRecord()
        {
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default;

            // Default ships with the standard UA model pre-registered.
            // A fields array with just the EventType pointing at
            // BaseEventType should resolve to a BaseEventTypeRecord.
            int eventTypePosition = FindEventTypeIndex(registry.StandardFields);
            Assert.That(eventTypePosition, Is.GreaterThanOrEqualTo(0),
                "Default registry must expose the EventType browse path "
                + "for filter construction.");

            Variant[] fields = new Variant[registry.StandardFields.Length];
            fields[eventTypePosition] = Variant.From((NodeId)ObjectTypeIds.BaseEventType);

            EventRecord record = registry.Decode(fields);

            Assert.That(record, Is.Not.Null);
            Assert.That(record, Is.InstanceOf<BaseEventTypeRecord>());
        }

        [Test]
        public void DecodeReturnsNullWhenFieldsEmpty()
        {
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default;
            Assert.That(registry.Decode(System.Array.Empty<Variant>()), Is.Null);
        }

        [Test]
        public void ChildScopeInheritsParentRegistrations()
        {
            EventRecordDecoderRegistry parent = EventRecordDecoderRegistry.Default;
            EventRecordDecoderRegistry child = parent.CreateChildScope();

            // The child inherits the parent's registrations transparently
            // — composed StandardFields contains every parent path.
            Assert.That(child.StandardFields,
                Has.Length.EqualTo(parent.StandardFields.Length));

            // New registrations on the child do not affect the parent.
            int parentCount = parent.StandardFields.Length;
            child.TryRegister(
                new NodeId(987654321u),
                [[new QualifiedName("VendorField", 0)]],
                _ => null);
            Assert.That(child.StandardFields, Has.Length.GreaterThan(parentCount));
            Assert.That(parent.StandardFields, Has.Length.EqualTo(parentCount),
                "Child mutations must not leak into the parent scope.");
        }

        [Test]
        public void DecodeAsWalksSuperTypeChain()
        {
            var registry = new EventRecordDecoderRegistry();
            registry.TryRegister(
                ObjectTypeIds.BaseEventType,
                BaseEventTypeRecord.Decoder.StandardFields,
                BaseEventTypeRecord.Decoder.Decode);

            // Simulate a vendor event type the registry has never seen
            // — the super-type resolver walks up to BaseEventType which
            // IS registered.
            var unknownVendor = new NodeId(42u, 5);
            registry.SuperTypeResolver = current =>
                current == unknownVendor ? (NodeId)ObjectTypeIds.BaseEventType : null;

            EventRecord record = registry.DecodeAs(
                unknownVendor,
                new Variant[] { default, Variant.From((NodeId)unknownVendor) });

            Assert.That(record, Is.InstanceOf<BaseEventTypeRecord>());
        }

        [Test]
        public void DecodeAsReturnsNullWhenNoAncestorRegistered()
        {
            var registry = new EventRecordDecoderRegistry();
            var unknownType = new NodeId(987u, 7);

            EventRecord record = registry.DecodeAs(unknownType, new[] { Variant.From(1) });

            Assert.That(record, Is.Null);
        }

        [Test]
        public void StandardFieldsContainsEventTypePath()
        {
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default;
            int eventTypeIndex = FindEventTypeIndex(registry.StandardFields);

            Assert.That(eventTypeIndex, Is.GreaterThanOrEqualTo(0),
                "EventType browse path must be present in the composed "
                + "StandardFields so the registry can route on it.");
        }

        [Test]
        public void TryRegisterThrowsForNullEventTypeId()
        {
            var registry = new EventRecordDecoderRegistry();
            Assert.That(() => registry.TryRegister(
                NodeId.Null,
                BaseEventTypeRecord.Decoder.StandardFields,
                BaseEventTypeRecord.Decoder.Decode),
                Throws.ArgumentException);
        }

        [Test]
        public void DecodeRemapsCallerFieldsToDecoderLocalLayout()
        {
            // Build a registry with two distinct decoders. The composed
            // StandardFields contains both layouts deduplicated, with
            // positions different from each decoder's local layout. The
            // registry must remap before invoking the decoder.
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default;

            QualifiedName[][] composed = registry.StandardFields;
            int eventTypePosition = FindEventTypeIndex(composed);
            int severityPosition = FindPath(composed, BrowseNames.Severity);
            Assert.That(eventTypePosition, Is.GreaterThanOrEqualTo(0));
            Assert.That(severityPosition, Is.GreaterThanOrEqualTo(0));

            Variant[] fields = new Variant[composed.Length];
            fields[eventTypePosition] = Variant.From((NodeId)ObjectTypeIds.BaseEventType);
            fields[severityPosition] = Variant.From((ushort)999);

            EventRecord record = registry.Decode(fields);

            Assert.That(record, Is.InstanceOf<BaseEventTypeRecord>());
            BaseEventTypeRecord baseRecord = (BaseEventTypeRecord)record;
            Assert.That(baseRecord.Severity, Is.EqualTo((ushort)999),
                "Registry must remap composed-layout fields to each "
                + "decoder's local positional layout before decoding.");
        }

        [Test]
        public void DecodeReturnsNullForExplicitNullEventTypeField()
        {
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default;
            int eventTypePosition = FindEventTypeIndex(registry.StandardFields);

            Variant[] fields = new Variant[registry.StandardFields.Length];
            fields[eventTypePosition] = Variant.From(NodeId.Null);

            EventRecord record = registry.Decode(fields);

            Assert.That(record, Is.Null);
        }

        private static int FindPath(QualifiedName[][] standardFields, string browseName)
        {
            for (int i = 0; i < standardFields.Length; i++)
            {
                QualifiedName[] path = standardFields[i];
                if (path.Length == 1 &&
                    path[0].NamespaceIndex == 0 &&
                    path[0].Name == browseName)
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindEventTypeIndex(QualifiedName[][] standardFields)
        {
            for (int i = 0; i < standardFields.Length; i++)
            {
                QualifiedName[] path = standardFields[i];
                if (path.Length == 1 &&
                    path[0].NamespaceIndex == 0 &&
                    path[0].Name == BrowseNames.EventType)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
