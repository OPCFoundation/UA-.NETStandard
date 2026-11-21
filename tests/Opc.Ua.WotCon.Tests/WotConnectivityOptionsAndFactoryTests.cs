// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

            Assert.That(param.DataType, Is.EqualTo(Ua.DataTypeIds.String));
            Assert.That(param.HasInitialValue, Is.False);
            Assert.That(param.InitialValue.IsNull, Is.True);
            Assert.That(param.Writable, Is.True);
            Assert.That(param.Description, Is.Null);
        }

        [Test]
        public void SettingConfigurationParameterInitialValueMarksItSpecified()
        {
            var param = new WotConfigurationParameter
            {
                InitialValue = Variant.Null
            };

            Assert.That(param.HasInitialValue, Is.True);
            Assert.That(param.InitialValue, Is.EqualTo(Variant.Null));
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
