// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Tests
{
    [TestFixture]
    [Category("Generator")]
    [Parallelizable]
    public sealed class StandardMethodStateFallbackTests
    {
        /// <summary>
        /// Verifies that the fallback scope follows the logical execution
        /// context when generator work moves to another thread.
        /// </summary>
        [Test]
        public async Task FallbackScopeFlowsToDedicatedThreadAsync()
        {
            int sourceThreadId = Environment.CurrentManagedThreadId;
            using IDisposable scope = StandardMethodStateFallback.Enter(
                ImmutableHashSet<string>.Empty);

            (bool shouldFallback, int threadId) = await Task.Factory.StartNew(
                () => (
                    StandardMethodStateFallback.ShouldFallBackToBase(
                        "global::Opc.Ua.MissingMethodState"),
                    Environment.CurrentManagedThreadId),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(threadId, Is.Not.EqualTo(sourceThreadId));
                Assert.That(shouldFallback, Is.True);
            });
        }

        /// <summary>
        /// Verifies that declaration names can opt out of the reference-only
        /// fallback policy.
        /// </summary>
        [Test]
        public void DeclarationNameDoesNotApplyFallback()
        {
            const string namespaceUri = "http://opcfoundation.org/UA/";
            var method = new MethodDesign
            {
                HasArguments = true,
                SymbolicName = new XmlQualifiedName("Missing", namespaceUri)
            };
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Value = namespaceUri,
                    Prefix = "Opc.Ua"
                }
            ];
            using IDisposable scope = StandardMethodStateFallback.Enter(
                ImmutableHashSet<string>.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(
                    method.GetNodeStateClassName(namespaceUri, namespaces),
                    Is.EqualTo("global::Opc.Ua.MethodState"));
                Assert.That(
                    method.GetNodeStateClassName(
                        namespaceUri,
                        namespaces,
                        applyStandardFallback: false),
                    Is.EqualTo("global::Opc.Ua.MissingMethodState"));
                Assert.That(
                    method.GetNodeStateClassName(
                        namespaceUri,
                        [],
                        applyStandardFallback: false),
                    Is.EqualTo("MissingMethodState"));
            });
        }

        /// <summary>
        /// Verifies that disposing a nested fallback scope restores the outer
        /// immutable availability index.
        /// </summary>
        [Test]
        public void NestedScopeRestoresOuterAvailabilityIndex()
        {
            ImmutableHashSet<string> availableStateTypeNames =
                ImmutableHashSet<string>.Empty.Add("KnownMethodState");

            using (StandardMethodStateFallback.Enter(availableStateTypeNames))
            {
                Assert.That(
                    StandardMethodStateFallback.ShouldFallBackToBase(
                        "global::Opc.Ua.KnownMethodState"),
                    Is.False);

                using (StandardMethodStateFallback.Enter(ImmutableHashSet<string>.Empty))
                {
                    Assert.That(
                        StandardMethodStateFallback.ShouldFallBackToBase(
                            "global::Opc.Ua.KnownMethodState"),
                        Is.True);
                }

                Assert.That(
                    StandardMethodStateFallback.ShouldFallBackToBase(
                        "global::Opc.Ua.KnownMethodState"),
                    Is.False);
            }

            Assert.That(
                StandardMethodStateFallback.ShouldFallBackToBase(
                    "global::Opc.Ua.KnownMethodState"),
                Is.False);
        }
    }
}
