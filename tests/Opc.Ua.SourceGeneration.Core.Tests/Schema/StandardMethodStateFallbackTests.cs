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
