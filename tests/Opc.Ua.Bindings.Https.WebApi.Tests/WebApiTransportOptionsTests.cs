// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET8_0_OR_GREATER

using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Smoke tests for <see cref="WebApiTransportOptions"/>: POCO default
    /// values and property round-trips.
    /// </summary>
    [TestFixture]
    [Category("DIExtensionsBatch1")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class WebApiTransportOptionsTests
    {
        [Test]
        public void DefaultHostingModeIsSharedWithHttpsListener()
        {
            var options = new WebApiTransportOptions();

            Assert.That(
                options.HostingMode,
                Is.EqualTo(WebApiHostingMode.SharedWithHttpsListener));
        }

        [Test]
        public void DefaultEncodingIsCompact()
        {
            var options = new WebApiTransportOptions();

            Assert.That(options.DefaultEncoding, Is.EqualTo(WebApiEncoding.Compact));
        }

        [Test]
        public void HostingModePropertyRoundtrips()
        {
            var options = new WebApiTransportOptions
            {
                HostingMode = WebApiHostingMode.OwnListener
            };

            Assert.That(options.HostingMode, Is.EqualTo(WebApiHostingMode.OwnListener));
        }

        [Test]
        public void DefaultEncodingPropertyRoundtrips()
        {
            var options = new WebApiTransportOptions
            {
                DefaultEncoding = WebApiEncoding.Verbose
            };

            Assert.That(options.DefaultEncoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void ServiceSetDefaultsToAllServices()
        {
            var options = new WebApiTransportOptions();

            Assert.That(options.ServiceSet, Is.EqualTo(WebApiServiceSet.AllServices));
        }

        [Test]
        public void OpenApiDocumentIsOffByDefault()
        {
            var options = new WebApiTransportOptions();

            Assert.That(options.OpenApiDocumentPath, Is.Null);
            Assert.That(options.OpenApiIncludeSchemas, Is.False);
        }

        [Test]
        public void OpenApiPropertiesRoundtrip()
        {
            var options = new WebApiTransportOptions
            {
                ServiceSet = WebApiServiceSet.Sessionless,
                OpenApiDocumentPath = "/openapi.json",
                OpenApiIncludeSchemas = true
            };

            Assert.That(options.ServiceSet, Is.EqualTo(WebApiServiceSet.Sessionless));
            Assert.That(options.OpenApiDocumentPath, Is.EqualTo("/openapi.json"));
            Assert.That(options.OpenApiIncludeSchemas, Is.True);
        }

        [Test]
        public void MultiplePropertyMutationsAreIndependent()
        {
            var options = new WebApiTransportOptions
            {
                DefaultEncoding = WebApiEncoding.Verbose,
                HostingMode = WebApiHostingMode.OwnListener
            };

            Assert.That(options.DefaultEncoding, Is.EqualTo(WebApiEncoding.Verbose));
            Assert.That(options.HostingMode, Is.EqualTo(WebApiHostingMode.OwnListener));
        }
    }
}

#endif // NET8_0_OR_GREATER
