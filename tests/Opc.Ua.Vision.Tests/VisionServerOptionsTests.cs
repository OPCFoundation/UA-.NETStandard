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
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Covers <see cref="VisionServerOptions.Validate"/> — the five failure
    /// branches (blank URI, non-absolute URI, blank version, URI clashing
    /// with the OPC UA base namespace, URI clashing with the Vision
    /// companion namespace) and the happy path.
    /// </summary>
    [TestFixture]
    public sealed class VisionServerOptionsTests
    {
        [Test]
        public void DefaultsPassValidateWithoutThrowing()
        {
            var options = new VisionServerOptions();

            Assert.That(options.Validate, Throws.Nothing);
        }

        [Test]
        public void DefaultsExposeTheDocumentedConstants()
        {
            var options = new VisionServerOptions();

            Assert.Multiple(() =>
            {
                Assert.That(options.InstanceNamespaceUri,
                    Is.EqualTo(VisionServerOptions.DefaultInstanceNamespaceUri));
                Assert.That(options.SpecificationVersion,
                    Is.EqualTo(VisionServerOptions.DefaultSpecificationVersion));
                Assert.That(options.AdditionalFacets.Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void ValidateThrowsArgumentExceptionWhenInstanceNamespaceUriIsEmpty()
        {
            var options = new VisionServerOptions { InstanceNamespaceUri = string.Empty };

            Assert.That(options.Validate,
                Throws.TypeOf<ArgumentException>()
                      .With.Property("ParamName").EqualTo("InstanceNamespaceUri"));
        }

        [Test]
        public void ValidateThrowsArgumentExceptionWhenInstanceNamespaceUriIsWhitespace()
        {
            var options = new VisionServerOptions { InstanceNamespaceUri = "   " };

            Assert.That(options.Validate,
                Throws.TypeOf<ArgumentException>()
                      .With.Property("ParamName").EqualTo("InstanceNamespaceUri"));
        }

        [Test]
        public void ValidateThrowsArgumentExceptionWhenInstanceNamespaceUriIsRelative()
        {
            var options = new VisionServerOptions { InstanceNamespaceUri = "not/absolute" };

            Assert.That(options.Validate,
                Throws.TypeOf<ArgumentException>()
                      .With.Property("ParamName").EqualTo("InstanceNamespaceUri"));
        }

        [Test]
        public void ValidateThrowsArgumentExceptionWhenSpecificationVersionIsEmpty()
        {
            var options = new VisionServerOptions
            {
                InstanceNamespaceUri = "urn:custom:instances",
                SpecificationVersion = string.Empty
            };

            Assert.That(options.Validate,
                Throws.TypeOf<ArgumentException>()
                      .With.Property("ParamName").EqualTo("SpecificationVersion"));
        }

        [Test]
        public void ValidateThrowsBadConfigurationErrorWhenNamespaceEqualsOpcUaBase()
        {
            var options = new VisionServerOptions
            {
                InstanceNamespaceUri = Namespaces.OpcUa
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(options.Validate)!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void ValidateThrowsBadConfigurationErrorWhenNamespaceEqualsVisionModel()
        {
            var options = new VisionServerOptions
            {
                InstanceNamespaceUri = global::Opc.Ua.Vision.Namespaces.Vision
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(options.Validate)!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void ValidateAcceptsCustomAdditionalFacets()
        {
            var options = new VisionServerOptions
            {
                InstanceNamespaceUri = "urn:custom:vision:instances",
                AdditionalFacets = s_stringValues1.ToArrayOf()
            };

            Assert.That(options.Validate, Throws.Nothing);
            Assert.That(options.AdditionalFacets.Count, Is.EqualTo(1));
        }

        private static readonly string[] s_stringValues1 = ["VIS-Custom"];
    }
}
