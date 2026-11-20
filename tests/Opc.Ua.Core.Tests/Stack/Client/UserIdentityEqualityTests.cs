// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Verifies that <see cref="UserIdentity.Equals(object)"/> and
    /// <see cref="UserIdentity.GetHashCode"/> agree.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Parallelizable]
    public sealed class UserIdentityEqualityTests
    {
        [Test]
        public void EqualAnonymousIdentitiesWithDifferentPolicyIdHaveEqualHashCodes()
        {
            var a = new UserIdentity();
            var b = new UserIdentity { PolicyId = "1" };

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(new HashSet<UserIdentity> { a, b }, Has.Count.EqualTo(1));
        }

        [Test]
        public void EqualUserNameIdentitiesWithDifferentDisplayNameHaveEqualHashCodes()
        {
            var a = new UserIdentity("user", Encoding.UTF8.GetBytes("password"));
            var b = new UserIdentity("user", Encoding.UTF8.GetBytes("password"))
            {
                DisplayName = "Other",
                PolicyId = "2"
            };

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void DifferentUserNamesAreNotEqual()
        {
            var a = new UserIdentity("user1", Encoding.UTF8.GetBytes("password"));
            var b = new UserIdentity("user2", Encoding.UTF8.GetBytes("password"));

            Assert.That(a, Is.Not.EqualTo(b));
        }
    }
}
