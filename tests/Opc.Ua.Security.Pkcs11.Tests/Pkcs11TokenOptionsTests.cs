// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.Security.Pkcs11;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Tests for the RFC 7512 PKCS#11 URI parser and the store selection it
    /// drives.
    /// </summary>
    /// <remarks>
    /// These need no token, so they cover the addressing rules on every agent
    /// including ones with no PKCS#11 module installed.
    /// </remarks>
    [TestFixture]
    [Category("Pkcs11")]
    [Parallelizable(ParallelScope.All)]
    public class Pkcs11TokenOptionsTests
    {
        [Test]
        public void ParseReadsTokenAndModulePath()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse(
                "pkcs11:token=opcua;object=server?module-path=/usr/lib/libsofthsm2.so");

            Assert.Multiple(() =>
            {
                Assert.That(options.TokenLabel, Is.EqualTo("opcua"));
                Assert.That(options.ObjectLabel, Is.EqualTo("server"));
                Assert.That(options.ModulePath, Is.EqualTo("/usr/lib/libsofthsm2.so"));
            });
        }

        [Test]
        public void ParseReadsSerialAndSlotId()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse(
                "pkcs11:serial=0123456789;slot-id=42?module-path=/tmp/m.so");

            Assert.Multiple(() =>
            {
                Assert.That(options.TokenSerial, Is.EqualTo("0123456789"));
                Assert.That(options.SlotId, Is.EqualTo(42UL));
            });
        }

        [Test]
        public void ParseDecodesPercentEncodedObjectId()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse(
                "pkcs11:id=%01%A2%ff?module-path=/tmp/m.so");

            Assert.That(options.ObjectId.ToArray(), Is.EqualTo(new byte[] { 0x01, 0xA2, 0xFF }));
        }

        [Test]
        public void ParseDecodesPercentEncodedLabel()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse(
                "pkcs11:token=my%20token?module-path=/tmp/m.so");

            Assert.That(options.TokenLabel, Is.EqualTo("my token"));
        }

        [Test]
        public void ParseReadsPinValue()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse(
                "pkcs11:token=t?module-path=/tmp/m.so&pin-value=1234");

            Assert.That(options.GetPin(), Is.EqualTo("1234"));
        }

        [Test]
        public void ParseIgnoresUnknownAttributes()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse(
                "pkcs11:token=t;model=whatever;manufacturer=acme?module-path=/tmp/m.so");

            Assert.That(options.TokenLabel, Is.EqualTo("t"));
        }

        [Test]
        public void ParseWithoutQueryLeavesModulePathUnset()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse("pkcs11:token=t");

            Assert.Multiple(() =>
            {
                Assert.That(options.TokenLabel, Is.EqualTo("t"));
                Assert.That(options.ModulePath, Is.Null);
            });
        }

        [Test]
        public void ParseRejectsNonPkcs11Uri()
        {
            Assert.Throws<ArgumentException>(
                () => Pkcs11TokenOptions.Parse("/some/directory"));
        }

        [Test]
        public void ParseRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => Pkcs11TokenOptions.Parse(null!));
        }

        [Test]
        [TestCase("pkcs11:token=t", true)]
        [TestCase("PKCS11:token=t", true)]
        [TestCase("/some/directory", false)]
        [TestCase("simhw:token", false)]
        [TestCase(null, false)]
        public void IsPkcs11UriRecognisesTheScheme(string? storePath, bool expected)
        {
            Assert.That(Pkcs11TokenOptions.IsPkcs11Uri(storePath), Is.EqualTo(expected));
        }

        [Test]
        public void PinProviderTakesPrecedenceOverPin()
        {
            var options = new Pkcs11TokenOptions
            {
                Pin = "from-configuration",
                PinProvider = () => "from-secret-store"
            };

            Assert.That(options.GetPin(), Is.EqualTo("from-secret-store"));
        }

        [Test]
        public void GetPinFallsBackToPinWhenProviderReturnsNull()
        {
            var options = new Pkcs11TokenOptions
            {
                Pin = "from-configuration",
                PinProvider = () => null
            };

            Assert.That(options.GetPin(), Is.EqualTo("from-configuration"));
        }
    }
}
