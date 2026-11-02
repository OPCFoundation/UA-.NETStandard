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

using System;
using NUnit.Framework;
using Opc.Ua.LADS.Server;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Tests the validation of the LADS server options.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [Parallelizable]
    public sealed class LadsServerOptionsTests
    {
        [Test]
        public void DefaultsAreValid()
        {
            var options = new LadsServerOptions();

            Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:opcua-netstandard:lads:instances"));
            Assert.That(options.MaxResultFileBytes, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(options.MaxRetainedResults, Is.EqualTo(100));
            Assert.That(options.RequiredMachineryParts, Is.EqualTo(MachineryParts.BuildingBlocks));
            Assert.That(options.Validate, Throws.Nothing);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveMaxResultFileBytesIsRejected(int value)
        {
            var options = new LadsServerOptions { MaxResultFileBytes = value };

            Assert.That(options.Validate, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void NonPositiveMaxRetainedResultsIsRejected(int value)
        {
            var options = new LadsServerOptions { MaxRetainedResults = value };

            Assert.That(options.Validate, Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [TestCase("")]
        [TestCase("relative/path")]
        public void RelativeInstanceNamespaceIsRejected(string uri)
        {
            var options = new LadsServerOptions { InstanceNamespaceUri = uri };

            Assert.That(options.Validate, Throws.ArgumentException);
        }

        [TestCase(Namespaces.LADS)]
        [TestCase(Opc.Ua.Machinery.Namespaces.Machinery)]
        [TestCase(Opc.Ua.Di.Namespaces.OpcUaDi)]
        [TestCase(Opc.Ua.Namespaces.OpcUa)]
        public void ModelNamespaceIsRejected(string uri)
        {
            var options = new LadsServerOptions { InstanceNamespaceUri = uri };

            Assert.That(options.Validate, Throws.ArgumentException);
        }

        [Test]
        public void CustomNamespaceIsAccepted()
        {
            var options = new LadsServerOptions
            {
                InstanceNamespaceUri = "urn:example:incubators",
                MaxResultFileBytes = 1,
                MaxRetainedResults = 1
            };

            Assert.That(options.Validate, Throws.Nothing);
        }
    }
}
