// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class AdminCredentialsTests
    {
        [Test]
        public void ConstructorCreatesInstance()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args, Is.Not.Null);
        }

        [Test]
        public void InheritsFromEventArgs()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args, Is.InstanceOf<EventArgs>());
        }

        [Test]
        public void CredentialsDefaultsToNull()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args.Credentials, Is.Null);
        }

        [Test]
        public void CredentialsRoundTrip()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            var identity = new UserIdentity();
            args.Credentials = identity;
            Assert.That(args.Credentials, Is.SameAs(identity));
        }

        [Test]
        public void CacheCredentialsDefaultsToFalse()
        {
            var args = new AdminCredentialsRequiredEventArgs();
            Assert.That(args.CacheCredentials, Is.False);
        }

        [Test]
        public void CacheCredentialsRoundTrip()
        {
            var args = new AdminCredentialsRequiredEventArgs
            {
                CacheCredentials = true
            };
            Assert.That(args.CacheCredentials, Is.True);
        }
    }
}
