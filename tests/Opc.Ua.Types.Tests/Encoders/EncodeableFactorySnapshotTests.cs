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
    [TestFixture]
    [Parallelizable]
    public sealed class EncodeableFactorySnapshotTests
    {
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
                Assert.That(snapshot.KnownTypeIds, Is.EquivalentTo(new[] { s_structureId, s_enumId }));
                Assert.That(snapshot.TryGetEncodeableType(s_structureId, out IEncodeableType stored), Is.True);
                Assert.That(stored, Is.SameAs(structure));
                Assert.That(snapshot.TryGetEnumeratedType(s_enumId, out IEnumeratedType storedEnum), Is.True);
                Assert.That(storedEnum, Is.SameAs(enumeration));
                Assert.That(snapshot.TryGetType(structure.XmlName, out IType xmlStructure), Is.True);
                Assert.That(xmlStructure, Is.SameAs(structure));
                Assert.That(snapshot.TryGetType(enumeration.XmlName, out IType xmlEnum), Is.True);
                Assert.That(xmlEnum, Is.SameAs(enumeration));
            }
            using EncodeableFactory.Publication publication = factory.BeginPublication(source, revision);
            Assert.That(snapshot.TryGetEncodeableType(s_structureId, out _), Is.True);
        }

        [Test]
        public void SelectedFactoryRoutesBuildersLookupsAndForks()
        {
            var factory = new EncodeableFactory();
            EncodeableFactory selected = factory.Fork();
            IEncodeableType structure = CreateStructure();
            IEnumeratedType enumeration = CreateEnumeration();
            factory.SetViewSelector(() => selected);
            factory.Builder.AddEncodeableType(s_structureId, structure).AddEnumeratedType(s_enumId, enumeration).Commit();
            EncodeableFactory fork = factory.Fork();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType encoded), Is.True);
                Assert.That(encoded, Is.SameAs(structure));
                Assert.That(factory.TryGetEnumeratedType(s_enumId, out IEnumeratedType enumerated), Is.True);
                Assert.That(enumerated, Is.SameAs(enumeration));
                Assert.That(factory.TryGetType(structure.XmlName, out IType xmlType), Is.True);
                Assert.That(xmlType, Is.SameAs(structure));
                Assert.That(factory.KnownTypeIds, Is.EquivalentTo(fork.KnownTypeIds));
            }
            selected = new EncodeableFactory();
            Assert.That(factory.KnownTypeIds, Is.Empty);
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.False);
            Assert.That(fork.TryGetEncodeableType(s_structureId, out IEncodeableType retained), Is.True);
            Assert.That(retained, Is.SameAs(structure));
        }

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
                Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType stored), Is.True);
                Assert.That(stored, Is.SameAs(structure));
            }
        }

        [Test]
        public void ChangedFactoryCannotPublishAnEarlierCapture()
        {
            var factory = new EncodeableFactory();
            _ = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            factory.Builder.AddEncodeableType(s_structureId, CreateStructure()).Commit();
            Assert.Throws<InvalidOperationException>(() => factory.BeginPublication(source, revision));
            Assert.That(factory.TryGetEncodeableType(s_structureId, out _), Is.True);
        }

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

        [Test]
        public void ViewOwnerReleasePreservesRegistrationsAndAllowsReuse()
        {
            var factory = new EncodeableFactory();
            EncodeableFactory selected = factory.Fork();
            EncodeableFactory.ViewOwner owner = factory.SetViewSelector(() => selected);
            IEncodeableType structure = CreateStructure();
            IEnumeratedType enumeration = CreateEnumeration();
            factory.Builder.AddEncodeableType(s_structureId, structure).AddEnumeratedType(s_enumId, enumeration).Commit();
            owner.Release(selected);
            owner.Release(selected);
            Assert.That(factory.TryGetEncodeableType(s_structureId, out IEncodeableType retained), Is.True);
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
