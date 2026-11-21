// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerCapabilitiesTests
    {
        [Test]
        public void ConstructorCreatesInstance()
        {
            var capabilities = new ServerCapabilities();
            Assert.That(capabilities, Is.Not.Null);
        }

        [Test]
        public void ConstructorPopulatesFromGeneratedCatalog()
        {
            var capabilities = new ServerCapabilities();
            int count = capabilities.Count();
            Assert.That(count, Is.EqualTo(ServerCapability.All.Count));
            Assert.That(count, Is.GreaterThan(0));
        }

        [Test]
        public void FindReturnsCapabilityById()
        {
            var capabilities = new ServerCapabilities();
            ServerCapabilityInfo result = capabilities.Find(ServerCapability.DA)!;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo("DA"));
            Assert.That(result.Description, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void FindReturnsCapabilityForGdsAndLds()
        {
            var capabilities = new ServerCapabilities();
            Assert.That(capabilities.Find(ServerCapability.GDS), Is.Not.Null);
            Assert.That(capabilities.Find(ServerCapability.LDS), Is.Not.Null);
        }

        [Test]
        public void FindReturnsNullForUnknownId()
        {
            var capabilities = new ServerCapabilities();
            ServerCapabilityInfo? result = capabilities.Find("UNKNOWN_XYZ");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindReturnsNullForNullId()
        {
            var capabilities = new ServerCapabilities();
            ServerCapabilityInfo? result = capabilities.Find(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetEnumeratorEnumeratesCapabilities()
        {
            var capabilities = new ServerCapabilities();
            var list = new List<ServerCapabilityInfo>();

            list.AddRange(capabilities);

            Assert.That(list, Has.Count.EqualTo(ServerCapability.All.Count));
            Assert.That(list.All(c => !string.IsNullOrEmpty(c.Id)), Is.True);
        }
    }
}
