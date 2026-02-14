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

using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the ListOfChildren class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ListOfChildrenTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullOther_ReturnsFalse()
        {
            // Arrange
            var instance = new ListOfChildren
            {
                Items = [new ObjectDesign()]
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with the same reference.
        /// </summary>
        [Test]
        public void Equals_SameReference_ReturnsTrue()
        {
            // Arrange
            var instance = new ListOfChildren
            {
                Items = [new ObjectDesign()]
            };

            // Act
            bool result = instance.Equals(instance);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null Items arrays.
        /// </summary>
        [Test]
        public void Equals_BothItemsNull_ReturnsTrue()
        {
            // Arrange
            var instance1 = new ListOfChildren { Items = null };
            var instance2 = new ListOfChildren { Items = null };

            // Act
            bool result1 = instance1.Equals(instance2);
            bool result2 = instance1.Equals((object)instance2);

            // Assert
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when this instance has null Items and other
        /// has non-null Items.
        /// </summary>
        [Test]
        public void Equals_ThisItemsNullOtherItemsNotNull_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ListOfChildren { Items = null };
            var instance2 = new ListOfChildren { Items = [new ObjectDesign()] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when this instance has non-null Items and other has null Items.
        /// </summary>
        [Test]
        public void Equals_ThisItemsNotNullOtherItemsNull_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ListOfChildren { Items = [new ObjectDesign()] };
            var instance2 = new ListOfChildren { Items = null };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have empty Items arrays.
        /// </summary>
        [Test]
        public void Equals_BothItemsEmpty_ReturnsTrue()
        {
            // Arrange
            var instance1 = new ListOfChildren { Items = [] };
            var instance2 = new ListOfChildren { Items = [] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have identical single-element Items arrays.
        /// </summary>
        [Test]
        public void Equals_SameSingleItem_ReturnsTrue()
        {
            // Arrange
            var sharedItem = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("TestObject")
            };
            var instance1 = new ListOfChildren { Items = [sharedItem] };
            var instance2 = new ListOfChildren { Items = [sharedItem] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have identical multiple-element Items arrays.
        /// </summary>
        [Test]
        public void Equals_SameMultipleItems_ReturnsTrue()
        {
            // Arrange
            var item1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("Object1")
            };
            var item2 = new VariableDesign
            {
                SymbolicId = new XmlQualifiedName("Variable1")
            };
            var item3 = new PropertyDesign
            {
                SymbolicId = new XmlQualifiedName("Property1")
            };

            var instance1 = new ListOfChildren { Items = [item1, item2, item3] };
            var instance2 = new ListOfChildren { Items = [item1, item2, item3] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Items arrays have different lengths.
        /// </summary>
        [Test]
        public void Equals_DifferentLengths_ReturnsFalse()
        {
            // Arrange
            var instance1 = new ListOfChildren
            {
                Items = [new ObjectDesign()]
            };
            var instance2 = new ListOfChildren
            {
                Items = [new ObjectDesign(), new VariableDesign()]
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Items arrays have same length but different elements.
        /// </summary>
        [Test]
        public void Equals_SameLengthDifferentElements_ReturnsFalse()
        {
            // Arrange
            var item1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("Object1")
            };
            var item2 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("Object2")
            };

            var instance1 = new ListOfChildren { Items = [item1] };
            var instance2 = new ListOfChildren { Items = [item2] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Items arrays have same length but elements in different order.
        /// </summary>
        [Test]
        public void Equals_SameElementsDifferentOrder_ReturnsFalse()
        {
            // Arrange
            var item1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("Object1")
            };
            var item2 = new VariableDesign
            {
                SymbolicId = new XmlQualifiedName("Variable1")
            };

            var instance1 = new ListOfChildren { Items = [item1, item2] };
            var instance2 = new ListOfChildren { Items = [item2, item1] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing empty arrays via ArrayEqualityComparer.
        /// </summary>
        [Test]
        public void Equals_EmptyArrays_ReturnsTrue()
        {
            // Arrange
            var instance1 = new ListOfChildren { Items = [] };
            var instance2 = new ListOfChildren { Items = [] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when Items arrays contain different derived types but represent equal instances.
        /// </summary>
        [Test]
        public void Equals_MixedDerivedTypes_ReturnsTrue()
        {
            // Arrange
            var object1 = new ObjectDesign
            {
                SymbolicId = new XmlQualifiedName("Object1")
            };
            var variable1 = new VariableDesign
            {
                SymbolicId = new XmlQualifiedName("Variable1")
            };
            var method1 = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName("Method1")
            };

            var instance1 = new ListOfChildren { Items = [object1, variable1, method1] };
            var instance2 = new ListOfChildren { Items = [object1, variable1, method1] };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// Input: null object
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var listOfChildren = new ListOfChildren { Items = null };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = listOfChildren.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance with itself.
        /// Input: Same instance
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var listOfChildren = new ListOfChildren { Items = [new PropertyDesign()] };

            // Act
            bool result = listOfChildren.Equals((object)listOfChildren);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different type.
        /// Input: Object of different type (string)
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var listOfChildren = new ListOfChildren { Items = null };
            const string differentType = "not a ListOfChildren";

            // Act
            bool result = listOfChildren.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with another object type.
        /// Input: Object of different type (int)
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentTypeInt_ReturnsFalse()
        {
            // Arrange
            var listOfChildren = new ListOfChildren { Items = null };
            const int differentType = 42;

            // Act
            bool result = listOfChildren.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one instance has null Items and the other has non-null Items.
        /// Input: First instance with null Items, second with non-null Items
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_ThisItemsNullOtherNotNull_ReturnsFalse()
        {
            // Arrange
            var listOfChildren1 = new ListOfChildren { Items = null };
            var listOfChildren2 = new ListOfChildren { Items = [new PropertyDesign()] };

            // Act
            bool result = listOfChildren1.Equals((object)listOfChildren2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when this instance has non-null Items and the other has null Items.
        /// Input: First instance with non-null Items, second with null Items
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_ThisItemsNotNullOtherNull_ReturnsFalse()
        {
            // Arrange
            var listOfChildren1 = new ListOfChildren { Items = [new PropertyDesign()] };
            var listOfChildren2 = new ListOfChildren { Items = null };

            // Act
            bool result = listOfChildren1.Equals((object)listOfChildren2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have arrays with the same single element.
        /// Input: Two instances with identical single-element arrays
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_SingleElementArraysSameInstance_ReturnsTrue()
        {
            // Arrange
            var propertyDesign = new PropertyDesign { SymbolicId = new XmlQualifiedName("Test") };
            var listOfChildren1 = new ListOfChildren { Items = [propertyDesign] };
            var listOfChildren2 = new ListOfChildren { Items = [propertyDesign] };

            // Act
            bool result = listOfChildren1.Equals((object)listOfChildren2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when instances have arrays of different lengths.
        /// Input: Two instances with different array lengths
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentArrayLengths_ReturnsFalse()
        {
            // Arrange
            var listOfChildren1 = new ListOfChildren { Items = [new PropertyDesign()] };
            var listOfChildren2 = new ListOfChildren
            {
                Items = [new PropertyDesign(), new PropertyDesign()]
            };

            // Act
            bool result = listOfChildren1.Equals((object)listOfChildren2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when instances have arrays with different element instances.
        /// Input: Two instances with arrays containing different element instances
        /// Expected: false
        /// </summary>
        [Test]
        public void Equals_DifferentElementInstances_ReturnsFalse()
        {
            // Arrange
            var listOfChildren1 = new ListOfChildren
            {
                Items = [new PropertyDesign { SymbolicId = new XmlQualifiedName("Test1") }]
            };
            var listOfChildren2 = new ListOfChildren
            {
                Items = [new PropertyDesign { SymbolicId = new XmlQualifiedName("Test2") }]
            };

            // Act
            bool result = listOfChildren1.Equals((object)listOfChildren2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when instances have arrays with multiple equal elements.
        /// Input: Two instances with multi-element arrays containing the same instances
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_MultipleEqualElements_ReturnsTrue()
        {
            // Arrange
            var property1 = new PropertyDesign { SymbolicId = new XmlQualifiedName("Test1") };
            var property2 = new PropertyDesign { SymbolicId = new XmlQualifiedName("Test2") };
            var listOfChildren1 = new ListOfChildren { Items = [property1, property2] };
            var listOfChildren2 = new ListOfChildren { Items = [property1, property2] };

            // Act
            bool result = listOfChildren1.Equals((object)listOfChildren2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing with a ListOfChildren cast to object.
        /// Input: Two equal instances, one cast to object
        /// Expected: true
        /// </summary>
        [Test]
        public void Equals_CastToObject_ReturnsTrue()
        {
            // Arrange
            var property = new PropertyDesign { SymbolicId = new XmlQualifiedName("Test") };
            var listOfChildren1 = new ListOfChildren { Items = [property] };
            object listOfChildren2 = new ListOfChildren { Items = [property] };

            // Act
            bool result = listOfChildren1.Equals(listOfChildren2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetHashCode returns a consistent hash code when Items is null.
        /// </summary>
        [Test]
        public void GetHashCode_NullItems_ReturnsConsistentHashCode()
        {
            // Arrange
            var listOfChildren = new ListOfChildren
            {
                Items = null
            };

            // Act
            int hashCode = listOfChildren.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns a consistent hash code when Items is an empty array.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyArray_ReturnsConsistentHashCode()
        {
            // Arrange
            var listOfChildren = new ListOfChildren
            {
                Items = []
            };

            // Act
            int hashCode = listOfChildren.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns a consistent hash code when Items has a single element.
        /// </summary>
        [Test]
        public void GetHashCode_SingleItem_ReturnsConsistentHashCode()
        {
            // Arrange
            var instance = new InstanceDesign();
            var listOfChildren = new ListOfChildren
            {
                Items = [instance]
            };

            // Act
            int hashCode = listOfChildren.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns a consistent hash code when Items has multiple elements.
        /// </summary>
        [Test]
        public void GetHashCode_MultipleItems_ReturnsConsistentHashCode()
        {
            // Arrange
            var instance1 = new InstanceDesign();
            var instance2 = new InstanceDesign();
            var instance3 = new InstanceDesign();
            var listOfChildren = new ListOfChildren
            {
                Items = [instance1, instance2, instance3]
            };

            // Act
            int hashCode = listOfChildren.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.Zero);
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value when called multiple times on the same object.
        /// </summary>
        [Test]
        public void GetHashCode_CalledTwice_ReturnsSameValue()
        {
            // Arrange
            var instance = new InstanceDesign();
            var listOfChildren = new ListOfChildren
            {
                Items = [instance]
            };

            // Act
            int hashCode1 = listOfChildren.GetHashCode();
            int hashCode2 = listOfChildren.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for two different objects with the same Items reference.
        /// </summary>
        [Test]
        public void GetHashCode_SameItemsReference_ReturnsSameHashCode()
        {
            // Arrange
            var instance = new InstanceDesign();
            InstanceDesign[] items = [instance];
            var listOfChildren1 = new ListOfChildren { Items = items };
            var listOfChildren2 = new ListOfChildren { Items = items };

            // Act
            int hashCode1 = listOfChildren1.GetHashCode();
            int hashCode2 = listOfChildren2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for two different objects with equal Items arrays.
        /// </summary>
        [Test]
        public void GetHashCode_EqualItemsArrays_ReturnsSameHashCode()
        {
            // Arrange
            var instance1 = new InstanceDesign();
            var instance2 = new InstanceDesign();
            var listOfChildren1 = new ListOfChildren
            {
                Items = [instance1]
            };
            var listOfChildren2 = new ListOfChildren
            {
                Items = [instance2]
            };

            // Act
            int hashCode1 = listOfChildren1.GetHashCode();
            int hashCode2 = listOfChildren2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode likely returns different hash codes for objects
        /// with different items.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentItems_LikelyReturnsDifferentHashCode()
        {
            // Arrange
            var instance1 = new InstanceDesign { SymbolicId = new XmlQualifiedName("Instance1") };
            var instance2 = new InstanceDesign { SymbolicId = new XmlQualifiedName("Instance2") };
            var listOfChildren1 = new ListOfChildren
            {
                Items = [instance1]
            };
            var listOfChildren2 = new ListOfChildren
            {
                Items = [instance2]
            };

            // Act
            int hashCode1 = listOfChildren1.GetHashCode();
            int hashCode2 = listOfChildren2.GetHashCode();

            // Assert
            // Note: Different objects don't guarantee different hash codes, but it's highly likely
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns same hash codes for null Items vs empty array.
        /// </summary>
        [Test]
        public void GetHashCode_NullVsEmptyArray_ReturnsSameHashCode()
        {
            // Arrange
            var listOfChildren1 = new ListOfChildren { Items = null };
            var listOfChildren2 = new ListOfChildren { Items = [] };

            // Act
            int hashCode1 = listOfChildren1.GetHashCode();
            int hashCode2 = listOfChildren2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode1, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for arrays with different number of elements.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentArrayLength_ReturnsDifferentHashCode()
        {
            // Arrange
            var instance1 = new InstanceDesign();
            var instance2 = new InstanceDesign();
            var listOfChildren1 = new ListOfChildren
            {
                Items = [instance1]
            };
            var listOfChildren2 = new ListOfChildren
            {
                Items = [instance1, instance2]
            };

            // Act
            int hashCode1 = listOfChildren1.GetHashCode();
            int hashCode2 = listOfChildren2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }
    }
}
