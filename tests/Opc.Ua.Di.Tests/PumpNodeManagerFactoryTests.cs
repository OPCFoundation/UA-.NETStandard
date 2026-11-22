// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Smoke tests for the <see cref="global::Pumps.PumpNodeManagerFactory"/>:
    /// namespace URIs reported, factory is async-capable.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    public sealed class PumpNodeManagerFactoryTests
    {
        [Test]
        public void FactoryReportsAllThreeNamespaces()
        {
            var factory = new global::Pumps.PumpNodeManagerFactory();
            Assert.That(factory, Is.InstanceOf<IAsyncNodeManagerFactory>());

            ArrayOf<string> uris = factory.NamespacesUris;
            Assert.That(uris.ToArray(), Has.Member("http://opcfoundation.org/UA/Pumps/"));
            Assert.That(uris.ToArray(), Has.Member("http://opcfoundation.org/UA/Machinery/"));
            Assert.That(uris.ToArray(), Has.Member("http://opcfoundation.org/UA/DI/"));
        }
    }
}
