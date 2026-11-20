// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
                InstanceNamespaceUri = Perception.Namespaces.Vision
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
