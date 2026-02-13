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
    /// Unit tests for the RolePermission class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RolePermissionTests
    {
        /// <summary>
        /// Tests that GetHashCode returns a consistent hash code for the same instance.
        /// Expected: The same hash code is returned on multiple calls.
        /// </summary>
        [Test]
        public void GetHashCode_SameInstance_ReturnsConsistentHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Write],
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission.GetHashCode();
            int hashCode2 = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for equal objects.
        /// Expected: Objects with the same Permission and Role return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualObjects_ReturnsSameHashCode()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Write],
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Write],
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different Permission arrays.
        /// Expected: Different Permission arrays should likely result in different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentPermissions_ReturnsDifferentHashCodes()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("User", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Write],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for objects with different Roles.
        /// Expected: Different Role values should likely result in different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentRoles_ReturnsDifferentHashCodes()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Permission array.
        /// Expected: Returns a valid hash code without throwing an exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullPermission_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = null,
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };

            // Act
            int hashCode = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles null Role.
        /// Expected: Returns a valid hash code without throwing an exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullRole_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = null
            };

            // Act
            int hashCode = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles both null Permission and null Role.
        /// Expected: Returns a valid hash code without throwing an exception.
        /// </summary>
        [Test]
        public void GetHashCode_NullPermissionAndNullRole_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = null,
                Role = null
            };

            // Act
            int hashCode = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles empty Permission array.
        /// Expected: Returns a valid hash code for empty Permission array.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyPermissionArray_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            int hashCode = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode returns the same hash code for equal empty Permission arrays.
        /// Expected: Two objects with empty Permission arrays and same Role return the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_EqualEmptyPermissionArrays_ReturnsSameHashCode()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("User", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles Permission arrays with multiple elements.
        /// Expected: Returns consistent hash codes for arrays with same elements in same order.
        /// </summary>
        [Test]
        public void GetHashCode_MultiplePermissions_ReturnsConsistentHashCode()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read, Permissions.Write, Permissions.Call],
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read, Permissions.Write, Permissions.Call],
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode returns different hash codes for Permission arrays with same elements but different order.
        /// Expected: Arrays with same elements in different order should result in different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentPermissionOrder_ReturnsDifferentHashCodes()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Write],
                Role = new XmlQualifiedName("User", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Write, Permissions.Read],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles Role with different namespaces.
        /// Expected: Roles with different namespaces should result in different hash codes.
        /// </summary>
        [Test]
        public void GetHashCode_DifferentRoleNamespaces_ReturnsDifferentHashCodes()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("Administrator", "http://example1.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("Administrator", "http://example2.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.Not.EqualTo(hashCode2));
        }

        /// <summary>
        /// Tests that GetHashCode handles Role with empty name.
        /// Expected: Returns a valid hash code for empty Role name.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyRoleName_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName(string.Empty, "http://example.com")
            };

            // Act
            int hashCode = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles Role with empty namespace.
        /// Expected: Returns a valid hash code for empty Role namespace.
        /// </summary>
        [Test]
        public void GetHashCode_EmptyRoleNamespace_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("Administrator", string.Empty)
            };

            // Act
            int hashCode = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode handles all defined Permission enum values.
        /// Expected: Returns a valid hash code for all Permission enum combinations.
        /// </summary>
        [Test]
        public void GetHashCode_AllPermissionValues_ReturnsValidHashCode()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission =
                [
                    Permissions.Browse, Permissions.ReadRolePermissions, Permissions.WriteAttribute,
                    Permissions.WriteRolePermissions, Permissions.WriteHistorizing, Permissions.Read,
                    Permissions.Write, Permissions.ReadHistory, Permissions.InsertHistory,
                    Permissions.ModifyHistory, Permissions.DeleteHistory, Permissions.ReceiveEvents,
                    Permissions.Call, Permissions.AddReference, Permissions.RemoveReference,
                    Permissions.DeleteNode, Permissions.AddNode, Permissions.All,
                    Permissions.AllRead, Permissions.None
                ],
                Role = new XmlQualifiedName("Administrator", "http://example.com")
            };

            // Act
            int hashCode = rolePermission.GetHashCode();

            // Assert
            Assert.That(hashCode, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Tests that GetHashCode returns same hash code for objects where only non-hash properties differ.
        /// This ensures GetHashCode is only based on Permission and Role, not other potential properties.
        /// Expected: Two equal objects produce the same hash code.
        /// </summary>
        [Test]
        public void GetHashCode_SamePermissionAndRole_ReturnsSameHashCodeRegardlessOfOtherProperties()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Write],
                Role = new XmlQualifiedName("User", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Write],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            int hashCode1 = rolePermission1.GetHashCode();
            int hashCode2 = rolePermission2.GetHashCode();

            // Assert
            Assert.That(hashCode1, Is.EqualTo(hashCode2));
            Assert.That(rolePermission1.Equals(rolePermission2), Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when comparing with null.
        /// </summary>
        [Test]
        public void Equals_NullParameter_ReturnsFalse()
        {
            // Arrange
            var instance = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = instance.Equals(null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an instance with itself.
        /// </summary>
        [Test]
        public void Equals_SameInstance_ReturnsTrue()
        {
            // Arrange
            var instance = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance.Equals(instance);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two instances with identical Permission arrays and Role.
        /// </summary>
        [Test]
        public void Equals_IdenticalPermissionAndRole_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Permission arrays differ.
        /// </summary>
        [Test]
        public void Equals_DifferentPermissions_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Write, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Role differs.
        /// </summary>
        [Test]
        public void Equals_DifferentRole_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role2", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when both Permission and Role differ.
        /// </summary>
        [Test]
        public void Equals_DifferentPermissionAndRole_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Write, Permissions.Call],
                Role = new XmlQualifiedName("Role2", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Permission arrays have different lengths.
        /// </summary>
        [Test]
        public void Equals_DifferentPermissionArrayLength_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read, Permissions.Write],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Permission arrays are null and Roles are equal.
        /// </summary>
        [Test]
        public void Equals_BothPermissionsNull_SameRole_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = null,
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = null,
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one Permission array is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OnePermissionNull_OtherNotNull_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = null,
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Permission arrays are empty and Roles are equal.
        /// </summary>
        [Test]
        public void Equals_BothPermissionsEmpty_SameRole_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one Permission array is empty and the other has elements.
        /// </summary>
        [Test]
        public void Equals_OnePermissionEmpty_OtherHasElements_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Roles are null and Permission arrays are equal.
        /// </summary>
        [Test]
        public void Equals_BothRolesNull_SamePermission_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = null
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = null
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when one Role is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneRoleNull_OtherNotNull_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = null
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when Role namespaces differ.
        /// </summary>
        [Test]
        public void Equals_DifferentRoleNamespace_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test1.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test2.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Permission and Role are null.
        /// </summary>
        [Test]
        public void Equals_BothPermissionsAndRolesNull_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = null,
                Role = null
            };

            var instance2 = new RolePermission
            {
                Permission = null,
                Role = null
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Permission arrays have same elements but in different order.
        /// </summary>
        [Test]
        public void Equals_PermissionsDifferentOrder_ReturnsFalse()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Browse],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when Permission arrays have single identical element.
        /// </summary>
        [Test]
        public void Equals_SinglePermissionElement_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when Permission arrays have multiple identical elements including duplicates.
        /// </summary>
        [Test]
        public void Equals_PermissionsWithDuplicates_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read, Permissions.Browse],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read, Permissions.Browse],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when Roles have empty name and same namespace.
        /// </summary>
        [Test]
        public void Equals_RoleWithEmptyName_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName(string.Empty, "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName(string.Empty, "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing instances with all possible Permission enum values.
        /// </summary>
        [Test]
        public void Equals_AllPermissionTypes_ReturnsTrue()
        {
            // Arrange
            var instance1 = new RolePermission
            {
                Permission =
                [
                    Permissions.Browse,
                    Permissions.ReadRolePermissions,
                    Permissions.WriteAttribute,
                    Permissions.WriteRolePermissions,
                    Permissions.WriteHistorizing,
                    Permissions.Read,
                    Permissions.Write,
                    Permissions.ReadHistory,
                    Permissions.InsertHistory,
                    Permissions.ModifyHistory,
                    Permissions.DeleteHistory,
                    Permissions.ReceiveEvents,
                    Permissions.Call,
                    Permissions.AddReference,
                    Permissions.RemoveReference,
                    Permissions.DeleteNode,
                    Permissions.AddNode,
                    Permissions.All,
                    Permissions.AllRead,
                    Permissions.None
                ],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            var instance2 = new RolePermission
            {
                Permission =
                [
                    Permissions.Browse,
                    Permissions.ReadRolePermissions,
                    Permissions.WriteAttribute,
                    Permissions.WriteRolePermissions,
                    Permissions.WriteHistorizing,
                    Permissions.Read,
                    Permissions.Write,
                    Permissions.ReadHistory,
                    Permissions.InsertHistory,
                    Permissions.ModifyHistory,
                    Permissions.DeleteHistory,
                    Permissions.ReceiveEvents,
                    Permissions.Call,
                    Permissions.AddReference,
                    Permissions.RemoveReference,
                    Permissions.DeleteNode,
                    Permissions.AddNode,
                    Permissions.All,
                    Permissions.AllRead,
                    Permissions.None
                ],
                Role = new XmlQualifiedName("Role1", "http://test.com")
            };

            // Act
            bool result = instance1.Equals(instance2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when the input object is null.
        /// </summary>
        [Test]
        public void Equals_NullObject_ReturnsFalse()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
#pragma warning disable CA1508 // Avoid dead conditional code
            bool result = rolePermission.Equals((object)null);
#pragma warning restore CA1508 // Avoid dead conditional code

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing an object to itself.
        /// </summary>
        [Test]
        public void Equals_SameReference_ReturnsTrue()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            bool result = rolePermission.Equals((object)rolePermission);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when the input object is of a different type.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            // Arrange
            var rolePermission = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            object differentType = "string object";

            // Act
            bool result = rolePermission.Equals(differentType);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when comparing two RolePermission objects with identical values.
        /// </summary>
        [Test]
        public void Equals_IdenticalValues_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Permission arrays differ in values.
        /// </summary>
        [Test]
        public void Equals_DifferentPermissionValues_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one Permission array is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OnePermissionArrayNull_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = null,
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Permission arrays are null.
        /// </summary>
        [Test]
        public void Equals_BothPermissionArraysNull_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = null,
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = null,
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when both Permission arrays are empty.
        /// </summary>
        [Test]
        public void Equals_BothPermissionArraysEmpty_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Role values differ in name.
        /// </summary>
        [Test]
        public void Equals_DifferentRoleName_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("User", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns false when one Role is null and the other is not.
        /// </summary>
        [Test]
        public void Equals_OneRoleNull_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = null
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both Role values are null.
        /// </summary>
        [Test]
        public void Equals_BothRolesNull_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = null
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = null
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when both Permission and Role differ.
        /// </summary>
        [Test]
        public void Equals_BothPropertiesDifferent_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Read],
                Role = new XmlQualifiedName("User", "http://different.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when both objects are completely empty.
        /// </summary>
        [Test]
        public void Equals_BothObjectsEmpty_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission();
            var rolePermission2 = new RolePermission();

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns true when Permission arrays contain all possible enum values and match.
        /// </summary>
        [Test]
        public void Equals_AllPermissionEnumValues_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission =
                [
                    Permissions.Browse,
                    Permissions.ReadRolePermissions,
                    Permissions.WriteAttribute,
                    Permissions.WriteRolePermissions,
                    Permissions.WriteHistorizing,
                    Permissions.Read,
                    Permissions.Write,
                    Permissions.ReadHistory,
                    Permissions.InsertHistory,
                    Permissions.ModifyHistory,
                    Permissions.DeleteHistory,
                    Permissions.ReceiveEvents,
                    Permissions.Call,
                    Permissions.AddReference,
                    Permissions.RemoveReference,
                    Permissions.DeleteNode,
                    Permissions.AddNode
                ],
                Role = new XmlQualifiedName("SuperAdmin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission =
                [
                    Permissions.Browse,
                    Permissions.ReadRolePermissions,
                    Permissions.WriteAttribute,
                    Permissions.WriteRolePermissions,
                    Permissions.WriteHistorizing,
                    Permissions.Read,
                    Permissions.Write,
                    Permissions.ReadHistory,
                    Permissions.InsertHistory,
                    Permissions.ModifyHistory,
                    Permissions.DeleteHistory,
                    Permissions.ReceiveEvents,
                    Permissions.Call,
                    Permissions.AddReference,
                    Permissions.RemoveReference,
                    Permissions.DeleteNode,
                    Permissions.AddNode
                ],
                Role = new XmlQualifiedName("SuperAdmin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that Equals returns false when Permission arrays have same length but different order.
        /// </summary>
        [Test]
        public void Equals_PermissionArraysDifferentOrder_ReturnsFalse()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse, Permissions.Read],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Read, Permissions.Browse],
                Role = new XmlQualifiedName("Admin", "http://example.com")
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that Equals returns true when Role has empty string name and namespace.
        /// </summary>
        [Test]
        public void Equals_RoleWithEmptyStrings_ReturnsTrue()
        {
            // Arrange
            var rolePermission1 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName(string.Empty, string.Empty)
            };
            var rolePermission2 = new RolePermission
            {
                Permission = [Permissions.Browse],
                Role = new XmlQualifiedName(string.Empty, string.Empty)
            };

            // Act
            bool result = rolePermission1.Equals((object)rolePermission2);

            // Assert
            Assert.That(result, Is.True);
        }
    }
}
