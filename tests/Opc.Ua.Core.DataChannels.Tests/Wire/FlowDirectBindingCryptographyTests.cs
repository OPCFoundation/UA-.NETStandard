/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Wire
{
    /// <summary>
    /// Verifies canonical transcript bytes and role-separated direct-binding proofs.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class FlowDirectBindingCryptographyTests
    {
        /// <summary>
        /// Computes the pinned transcript vector using Part 6 field order and binary encodings.
        /// </summary>
        [Test]
        public void TranscriptDigestMatchesCanonicalBinaryVector()
        {
            FlowBindingTranscript transcript = CreateTranscript();

            ByteString digest = FlowDirectBindingCryptography.ComputeTranscriptDigest(transcript);

            Assert.That(
                digest.ToHexString().ToLowerInvariant(),
                Is.EqualTo("46f9dccffa9b95bb3087962f0d4d26b515b8889c43b2c24751bd54f24684d14c"));
        }

        /// <summary>
        /// Binds each identity, context, challenge, capability and endpoint field independently.
        /// </summary>
        [Test]
        public void TranscriptDigestChangesWhenAnyBoundFieldChanges()
        {
            FlowBindingTranscript transcript = CreateTranscript();
            ByteString digest = FlowDirectBindingCryptography.ComputeTranscriptDigest(transcript);
            FlowBindingTranscript[] changed =
            [
                transcript with { TransportProfileUri = "urn:other:transport" },
                transcript with { Capability = "other-direct-draft" },
                transcript with { ContextId = Guid.NewGuid() },
                transcript with { Generation = transcript.Generation + 1 },
                transcript with { InitiatorApplicationUri = "urn:other:initiator" },
                transcript with { AcceptorApplicationUri = "urn:other:acceptor" },
                transcript with { InitiatorCertificateThumbprint = Bytes(1) },
                transcript with { AcceptorCertificateThumbprint = Bytes(33) },
                transcript with { InitiatorChallenge = Bytes(65) },
                transcript with { AcceptorChallenge = Bytes(97) },
                transcript with { Authority = "other.example.test:4840" },
                transcript with { Path = "/different?x=1" }
            ];

            foreach (FlowBindingTranscript variant in changed)
            {
                Assert.That(
                    FlowDirectBindingCryptography.ComputeTranscriptDigest(variant),
                    Is.Not.EqualTo(digest));
            }
        }

        /// <summary>
        /// Uses distinct initiator and acceptor proofs and rejects role reflection and modification.
        /// </summary>
        [Test]
        public void ProofUsesExactRoleLabelsAndRejectsReflection()
        {
            ByteString exporter = Bytes(128);
            ByteString digest = FlowDirectBindingCryptography.ComputeTranscriptDigest(CreateTranscript());
            ByteString initiator = FlowDirectBindingCryptography.CreateProof(exporter, 0, digest);
            ByteString acceptor = FlowDirectBindingCryptography.CreateProof(exporter, 1, digest);

            Assert.Multiple(() =>
            {
                Assert.That(
                    initiator.ToHexString().ToLowerInvariant(),
                    Is.EqualTo("9ef739b7fdcc85a918b69687e92ab1b30aef9383fa944f643617b86f5fd06bfa"));
                Assert.That(
                    acceptor.ToHexString().ToLowerInvariant(),
                    Is.EqualTo("c620274815bc8f6e252d7f1fb3687bcb4787bc9a2a35667680f1ffa8849c7391"));
                Assert.That(FlowDirectBindingCryptography.VerifyProof(exporter, 0, digest, initiator), Is.True);
                Assert.That(FlowDirectBindingCryptography.VerifyProof(exporter, 1, digest, acceptor), Is.True);
                Assert.That(FlowDirectBindingCryptography.VerifyProof(exporter, 1, digest, initiator), Is.False);
                Assert.That(
                    FlowDirectBindingCryptography.VerifyProof(exporter, 0, digest, Bytes(129)),
                    Is.False);
            });
        }

        /// <summary>
        /// Rejects zero generation, empty or oversized strings, invalid Unicode and non-32-byte fields.
        /// </summary>
        [Test]
        public void TranscriptRejectsInvalidFieldsBeforeEncoding()
        {
            FlowBindingTranscript transcript = CreateTranscript();

            Assert.Multiple(() =>
            {
                Assert.Throws<FlowCodecException>(() =>
                    FlowDirectBindingCryptography.ComputeTranscriptDigest(transcript with { Generation = 0 }));
                Assert.Throws<FlowCodecException>(() =>
                    FlowDirectBindingCryptography.ComputeTranscriptDigest(transcript with { Authority = string.Empty }));
                Assert.Throws<FlowCodecException>(() =>
                    FlowDirectBindingCryptography.ComputeTranscriptDigest(
                        transcript with { Capability = new string('x', 256) }));
                Assert.Throws<FlowCodecException>(() =>
                    FlowDirectBindingCryptography.ComputeTranscriptDigest(
                        transcript with { Path = "\ud800" }));
                Assert.Throws<FlowCodecException>(() =>
                    FlowDirectBindingCryptography.ComputeTranscriptDigest(
                        transcript with { InitiatorChallenge = new ByteString(new byte[31]) }));
                Assert.Throws<FlowCodecException>(() =>
                    FlowDirectBindingCryptography.CreateProof(new ByteString(new byte[31]), 0, Bytes(0)));
                Assert.Throws<FlowCodecException>(() =>
                    FlowDirectBindingCryptography.CreateProof(Bytes(0), 2, Bytes(0)));
            });
        }

        /// <summary>
        /// Creates the independently verified transcript test vector.
        /// </summary>
        private static FlowBindingTranscript CreateTranscript()
        {
            return new FlowBindingTranscript(
                "urn:opcfoundation:transport:quic",
                "quic-direct-draft",
                Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                7,
                "urn:opcfoundation:test:initiator",
                "urn:opcfoundation:test:acceptor",
                Bytes(0),
                Bytes(32),
                Bytes(64),
                Bytes(96),
                "example.test:4840",
                "/ua?x=1");
        }

        /// <summary>
        /// Creates 32 sequential bytes beginning at the supplied value.
        /// </summary>
        private static ByteString Bytes(int first)
        {
            return new ByteString(Enumerable.Range(first, 32).Select(value => (byte)value).ToArray());
        }
    }
}
