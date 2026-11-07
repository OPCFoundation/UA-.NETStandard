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
using System.Threading.Tasks;
using System.Xml;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Exercises additive factory commits against ordinary commits and publication ownership.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class EncodeableFactoryAdditiveTests
    {
        /// <summary>
        /// Preserves a caller mapping while completing missing datatype, encoding, enum and XML entries.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AdditiveCommitCompletesPartialMappingsWithoutReplacingCollisions(int registeredIndex)
        {
            var factory = new EncodeableFactory();
            ExpandedNodeId[] ids = [new(10001), new(10002), new(10003)];
            var enumId = new ExpandedNodeId(10004);
            var missingEnumId = new ExpandedNodeId(10005);
            IEncodeableType custom = CreateStructure("Substance");
            IEncodeableType generated = CreateStructure("Substance");
            IEnumeratedType customEnum = CreateEnumeration("Dictionary");
            IEnumeratedType generatedEnum = CreateEnumeration("Dictionary");
            IEnumeratedType missingEnum = CreateEnumeration("ResetMode");
            factory.Builder.AddEncodeableType(ids[registeredIndex], custom)
                .AddEnumeratedType(enumId, customEnum).Commit();

            IEncodeableFactoryBuilder builder = factory.Builder;
            foreach (ExpandedNodeId id in ids)
            {
                builder.AddEncodeableType(id, generated);
            }
            builder.AddEnumeratedType(enumId, generatedEnum)
                .AddEnumeratedType(missingEnumId, missingEnum).CommitIfAbsent();

            using (Assert.EnterMultipleScope())
            {
                for (int ii = 0; ii < ids.Length; ii++)
                {
                    Assert.That(factory.TryGetEncodeableType(ids[ii], out IEncodeableType? actual), Is.True);
                    Assert.That(actual, Is.SameAs(ii == registeredIndex ? custom : generated));
                }
                Assert.That(factory.TryGetEnumeratedType(enumId, out IEnumeratedType? actualEnum), Is.True);
                Assert.That(actualEnum, Is.SameAs(customEnum));
                Assert.That(factory.TryGetEnumeratedType(missingEnumId, out IEnumeratedType? addedEnum), Is.True);
                Assert.That(addedEnum, Is.SameAs(missingEnum));
                Assert.That(factory.TryGetType(custom.XmlName, out IType? xmlStructure), Is.True);
                Assert.That(xmlStructure, Is.SameAs(custom));
                Assert.That(factory.TryGetType(customEnum.XmlName, out IType? xmlEnum), Is.True);
                Assert.That(xmlEnum, Is.SameAs(customEnum));
                Assert.That(factory.TryGetType(missingEnum.XmlName, out IType? addedXml), Is.True);
                Assert.That(addedXml, Is.SameAs(missingEnum));
            }
        }

        /// <summary>
        /// An ordinary commit wins collisions whether it runs before or after an additive commit.
        /// </summary>
        [Test]
        public async Task ConcurrentOrdinaryCommitPreservesCallerMappingsAgainstAdditiveCommit()
        {
            var factory = new EncodeableFactory();
            var typeId = new ExpandedNodeId(10101);
            var enumId = new ExpandedNodeId(10102);
            var binaryId = new ExpandedNodeId(10103);
            IEncodeableType custom = CreateStructure("ConcurrentSubstance");
            IEncodeableType generated = CreateStructure("ConcurrentSubstance");
            IEnumeratedType customEnum = CreateEnumeration("ConcurrentDictionary");
            IEnumeratedType generatedEnum = CreateEnumeration("ConcurrentDictionary");
            IEncodeableFactoryBuilder ordinary = factory.Builder
                .AddEncodeableType(typeId, custom).AddEnumeratedType(enumId, customEnum);
            IEncodeableFactoryBuilder additive = factory.Builder
                .AddEncodeableType(typeId, generated).AddEncodeableType(binaryId, generated)
                .AddEnumeratedType(enumId, generatedEnum);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ordinaryReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var additiveReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task ordinaryCommit = Task.Run(async () =>
            {
                ordinaryReady.SetResult(true);
                await start.Task.ConfigureAwait(false);
                ordinary.Commit();
            });
            Task additiveCommit = Task.Run(async () =>
            {
                additiveReady.SetResult(true);
                await start.Task.ConfigureAwait(false);
                additive.CommitIfAbsent();
            });
            await Task.WhenAll(ordinaryReady.Task, additiveReady.Task).ConfigureAwait(false);
            start.SetResult(true);
            await Task.WhenAll(ordinaryCommit, additiveCommit).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.TryGetEncodeableType(typeId, out IEncodeableType? structure), Is.True);
                Assert.That(structure, Is.SameAs(custom));
                Assert.That(factory.TryGetEncodeableType(binaryId, out IEncodeableType? binary), Is.True);
                Assert.That(binary, Is.SameAs(generated));
                Assert.That(factory.TryGetEnumeratedType(enumId, out IEnumeratedType? enumeration), Is.True);
                Assert.That(enumeration, Is.SameAs(customEnum));
                Assert.That(factory.TryGetType(custom.XmlName, out IType? xmlStructure), Is.True);
                Assert.That(xmlStructure, Is.SameAs(custom));
                Assert.That(factory.TryGetType(customEnum.XmlName, out IType? xmlEnum), Is.True);
                Assert.That(xmlEnum, Is.SameAs(customEnum));
            }
        }

        /// <summary>
        /// A staged additive builder observes registrations committed by another builder before publication.
        /// </summary>
        [Test]
        public void AdditiveCommitPreservesMappingsRegisteredAfterBuilderCreation()
        {
            var factory = new EncodeableFactory();
            var typeId = new ExpandedNodeId(10201);
            IEncodeableType generated = CreateStructure("LateSubstance");
            IEncodeableType custom = CreateStructure("LateSubstance");
            IEncodeableFactoryBuilder additive = factory.Builder.AddEncodeableType(typeId, generated);
            Assert.That(factory.TryGetEncodeableType(typeId, out _), Is.False);
            factory.Builder.AddEncodeableType(typeId, custom).Commit();

            additive.CommitIfAbsent();
            additive.CommitIfAbsent();

            Assert.That(factory.TryGetEncodeableType(typeId, out IEncodeableType? actual), Is.True);
            Assert.That(actual, Is.SameAs(custom));
            Assert.That(factory.TryGetType(custom.XmlName, out IType? xmlType), Is.True);
            Assert.That(xmlType, Is.SameAs(custom));
        }

        /// <summary>
        /// An existing registration of the opposite kind cannot be shadowed by an additive commit.
        /// </summary>
        [Test]
        public void AdditiveCommitPreservesIdentifiersAcrossTypeKinds()
        {
            var factory = new EncodeableFactory();
            var structureId = new ExpandedNodeId(10301);
            var enumId = new ExpandedNodeId(10302);
            IEncodeableType structure = CreateStructure("CrossKindSubstance");
            IEnumeratedType enumeration = CreateEnumeration("CrossKindDictionary");
            factory.Builder.AddEncodeableType(structureId, structure)
                .AddEnumeratedType(enumId, enumeration).Commit();

            factory.Builder.AddEnumeratedType(structureId, enumeration)
                .AddEncodeableType(enumId, structure).CommitIfAbsent();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.TryGetEncodeableType(structureId, out IEncodeableType? actual), Is.True);
                Assert.That(actual, Is.SameAs(structure));
                Assert.That(factory.TryGetEnumeratedType(structureId, out _), Is.False);
                Assert.That(factory.TryGetEnumeratedType(enumId, out IEnumeratedType? actualEnum), Is.True);
                Assert.That(actualEnum, Is.SameAs(enumeration));
                Assert.That(factory.TryGetEncodeableType(enumId, out _), Is.False);
            }
        }

        /// <summary>
        /// Missing identifiers are added without changing an XML-name mapping of the opposite kind.
        /// </summary>
        [Test]
        public void AdditiveCommitPreservesXmlNamesAcrossTypeKinds()
        {
            var factory = new EncodeableFactory();
            var structureId = new ExpandedNodeId(10311);
            var enumId = new ExpandedNodeId(10312);
            var missingStructureId = new ExpandedNodeId(10313);
            var missingEnumId = new ExpandedNodeId(10314);
            IEncodeableType structure = CreateStructure("SharedStructureName");
            IEnumeratedType enumeration = CreateEnumeration("SharedEnumName");
            IEncodeableType addedStructure = CreateStructure("SharedEnumName");
            IEnumeratedType addedEnum = CreateEnumeration("SharedStructureName");
            factory.Builder.AddEncodeableType(structureId, structure)
                .AddEnumeratedType(enumId, enumeration).Commit();

            factory.Builder.AddEncodeableType(missingStructureId, addedStructure)
                .AddEnumeratedType(missingEnumId, addedEnum).CommitIfAbsent();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(factory.TryGetType(structure.XmlName, out IType? xmlStructure), Is.True);
                Assert.That(xmlStructure, Is.SameAs(structure));
                Assert.That(factory.TryGetType(enumeration.XmlName, out IType? xmlEnum), Is.True);
                Assert.That(xmlEnum, Is.SameAs(enumeration));
                Assert.That(factory.TryGetEncodeableType(
                    missingStructureId, out IEncodeableType? actualStructure), Is.True);
                Assert.That(actualStructure, Is.SameAs(addedStructure));
                Assert.That(factory.TryGetEnumeratedType(missingEnumId, out IEnumeratedType? actualEnum), Is.True);
                Assert.That(actualEnum, Is.SameAs(addedEnum));
            }
        }

        /// <summary>
        /// Additive commits update the owning factory without changing an independent snapshot.
        /// </summary>
        [Test]
        public void AdditiveCommitUsesOwningFactoryImage()
        {
            var factory = new EncodeableFactory();
            EncodeableFactory snapshot = factory.Fork();
            var id = new ExpandedNodeId(10321);
            IEncodeableType structure = CreateStructure("SelectedSubstance");
            factory.Builder.AddEncodeableType(id, structure).CommitIfAbsent();

            Assert.That(factory.TryGetEncodeableType(id, out IEncodeableType? actual), Is.True);
            Assert.That(actual, Is.SameAs(structure));
            Assert.That(snapshot.TryGetEncodeableType(id, out _), Is.False);
        }

        /// <summary>
        /// Additive commits are blocked only while publication owns the factory.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveCommitIsBlockedOnlyDuringPublication(bool publish)
        {
            var factory = new EncodeableFactory();
            _ = factory.CaptureSnapshot(out EncodeableFactory source, out long revision);
            var id = new ExpandedNodeId(10401);
            IEncodeableType structure = CreateStructure("PublishedSubstance");
            IEncodeableFactoryBuilder builder = factory.Builder.AddEncodeableType(id, structure);
            using (EncodeableFactory.Publication publication = factory.BeginPublication(source, revision))
            {
                Assert.Throws<InvalidOperationException>(builder.CommitIfAbsent);
                Assert.That(factory.TryGetEncodeableType(id, out _), Is.False);
                if (publish)
                {
                    publication.Complete(factory);
                }
            }
            builder.CommitIfAbsent();
            Assert.That(factory.TryGetEncodeableType(id, out IEncodeableType? actual), Is.True);
            Assert.That(actual, Is.SameAs(structure));
        }

        /// <summary>
        /// An unsupported builder is rejected without calling its ordinary commit.
        /// </summary>
        [Test]
        public void UnsupportedBuilderCannotFallBackToOverwritingCommit()
        {
            var builder = new Mock<IEncodeableFactoryBuilder>();
            Assert.Throws<NotSupportedException>(builder.Object.CommitIfAbsent);
            builder.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A null builder is rejected before capability inspection.
        /// </summary>
        [Test]
        public void NullBuilderIsRejected()
        {
            IEncodeableFactoryBuilder builder = null!;
            Assert.Throws<ArgumentNullException>(builder.CommitIfAbsent);
        }

        /// <summary>
        /// Creates a structure activator with a controlled XML-name collision.
        /// </summary>
        private static IEncodeableType CreateStructure(string name)
        {
            var type = new Mock<IEncodeableType>();
            type.SetupGet(value => value.XmlName).Returns(new XmlQualifiedName(name, "urn:additive:factory"));
            return type.Object;
        }

        /// <summary>
        /// Creates an enumeration activator with a controlled XML-name collision.
        /// </summary>
        private static IEnumeratedType CreateEnumeration(string name)
        {
            var type = new Mock<IEnumeratedType>();
            type.SetupGet(value => value.XmlName).Returns(new XmlQualifiedName(name, "urn:additive:factory"));
            return type.Object;
        }
    }
}
