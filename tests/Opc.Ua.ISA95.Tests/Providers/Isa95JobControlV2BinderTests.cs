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
