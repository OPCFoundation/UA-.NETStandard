// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
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
    [NonParallelizable]
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

        /// <summary>
        /// A request over many namespaces, below and beyond the slot array, returns the
        /// defaults of each namespace, and an entry of a retired generation is not returned.
        /// </summary>
        [Test]
        public void ServiceRequestRemembersDefaultsAcrossManyNamespaces()
        {
            using OperationContext operation = CreateOperation(RequestType.Read);
            for (ushort ii = 0; ii < 300; ii++)
            {
                operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                    ii, RestrictionsOf(ii), default, default));
            }

            for (ushort ii = 0; ii < 300; ii++)
            {
                Assert.That(operation.TryGetDefaultPermissions(ii, out NamespaceDefaultPermissions entry), Is.True);
                Assert.That(entry.NamespaceIndex, Is.EqualTo(ii));
                Assert.That(entry.DefaultAccessRestrictions, Is.EqualTo(RestrictionsOf(ii)));
            }

            Assert.That(operation.TryGetDefaultPermissions(300, out _), Is.False);

            NamespaceDefaultPermissionReader.Invalidate();

            Assert.That(operation.TryGetDefaultPermissions(1, out _), Is.False);
            Assert.That(operation.TryGetDefaultPermissions(200, out _), Is.False);
        }

        /// <summary>
        /// Node managers validating the nodes of one request concurrently never observe the
        /// defaults of another namespace.
        /// </summary>
        [Test]
        public void ConcurrentValidationReturnsTheRequestedNamespace()
        {
            using OperationContext operation = CreateOperation(RequestType.Read);
            int mismatches = 0;
            Parallel.For(0, 64_000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, ii =>
            {
                ushort namespaceIndex = (ushort)(ii % 160);
                if (operation.TryGetDefaultPermissions(namespaceIndex, out NamespaceDefaultPermissions entry))
                {
                    if (entry.NamespaceIndex != namespaceIndex ||
                        entry.DefaultAccessRestrictions != RestrictionsOf(namespaceIndex))
                    {
                        Interlocked.Increment(ref mismatches);
                    }

                    return;
                }

                operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                    namespaceIndex, RestrictionsOf(namespaceIndex), default, default));
            });

            Assert.That(mismatches, Is.Zero);
            for (ushort ii = 0; ii < 160; ii++)
            {
                Assert.That(operation.TryGetDefaultPermissions(ii, out _), Is.True);
            }
        }

        /// <summary>
        /// A context that stops remembering drops what it remembered.
        /// </summary>
        [Test]
        public void DisablingTheCacheDropsEntries()
        {
            using OperationContext operation = CreateOperation(RequestType.Read);
            operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                2, AccessRestrictionType.None, default, default));
            operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                100, AccessRestrictionType.None, default, default));

            operation.DisableDefaultPermissionCache();
            operation.SetDefaultPermissions(new NamespaceDefaultPermissions(
                3, AccessRestrictionType.None, default, default));

            Assert.That(operation.TryGetDefaultPermissions(2, out _), Is.False);
            Assert.That(operation.TryGetDefaultPermissions(100, out _), Is.False);
            Assert.That(operation.TryGetDefaultPermissions(3, out _), Is.False);
        }

        private static AccessRestrictionType RestrictionsOf(ushort namespaceIndex)
        {
            return (AccessRestrictionType)(namespaceIndex % 16);
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
