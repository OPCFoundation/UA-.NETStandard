/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the namespace default permissions remembered on <see cref="OperationContext"/>
    /// for the duration of one service request.
    /// </summary>
    [TestFixture]
    [Category("OperationContext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class OperationContextDefaultPermissionsCacheTests
    {
        /// <summary>
        /// A service request remembers the defaults of each namespace separately.
        /// </summary>
        [Test]
        public void ServiceRequestRemembersDefaultsPerNamespace()
        {
            using OperationContext operation = CreateOperation(RequestType.Read);
            ArrayOf<RolePermissionType> roles = new[]
            {
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)PermissionType.Browse
                }
            }.ToArrayOf();

            Assert.That(operation.TryGetDefaultPermissions(2, out _), Is.False);

            operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                2, AccessRestrictionType.EncryptionRequired, roles, default));
            operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                3, AccessRestrictionType.None, default, default));

            Assert.That(operation.TryGetDefaultPermissions(2, out NamespaceDefaultPermissions ns2), Is.True);
            Assert.That(ns2.DefaultAccessRestrictions, Is.EqualTo(AccessRestrictionType.EncryptionRequired));
            Assert.That(ns2.DefaultRolePermissions.Count, Is.EqualTo(1));
            Assert.That(operation.TryGetDefaultPermissions(3, out NamespaceDefaultPermissions ns3), Is.True);
            Assert.That(ns3.DefaultAccessRestrictions, Is.EqualTo(AccessRestrictionType.None));
            Assert.That(ns3.DefaultRolePermissions.IsNull, Is.True);
            Assert.That(operation.TryGetDefaultPermissions(4, out _), Is.False);
        }

        /// <summary>
        /// A context that is not a service request, such as one kept for a monitored item,
        /// remembers nothing, so a changed default applies to it on the next check.
        /// </summary>
        [Test]
        public void NonRequestContextRemembersNothing()
        {
            using OperationContext operation = CreateOperation(RequestType.Unknown);

            operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                2, AccessRestrictionType.EncryptionRequired, default, default));

            Assert.That(operation.TryGetDefaultPermissions(2, out _), Is.False);
        }

        private static OperationContext CreateOperation(RequestType requestType)
        {
            return new OperationContext(
                new RequestHeader(),
                null,
                requestType,
                RequestLifetime.None);
        }
    }
}
