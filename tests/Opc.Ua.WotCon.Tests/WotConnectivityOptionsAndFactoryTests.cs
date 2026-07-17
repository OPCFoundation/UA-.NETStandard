/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.WotCon.Server;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Smoke tests for <see cref="WotConnectivityServerOptions"/> and
    /// <see cref="WotConnectivityNodeManagerFactory"/> — the small DTO /
    /// factory surface that doesn't justify a heavy fixture but still
    /// needs basic contract verification.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotConnectivityOptionsAndFactoryTests
    {
        [Test]
        public void DefaultOptionsHaveSensibleDefaults()
        {
            var options = new WotConnectivityServerOptions();

            Assert.That(options.AssetNamespaceUri,
                Is.EqualTo(WotConnectivityServerOptions.DefaultAssetNamespaceUri));
            Assert.That(options.MaxThingDescriptionSize, Is.EqualTo(1024 * 1024));
            Assert.That(options.MaxOpenFileHandlesPerAsset, Is.EqualTo(10));
            Assert.That(options.Bindings, Is.Empty);
            Assert.That(options.Discovery, Is.Null);
            Assert.That(options.Configuration, Is.Empty);
            Assert.That(options.License, Is.Null);
            Assert.That(options.ThingDescriptionStorageFolder, Is.Null);
        }

        [Test]
        public void DefaultConfigurationParameterIsStringWritableWithoutInitialValue()
        {
            var param = new WotConfigurationParameter();

            Assert.That(param.DataType, Is.EqualTo(DataTypeIds.String));
            Assert.That(param.InitialValue, Is.Null);
            Assert.That(param.Writable, Is.True);
            Assert.That(param.Description, Is.Null);
        }

        [Test]
        public void NodeManagerFactoryThrowsOnNullOptions()
        {
            Assert.Throws<ArgumentNullException>(
                () => new WotConnectivityNodeManagerFactory(null!));
        }

        [Test]
        public void NodeManagerFactoryAdvertisesAssetAndWotConNamespaces()
        {
            var options = new WotConnectivityServerOptions
            {
                AssetNamespaceUri = "urn:test:assets"
            };
            var factory = new WotConnectivityNodeManagerFactory(options);

            ArrayOf<string> namespaces = factory.NamespacesUris;

            Assert.That(namespaces.Count, Is.EqualTo(2));
            Assert.That(namespaces[0], Is.EqualTo("urn:test:assets"));
            Assert.That(namespaces[1], Is.EqualTo(Namespaces.WotCon));
        }
    }
}
