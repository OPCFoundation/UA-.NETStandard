/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Tests.Providers
{
    [TestFixture]
    public sealed class Isa95JobControlV2BinderTests
    {
        [Test]
        public void NamespaceIndexIsTheIndexOfTheJobControlV2Model()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:other");
            namespaceUris.Append(V2.Namespaces.ISA95JobControlV2);
            var binder = new Isa95JobControlV2Binder(
                new SystemContext(telemetry: null!),
                namespaceUris);

            Assert.That(binder.JobControlV2NamespaceIndex, Is.EqualTo((ushort)2));
        }

        [Test]
        public void AMissingJobControlV2NamespaceIsAConfigurationError()
        {
            // A binder used by a node manager that did not load the Job
            // Control V2 model must not fall back to namespace index 65535.
            var binder = new Isa95JobControlV2Binder(
                new SystemContext(telemetry: null!),
                new NamespaceTable());

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => _ = binder.JobControlV2NamespaceIndex)!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }
    }
}
