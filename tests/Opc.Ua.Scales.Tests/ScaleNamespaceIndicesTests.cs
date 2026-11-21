// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.Scales.Server.Builders;

namespace Opc.Ua.Scales.Tests
{
    [TestFixture]
    [Category("Scales")]
    public sealed class ScaleNamespaceIndicesTests
    {
        [Test]
        public void ResolveReturnsRegisteredIndicesInChildSearchOrder()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.Scales);
            namespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            namespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            namespaceUris.GetIndexOrAppend(Opc.Ua.IA.Namespaces.IA);
            namespaceUris.GetIndexOrAppend(Opc.Ua.PackML.Namespaces.PackML);

            ScaleNamespaceIndices actual = ScaleNamespaceIndices.Resolve(namespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(actual.Scales, Is.EqualTo((ushort)namespaceUris.GetIndex(Namespaces.Scales)));
                Assert.That(actual.Di, Is.EqualTo((ushort)namespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi)));
                Assert.That(
                    actual.Machinery,
                    Is.EqualTo((ushort)namespaceUris.GetIndex(Opc.Ua.Machinery.Namespaces.Machinery)));
                Assert.That(actual.IA, Is.EqualTo((ushort)namespaceUris.GetIndex(Opc.Ua.IA.Namespaces.IA)));
                Assert.That(actual.PackML, Is.EqualTo((ushort)namespaceUris.GetIndex(Opc.Ua.PackML.Namespaces.PackML)));
                Assert.That(
                    actual.SearchOrder.ToArray(),
                    Is.EqualTo(new[]
                    {
                        actual.Scales,
                        actual.Di,
                        actual.Machinery,
                        actual.IA,
                        actual.PackML,
                        (ushort)0
                    }));
            });
        }

        [Test]
        public void ResolveRejectsAnUnregisteredModelNamespace()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.Scales);
            namespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
            namespaceUris.GetIndexOrAppend(Opc.Ua.Machinery.Namespaces.Machinery);
            namespaceUris.GetIndexOrAppend(Opc.Ua.IA.Namespaces.IA);

            ServiceResultException? failure =
                Assert.Throws<ServiceResultException>(() => ScaleNamespaceIndices.Resolve(namespaceUris));

            Assert.That(failure!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }
    }
}
