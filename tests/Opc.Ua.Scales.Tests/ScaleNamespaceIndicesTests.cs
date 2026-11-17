/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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
