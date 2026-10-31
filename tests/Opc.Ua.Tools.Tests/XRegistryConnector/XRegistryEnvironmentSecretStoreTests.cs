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

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Opc.Ua.XRegistry.Connector;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    public sealed class XRegistryEnvironmentSecretStoreTests
    {
        [TestCase(null)]
        [TestCase("")]
        public async Task LogicalNamesResolveConfiguredVariableReferencesForUnscopedIdentifiersAsync(string? storePath)
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:inbound"] = "XREG_TEST_INBOUND_REFERENCE",
                ["Secrets:outbound"] = "XREG_TEST_OUTBOUND_REFERENCE"
            };
            var reads = new List<string>();
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), variable =>
            {
                reads.Add(variable);
                return variable switch
                {
                    "XREG_TEST_INBOUND_REFERENCE" => "inbound-fixture",
                    "XREG_TEST_OUTBOUND_REFERENCE" => "outbound-\u6c34",
                    _ => throw new InvalidOperationException("Unexpected variable lookup.")
                };
            });
            Assert.That(store.StoreType, Is.EqualTo("Environment"));
            Assert.That(reads, Is.Empty);

            using ISecret? inbound = store.TryGet(new SecretIdentifier("inbound", "Environment", storePath));
            using ISecret? outbound = await store.GetAsync(new SecretIdentifier("outbound", "Environment", storePath))
                .ConfigureAwait(false);

            Assert.That(inbound, Is.Not.Null);
            Assert.That(outbound, Is.Not.Null);
            Assert.That(inbound!.Bytes.ToArray(), Is.EqualTo("inbound-fixture"u8.ToArray()));
            Assert.That(outbound!.Bytes.ToArray(), Is.EqualTo("outbound-\u6c34"u8.ToArray()));
            Assert.That(reads, Has.Count.EqualTo(2));
            Assert.That(reads[0], Is.EqualTo("XREG_TEST_INBOUND_REFERENCE"));
            Assert.That(reads[1], Is.EqualTo("XREG_TEST_OUTBOUND_REFERENCE"));
        }

        [TestCase("missing", "Environment", null)]
        [TestCase("Operator", "Environment", null)]
        [TestCase("operator", "OtherStore", null)]
        [TestCase("operator", "environment", null)]
        [TestCase("operator", "Environment", "scope")]
        [TestCase("operator", "Environment", "/")]
        [TestCase("operator", "Environment", " ")]
        public async Task WrongLogicalNameTypeOrPathNeverReadsEnvironmentAsync(
            string name,
            string storeType,
            string? storePath)
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:operator"] = "XREG_TEST_OPERATOR_REFERENCE"
            };
            int reads = 0;
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ =>
            {
                reads++;
                return "fixture-material";
            });
            var identifier = new SecretIdentifier(name, storeType, storePath);

            using ISecret? synchronous = store.TryGet(identifier);
            using ISecret? asynchronous = await store.GetAsync(identifier).ConfigureAwait(false);

            Assert.That(synchronous, Is.Null);
            Assert.That(asynchronous, Is.Null);
            Assert.That(reads, Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        public async Task MissingOrEmptyEnvironmentValuesReturnNoSecretAsync(string? value)
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:operator"] = "XREG_TEST_OPERATOR_REFERENCE"
            };
            string? variableRead = null;
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), variable =>
            {
                variableRead = variable;
                return value;
            });

            using ISecret? secret = await store.GetAsync(new SecretIdentifier("operator", "Environment"))
                .ConfigureAwait(false);

            Assert.That(secret, Is.Null);
            Assert.That(variableRead, Is.EqualTo("XREG_TEST_OPERATOR_REFERENCE"));
        }

        [Test]
        public void EnvironmentMaterialIsNotTrimmed()
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:operator"] = "XREG_TEST_OPERATOR_REFERENCE"
            };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => " \t ");

            using ISecret? secret = store.TryGet(new SecretIdentifier("operator", "Environment"));

            Assert.That(secret, Is.Not.Null);
            Assert.That(secret!.Bytes.ToArray(), Is.EqualTo(" \t "u8.ToArray()));
        }

        [Test]
        public void EachLookupReadsCurrentMaterialWithoutChangingPreviouslyIssuedSecrets()
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:operator"] = "XREG_TEST_OPERATOR_REFERENCE"
            };
            string current = "first-fixture";
            int reads = 0;
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ =>
            {
                reads++;
                return current;
            });
            var identifier = new SecretIdentifier("operator", "Environment");
            using ISecret? first = store.TryGet(identifier);
            current = "second-fixture";

            using ISecret? second = store.TryGet(identifier);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first!.Bytes.ToArray(), Is.EqualTo("first-fixture"u8.ToArray()));
            Assert.That(second!.Bytes.ToArray(), Is.EqualTo("second-fixture"u8.ToArray()));
            Assert.That(reads, Is.EqualTo(2));
            first.Dispose();
            Assert.That(second.Bytes.ToArray(), Is.EqualTo("second-fixture"u8.ToArray()));
        }

        [Test]
        public async Task EnvironmentReaderFailureIsNotConvertedToMissingSecretAsync()
        {
            using var configuration = new ConfigurationManager { ["Secrets:operator"] = "XREG_TEST_REFERENCE" };
            var failure = new InvalidOperationException("Injected environment reader failed.");
            int reads = 0;
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ =>
            {
                reads++;
                throw failure;
            });
            var identifier = new SecretIdentifier("operator", "Environment");

            Assert.That(() => store.TryGet(identifier), Throws.Exception.SameAs(failure));
            await Assert.ThatAsync(() => store.GetAsync(identifier).AsTask(), Throws.Exception.SameAs(failure))
                .ConfigureAwait(false);
            Assert.That(reads, Is.EqualTo(2));
        }

        [Test]
        public void DisposalZeroesTheIssuedBufferAndRejectsFurtherAccess()
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:operator"] = "XREG_TEST_OPERATOR_REFERENCE"
            };
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => "fixture-\u00e9");
            using ISecret? secret = store.TryGet(new SecretIdentifier("operator", "Environment"));
            Assert.That(secret, Is.Not.Null);

            // Retaining this span is test-only, to inspect the implementation's zeroization.
            ReadOnlySpan<byte> issuedBuffer = secret!.Bytes;
            Assert.That(issuedBuffer.ToArray(), Is.EqualTo("fixture-\u00e9"u8.ToArray()));

            secret.Dispose();

            Assert.That(issuedBuffer.Length, Is.EqualTo(10));
            Assert.That(issuedBuffer.ToArray(), Is.All.Zero);
            Assert.That(() => secret.Bytes.Length, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(secret.Dispose, Throws.Nothing);
        }

        [Test]
        public async Task CancelledLookupDoesNotReadEnvironmentOrMaterializeSecretAsync()
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:operator"] = "XREG_TEST_OPERATOR_REFERENCE"
            };
            int reads = 0;
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ =>
            {
                reads++;
                return "fixture-material";
            });
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            var identifier = new SecretIdentifier("operator", "Environment");

            await Assert.ThatAsync(async () =>
            {
                using ISecret? secret = await store.GetAsync(identifier, cancellation.Token).ConfigureAwait(false);
            }, Throws.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token)).ConfigureAwait(false);
            Assert.That(reads, Is.Zero);
        }

        [Test]
        public async Task EnvironmentStoreRejectsMutationsWithoutReadingOrEchoingSecretMaterialAsync()
        {
            using var configuration = new ConfigurationManager
            {
                ["Secrets:operator"] = "XREG_TEST_OPERATOR_REFERENCE"
            };
            int reads = 0;
            var store = new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ =>
            {
                reads++;
                return "existing-fixture";
            });
            var identifier = new SecretIdentifier("operator", "Environment");

            await Assert.ThatAsync(() => store.SetAsync(identifier, "not-for-error-output"u8.ToArray()).AsTask(),
                Throws.TypeOf<NotSupportedException>().With.Message.Not.Contains("not-for-error-output"))
                .ConfigureAwait(false);
            await Assert.ThatAsync(() => store.RemoveAsync(identifier).AsTask(),
                Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);
            Assert.That(reads, Is.Zero);
            using ISecret? remaining = store.TryGet(identifier);
            Assert.That(remaining, Is.Not.Null);
            Assert.That(remaining!.Bytes.ToArray(), Is.EqualTo("existing-fixture"u8.ToArray()));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public void ConstructorRejectsMissingOrBlankVariableReferences(string? variable)
        {
            using var configuration = new ConfigurationManager { ["Secrets:operator"] = variable };

            Assert.That(() => new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => "unused"),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("section"));
        }

        [Test]
        public void ConstructorRejectsNestedMappingsInsteadOfTreatingThemAsSecretMaterial()
        {
            using var configuration = new ConfigurationManager { ["Secrets:operator:nested"] = "XREG_TEST_REFERENCE" };

            Assert.That(() => new XRegistryEnvironmentSecretStore(configuration.GetSection("Secrets"), _ => "unused"),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("section"));
        }

        [Test]
        public async Task NullConfigurationAndIdentifiersAreRejectedExplicitlyAsync()
        {
            Assert.That(() => new XRegistryEnvironmentSecretStore(null!, _ => "unused"),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("section"));
            using var configuration = new ConfigurationManager();
            var store = new XRegistryEnvironmentSecretStore(configuration, _ => "unused");

            Assert.That(() => store.TryGet(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("id"));
            await Assert.ThatAsync(() => store.GetAsync(null!).AsTask(),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("id")).ConfigureAwait(false);
        }
    }
}
#endif
