// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.SourceGeneration.Dependency;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Covers every rejection reason and the single acceptance path of
    /// <see cref="Generators.ValidateFluentAccessorsOnlyTarget"/>, which gates
    /// a build that emits only fluent accessors for a model another assembly
    /// already produces.
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class FluentAccessorsOnlyTargetTests
    {
        [Test]
        public void AnUnknownModelUriIsRejected()
        {
            bool accepted = Validate(
                modelUri: string.Empty,
                producers: [],
                accessorProviders: [],
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("no referenced assembly supplies the target model URI"));
        }

        [Test]
        public void AnAssemblyThatAlreadyProvidesAccessorsIsRejected()
        {
            bool accepted = Validate(
                ModelUri,
                producers: [Producer("Producer", fluentAccessorsEmitted: false)],
                accessorProviders:
                [
                    new ModelFluentAccessorProviderReference("Accessors", ModelUri, Prefix)
                ],
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("'Accessors'"));
            Assert.That(reason, Does.Contain("already provides generated fluent accessors"));
        }

        [Test]
        public void AProviderForAnotherPrefixDoesNotBlockTheTarget()
        {
            bool accepted = Validate(
                ModelUri,
                producers: [Producer("Producer", fluentAccessorsEmitted: false)],
                accessorProviders:
                [
                    new ModelFluentAccessorProviderReference("Accessors", ModelUri, "Other.Prefix")
                ],
                out string reason);

            Assert.That(accepted, Is.True, reason);
        }

        [Test]
        public void AMalformedPayloadIsRejected()
        {
            var producer = new ModelDependencyReference(
                "Producer",
                ModelUri,
                Prefix,
                "1.0",
                "2026-01-01",
                payload: "not-a-valid-base64-payload");

            bool accepted = Validate(
                ModelUri,
                producers: [producer],
                accessorProviders: [],
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("malformed model dependency payload"));
        }

        [Test]
        public void NoPayloadBearingProducerIsRejected()
        {
            bool accepted = Validate(
                ModelUri,
                producers: [],
                accessorProviders: [],
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(
                reason,
                Does.Contain("no payload-bearing referenced model producer supplies the target model"));
        }

        [Test]
        public void AProducerSupplyingAnotherPrefixIsRejected()
        {
            bool accepted = Validate(
                ModelUri,
                producers:
                [
                    Producer("Producer", fluentAccessorsEmitted: false, prefix: "Other.Prefix")
                ],
                accessorProviders: [],
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("supplies prefix 'Other.Prefix'"));
        }

        [Test]
        public void AProducerWithUnknownLegacyCapabilityIsRejected()
        {
            bool accepted = Validate(
                ModelUri,
                producers: [Producer("Legacy", fluentAccessorsEmitted: null)],
                accessorProviders: [],
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("unknown legacy fluent-accessor capability"));
        }

        [Test]
        public void AProducerThatAlreadyEmittedAccessorsIsRejected()
        {
            bool accepted = Validate(
                ModelUri,
                producers: [Producer("Producer", fluentAccessorsEmitted: true)],
                accessorProviders: [],
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("already contains fluent accessors"));
        }

        [Test]
        public void AProducerWithoutAccessorsIsAccepted()
        {
            bool accepted = Validate(
                ModelUri,
                producers: [Producer("Producer", fluentAccessorsEmitted: false)],
                accessorProviders: [],
                out string reason);

            Assert.That(accepted, Is.True);
            Assert.That(reason, Is.Null, "an accepted target must not report a diagnostic.");
        }

        private static bool Validate(
            string modelUri,
            IReadOnlyList<ModelDependencyReference> producers,
            IReadOnlyList<ModelFluentAccessorProviderReference> accessorProviders,
            out string reason)
        {
            string? captured = null;
            bool accepted = Generators.ValidateFluentAccessorsOnlyTarget(
                modelUri,
                Prefix,
                "Model.ModelDesign.xml",
                producers,
                accessorProviders,
                (_, _, _, message) => captured = message);
            reason = captured!;
            return accepted;
        }

        private static ModelDependencyReference Producer(
            string assemblyName,
            bool? fluentAccessorsEmitted,
            string prefix = Prefix)
        {
            var dependency = new ModelDependencyV1
            {
                ModelUri = ModelUri,
                FluentAccessorsEmitted = fluentAccessorsEmitted
            };

            return new ModelDependencyReference(
                assemblyName,
                ModelUri,
                prefix,
                "1.0",
                "2026-01-01",
                payload: dependency.ToBase64Payload());
        }

        private const string ModelUri = "http://opcfoundation.org/UA/Test/";
        private const string Prefix = "Opc.Ua.Test";
    }
}
