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
            string captured = null;
            bool accepted = Generators.ValidateFluentAccessorsOnlyTarget(
                modelUri,
                Prefix,
                "Model.ModelDesign.xml",
                producers,
                accessorProviders,
                (_, _, _, message) => captured = message);
            reason = captured;
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
