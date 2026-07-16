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
    /// Unit tests for <see cref="RolePermissionSet"/> class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RolePermissionSetTests
    {
        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = rolePermissionSet.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false
            };
            object differentType = "NotARolePermissionSet";

            // Act
            bool result = rolePermissionSet.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Name property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentName_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "Role1",
                DoNotInheirit = false
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "Role2",
                DoNotInheirit = false
            };

            // Act
            bool result = rolePermissionSet1.Equals((object)rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when DoNotInheirit property differs.
        /// </summary>
        [Test]
        public void Equals_DifferentDoNotInheirit_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true
            };

            // Act
            bool result = rolePermissionSet1.Equals((object)rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null RolePermission arrays.
        /// </summary>
        [Test]
        public void Equals_BothNullRolePermissions_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals((object)rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have empty RolePermission arrays.
        /// </summary>
        [Test]
        public void Equals_BothEmptyRolePermissions_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission = []
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission = []
            };

            // Act
            bool result = rolePermissionSet1.Equals((object)rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both instances have null Name property.
        /// </summary>
        [Test]
        public void Equals_BothNullNames_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = false
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = false
            };

            // Act
            bool result = rolePermissionSet1.Equals((object)rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one instance has null Name and the other has a non-null Name.
        /// </summary>
        [Test]
        public void Equals_OneNullName_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = false
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false
            };

            // Act
            bool result = rolePermissionSet1.Equals((object)rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true for two instances with empty strings as Name.
        /// </summary>
        [Test]
        public void Equals_EmptyNames_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = string.Empty,
                DoNotInheirit = true
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = string.Empty,
                DoNotInheirit = true
            };

            // Act
            bool result = rolePermissionSet1.Equals((object)rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with an object of a numeric type.
        /// </summary>
        [Test]
        public void Equals_NumericType_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false
            };
            object numericValue = 42;

            // Act
            bool result = rolePermissionSet.Equals(numericValue);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value when called multiple times on the same object instance.
        /// Input: Same RolePermissionSet instance.
        /// Expected: Consistent hash code across multiple calls.
        /// </summary>
        [Test]
        public void GetHashCode_SameObjectCalledMultipleTimes_ReturnsSameHashCode()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    }
                ]
            };

            // Act
            int hashCode1 = rolePermissionSet.GetHashCode();
            int hashCode2 = rolePermissionSet.GetHashCode();
            int hashCode3 = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(hashCode2, Is.EqualTo(hashCode3));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same value for two objects with identical property values.
        /// Input: Two RolePermissionSet instances with identical properties.
        /// Expected: Same hash code for both instances.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    }
                ]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    }
                ]
            };

            // Act
            int hashCode1 = rolePermissionSet1.GetHashCode();
            int hashCode2 = rolePermissionSet2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns a valid hash code when RolePermission array is null.
        /// Input: RolePermissionSet with null RolePermission array.
        /// Expected: Valid hash code without throwing exception.
        /// </summary>
        [Test]
        public void GetHashCode_WithNullRolePermissionArray_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode returns a valid hash code when RolePermission array is empty.
        /// Input: RolePermissionSet with empty RolePermission array.
        /// Expected: Valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_WithEmptyRolePermissionArray_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission = []
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode returns a valid hash code when Name is null.
        /// Input: RolePermissionSet with null Name.
        /// Expected: Valid hash code without throwing exception.
        /// </summary>
        [Test]
        public void GetHashCode_WithNullName_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = true,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    }
                ]
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different Name values.
        /// Input: Two RolePermissionSet instances with different Name properties.
        /// Expected: Different hash codes (though collisions are technically allowed).
        /// </summary>
        [Test]
        public void GetHashCode_WithDifferentNames_ProducesDifferentHashCodes()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "Role1",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "Role2",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            int hashCode1 = rolePermissionSet1.GetHashCode();
            int hashCode2 = rolePermissionSet2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different DoNotInheirit values.
        /// Input: Two RolePermissionSet instances with different DoNotInheirit properties.
        /// Expected: Different hash codes (though collisions are technically allowed).
        /// </summary>
        [Test]
        public void GetHashCode_WithDifferentDoNotInheirit_ProducesDifferentHashCodes()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission = null
            };

            // Act
            int hashCode1 = rolePermissionSet1.GetHashCode();
            int hashCode2 = rolePermissionSet2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode produces different hash codes for objects with different RolePermission arrays.
        /// Input: Two RolePermissionSet instances with different RolePermission arrays.
        /// Expected: Different hash codes (though collisions are technically allowed).
        /// </summary>
        [Test]
        public void GetHashCode_WithDifferentRolePermissions_ProducesDifferentHashCodes()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    }
                ]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("User", "http://test.com")
                    }
                ]
            };

            // Act
            int hashCode1 = rolePermissionSet1.GetHashCode();
            int hashCode2 = rolePermissionSet2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns a valid hash code when all properties are set to non-null values.
        /// Input: RolePermissionSet with all properties populated.
        /// Expected: Valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_WithAllPropertiesSet_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "FullRole",
                DoNotInheirit = true,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    },
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("User", "http://test.com")
                    }
                ]
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode returns a valid hash code when all properties are at their default or null values.
        /// Input: RolePermissionSet with all properties null or default.
        /// Expected: Valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_WithAllPropertiesNullOrDefault_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for objects with arrays containing the same elements.
        /// Input: Two RolePermissionSet instances with RolePermission arrays containing equivalent elements.
        /// Expected: Same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_WithSameArrayContents_ReturnsSameHashCode()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    },
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("User", "http://test.com")
                    }
                ]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = true,
                RolePermission =
                [
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("Admin", "http://test.com")
                    },
                    new RolePermission
                    {
                        Role = new XmlQualifiedName("User", "http://test.com")
                    }
                ]
            };

            // Act
            int hashCode1 = rolePermissionSet1.GetHashCode();
            int hashCode2 = rolePermissionSet2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles extreme string values for Name property.
        /// Input: RolePermissionSet with empty string, whitespace, and very long string for Name.
        /// Expected: Valid hash codes for all cases.
        /// </summary>
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("   \t\n")]
        public void GetHashCode_WithSpecialStringValues_ReturnsValidHashCode(string name)
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = name,
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles very long strings for Name property.
        /// Input: RolePermissionSet with extremely long Name string.
        /// Expected: Valid hash code without performance issues.
        /// </summary>
        [Test]
        public void GetHashCode_WithVeryLongName_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = new string('A', 10000),
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that GetHashCode handles arrays with multiple RolePermission entries.
        /// Input: RolePermissionSet with large RolePermission array.
        /// Expected: Valid hash code.
        /// </summary>
        [Test]
        public void GetHashCode_WithLargeRolePermissionArray_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermissions = new RolePermission[100];
            for (int i = 0; i < 100; i++)
            {
                rolePermissions[i] = new RolePermission
                {
                    Role = new XmlQualifiedName($"Role{i}", "http://test.com")
                };
            }

            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestRole",
                DoNotInheirit = false,
                RolePermission = rolePermissions
            };

            // Act
            int hashCode = rolePermissionSet.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.TypeOf<int>());
        }

        /// <summary>
        /// Tests that Equals returns false when the other parameter is null.
        /// </summary>
        [Test]
        public void Equals_OtherIsNull_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [new RolePermission()]
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = rolePermissionSet.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when all properties are equal.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesEqual_ReturnsTrue()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Role = new XmlQualifiedName("TestRole", "http://test.com")
            };

            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = true,
                RolePermission = [rolePermission]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = true,
                RolePermission = [rolePermission]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both RolePermission arrays are null.
        /// </summary>
        [Test]
        public void Equals_BothRolePermissionArraysNull_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both RolePermission arrays are empty.
        /// </summary>
        [Test]
        public void Equals_BothRolePermissionArraysEmpty_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = []
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = []
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when RolePermission arrays differ (one null, one not null).
        /// </summary>
        [Test]
        public void Equals_RolePermissionOneNullOneNotNull_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [new RolePermission()]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when RolePermission arrays have different lengths.
        /// </summary>
        [Test]
        public void Equals_RolePermissionDifferentLengths_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [new RolePermission()]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [new RolePermission(), new RolePermission()]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when RolePermission arrays have different content.
        /// </summary>
        [Test]
        public void Equals_RolePermissionDifferentContent_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var rolePermission2 = new RolePermission
            {
                Role = new XmlQualifiedName("Role2", "http://test.com")
            };

            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [rolePermission1]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [rolePermission2]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Name properties are null.
        /// </summary>
        [Test]
        public void Equals_BothNamesNull_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Name properties differ (one null, one not null).
        /// </summary>
        [Test]
        public void Equals_NameOneNullOneNotNull_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = null,
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Name properties are empty strings.
        /// </summary>
        [Test]
        public void Equals_BothNamesEmpty_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = string.Empty,
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = string.Empty,
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Name properties are different non-empty strings.
        /// </summary>
        [Test]
        public void Equals_NamesDifferent_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "Name1",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "Name2",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when Name properties have the same value.
        /// </summary>
        [Test]
        public void Equals_NamesSameValue_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when DoNotInheirit properties differ.
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Equals_DoNotInheiritDifferent_ReturnsFalse(bool value1, bool value2)
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = value1,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = value2,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when DoNotInheirit properties are the same.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void Equals_DoNotInheiritSame_ReturnsTrue(bool value)
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = value,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = value,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when only RolePermission property differs.
        /// </summary>
        [Test]
        public void Equals_OnlyRolePermissionDifferent_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var rolePermission2 = new RolePermission
            {
                Role = new XmlQualifiedName("Role2", "http://test.com")
            };

            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = true,
                RolePermission = [rolePermission1]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = true,
                RolePermission = [rolePermission2]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when only Name property differs.
        /// </summary>
        [Test]
        public void Equals_OnlyNameDifferent_ReturnsFalse()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Role = new XmlQualifiedName("Role", "http://test.com")
            };

            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "Name1",
                DoNotInheirit = true,
                RolePermission = [rolePermission]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "Name2",
                DoNotInheirit = true,
                RolePermission = [rolePermission]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when only DoNotInheirit property differs.
        /// </summary>
        [Test]
        public void Equals_OnlyDoNotInheiritDifferent_ReturnsFalse()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Role = new XmlQualifiedName("Role", "http://test.com")
            };

            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = true,
                RolePermission = [rolePermission]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [rolePermission]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when all properties differ.
        /// </summary>
        [Test]
        public void Equals_AllPropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var rolePermission2 = new RolePermission
            {
                Role = new XmlQualifiedName("Role2", "http://test.com")
            };

            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "Name1",
                DoNotInheirit = true,
                RolePermission = [rolePermission1]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "Name2",
                DoNotInheirit = false,
                RolePermission = [rolePermission2]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when Name is whitespace-only and matches.
        /// </summary>
        [Test]
        public void Equals_NamesWhitespaceMatching_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "   ",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "   ",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Name is whitespace but different.
        /// </summary>
        [Test]
        public void Equals_NamesWhitespaceDifferent_ReturnsFalse()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "  ",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "   ",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when Name contains special characters and matches.
        /// </summary>
        [Test]
        public void Equals_NamesSpecialCharactersMatching_ReturnsTrue()
        {
            // Arrange
            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "Test!@#$%^&*()_+-=[]{}|;':\",./<>?`~",
                DoNotInheirit = false,
                RolePermission = null
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "Test!@#$%^&*()_+-=[]{}|;':\",./<>?`~",
                DoNotInheirit = false,
                RolePermission = null
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when RolePermission arrays have multiple matching elements.
        /// </summary>
        [Test]
        public void Equals_RolePermissionMultipleElementsMatching_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var rolePermission2 = new RolePermission
            {
                Role = new XmlQualifiedName("Role2", "http://test.com")
            };

            var rolePermissionSet1 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [rolePermission1, rolePermission2]
            };

            var rolePermissionSet2 = new RolePermissionSet
            {
                Name = "TestName",
                DoNotInheirit = false,
                RolePermission = [rolePermission1, rolePermission2]
            };

            // Act
            bool result = rolePermissionSet1.Equals(rolePermissionSet2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
