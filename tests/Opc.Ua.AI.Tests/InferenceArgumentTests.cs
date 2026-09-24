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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.AI;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;

namespace Opc.Ua.AI.Tests
{
    [TestFixture]
    [Category("AIModelManagement")]
    [SetCulture("de-DE")]
    [SetUICulture("en-us")]
    public sealed class InferenceArgumentTests
    {
        [Test]
        public async Task InvokeForwardsParametersToTheBackendAsync()
        {
            var backend = new FakeInferenceBackend("primary");
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            InvokeMethodStateResult result = await deployment.Invoke!.OnCallAsync!(
                nm.SystemContext,
                deployment.Invoke,
                nm.PrimaryDeploymentId,
                ByteString.From(Encoding.UTF8.GetBytes("{}")),
                string.Empty,
                "application/json",
                Parameters(),
                5000,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(backend.Requests, Has.Count.EqualTo(1));
                Assert.That(backend.Requests[0].Parameters["temperature"], Is.EqualTo("0.25"));
                Assert.That(backend.Requests[0].Parameters["max_tokens"], Is.EqualTo("64"));
            });
        }

        [Test]
        public async Task UriOnlyInferenceRequestsAreRejectedWithoutCallingTheBackendAsync()
        {
            var backend = new FakeInferenceBackend("primary");
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            InvokeMethodStateResult inline = await deployment.Invoke!.OnCallAsync!(
                nm.SystemContext,
                deployment.Invoke,
                nm.PrimaryDeploymentId,
                default,
                "https://example.test/request.json",
                "application/json",
                ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                5000,
                CancellationToken.None).ConfigureAwait(false);

            InvokeAsyncMethodStateResult asynchronous = await deployment.InvokeAsync!.OnCallAsync!(
                nm.SystemContext,
                deployment.InvokeAsync,
                nm.PrimaryDeploymentId,
                default,
                "https://example.test/request.json",
                "application/json",
                ArrayOf<Opc.Ua.KeyValuePair>.Empty,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(inline.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(asynchronous.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(asynchronous.Job, Is.EqualTo(NodeId.Null));
                Assert.That(backend.Requests, Is.Empty);
            });
        }

        [Test]
        public async Task InvalidBackendParametersAreReportedAsBadInvalidArgumentAsync()
        {
            using AINodeManager nm = await CreateAsync(new InvalidParameterBackend())
                .ConfigureAwait(false);
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            InvokeMethodStateResult result = await deployment.Invoke!.OnCallAsync!(
                nm.SystemContext,
                deployment.Invoke,
                nm.PrimaryDeploymentId,
                ByteString.From(Encoding.UTF8.GetBytes("{}")),
                string.Empty,
                "application/json",
                Parameters(),
                5000,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task IntegerParametersAreAcceptedInEveryNumericEncodingAsync()
        {
            var backend = new FakeInferenceBackend("primary");
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            InvokeMethodStateResult result = await deployment.Invoke!.OnCallAsync!(
                nm.SystemContext,
                deployment.Invoke,
                nm.PrimaryDeploymentId,
                ByteString.From(Encoding.UTF8.GetBytes("{}")),
                string.Empty,
                "application/json",
                [
                    Parameter("max_tokens", Variant.From(64L)),
                    Parameter("top_k", Variant.From((ushort)40)),
                    Parameter("seed", Variant.From(7UL)),
                    Parameter("width", Variant.From((short)512)),
                    Parameter("level", Variant.From((byte)3)),
                    Parameter("offset", Variant.From((sbyte)-2)),
                    Parameter("stream", Variant.From(false))
                ],
                5000,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            IReadOnlyDictionary<string, string> forwarded = backend.Requests[0].Parameters;
            Assert.Multiple(() =>
            {
                Assert.That(forwarded["max_tokens"], Is.EqualTo("64"));
                Assert.That(forwarded["top_k"], Is.EqualTo("40"));
                Assert.That(forwarded["seed"], Is.EqualTo("7"));
                Assert.That(forwarded["width"], Is.EqualTo("512"));
                Assert.That(forwarded["level"], Is.EqualTo("3"));
                Assert.That(forwarded["offset"], Is.EqualTo("-2"));
                Assert.That(forwarded["stream"], Is.EqualTo("false"));
            });
        }

        [Test]
        public async Task ParametersWithUnusableValuesAreRejectedAsync()
        {
            var backend = new FakeInferenceBackend("primary");
            using AINodeManager nm = await CreateAsync(backend).ConfigureAwait(false);
            DeploymentState deployment = nm.FindPredefinedNode<DeploymentState>(nm.PrimaryDeploymentId);

            var rejected = new Dictionary<string, Variant>
            {
                ["null string"] = Variant.From((string)null!),
                ["not a number"] = Variant.From(double.NaN),
                ["not a scalar"] = Variant.From(s_nonScalarValue)
            };

            foreach (KeyValuePair<string, Variant> candidate in rejected)
            {
                InvokeMethodStateResult result = await deployment.Invoke!.OnCallAsync!(
                    nm.SystemContext,
                    deployment.Invoke,
                    nm.PrimaryDeploymentId,
                    ByteString.From(Encoding.UTF8.GetBytes("{}")),
                    string.Empty,
                    "application/json",
                    [Parameter("temperature", candidate.Value)],
                    5000,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    result.ServiceResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidArgument),
                    candidate.Key + " must not reach the backend.");
            }

            Assert.That(backend.Requests, Is.Empty);
        }

        [Test]
        public async Task RootSpecificationVersionUsesTheGeneratedModelVersionAsync()
        {
            using AINodeManager nm = await CreateAsync(new FakeInferenceBackend("primary"))
                .ConfigureAwait(false);
            AiRootState root = nm.FindPredefinedNode<AiRootState>(nm.RootId);

            Assert.Multiple(() =>
            {
                Assert.That(AINodeManager.SpecificationVersion, Is.EqualTo(ModelVersions.Target));
                Assert.That(root.SpecificationVersion!.Value, Is.EqualTo(ModelVersions.Target));
                Assert.That(ModelVersions.Target, Is.EqualTo("0.4.1"));
            });
        }

        private static ArrayOf<Opc.Ua.KeyValuePair> Parameters()
        {
            return
            [
                Parameter("temperature", Variant.From(0.25)),
                Parameter("max_tokens", Variant.From(64))
            ];
        }

        private static Opc.Ua.KeyValuePair Parameter(string name, Variant value)
        {
            return new Opc.Ua.KeyValuePair
            {
                Key = new QualifiedName(name),
                Value = value
            };
        }

        private static Task<AINodeManager> CreateAsync(IInferenceBackend backend)
        {
            return AIServerTestHarness.CreateAsync(
                new InferenceBackends(backend),
                new AIOptions { EnableFallback = false });
        }

        private sealed class InvalidParameterBackend : IInferenceBackend
        {
            public InferenceSite Site => InferenceSite.OnServer;

            public ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
                string? filter,
                uint maxResults,
                CancellationToken ct)
            {
                return ValueTask.FromResult<IReadOnlyList<BackendModel>>([]);
            }

            public ValueTask<InferenceResult> InvokeAsync(
                InferenceRequest request,
                CancellationToken ct)
            {
                throw new ArgumentException("The parameter is invalid.", nameof(request));
            }

            public ValueTask<BackendProbe> ProbeAsync(CancellationToken ct)
            {
                return ValueTask.FromResult(new BackendProbe { Reachable = true });
            }
        }

        private static readonly int[] s_nonScalarValue = [1, 2];
    }
}
