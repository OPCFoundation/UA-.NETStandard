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
