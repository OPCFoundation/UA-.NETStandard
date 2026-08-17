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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Verifies that no credential material reaches the address space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The specification forbids it and a sample is the thing people copy, so this
    /// is checked by walking every Variable the Server publishes rather than by
    /// inspecting the two or three places a secret was expected to appear. A leak
    /// that only happens somewhere unexpected is the only kind worth testing for.
    /// </para>
    /// <para>
    /// The credential resolver is also checked directly, because "the address space
    /// is clean" and "the resolver refuses a path" are different claims and only one
    /// of them is about browsing.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class CredentialSafetyTests
    {
        private const string Secret = "sk-this-must-never-be-browsable-1234567890";

        [Test]
        public async Task NoPublishedVariableCarriesCredentialMaterialAsync()
        {
            var backendOptions = new InferenceBackendOptions
            {
                Authentication = BackendAuthentication.ApiKey,
                CredentialReference = "inference-api-key",
                EndpointUri = "https://example.invalid/openai/",
                Site = InferenceSite.Cloud,
                EgressPermitted = true
            };

            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(new FakeInferenceBackend("primary")),
                    new AIOptions { EnableFallback = false },
                    backendOptions)
                .ConfigureAwait(false);

            var offenders = new List<string>();

            foreach (NodeState node in Walk(nm))
            {
                if (node is not BaseVariableState variable)
                {
                    continue;
                }

                string? text = variable.Value.ToString();

                if (!string.IsNullOrEmpty(text) &&
                    text.Contains(Secret, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add(variable.BrowseName.ToString());
                }
            }

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public async Task TheSourcePublishesTheReferenceRatherThanTheSecretAsync()
        {
            var backendOptions = new InferenceBackendOptions
            {
                Authentication = BackendAuthentication.ApiKey,
                CredentialReference = "inference-api-key"
            };

            using AINodeManager nm = await AIServerTestHarness
                .CreateAsync(
                    new InferenceBackends(new FakeInferenceBackend("primary")),
                    new AIOptions { EnableFallback = false },
                    backendOptions)
                .ConfigureAwait(false);

            ModelSourceState? source = null;

            foreach (NodeState node in Walk(nm))
            {
                if (node is ModelSourceState found)
                {
                    source = found;
                    break;
                }
            }

            Assert.That(source, Is.Not.Null);

            Assert.Multiple(() =>
            {
                // A client is entitled to know WHICH credential is configured, so it
                // can tell whether the right one is. It is not entitled to the value.
                Assert.That(
                    source!.CredentialReference!.Value,
                    Is.EqualTo("inference-api-key"));
                Assert.That(
                    source.AuthenticationKind!.Value,
                    Is.EqualTo(AuthenticationKindEnum.ApiKey));
            });
        }

        [Test]
        public async Task TheFileResolverRefusesAReferenceThatCouldEscapeTheMountAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "ai-cred-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory, "key"), Secret).ConfigureAwait(false);

                var resolver = new FileCredentialResolver(directory);

                string? resolved = await resolver
                    .ResolveAsync("key", CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(resolved, Is.EqualTo(Secret));

                // A reference is configuration this Server controls, so one carrying
                // a separator is a mistake worth surfacing rather than input worth
                // repairing. Sanitising it quietly would hide the misconfiguration.
                foreach (string escape in new[] { "../key", "sub/key", "sub\\key", ".." })
                {
                    Assert.ThrowsAsync<ArgumentException>(
                        async () => await resolver
                            .ResolveAsync(escape, CancellationToken.None)
                            .ConfigureAwait(false),
                        "'{0}' must be refused",
                        escape);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// Every node the Server publishes, children included.
        /// </summary>
        private static IEnumerable<NodeState> Walk(AINodeManager nm)
        {
            var root = nm.FindPredefinedNode<NodeState>(nm.RootId);
            var pending = new Stack<NodeState>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                NodeState node = pending.Pop();
                yield return node;

                var children = new List<BaseInstanceState>();
                node.GetChildren(nm.SystemContext, children);

                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
        }
    }
}
