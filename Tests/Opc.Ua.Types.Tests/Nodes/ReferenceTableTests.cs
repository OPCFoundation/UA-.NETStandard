/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Nodes
{
    /// <summary>
    /// Unit tests for the <see cref="ReferenceCollection"/> class.
    /// </summary>
    [TestFixture]
    [Category("ReferenceTable")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceCollectionTests
    {
        private static NodeId RefType1 => new NodeId(1u);
        private static NodeId RefType2 => new NodeId(2u);
        private static NodeId RefType3 => new NodeId(3u);
        private static ExpandedNodeId Target1 => new ExpandedNodeId(100u);
        private static ExpandedNodeId Target2 => new ExpandedNodeId(200u);
        private static ExpandedNodeId Target3 => new ExpandedNodeId(300u);
        private static ExpandedNodeId AbsoluteTarget1 => new ExpandedNodeId(100u, "http://example.com/ns");
        private static ExpandedNodeId AbsoluteTarget2 => new ExpandedNodeId(200u, "http://example.com/ns");

        [Test]
        public void ConstructorCreatesEmptyCollection()
        {
            var collection = new ReferenceCollection();

            Assert.That(collection.Count, Is.Zero);
            Assert.That(collection.IsReadOnly, Is.False);
        }

        [Test]
        public void ToStringReturnsReferenceCount()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            string result = collection.ToString();

            Assert.That(result, Does.Contain("References"));
            Assert.That(result, Does.Contain("1"));
        }

        [Test]
        public void ToStringWithNullFormatReturnsReferenceCount()
        {
            var collection = new ReferenceCollection();

            string result = collection.ToString(null, null);

            Assert.That(result, Is.EqualTo("References 0"));
        }

        [Test]
        public void ToStringWithNonNullFormatThrowsFormatException()
        {
            var collection = new ReferenceCollection();

            Assert.That(
                () => collection.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void AddForwardReferenceIncrementsCount()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddInverseReferenceIncrementsCount()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, true, Target1 }
            };

            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddMultipleForwardReferencesWithDifferentTargets()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType1, false, Target2 }
            };

            Assert.That(collection, Has.Count.EqualTo(2));
        }

        [Test]
        public void AddReferencesWithDifferentTypes()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType2, false, Target1 }
            };

            Assert.That(collection, Has.Count.EqualTo(2));
        }

        [Test]
        public void AddForwardAndInverseReferencesWithSameTypeAndTarget()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType1, true, Target1 }
            };

            Assert.That(collection, Has.Count.EqualTo(2));
        }

        [Test]
        public void AddDuplicateReferenceReplacesExisting()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                // Adding same reference again via typed Add uses dictionary indexer (replace)
                { RefType1, false, Target1 }
            };

            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddAbsoluteTargetReference()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, AbsoluteTarget1 }
            };

            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddAbsoluteTargetInverseReference()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, true, AbsoluteTarget1 }
            };

            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddIReferenceIncrementsCount()
        {
            var collection = new ReferenceCollection();
            var reference = new ReferenceNode(RefType1, false, Target1);

            collection.Add(reference);

            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveExistingReferenceReturnsTrue()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Remove(RefType1, false, Target1);

            Assert.That(result, Is.True);
            Assert.That(collection.Count, Is.Zero);
        }

        [Test]
        public void RemoveNonExistentReferenceReturnsFalse()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Remove(RefType1, false, Target2);

            Assert.That(result, Is.False);
            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveReferenceWithWrongDirectionReturnsFalse()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Remove(RefType1, true, Target1);

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveIReferenceExistingReturnsTrue()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Remove(new ReferenceNode(RefType1, false, Target1));

            Assert.That(result, Is.True);
            Assert.That(collection.Count, Is.Zero);
        }

        [Test]
        public void RemoveIReferenceNonExistentReturnsFalse()
        {
            var collection = new ReferenceCollection();

            bool result = collection.Remove(new ReferenceNode(RefType1, false, Target1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveAllForwardReferencesOfType()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType1, false, Target2 },
                { RefType1, true, Target3 }
            };

            bool result = collection.RemoveAll(RefType1, false);

            Assert.That(result, Is.True);
            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveAllInverseReferencesOfType()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, true, Target1 },
                { RefType1, true, Target2 },
                { RefType1, false, Target3 }
            };

            bool result = collection.RemoveAll(RefType1, true);

            Assert.That(result, Is.True);
            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveAllWithNonExistentTypeReturnsFalse()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.RemoveAll(RefType2, false);

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveAllWithNullReferenceTypeReturnsFalse()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.RemoveAll(NodeId.Null, false);

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveAllRemovesEntryWhenEmpty()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            collection.RemoveAll(RefType1, false);

            // After removing all forward references, the entry should be removed
            // Verify by adding a new reference of the same type (should succeed)
            collection.Add(RefType1, false, Target1);
            Assert.That(collection, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveAllForwardWithAbsoluteTargets()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, AbsoluteTarget1 },
                { RefType1, false, AbsoluteTarget2 },
                { RefType1, false, Target1 }
            };

            bool result = collection.RemoveAll(RefType1, false);

            Assert.That(result, Is.True);
            Assert.That(collection.Count, Is.Zero);
        }

        [Test]
        public void RemoveAllInverseWithAbsoluteTargets()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, true, AbsoluteTarget1 },
                { RefType1, true, AbsoluteTarget2 },
                { RefType1, true, Target1 }
            };

            bool result = collection.RemoveAll(RefType1, true);

            Assert.That(result, Is.True);
            Assert.That(collection.Count, Is.Zero);
        }

        [Test]
        public void ClearRemovesAllReferences()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType2, true, Target2 }
            };

            collection.Clear();

            Assert.That(collection.Count, Is.Zero);
        }

        [Test]
        public void ContainsReturnsTrueForExistingReference()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Contains(new ReferenceNode(RefType1, false, Target1));

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsReturnsFalseForNonExistentReference()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Contains(new ReferenceNode(RefType1, false, Target2));

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsReturnsFalseForNullReference()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Contains(null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ExistsReturnsTrueForExactMatch()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Exists(RefType1, false, Target1, false, null);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ExistsReturnsFalseForNonExistentReference()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Exists(RefType1, false, Target2, false, null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ExistsReturnsFalseWhenSubtypesRequestedButNoTypeTree()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            bool result = collection.Exists(RefType2, false, Target1, true, null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ExistsWithSubtypesAndTypeTreeSearchesSubtypes()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            // RefType1 is added; searching for RefType2 with subtypes should find it
            // because IsTypeOf(RefType1, RefType2) returns true
            bool result = collection.Exists(RefType2, false, Target1, true, typeTree.Object);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ExistsWithSubtypesReturnsFalseWhenNoMatch()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>())).Returns(false);

            bool result = collection.Exists(RefType2, false, Target1, true, typeTree.Object);

            Assert.That(result, Is.False);
        }

        [Test]
        public void FindReturnsMatchingForwardReferences()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType1, false, Target2 },
                { RefType1, true, Target3 }
            };

            IList<IReference> result = collection.Find(RefType1, false, false, null);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.All(r => !r.IsInverse), Is.True);
        }

        [Test]
        public void FindReturnsMatchingInverseReferences()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, true, Target1 },
                { RefType1, true, Target2 },
                { RefType1, false, Target3 }
            };

            IList<IReference> result = collection.Find(RefType1, true, false, null);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.All(r => r.IsInverse), Is.True);
        }

        [Test]
        public void FindReturnsEmptyForNonExistentType()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            IList<IReference> result = collection.Find(RefType2, false, false, null);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindReturnsEmptyForNullReferenceType()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            IList<IReference> result = collection.Find(NodeId.Null, false, false, null);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindWithSubtypesSearchesTypeTree()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType2, false, Target2 }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType3)).Returns(true);
            typeTree.Setup(t => t.IsTypeOf(RefType2, RefType3)).Returns(true);

            IList<IReference> result = collection.Find(RefType3, false, true, typeTree.Object);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindWithSubtypesAndNullTypeTreeDoesNotSearchSubtypes()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            IList<IReference> result = collection.Find(RefType1, false, true, null);

            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public void FindWithAbsoluteTargets()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType1, false, AbsoluteTarget1 }
            };

            IList<IReference> result = collection.Find(RefType1, false, false, null);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindInverseWithAbsoluteTargets()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, true, Target1 },
                { RefType1, true, AbsoluteTarget1 }
            };

            IList<IReference> result = collection.Find(RefType1, true, false, null);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindTargetReturnsTargetAtIndex()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            ExpandedNodeId result = collection.FindTarget(RefType1, false, false, null, 0);

            Assert.That(result, Is.Not.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void FindTargetReturnsNullForOutOfRangeIndex()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            ExpandedNodeId result = collection.FindTarget(RefType1, false, false, null, 5);

            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void FindTargetReturnsNullForNegativeIndex()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            ExpandedNodeId result = collection.FindTarget(RefType1, false, false, null, -1);

            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void FindTargetReturnsNullForEmptyResult()
        {
            var collection = new ReferenceCollection();

            ExpandedNodeId result = collection.FindTarget(RefType1, false, false, null, 0);

            Assert.That(result, Is.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void FindTargetWithSubtypesUsesTypeTree()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            ExpandedNodeId result = collection.FindTarget(RefType2, false, true, typeTree.Object, 0);

            Assert.That(result, Is.Not.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void FindReferencesToTargetReturnsAllMatchingReferences()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType2, true, Target1 },
                { RefType1, false, Target2 }
            };

            IList<IReference> result = collection.FindReferencesToTarget(Target1);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindReferencesToTargetReturnsEmptyForNullTarget()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            IList<IReference> result = collection.FindReferencesToTarget(ExpandedNodeId.Null);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindReferencesToTargetReturnsEmptyForNonExistentTarget()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            IList<IReference> result = collection.FindReferencesToTarget(Target3);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void CopyToCopiesReferencesToArray()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType2, true, Target2 }
            };
            var array = new IReference[2];

            collection.CopyTo(array, 0);

            Assert.That(array[0], Is.Not.Null);
            Assert.That(array[1], Is.Not.Null);
        }

        [Test]
        public void CopyToWithOffsetCopiesAtCorrectPosition()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };
            var array = new IReference[3];

            collection.CopyTo(array, 1);

            Assert.That(array[0], Is.Null);
            Assert.That(array[1], Is.Not.Null);
        }

        [Test]
        public void CopyToThrowsOnNullArray()
        {
            var collection = new ReferenceCollection();

            Assert.That(
                () => collection.CopyTo(null, 0),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CopyToThrowsOnNegativeIndex()
        {
            var collection = new ReferenceCollection();
            var array = new IReference[1];

            Assert.That(
                () => collection.CopyTo(array, -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void CopyToThrowsOnIndexEqualToArrayLength()
        {
            var collection = new ReferenceCollection();
            var array = new IReference[1];

            Assert.That(
                () => collection.CopyTo(array, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void GetEnumeratorIteratesAllReferences()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 },
                { RefType2, true, Target2 }
            };

            var references = new List<IReference>();
            references.AddRange(collection);

            Assert.That(references, Has.Count.EqualTo(2));
        }

        [Test]
        public void NonGenericGetEnumeratorIteratesAllReferences()
        {
            var collection = new ReferenceCollection
            {
                { RefType1, false, Target1 }
            };

            IEnumerable enumerable = collection;
            int count = 0;
            foreach (object item in enumerable)
            {
                count++;
                Assert.That(item, Is.InstanceOf<IReference>());
            }

            Assert.That(count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Unit tests for the <see cref="ReferenceDictionary{T}"/> class.
    /// </summary>
    [TestFixture]
    [Category("ReferenceTable")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceDictionaryTests
    {
        private static NodeId RefType1 => new NodeId(1u);
        private static NodeId RefType2 => new NodeId(2u);
        private static NodeId RefType3 => new NodeId(3u);
        private static ExpandedNodeId Target1 => new ExpandedNodeId(100u);
        private static ExpandedNodeId Target2 => new ExpandedNodeId(200u);
        private static ExpandedNodeId Target3 => new ExpandedNodeId(300u);
        private static ExpandedNodeId AbsoluteTarget1 => new ExpandedNodeId(100u, "http://example.com/ns");
        private static ExpandedNodeId AbsoluteTarget2 => new ExpandedNodeId(200u, "http://example.com/ns");

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
        private static IReference MakeRef(NodeId refType, bool isInverse, ExpandedNodeId target)
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
        {
            return new ReferenceNode(refType, isInverse, target);
        }
        [Test]
        public void ConstructorCreatesEmptyDictionary()
        {
            var dict = new ReferenceDictionary<string>();

            Assert.That(dict.Count, Is.Zero);
            Assert.That(dict.IsReadOnly, Is.False);
            Assert.That(dict.Version, Is.Zero);
        }
        [Test]
        public void AddForwardReferenceIncrementsCount()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddInverseReferenceIncrementsCount()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "value1" }
            };

            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddAbsoluteForwardTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, AbsoluteTarget1), "value1" }
            };

            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddAbsoluteInverseTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, AbsoluteTarget1), "value1" }
            };

            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddMultipleReferencesIncrementsCount()
        {
            var dict = new ReferenceDictionary<int>
            {
                { MakeRef(RefType1, false, Target1), 1 },
                { MakeRef(RefType1, false, Target2), 2 },
                { MakeRef(RefType2, true, Target1), 3 }
            };

            Assert.That(dict, Has.Count.EqualTo(3));
        }

        [Test]
        public void AddDuplicateKeyThrowsArgumentException()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            Assert.That(
                () => dict.Add(MakeRef(RefType1, false, Target1), "value2"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AddDuplicateAbsoluteKeyThrowsArgumentException()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, AbsoluteTarget1), "value1" }
            };

            Assert.That(
                () => dict.Add(MakeRef(RefType1, false, AbsoluteTarget1), "value2"),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AddNullReferenceThrowsArgumentNullException()
        {
            var dict = new ReferenceDictionary<string>();

            Assert.That(
                () => dict.Add(null, "value"),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddReferenceWithNullTypeIdThrowsArgumentNullException()
        {
            var dict = new ReferenceDictionary<string>();

            Assert.That(
                () => dict.Add(MakeRef(NodeId.Null, false, Target1), "value"),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddReferenceWithNullTargetIdThrowsArgumentNullException()
        {
            var dict = new ReferenceDictionary<string>();

            Assert.That(
                () => dict.Add(MakeRef(RefType1, false, ExpandedNodeId.Null), "value"),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddKeyValuePair()
        {
            var dict = new ReferenceDictionary<string>();
            var kvp = new KeyValuePair<IReference, string>(
                MakeRef(RefType1, false, Target1), "value1");

            dict.Add(kvp);

            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddIncrementsVersion()
        {
            var dict = new ReferenceDictionary<string>();
            ulong initialVersion = dict.Version;

            dict.Add(MakeRef(RefType1, false, Target1), "value1");

            Assert.That(dict.Version, Is.GreaterThan(initialVersion));
        }
        [Test]
        public void IndexerGetReturnsValue()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, Target1);
            dict.Add(reference, "value1");

            string result = dict[reference];

            Assert.That(result, Is.EqualTo("value1"));
        }

        [Test]
        public void IndexerGetAbsoluteTargetReturnsValue()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, AbsoluteTarget1);
            dict.Add(reference, "absolute-value");

            string result = dict[reference];

            Assert.That(result, Is.EqualTo("absolute-value"));
        }

        [Test]
        public void IndexerGetInverseReferenceReturnsValue()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, true, Target1);
            dict.Add(reference, "inverse-value");

            string result = dict[reference];

            Assert.That(result, Is.EqualTo("inverse-value"));
        }

        [Test]
        public void IndexerGetInverseAbsoluteReferenceReturnsValue()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, true, AbsoluteTarget1);
            dict.Add(reference, "inv-abs-value");

            string result = dict[reference];

            Assert.That(result, Is.EqualTo("inv-abs-value"));
        }

        [Test]
        public void IndexerGetThrowsKeyNotFoundForMissingReference()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            Assert.That(
                () => _ = dict[MakeRef(RefType1, false, Target2)],
                Throws.TypeOf<KeyNotFoundException>());
        }

        [Test]
        public void IndexerGetThrowsArgumentNullExceptionForNullKey()
        {
            var dict = new ReferenceDictionary<string>();

            Assert.That(
                () => _ = dict[null],
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void IndexerSetReplacesExistingValue()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, Target1);
            dict.Add(reference, "value1");

            dict[reference] = "value2";

            Assert.That(dict[reference], Is.EqualTo("value2"));
            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void IndexerSetAddsNewEntry()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, Target1);

            dict[reference] = "value1";

            Assert.That(dict[reference], Is.EqualTo("value1"));
            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void IndexerSetReplacesAbsoluteTargetEntry()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, AbsoluteTarget1);
            dict.Add(reference, "old-value");

            dict[reference] = "new-value";

            Assert.That(dict[reference], Is.EqualTo("new-value"));
            Assert.That(dict, Has.Count.EqualTo(1));
        }
        [Test]
        public void ContainsKeyReturnsTrueForExistingForwardReference()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            bool result = dict.ContainsKey(MakeRef(RefType1, false, Target1));

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsKeyReturnsTrueForExistingInverseReference()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "value1" }
            };

            bool result = dict.ContainsKey(MakeRef(RefType1, true, Target1));

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsKeyReturnsFalseForNonExistent()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.ContainsKey(MakeRef(RefType1, false, Target1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyReturnsFalseForNullKey()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.ContainsKey(null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyReturnsFalseWhenTargetsNullForDirection()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add a forward reference so the reference type entry exists
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            // Searching for inverse should return false since InverseTargets is null
            bool result = dict.ContainsKey(MakeRef(RefType1, true, Target1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyReturnsFalseWhenAbsoluteTargetsNullForDirection()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add a forward reference (internal) so the reference type entry exists
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            // Searching for absolute target in inverse should return false
            bool result = dict.ContainsKey(MakeRef(RefType1, true, AbsoluteTarget1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyReturnsFalseWhenForwardExternalTargetsNull()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add a forward internal reference so entry exists but ForwardExternalTargets is null
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            // Searching for absolute forward target should return false
            bool result = dict.ContainsKey(MakeRef(RefType1, false, AbsoluteTarget1));

            Assert.That(result, Is.False);
        }
        [Test]
        public void ContainsKeyWithTypeTreeThrowsOnNullTypeTree()
        {
            var dict = new ReferenceDictionary<string>();

            Assert.That(
                () => dict.ContainsKey(MakeRef(RefType1, false, Target1), null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ContainsKeyWithTypeTreeReturnsFalseForInvalidReference()
        {
            var dict = new ReferenceDictionary<string>();
            var typeTree = new Mock<ITypeTable>();

            bool result = dict.ContainsKey(null, typeTree.Object);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyWithTypeTreeSearchesSubtypes()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            bool result = dict.ContainsKey(MakeRef(RefType2, false, Target1), typeTree.Object);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsKeyWithTypeTreeReturnsFalseWhenNotSubtype()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>())).Returns(false);

            bool result = dict.ContainsKey(MakeRef(RefType2, false, Target1), typeTree.Object);

            Assert.That(result, Is.False);
        }
        [Test]
        public void TryGetValueReturnsTrueAndValueForExistingKey()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            bool result = dict.TryGetValue(MakeRef(RefType1, false, Target1), out string value);

            Assert.That(result, Is.True);
            Assert.That(value, Is.EqualTo("value1"));
        }

        [Test]
        public void TryGetValueReturnsFalseForNonExistentKey()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.TryGetValue(MakeRef(RefType1, false, Target1), out string value);

            Assert.That(result, Is.False);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void TryGetValueReturnsFalseForNullKey()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.TryGetValue(null, out string value);

            Assert.That(result, Is.False);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void TryGetValueWithAbsoluteTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, AbsoluteTarget1), "abs-value" }
            };

            bool result = dict.TryGetValue(MakeRef(RefType1, false, AbsoluteTarget1), out string value);

            Assert.That(result, Is.True);
            Assert.That(value, Is.EqualTo("abs-value"));
        }

        [Test]
        public void TryGetValueWithInverseAbsoluteTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, AbsoluteTarget1), "inv-abs" }
            };

            bool result = dict.TryGetValue(MakeRef(RefType1, true, AbsoluteTarget1), out string value);

            Assert.That(result, Is.True);
            Assert.That(value, Is.EqualTo("inv-abs"));
        }
        [Test]
        public void RemoveExistingForwardReferenceReturnsTrue()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            bool result = dict.Remove(MakeRef(RefType1, false, Target1));

            Assert.That(result, Is.True);
            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void RemoveExistingInverseReferenceReturnsTrue()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "value1" }
            };

            bool result = dict.Remove(MakeRef(RefType1, true, Target1));

            Assert.That(result, Is.True);
            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void RemoveAbsoluteForwardReferenceReturnsTrue()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, AbsoluteTarget1), "value1" }
            };

            bool result = dict.Remove(MakeRef(RefType1, false, AbsoluteTarget1));

            Assert.That(result, Is.True);
            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void RemoveAbsoluteInverseReferenceReturnsTrue()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, AbsoluteTarget1), "value1" }
            };

            bool result = dict.Remove(MakeRef(RefType1, true, AbsoluteTarget1));

            Assert.That(result, Is.True);
            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void RemoveNonExistentReferenceReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.Remove(MakeRef(RefType1, false, Target1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveNullReferenceReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.Remove((IReference)null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveIncrementsVersion()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };
            ulong versionAfterAdd = dict.Version;

            dict.Remove(MakeRef(RefType1, false, Target1));

            Assert.That(dict.Version, Is.GreaterThan(versionAfterAdd));
        }

        [Test]
        public void RemoveLastReferenceOfTypeRemovesEntry()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            dict.Remove(MakeRef(RefType1, false, Target1));

            // Verify by checking that the reference type is no longer found
            Assert.That(dict.ContainsKey(MakeRef(RefType1, false, Target1)), Is.False);
        }

        [Test]
        public void RemoveKeyValuePairReturnsTrue()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, Target1);
            dict.Add(reference, "value1");

            bool result = dict.Remove(new KeyValuePair<IReference, string>(reference, "value1"));

            Assert.That(result, Is.True);
            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void RemoveReturnsFalseWhenInternalTargetsNull()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add forward reference to create entry
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            // Try to remove inverse reference with same type (InverseTargets is null)
            bool result = dict.Remove(MakeRef(RefType1, true, Target1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveReturnsFalseWhenExternalTargetsNull()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add forward internal reference to create entry
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            // Try to remove forward absolute reference (ForwardExternalTargets is null)
            bool result = dict.Remove(MakeRef(RefType1, false, AbsoluteTarget1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveReturnsFalseWhenInverseExternalTargetsNull()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add forward internal reference to create entry
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            // Try to remove inverse absolute reference (InverseExternalTargets is null)
            bool result = dict.Remove(MakeRef(RefType1, true, AbsoluteTarget1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveReturnsFalseWhenTargetNotFoundInInternalTargets()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "value1" }
            };

            // Try to remove a different target of the same type/direction
            bool result = dict.Remove(MakeRef(RefType1, false, Target2));

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveReturnsFalseWhenTargetNotFoundInExternalTargets()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, AbsoluteTarget1), "value1" }
            };

            // Try to remove a different absolute target
            bool result = dict.Remove(MakeRef(RefType1, false, AbsoluteTarget2));

            Assert.That(result, Is.False);
        }
        [Test]
        public void RemoveAllForwardReferencesOfType()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType1, false, Target2), "v2" },
                { MakeRef(RefType1, true, Target3), "v3" }
            };

            bool result = dict.RemoveAll(RefType1, false);

            Assert.That(result, Is.True);
            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveAllInverseReferencesOfType()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "v1" },
                { MakeRef(RefType1, true, Target2), "v2" },
                { MakeRef(RefType1, false, Target3), "v3" }
            };

            bool result = dict.RemoveAll(RefType1, true);

            Assert.That(result, Is.True);
            Assert.That(dict, Has.Count.EqualTo(1));
        }

        [Test]
        public void RemoveAllReturnsFalseForNullType()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.RemoveAll(NodeId.Null, false);

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveAllReturnsFalseForNonExistentType()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            bool result = dict.RemoveAll(RefType2, false);

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveAllForwardWithAbsoluteAndInternalTargets()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType1, false, AbsoluteTarget1), "v2" }
            };

            bool result = dict.RemoveAll(RefType1, false);

            Assert.That(result, Is.True);
            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void RemoveAllInverseWithAbsoluteAndInternalTargets()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "v1" },
                { MakeRef(RefType1, true, AbsoluteTarget1), "v2" }
            };

            bool result = dict.RemoveAll(RefType1, true);

            Assert.That(result, Is.True);
            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void RemoveAllRemovesEntryWhenCompletelyEmpty()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            dict.RemoveAll(RefType1, false);

            // Type entry should be fully removed
            IList<IReference> found = dict.Find(RefType1, false);
            Assert.That(found, Is.Empty);
        }

        [Test]
        public void RemoveAllKeepsEntryWhenOtherDirectionHasReferences()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "forward" },
                { MakeRef(RefType1, true, Target2), "inverse" }
            };

            dict.RemoveAll(RefType1, false);

            // The inverse reference should still be there
            Assert.That(dict, Has.Count.EqualTo(1));
            Assert.That(dict.ContainsKey(MakeRef(RefType1, true, Target2)), Is.True);
        }
        [Test]
        public void FindReturnsForwardReferences()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType1, false, Target2), "v2" },
                { MakeRef(RefType1, true, Target3), "v3" }
            };

            IList<IReference> result = dict.Find(RefType1, false);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.All(r => !r.IsInverse), Is.True);
        }

        [Test]
        public void FindReturnsInverseReferences()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "v1" },
                { MakeRef(RefType1, true, Target2), "v2" },
                { MakeRef(RefType1, false, Target3), "v3" }
            };

            IList<IReference> result = dict.Find(RefType1, true);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.All(r => r.IsInverse), Is.True);
        }

        [Test]
        public void FindReturnsEmptyForNullType()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            IList<IReference> result = dict.Find(NodeId.Null, false);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindReturnsEmptyForNonExistentType()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            IList<IReference> result = dict.Find(RefType2, false);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindIncludesAbsoluteTargets()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType1, false, AbsoluteTarget1), "v2" }
            };

            IList<IReference> result = dict.Find(RefType1, false);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindInverseIncludesAbsoluteTargets()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "v1" },
                { MakeRef(RefType1, true, AbsoluteTarget1), "v2" }
            };

            IList<IReference> result = dict.Find(RefType1, true);

            Assert.That(result, Has.Count.EqualTo(2));
        }
        [Test]
        public void FindWithTypeTreeThrowsOnNull()
        {
            var dict = new ReferenceDictionary<string>();

            Assert.That(
                () => dict.Find(RefType1, false, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void FindWithTypeTreeSearchesSubtypes()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType2, false, Target2), "v2" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType3)).Returns(true);
            typeTree.Setup(t => t.IsTypeOf(RefType2, RefType3)).Returns(true);

            IList<IReference> result = dict.Find(RefType3, false, typeTree.Object);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindWithTypeTreeReturnsEmptyForNullType()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();

            IList<IReference> result = dict.Find(NodeId.Null, false, typeTree.Object);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindWithTypeTreeReturnsEmptyWhenNoMatch()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>())).Returns(false);

            IList<IReference> result = dict.Find(RefType2, false, typeTree.Object);

            Assert.That(result, Is.Empty);
        }
        [Test]
        public void FindReferencesToTargetReturnsMatchingReferences()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType2, true, Target1), "v2" },
                { MakeRef(RefType1, false, Target2), "v3" }
            };

            IList<IReference> result = dict.FindReferencesToTarget(Target1);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FindReferencesToTargetReturnsEmptyForNullTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            IList<IReference> result = dict.FindReferencesToTarget(ExpandedNodeId.Null);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindReferencesToTargetReturnsEmptyForNonExistentTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            IList<IReference> result = dict.FindReferencesToTarget(Target3);

            Assert.That(result, Is.Empty);
        }
        [Test]
        public void ClearRemovesAllEntries()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType2, true, Target2), "v2" }
            };

            dict.Clear();

            Assert.That(dict.Count, Is.Zero);
        }

        [Test]
        public void ClearIncrementsVersion()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };
            ulong versionBeforeClear = dict.Version;

            dict.Clear();

            Assert.That(dict.Version, Is.GreaterThan(versionBeforeClear));
        }
        [Test]
        public void ContainsReturnsTrueForExistingPair()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, Target1);
            dict.Add(reference, "value1");

            bool result = dict.Contains(
                new KeyValuePair<IReference, string>(reference, "value1"));

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsReturnsFalseForMismatchedValue()
        {
            var dict = new ReferenceDictionary<string>();
            IReference reference = MakeRef(RefType1, false, Target1);
            dict.Add(reference, "value1");

            bool result = dict.Contains(
                new KeyValuePair<IReference, string>(reference, "other-value"));

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsReturnsFalseForNonExistentKey()
        {
            var dict = new ReferenceDictionary<string>();

            bool result = dict.Contains(
                new KeyValuePair<IReference, string>(MakeRef(RefType1, false, Target1), "v1"));

            Assert.That(result, Is.False);
        }
        [Test]
        public void KeysReturnsAllReferenceKeys()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType2, true, Target2), "v2" }
            };

            ICollection<IReference> keys = dict.Keys;

            Assert.That(keys, Has.Count.EqualTo(2));
        }

        [Test]
        public void ValuesReturnsAllValues()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType2, true, Target2), "v2" }
            };

            ICollection<string> values = dict.Values;

            Assert.That(values, Has.Count.EqualTo(2));
            Assert.That(values, Does.Contain("v1"));
            Assert.That(values, Does.Contain("v2"));
        }

        [Test]
        public void KeysReturnsEmptyForEmptyDictionary()
        {
            var dict = new ReferenceDictionary<string>();

            ICollection<IReference> keys = dict.Keys;

            Assert.That(keys, Is.Empty);
        }

        [Test]
        public void ValuesReturnsEmptyForEmptyDictionary()
        {
            var dict = new ReferenceDictionary<string>();

            ICollection<string> values = dict.Values;

            Assert.That(values, Is.Empty);
        }
        [Test]
        public void CopyToCopiesAllEntries()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType2, true, Target2), "v2" }
            };

            var array = new KeyValuePair<IReference, string>[2];
            dict.CopyTo(array, 0);

            Assert.That(array[0].Value, Is.Not.Null);
            Assert.That(array[1].Value, Is.Not.Null);
        }
        [Test]
        public void GetEnumeratorIteratesAllEntries()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" },
                { MakeRef(RefType2, true, Target2), "v2" }
            };

            var entries = new List<KeyValuePair<IReference, string>>();
            entries.AddRange(dict);

            Assert.That(entries, Has.Count.EqualTo(2));
        }

        [Test]
        public void NonGenericEnumeratorIteratesAllEntries()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            IEnumerable enumerable = dict;
            int count = 0;
            foreach (object item in enumerable)
            {
                count++;
            }

            Assert.That(count, Is.EqualTo(1));
        }
        [Test]
        public void MixedReferencesAllDirectionsAndTargetTypes()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add all four combinations
                { MakeRef(RefType1, false, Target1), "forward-internal" },
                { MakeRef(RefType1, true, Target2), "inverse-internal" },
                { MakeRef(RefType1, false, AbsoluteTarget1), "forward-external" },
                { MakeRef(RefType1, true, AbsoluteTarget2), "inverse-external" }
            };

            Assert.That(dict, Has.Count.EqualTo(4));

            // Verify forward finds both internal and external
            IList<IReference> forwardRefs = dict.Find(RefType1, false);
            Assert.That(forwardRefs, Has.Count.EqualTo(2));

            // Verify inverse finds both internal and external
            IList<IReference> inverseRefs = dict.Find(RefType1, true);
            Assert.That(inverseRefs, Has.Count.EqualTo(2));
        }

        [Test]
        public void RemoveAllForwardKeepsInverseReferences()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "forward-internal" },
                { MakeRef(RefType1, true, Target2), "inverse-internal" },
                { MakeRef(RefType1, false, AbsoluteTarget1), "forward-external" },
                { MakeRef(RefType1, true, AbsoluteTarget2), "inverse-external" }
            };

            dict.RemoveAll(RefType1, false);

            Assert.That(dict, Has.Count.EqualTo(2));
            Assert.That(dict.ContainsKey(MakeRef(RefType1, true, Target2)), Is.True);
            Assert.That(dict.ContainsKey(MakeRef(RefType1, true, AbsoluteTarget2)), Is.True);
        }

        [Test]
        public void RemoveAllInverseKeepsForwardReferences()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "forward-internal" },
                { MakeRef(RefType1, true, Target2), "inverse-internal" },
                { MakeRef(RefType1, false, AbsoluteTarget1), "forward-external" },
                { MakeRef(RefType1, true, AbsoluteTarget2), "inverse-external" }
            };

            dict.RemoveAll(RefType1, true);

            Assert.That(dict, Has.Count.EqualTo(2));
            Assert.That(dict.ContainsKey(MakeRef(RefType1, false, Target1)), Is.True);
            Assert.That(dict.ContainsKey(MakeRef(RefType1, false, AbsoluteTarget1)), Is.True);
        }
        [Test]
        public void RemoveReferenceWithNullTypeIdReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            // A reference with IsNull ReferenceTypeId should return false (not throw)
            bool result = dict.Remove(MakeRef(NodeId.Null, false, Target1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void RemoveReferenceWithNullTargetIdReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            bool result = dict.Remove(MakeRef(RefType1, false, ExpandedNodeId.Null));

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyWithNullTypeIdReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            bool result = dict.ContainsKey(MakeRef(NodeId.Null, false, Target1));

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyWithNullTargetIdReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            bool result = dict.ContainsKey(MakeRef(RefType1, false, ExpandedNodeId.Null));

            Assert.That(result, Is.False);
        }

        [Test]
        public void TryGetValueForAbsoluteTargetNotFoundReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add one absolute target so the external targets dictionary exists
                { MakeRef(RefType1, false, AbsoluteTarget1), "v1" }
            };

            // Look for a different absolute target (same type, same direction)
            bool result = dict.TryGetValue(MakeRef(RefType1, false, AbsoluteTarget2), out string value);

            Assert.That(result, Is.False);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void TryGetValueForInverseAbsoluteTargetNotFoundReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, AbsoluteTarget1), "v1" }
            };

            bool result = dict.TryGetValue(MakeRef(RefType1, true, AbsoluteTarget2), out string value);

            Assert.That(result, Is.False);
            Assert.That(value, Is.Null);
        }

        [Test]
        public void TryGetValueForInternalTargetNotFoundReturnsFalse()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            bool result = dict.TryGetValue(MakeRef(RefType1, false, Target2), out string value);

            Assert.That(result, Is.False);
            Assert.That(value, Is.Null);
        }
        [Test]
        public void ContainsKeyWithTypeTreeFindsAbsoluteForwardTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, false, AbsoluteTarget1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            // Search for RefType2 with absolute forward target; RefType1 is a subtype
            bool result = dict.ContainsKey(MakeRef(RefType2, false, AbsoluteTarget1), typeTree.Object);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsKeyWithTypeTreeFindsAbsoluteInverseTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, AbsoluteTarget1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            bool result = dict.ContainsKey(MakeRef(RefType2, true, AbsoluteTarget1), typeTree.Object);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsKeyWithTypeTreeFindsInternalInverseTarget()
        {
            var dict = new ReferenceDictionary<string>
            {
                { MakeRef(RefType1, true, Target1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            bool result = dict.ContainsKey(MakeRef(RefType2, true, Target1), typeTree.Object);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ContainsKeyWithTypeTreeReturnsFalseWhenAbsoluteTargetsNull()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Only add internal forward target
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            // Search for absolute target (ForwardExternalTargets is null)
            bool result = dict.ContainsKey(MakeRef(RefType2, false, AbsoluteTarget1), typeTree.Object);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyWithTypeTreeReturnsFalseWhenInverseTargetsNull()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Only add forward target
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            // Search for inverse target (InverseTargets is null)
            bool result = dict.ContainsKey(MakeRef(RefType2, true, Target1), typeTree.Object);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ContainsKeyWithTypeTreeReturnsFalseWhenInverseExternalTargetsNull()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Only add forward target
                { MakeRef(RefType1, false, Target1), "v1" }
            };

            var typeTree = new Mock<ITypeTable>();
            typeTree.Setup(t => t.IsTypeOf(RefType1, RefType2)).Returns(true);

            // Search for absolute inverse target (InverseExternalTargets is null)
            bool result = dict.ContainsKey(MakeRef(RefType2, true, AbsoluteTarget1), typeTree.Object);

            Assert.That(result, Is.False);
        }
        [Test]
        public void RemoveAllForwardKeepsForwardExternalTargetsInEntry()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add forward internal + forward external + inverse
                { MakeRef(RefType1, false, Target1), "internal-fwd" },
                { MakeRef(RefType1, false, AbsoluteTarget1), "external-fwd" },
                { MakeRef(RefType1, true, Target2), "inv" }
            };

            // RemoveAll forward removes both internal and external forward
            dict.RemoveAll(RefType1, false);

            Assert.That(dict, Has.Count.EqualTo(1));
            Assert.That(dict.ContainsKey(MakeRef(RefType1, true, Target2)), Is.True);
        }

        [Test]
        public void RemoveLastAbsoluteInverseChecksIsEmpty()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add only inverse external targets so that IsEmpty checks
                // InverseExternalTargets (line 897-899)
                { MakeRef(RefType1, true, AbsoluteTarget1), "inv-ext" }
            };

            // Remove the inverse external target
            dict.Remove(MakeRef(RefType1, true, AbsoluteTarget1));

            Assert.That(dict.Count, Is.Zero);
            // The entry should be removed since it's empty
            Assert.That(dict.Find(RefType1, true), Is.Empty);
        }

        [Test]
        public void RemoveLastAbsoluteForwardChecksIsEmpty()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add only forward external target so that IsEmpty checks
                // ForwardExternalTargets (line 887-889)
                { MakeRef(RefType1, false, AbsoluteTarget1), "fwd-ext" }
            };

            // Remove the forward external target
            dict.Remove(MakeRef(RefType1, false, AbsoluteTarget1));

            Assert.That(dict.Count, Is.Zero);
            Assert.That(dict.Find(RefType1, false), Is.Empty);
        }

        [Test]
        public void EntryNotRemovedWhenOnlyExternalForwardRemains()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add internal forward + external forward
                { MakeRef(RefType1, false, Target1), "internal" },
                { MakeRef(RefType1, false, AbsoluteTarget1), "external" }
            };

            // Remove only the internal forward
            dict.Remove(MakeRef(RefType1, false, Target1));

            // Entry should still exist because ForwardExternalTargets is not empty
            Assert.That(dict, Has.Count.EqualTo(1));
            Assert.That(dict.ContainsKey(MakeRef(RefType1, false, AbsoluteTarget1)), Is.True);
        }

        [Test]
        public void EntryNotRemovedWhenOnlyExternalInverseRemains()
        {
            var dict = new ReferenceDictionary<string>
            {
                // Add internal inverse + external inverse
                { MakeRef(RefType1, true, Target1), "internal" },
                { MakeRef(RefType1, true, AbsoluteTarget1), "external" }
            };

            // Remove only the internal inverse
            dict.Remove(MakeRef(RefType1, true, Target1));

            // Entry should still exist because InverseExternalTargets is not empty
            Assert.That(dict, Has.Count.EqualTo(1));
            Assert.That(dict.ContainsKey(MakeRef(RefType1, true, AbsoluteTarget1)), Is.True);
        }
        [Test]
        public void VersionIncreasesOnEachMutation()
        {
            var dict = new ReferenceDictionary<string>();
            Assert.That(dict.Version, Is.Zero);

            dict.Add(MakeRef(RefType1, false, Target1), "v1");
            Assert.That(dict.Version, Is.EqualTo(1));

            dict[MakeRef(RefType1, false, Target1)] = "v2";
            Assert.That(dict.Version, Is.EqualTo(2));

            dict.Remove(MakeRef(RefType1, false, Target1));
            Assert.That(dict.Version, Is.EqualTo(3));

            dict.Add(MakeRef(RefType1, false, Target1), "v3");
            dict.Clear();
            Assert.That(dict.Version, Is.EqualTo(5));
        }
    }
}
