// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.Bindings.Native;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Adjacent-boundary tests for every local native admission/deadline setting, independent of platform availability.
    /// </summary>
    [TestFixture]
    public sealed class QuicNativeOptionsTests
    {
        /// <summary>
        /// The shared stack verifier must retain the complete 66-byte P-521 ECDSA integer width.
        /// </summary>
        [Test]
        public void P521SelfSignedCertificateIsVerifiedByStack()
        {
            using Certificate certificate = DefaultCertificateFactory.Instance.CreateCertificate("CN=Native P521 regression")
                .SetCAConstraint()
                .SetECCurve(ECCurve.NamedCurves.nistP521)
                .CreateForECDsa();
            Assert.That(X509Utils.VerifySelfSigned(certificate), Is.True);
        }

        /// <summary>
        /// Native local settings cannot disable finite resource and timeout bounds.
        /// </summary>
        [TestCase("streams", 0)]
        [TestCase("streams", -1)]
        [TestCase("streams", 33)]
        [TestCase("operations", 3)]
        [TestCase("operations", -1)]
        [TestCase("operations", 65)]
        [TestCase("read", 12)]
        [TestCase("read", -1)]
        [TestCase("read", 65537)]
        [TestCase("operationTimeout", 0)]
        [TestCase("operationTimeout", -1)]
        [TestCase("operationTimeout", 60001)]
        [TestCase("handshakeTimeout", 0)]
        [TestCase("handshakeTimeout", -1)]
        [TestCase("handshakeTimeout", 60001)]
        [TestCase("idleTimeout", 999)]
        [TestCase("idleTimeout", -1)]
        [TestCase("idleTimeout", 600001)]
        [TestCase("recheck", 0)]
        [TestCase("recheck", -1)]
        [TestCase("recheck", 60001)]
        public void InvalidNativeLimitIsRejected(string setting, int value)
        {
            var defaults = new NativeQuicConnectionSettings();
            NativeQuicConnectionSettings options = setting switch
            {
                "streams" => defaults with { MaxStreams = value },
                "operations" => defaults with { MaxPendingOperations = value },
                "read" => defaults with { ReadSize = value },
                "operationTimeout" => defaults with { OperationTimeout = TimeSpan.FromMilliseconds(value) },
                "handshakeTimeout" => defaults with { HandshakeTimeout = TimeSpan.FromMilliseconds(value) },
                "idleTimeout" => defaults with { IdleTimeout = TimeSpan.FromMilliseconds(value) },
                "recheck" => defaults with { CertificateRecheckInterval = TimeSpan.FromMilliseconds(value) },
                _ => throw new ArgumentOutOfRangeException(nameof(setting))
            };
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }

        /// <summary>
        /// Native setting boundaries are inclusive, not accidental off-by-one refusals.
        /// </summary>
        [Test]
        public void ExactNativeLimitBoundariesAreAccepted()
        {
            var minimum = new NativeQuicConnectionSettings
            {
                MaxStreams = 1,
                MaxPendingOperations = 4,
                ReadSize = 13,
                OperationTimeout = TimeSpan.FromMilliseconds(1),
                HandshakeTimeout = TimeSpan.FromMilliseconds(1),
                IdleTimeout = TimeSpan.FromSeconds(1),
                CertificateRecheckInterval = TimeSpan.FromMilliseconds(1)
            };
            var maximum = new NativeQuicConnectionSettings
            {
                MaxStreams = 32,
                MaxPendingOperations = 64,
                ReadSize = 65536,
                OperationTimeout = TimeSpan.FromMinutes(1),
                HandshakeTimeout = TimeSpan.FromMinutes(1),
                IdleTimeout = TimeSpan.FromMinutes(10),
                CertificateRecheckInterval = TimeSpan.FromMinutes(1)
            };
            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(minimum.Validate);
                Assert.DoesNotThrow(maximum.Validate);
            });
        }

        /// <summary>
        /// The binding cannot admit unlimited pending streams, reused-stream history or unbounded common records.
        /// </summary>
        [TestCase("pending", 0)]
        [TestCase("pending", -1)]
        [TestCase("pending", 9)]
        [TestCase("history", 7)]
        [TestCase("history", -1)]
        [TestCase("history", 65537)]
        [TestCase("record", 1023)]
        [TestCase("record", -1)]
        [TestCase("record", 65537)]
        [TestCase("timeout", 0)]
        [TestCase("timeout", -1)]
        [TestCase("timeout", 60001)]
        [TestCase("nativeStreams", 3)]
        [TestCase("nativeOperations", 6)]
        [TestCase("nativeOperations", 10)]
        public void InvalidFlowBindingLimitIsRejected(string setting, int value)
        {
            var defaults = new QuicDataChannelOptions();
            QuicDataChannelOptions options = setting switch
            {
                "pending" => defaults with { MaxPendingStreams = value },
                "history" => defaults with { MaxStreamHistory = value },
                "record" => defaults with { MaxRecordSize = value },
                "timeout" => defaults with { EstablishmentTimeout = TimeSpan.FromMilliseconds(value) },
                "nativeStreams" => defaults with { Native = defaults.Native with { MaxStreams = value } },
                "nativeOperations" => defaults with { Native = defaults.Native with { MaxPendingOperations = value } },
                _ => throw new ArgumentOutOfRangeException(nameof(setting))
            };
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }
    }
}
