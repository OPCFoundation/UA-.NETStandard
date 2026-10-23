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
using System.Xml;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Contains tests for encodeable factory snapshot.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class EncodeableFactorySnapshotTests
    {
        /// <summary>
        /// Verifies that the unchanged-capture shortcut still produces independently owned registration maps.
        /// </summary>
        [Test]
        public void UnchangedAdditiveMergeOwnsItsRegistrationMaps()
        {
            var factory = new EncodeableFactory();
            IEncodeableType serving = CreateTypedStructure(typeof(StructureDefinition));
            factory.Builder.AddEncodeableType(s_structureId, serving).Commit();
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            EncodeableFactory merged = factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable());
            merged.Builder.AddEnumeratedType(s_enumId, CreateEnumeration()).Commit();

            Assert.That(proposed.TryGetEnumeratedType(s_enumId, out _), Is.False);
            Assert.That(factory.TryGetEnumeratedType(s_enumId, out _), Is.False);
            Assert.That(merged.TryGetEnumeratedType(s_enumId, out _), Is.True);
        }

        /// <summary>
        /// Verifies that matching CLR identities do not hide asymmetric definition-source capabilities.
        /// </summary>
        /// <param name="sourceHasDefinition">Whether only the serving activator exposes a definition.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveMergeRejectsAsymmetricDefinitionSources(bool sourceHasDefinition)
        {
            var factory = new EncodeableFactory();
            StructureDefinition definition = CreateDefinition(DataTypeIds.Int32);
            IEncodeableType serving = CreateTypedStructure(
                typeof(StructureDefinition), definition: sourceHasDefinition ? definition : null);
            factory.Builder.AddEncodeableType(s_structureId, serving).Commit();
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            proposed.Builder.AddEncodeableType(s_structureId, CreateTypedStructure(
                typeof(StructureDefinition), definition: sourceHasDefinition ? null : definition)).Commit();

            Assert.Throws<InvalidOperationException>(() =>
                factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable()));
        }

        /// <summary>
        /// Verifies that additive preparation preserves serving activators and owns its maps.
        /// </summary>
        [Test]
        public void AdditiveMergePreservesServingActivatorsAndIndependentMaps()
        {
            var factory = new EncodeableFactory();
            IEncodeableType serving = CreateTypedStructure(typeof(StructureDefinition));
            factory.Builder.AddEncodeableType(s_structureId, serving).Commit();
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            proposed.Builder
                .AddEncodeableType(s_structureId, CreateTypedStructure(typeof(StructureDefinition)))
                .AddEnumeratedType(s_enumId, CreateEnumeration()).Commit();

            EncodeableFactory merged = factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable());
            using (Assert.EnterMultipleScope())
            {
                Assert.That(merged.TryGetEncodeableType(s_structureId, out IEncodeableType? retained), Is.True);
                Assert.That(retained, Is.SameAs(serving));
                Assert.That(merged.TryGetType(serving.XmlName, out IType? named), Is.True);
                Assert.That(named, Is.SameAs(serving));
                Assert.That(merged.TryGetEnumeratedType(s_enumId, out _), Is.True);
                Assert.That(factory.TryGetEnumeratedType(s_enumId, out _), Is.False);
            }
            ExpandedNodeId additional = new(9003, kNamespace);
            merged.Builder.AddEncodeableType(additional, serving).Commit();
            Assert.That(proposed.TryGetEncodeableType(additional, out _), Is.False);
        }

        /// <summary>
        /// Verifies that serving registrations cannot change between structures and enumerations.
        /// </summary>
        /// <param name="structureFirst">Whether the serving registration is a structure.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveMergeRejectsRegistrationKindChanges(bool structureFirst)
        {
            var factory = new EncodeableFactory();
            if (structureFirst)
            {
                factory.Builder.AddEncodeableType(s_structureId, CreateStructure()).Commit();
            }
            else
            {
                factory.Builder.AddEnumeratedType(s_structureId, CreateEnumeration()).Commit();
            }
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            if (structureFirst)
            {
                proposed.Builder.AddEnumeratedType(s_structureId, CreateEnumeration()).Commit();
            }
            else
            {
                proposed.Builder.AddEncodeableType(s_structureId, CreateStructure()).Commit();
            }
            Assert.Throws<InvalidOperationException>(() =>
                factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable()));
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.EqualTo(structureFirst));
            Assert.That(factory.TryGetEnumeratedType(s_structureId, out _), Is.EqualTo(!structureFirst));
        }

        /// <summary>
        /// Verifies that serving registrations cannot change CLR type or XML name.
        /// </summary>
        /// <param name="changeName">Whether the XML name rather than the CLR type changes.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveMergeRejectsActivatorIdentityChanges(bool changeName)
        {
            var factory = new EncodeableFactory();
            IEncodeableType serving = CreateTypedStructure(typeof(StructureDefinition));
            factory.Builder.AddEncodeableType(s_structureId, serving).Commit();
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            IEncodeableType replacement = CreateTypedStructure(
                changeName ? typeof(StructureDefinition) : typeof(EnumDefinition),
                changeName ? new XmlQualifiedName("Renamed", kNamespace) : serving.XmlName);
            proposed.Builder.AddEncodeableType(s_structureId, replacement).Commit();

            Assert.Throws<InvalidOperationException>(() =>
                factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable()));
            Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType? retained), Is.True);
            Assert.That(retained, Is.SameAs(serving));
        }

        /// <summary>
        /// Verifies that structural definitions are checked even when CLR types are equal.
        /// </summary>
        /// <param name="compatible">Whether the staged definition preserves the field data type.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveMergeChecksDataTypeDefinitions(bool compatible)
        {
            var factory = new EncodeableFactory();
            IEncodeableType serving = CreateTypedStructure(
                typeof(StructureDefinition), definition: CreateDefinition(DataTypeIds.Int32));
            factory.Builder.AddEncodeableType(s_structureId, serving).Commit();
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            proposed.Builder.AddEncodeableType(s_structureId, CreateTypedStructure(
                typeof(StructureDefinition),
                definition: CreateDefinition(compatible ? DataTypeIds.Int32 : DataTypeIds.String))).Commit();

            if (compatible)
            {
                EncodeableFactory merged = factory.PrepareAdditiveMerge(
                    proposed, source, revision, new NamespaceTable());
                Assert.That(merged.TryGetEncodeableType(s_structureId, out IEncodeableType? retained), Is.True);
                Assert.That(retained, Is.SameAs(serving));
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() =>
                    factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable()));
            }
            Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType? original), Is.True);
            Assert.That(original, Is.SameAs(serving));
        }

        /// <summary>
        /// Verifies that a new identifier cannot steal a serving XML registration.
        /// </summary>
        [Test]
        public void AdditiveMergeRejectsXmlNameCollisions()
        {
            var factory = new EncodeableFactory();
            IEncodeableType serving = CreateTypedStructure(typeof(StructureDefinition));
            factory.Builder.AddEncodeableType(s_structureId, serving).Commit();
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            proposed.Builder.AddEncodeableType(
                new ExpandedNodeId(9003, kNamespace),
                CreateTypedStructure(typeof(EnumDefinition), serving.XmlName)).Commit();

            Assert.Throws<InvalidOperationException>(() =>
                factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable()));
            Assert.That(factory.KnownTypeIds, Is.EqualTo(new[] { s_structureId }));
        }

        /// <summary>
        /// Verifies that preparation rejects changed serving identity or revision.
        /// </summary>
        /// <param name="changeRevision">Whether the revision rather than the source identity changes.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveMergeRejectsChangedSource(bool changeRevision)
        {
            var factory = new EncodeableFactory();
            EncodeableFactory proposed = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            if (changeRevision)
            {
                factory.Builder.AddEncodeableType(s_structureId, CreateStructure()).Commit();
            }
            else
            {
                source = new EncodeableFactory();
            }
            Assert.Throws<InvalidOperationException>(() =>
                factory.PrepareAdditiveMerge(proposed, source, revision, new NamespaceTable()));
        }

        /// <summary>
        /// Verifies that snapshot owns encodeable enumerated and XML maps.
        /// </summary>
        [Test]
        public void SnapshotOwnsEncodeableEnumeratedAndXmlMaps()
        {
            var factory = new EncodeableFactory();
            EncodeableFactory snapshot = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            IEncodeableType structure = CreateStructure();
            IEnumeratedType enumeration = CreateEnumeration();
            snapshot.Builder.AddEncodeableType(s_structureId, structure)
                .AddEnumeratedType(s_enumId, enumeration).Commit();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(source, Is.SameAs(factory));
                Assert.That(factory.KnownTypeIds, Is.Empty);
                Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.False);
                Assert.That(factory.TryGetEnumeratedType(s_enumId, out _), Is.False);
                Assert.That(factory.TryGetType(structure.XmlName, out _), Is.False);
                Assert.That(factory.TryGetType(enumeration.XmlName, out _), Is.False);
                Assert.That(snapshot.KnownTypeIds, Is.EquivalentTo([s_structureId, s_enumId]));
                Assert.That(snapshot.TryGetEncodeableType(s_structureId, out IEncodeableType? stored), Is.True);
                Assert.That(stored, Is.SameAs(structure));
                Assert.That(snapshot.TryGetEnumeratedType(s_enumId, out IEnumeratedType? storedEnum), Is.True);
                Assert.That(storedEnum, Is.SameAs(enumeration));
                Assert.That(snapshot.TryGetType(structure.XmlName, out IType? xmlStructure), Is.True);
                Assert.That(xmlStructure, Is.SameAs(structure));
                Assert.That(snapshot.TryGetType(enumeration.XmlName, out IType? xmlEnum), Is.True);
                Assert.That(xmlEnum, Is.SameAs(enumeration));
            }
            using EncodeableFactory.Publication publication = factory.BeginPublication(source, revision);
            Assert.That(snapshot.TryGetEncodeableType(s_structureId, out _), Is.True);
        }

        /// <summary>
        /// Verifies that selected factory routes builders lookups and forks.
        /// </summary>
        [Test]
        public void SelectedFactoryRoutesBuildersLookupsAndForks()
        {
            var factory = new EncodeableFactory();
            EncodeableFactory selected = factory.Fork();
            IEncodeableType structure = CreateStructure();
            IEnumeratedType enumeration = CreateEnumeration();
            factory.SetViewSelector(() => selected);
            factory.Builder.AddEncodeableType(s_structureId, structure)
                .AddEnumeratedType(s_enumId, enumeration).Commit();
            EncodeableFactory fork = factory.Fork();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType? encoded), Is.True);
                Assert.That(encoded, Is.SameAs(structure));
                Assert.That(factory.TryGetEnumeratedType(s_enumId, out IEnumeratedType? enumerated), Is.True);
                Assert.That(enumerated, Is.SameAs(enumeration));
                Assert.That(factory.TryGetType(structure.XmlName, out IType? xmlType), Is.True);
                Assert.That(xmlType, Is.SameAs(structure));
                Assert.That(factory.KnownTypeIds, Is.EquivalentTo(fork.KnownTypeIds));
            }
            selected = new EncodeableFactory();
            Assert.That(factory.KnownTypeIds, Is.Empty);
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.False);
            Assert.That(fork.TryGetEncodeableType(s_structureId, out IEncodeableType? retained), Is.True);
            Assert.That(retained, Is.SameAs(structure));
        }

        /// <summary>
        /// Verifies that publication protects pending and retired factory images.
        /// </summary>
        /// <param name="publish">The publish flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void PublicationProtectsPendingAndRetiredFactoryImages(bool publish)
        {
            var factory = new EncodeableFactory();
            _ = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            IEncodeableType structure = CreateStructure();
            IEncodeableFactoryBuilder builder = factory.Builder.AddEncodeableType(s_structureId, structure);
            using (EncodeableFactory.Publication publication = factory.BeginPublication(source, revision))
            {
                Assert.Throws<InvalidOperationException>(builder.Commit);
                Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.False);
                Assert.That(factory.TryGetType(structure.XmlName, out _), Is.False);
                factory.Builder.Commit();
                if (publish)
                {
                    publication.Complete();
                }
            }
            if (publish)
            {
                Assert.Throws<InvalidOperationException>(builder.Commit);
                Assert.That(factory.KnownTypeIds, Is.Empty);
            }
            else
            {
                builder.Commit();
                Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType? stored), Is.True);
                Assert.That(stored, Is.SameAs(structure));
            }
        }

        /// <summary>
        /// Verifies that changed factory cannot publish an earlier capture.
        /// </summary>
        [Test]
        public void ChangedFactoryCannotPublishAnEarlierCapture()
        {
            var factory = new EncodeableFactory();
            _ = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            factory.Builder.AddEncodeableType(s_structureId, CreateStructure()).Commit();
            Assert.Throws<InvalidOperationException>(() => factory.BeginPublication(source, revision));
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.True);
        }

        /// <summary>
        /// Verifies that publication releases once and rejects another owner.
        /// </summary>
        [Test]
        public void PublicationReleasesOnceAndRejectsAnotherOwner()
        {
            var factory = new EncodeableFactory();
            _ = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            using EncodeableFactory.Publication publication = factory.BeginPublication(source, revision);
            Assert.Throws<InvalidOperationException>(() => factory.BeginPublication(source, revision));
            publication.Dispose();
            publication.Dispose();
            Assert.Throws<ObjectDisposedException>(publication.Complete);
            factory.Builder.AddEncodeableType(s_structureId, CreateStructure()).Commit();
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.True);
        }

        /// <summary>
        /// Verifies that builder captured before ownership uses selected factory.
        /// </summary>
        [Test]
        public void BuilderCapturedBeforeOwnershipUsesSelectedFactory()
        {
            var factory = new EncodeableFactory();
            IEncodeableFactoryBuilder builder = factory.Builder.AddEncodeableType(s_structureId, CreateStructure());
            EncodeableFactory selected = factory.Fork();
            factory.SetViewSelector(() => selected);
            builder.Commit();
            Assert.That(selected.TryGetEncodeableType(s_structureId, out _), Is.True);
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.True);
        }

        /// <summary>
        /// Verifies that view owner release preserves registrations and allows reuse.
        /// </summary>
        [Test]
        public void ViewOwnerReleasePreservesRegistrationsAndAllowsReuse()
        {
            var factory = new EncodeableFactory();
            EncodeableFactory selected = factory.Fork();
            EncodeableFactory.ViewOwner owner = factory.SetViewSelector(() => selected);
            IEncodeableType structure = CreateStructure();
            IEnumeratedType enumeration = CreateEnumeration();
            factory.Builder.AddEncodeableType(s_structureId, structure)
                .AddEnumeratedType(s_enumId, enumeration).Commit();
            owner.Release(selected);
            owner.Release(selected);
            Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType? retained), Is.True);
            Assert.That(retained, Is.SameAs(structure));
            Assert.That(factory.TryGetEnumeratedType(s_enumId, out _), Is.True);
            Assert.That(factory.TryGetType(structure.XmlName, out _), Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                selected.Builder.AddEncodeableType(s_structureId, structure).Commit());
            EncodeableFactory next = factory.Fork();
            EncodeableFactory.ViewOwner nextOwner = factory.SetViewSelector(() => next);
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.True);
            nextOwner.Release(next);
        }

        /// <summary>
        /// Verifies that view owner release can retry after pending publication.
        /// </summary>
        [Test]
        public void ViewOwnerReleaseCanRetryAfterPendingPublication()
        {
            var factory = new EncodeableFactory();
            EncodeableFactory selected = factory.CaptureSnapshot(out _, out _);
            EncodeableFactory.ViewOwner owner = factory.SetViewSelector(() => selected);
            _ = selected.CaptureSnapshot(out EncodeableFactory source, out long revision);
            using (EncodeableFactory.Publication publication = selected.BeginPublication(source, revision))
            {
                Assert.Throws<InvalidOperationException>(() => owner.Release(selected));
            }
            owner.Release(selected);
            factory.Builder.AddEncodeableType(s_structureId, CreateStructure()).Commit();
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.True);
        }

        /// <summary>
        /// Creates an activator with a stable CLR identity and optional structural definition.
        /// </summary>
        /// <param name="systemType">The CLR identity.</param>
        /// <param name="name">The XML name, or the fixture default.</param>
        /// <param name="definition">The definition exposed by the activator, when available.</param>
        /// <returns>The activator.</returns>
        private static IEncodeableType CreateTypedStructure(
            Type systemType,
            XmlQualifiedName? name = null,
            DataTypeDefinition? definition = null)
        {
            var type = new Mock<IEncodeableType>();
            type.SetupGet(value => value.Type).Returns(systemType);
            type.SetupGet(value => value.XmlName).Returns(name ?? new XmlQualifiedName("Structure", kNamespace));
            if (definition is not null)
            {
                type.As<IDataTypeDefinitionSource>()
                    .Setup(value => value.GetDataTypeDefinition(It.IsAny<NamespaceTable>())).Returns(definition);
            }
            return type.Object;
        }

        /// <summary>
        /// Creates a structure definition whose field type participates in compatibility.
        /// </summary>
        /// <param name="fieldType">The field data type.</param>
        /// <returns>The definition.</returns>
        private static StructureDefinition CreateDefinition(NodeId fieldType)
        {
            return new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField
                    {
                        Name = "Value",
                        DataType = fieldType,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };
        }

        private static IEncodeableType CreateStructure()
        {
            var type = new Mock<IEncodeableType>();
            type.SetupGet(value => value.XmlName).Returns(new XmlQualifiedName("Structure", kNamespace));
            return type.Object;
        }

        private static IEnumeratedType CreateEnumeration()
        {
            var type = new Mock<IEnumeratedType>();
            type.SetupGet(value => value.XmlName).Returns(new XmlQualifiedName("Enumeration", kNamespace));
            return type.Object;
        }

        private const string kNamespace = "urn:opcfoundation.org:Tests:FactorySnapshots";
        private static readonly ExpandedNodeId s_structureId = new(9001, kNamespace);
        private static readonly ExpandedNodeId s_enumId = new(9002, kNamespace);
    }
}
