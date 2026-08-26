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
using Microsoft.Extensions.AI;
using Moq;
using NUnit.Framework;
using Opc.Ua.AI.Inference;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Covers the <see cref="ChatClientInferenceBackend"/> projection.
    /// </summary>
    /// <remarks>
    /// The point of routing through <c>Microsoft.Extensions.AI</c> is that one
    /// abstraction covers a hosted service and an on-device runtime, which is the
    /// property clause 8.1 asserts about where inference runs. These tests use a
    /// stub <see cref="IChatClient"/>, so they exercise the projection both ways
    /// without a model, a network or a GPU - which is also what lets them run in
    /// CI. What they mostly check is that nothing is invented: usage the client
    /// did not report must not be estimated, and a model it did not name must not
    /// be guessed at.
    /// </remarks>
    [TestFixture]
    public sealed class ChatClientInferenceBackendTests
    {
        [Test]
        public async Task InvokeProjectsMessagesAndReportsWhatTheClientAnswered()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))
                {
                    ModelId = "served-model",
                    FinishReason = ChatFinishReason.Stop,
                    Usage = new UsageDetails
                    {
                        InputTokenCount = 11,
                        OutputTokenCount = 22,
                        TotalTokenCount = 33
                    }
                });
            using var backend = new ChatClientInferenceBackend(client, InferenceSite.EdgeOffServer);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"ping"}]}""", "asked-model"),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True);
                Assert.That(Encoding.UTF8.GetString(result.Payload.Span), Is.EqualTo("pong"));
                Assert.That(result.ModelUsed, Is.EqualTo("served-model"),
                    "The model that actually answered is not always the one asked for, " +
                    "and a caller that cannot see the substitution cannot tell a degraded " +
                    "answer from a good one.");
                Assert.That(result.InputUnits, Is.EqualTo(11UL));
                Assert.That(result.OutputUnits, Is.EqualTo(22UL));
                Assert.That(result.TotalUnits, Is.EqualTo(33UL));
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Stop));
                Assert.That(client.LastMessages, Has.Count.EqualTo(1));
                Assert.That(client.LastMessages![0].Role, Is.EqualTo(ChatRole.User));
                Assert.That(client.LastMessages![0].Text, Is.EqualTo("ping"));
                Assert.That(client.LastOptions?.ModelId, Is.EqualTo("asked-model"));
            });
        }

        [Test]
        public async Task InvokeReportsTheRequestedModelWhenTheClientNamesNone()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(client);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"hi"}]}""", "asked-model"),
                CancellationToken.None).ConfigureAwait(false);

            // The request named a model and nothing observed contradicts it, so it
            // is reported rather than second-guessed.
            Assert.That(result.ModelUsed, Is.EqualTo("asked-model"));
        }

        [Test]
        public async Task InvokeKeepsPlainStringContentAsATextMessage()
        {
            List<ChatMessage>? captured = null;
            Mock<IChatClient> client = CreateCapturingClient((messages, _, _) => captured = new List<ChatMessage>(messages));
            using var backend = new ChatClientInferenceBackend(client.Object);

            await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"ping"}]}"""),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured![0].Role, Is.EqualTo(ChatRole.User));
                Assert.That(captured[0].Text, Is.EqualTo("ping"));
            });
        }

        [Test]
        public async Task InvokeProjectsTextAndDataImagePartsToMultimodalContent()
        {
            byte[] imageBytes = [1, 2, 3, 4];
            string image = Convert.ToBase64String(imageBytes);
            List<ChatMessage>? captured = null;
            Mock<IChatClient> client = CreateCapturingClient((messages, _, _) => captured = new List<ChatMessage>(messages));
            using var backend = new ChatClientInferenceBackend(client.Object);

            await backend.InvokeAsync(
                Request(
                    """
                    {"messages":[{"role":"user","content":[
                      {"type":"text","text":"Measure the bore diameter."},
                      {"type":"image_url","image_url":{"url":"data:image/png;base64,$IMAGE$","detail":"high"}}
                    ]}]}
                    """.Replace("$IMAGE$", image, StringComparison.Ordinal)),
                CancellationToken.None).ConfigureAwait(false);

            IList<AIContent> contents = captured![0].Contents;
            var text = (TextContent)contents[0];
            var data = (DataContent)contents[1];
            Assert.Multiple(() =>
            {
                Assert.That(contents, Has.Count.EqualTo(2));
                Assert.That(text.Text, Is.EqualTo("Measure the bore diameter."));
                Assert.That(data.MediaType, Is.EqualTo("image/png"));
                Assert.That(data.Data.ToArray(), Is.EqualTo(imageBytes).AsCollection);
                Assert.That(data.AdditionalProperties?["detail"], Is.EqualTo("high"));
            });
        }

        [Test]
        public async Task InvokeProjectsRemoteImagePartToUriContent()
        {
            List<ChatMessage>? captured = null;
            Mock<IChatClient> client = CreateCapturingClient((messages, _, _) => captured = new List<ChatMessage>(messages));
            using var backend = new ChatClientInferenceBackend(client.Object);

            await backend.InvokeAsync(
                Request("""
                    {"messages":[{"role":"user","content":[
                      {"type":"image_url","image_url":{"url":"https://example.test/frame.png"}}
                    ]}]}
                    """),
                CancellationToken.None).ConfigureAwait(false);

            var uri = (UriContent)captured![0].Contents[0];
            Assert.Multiple(() =>
            {
                Assert.That(uri.Uri, Is.EqualTo(new Uri("https://example.test/frame.png")));
                Assert.That(uri.MediaType, Is.EqualTo("image/png"));
            });
        }

        [Test]
        public async Task InvokeRefusesMalformedImageDataUriBeforeCallingTheClient()
        {
            List<ChatMessage>? captured = null;
            Mock<IChatClient> client = CreateCapturingClient((messages, _, _) => captured = new List<ChatMessage>(messages));
            using var backend = new ChatClientInferenceBackend(client.Object);

            InferenceResult result = await backend.InvokeAsync(
                Request("""
                    {"messages":[{"role":"user","content":[
                      {"type":"image_url","image_url":{"url":"data:image/png;base64,not-base64"}}
                    ]}]}
                    """),
                CancellationToken.None).ConfigureAwait(false);

            // A malformed image is refused before inference so callers do not get
            // an answer about a picture the model never received.
            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Message, Does.Contain("base64"));
                Assert.That(captured, Is.Null);
            });
        }

        [Test]
        [TestCase("data:foo;base64,AAAA", TestName = "MediaTypeWithoutASubtype")]
        [TestCase("data:image;base64,AAAA", TestName = "MediaTypeMissingItsSubtype")]
        [TestCase("data:image//png;base64,AAAA", TestName = "MediaTypeWithTwoSeparators")]
        public async Task InvokeRefusesAnInvalidImageMediaTypeAsAStructuredResult(string url)
        {
            List<ChatMessage>? captured = null;
            Mock<IChatClient> client = CreateCapturingClient((messages, _, _) => captured = new List<ChatMessage>(messages));
            using var backend = new ChatClientInferenceBackend(client.Object);

            InferenceResult result = await backend.InvokeAsync(
                Request(
                    "{\"messages\":[{\"role\":\"user\",\"content\":[" +
                    "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" +
                    url +
                    "\"}}" +
                    "]}]}"),
                CancellationToken.None).ConfigureAwait(false);

            // DataContent validates the media type itself and throws
            // ArgumentException, not FormatException. Left uncaught that escapes
            // InvokeAsync entirely, so the caller gets a bare Bad status carrying an
            // internal exception message rather than a refusal naming the problem -
            // and on the chunked-transfer path the transfer is stranded Executing,
            // holding a concurrency slot until it expires.
            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Error));
                Assert.That(result.Message, Does.Contain("media type"));
                Assert.That(captured, Is.Null);
            });
        }

        [Test]
        public async Task InvokeRefusesANonStringRoleAsAStructuredResult()
        {
            List<ChatMessage>? captured = null;
            Mock<IChatClient> client = CreateCapturingClient(
                (messages, _, _) => captured = new List<ChatMessage>(messages));
            using var backend = new ChatClientInferenceBackend(client.Object);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":5,"content":"hi"}]}"""),
                CancellationToken.None).ConfigureAwait(false);

            // JsonElement.GetString throws on a non-string element, which would
            // escape the handler that turns a bad payload into a refusal.
            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Message, Does.Contain("role"));
                Assert.That(captured, Is.Null);
            });
        }

        [Test]
        public async Task InvokeReportsZeroUnitsWhenTheClientReportsNoUsage()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(client);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"hi"}]}"""),
                CancellationToken.None).ConfigureAwait(false);

            // Estimating usage a backend did not report would produce a number that
            // looks metered and is not, and it would be billed against.
            Assert.Multiple(() =>
            {
                Assert.That(result.InputUnits, Is.Zero);
                Assert.That(result.OutputUnits, Is.Zero);
                Assert.That(result.TotalUnits, Is.Zero);
            });
        }

        [Test]
        public async Task InvokeRefusesAPayloadThatIsNotValidJson()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
            using var backend = new ChatClientInferenceBackend(client);

            InferenceResult result = await backend.InvokeAsync(
                Request("{ not json"),
                CancellationToken.None).ConfigureAwait(false);

            // Malformed input from a caller is expected rather than exceptional,
            // and the reason is more use than a transport fault.
            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Error));
                Assert.That(result.Message, Does.Contain("not a valid chat completions body"));
                Assert.That(client.LastMessages, Is.Null, "The client must not be reached.");
            });
        }

        [Test]
        public async Task InvokeRefusesAPayloadCarryingNoMessages()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
            using var backend = new ChatClientInferenceBackend(client);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[]}"""),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Message, Does.Contain("no messages"));
                Assert.That(client.LastMessages, Is.Null);
            });
        }

        [Test]
        public async Task InvokeMapsTheRolesTheContractDefines()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(client);

            await backend.InvokeAsync(
                Request("""
                    {"messages":[
                      {"role":"system","content":"s"},
                      {"role":"user","content":"u"},
                      {"role":"assistant","content":"a"},
                      {"role":"tool","content":"t"}]}
                    """),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                new[]
                {
                    client.LastMessages![0].Role,
                    client.LastMessages![1].Role,
                    client.LastMessages![2].Role,
                    client.LastMessages![3].Role
                },
                Is.EqualTo(new[]
                {
                    ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool
                }).AsCollection);
        }

        [Test]
        public async Task InvokeAppliesTheCallParametersTheBackendSupports()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(client);

            await backend.InvokeAsync(
                Request(
                    """{"messages":[{"role":"user","content":"hi"}]}""",
                    parameters: new Dictionary<string, string>
                    {
                        ["temperature"] = "0.25",
                        ["max_tokens"] = "64",
                        ["top_p"] = "0.9"
                    }),
                CancellationToken.None).ConfigureAwait(false);

            // Parsed invariantly: on a machine whose locale writes 0,25 a
            // culture-sensitive parse silently drops the decimal point.
            Assert.Multiple(() =>
            {
                Assert.That(client.LastOptions!.Temperature, Is.EqualTo(0.25f));
                Assert.That(client.LastOptions!.MaxOutputTokens, Is.EqualTo(64));
                Assert.That(client.LastOptions!.TopP, Is.EqualTo(0.9f));
            });
        }

        [Test]
        public void InvokeRefusesACallParameterItCannotHonour()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(client);

            // A caller whose parameter was silently dropped believes it took
            // effect, and there is no later point at which it can find out.
            Assert.That(
                async () => await backend.InvokeAsync(
                    Request(
                        """{"messages":[{"role":"user","content":"hi"}]}""",
                        parameters: new Dictionary<string, string> { ["seed"] = "1" }),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public async Task ProbeReportsUnreachableRatherThanRaisingWhenTheClientFails()
        {
            var client = new StubChatClient(new InvalidOperationException("no route to host"));
            using var backend = new ChatClientInferenceBackend(client);

            BackendProbe probe = await backend.ProbeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // A probe exists so a commissioning engineer learns the endpoint is
            // wrong before a deployment depends on it, so it reports rather than
            // throws.
            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.False);
                Assert.That(probe.Detail, Does.Contain("no route to host"));
            });
        }

        [Test]
        public async Task ProbeReportsReachableAndNamesTheModelThatAnswered()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))
                {
                    ModelId = "served-model"
                });
            using var backend = new ChatClientInferenceBackend(client);

            BackendProbe probe = await backend.ProbeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.True);
                Assert.That(probe.Detail, Is.EqualTo("served-model"));
            });
        }

        [Test]
        public async Task ListModelsFiltersAndBoundsTheSuppliedCatalogue()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(
                client,
                InferenceSite.OnServer,
                new[]
                {
                    new BackendModel { Name = "alpha-small" },
                    new BackendModel { Name = "alpha-large" },
                    new BackendModel { Name = "beta" }
                });

            IReadOnlyList<BackendModel> all = await backend
                .ListModelsAsync(null, 0, CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<BackendModel> filtered = await backend
                .ListModelsAsync("alpha", 0, CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<BackendModel> bounded = await backend
                .ListModelsAsync(null, 2, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(all, Has.Count.EqualTo(3));
                Assert.That(filtered, Has.Count.EqualTo(2));
                Assert.That(bounded, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void SiteIsWhatTheHostStatedRatherThanSomethingInferred()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(client, InferenceSite.OnServer);

            // An IChatClient over a local runtime and one over a hosted service are
            // the same type, so the client cannot be asked where it runs.
            Assert.That(backend.Site, Is.EqualTo(InferenceSite.OnServer));
        }

        [Test]
        public void ConstructorRefusesANullClient()
        {
            Assert.That(
                () => new ChatClientInferenceBackend(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void InvokeRefusesANullRequest()
        {
            var client = new StubChatClient(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            using var backend = new ChatClientInferenceBackend(client);

            Assert.That(
                async () => await backend.InvokeAsync(null!, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        private static InferenceRequest Request(
            string body,
            string model = "",
            IReadOnlyDictionary<string, string>? parameters = null)
        {
            return new InferenceRequest
            {
                Model = model,
                Payload = Encoding.UTF8.GetBytes(body),
                ContentType = "application/json",
                Parameters = parameters ?? new Dictionary<string, string>()
            };
        }

        private static Mock<IChatClient> CreateCapturingClient(
            Action<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken> callback)
        {
            var client = new Mock<IChatClient>();
            client
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback(callback)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            return client;
        }

        private sealed class StubChatClient : IChatClient
        {
            private readonly ChatResponse? m_response;
            private readonly Exception? m_failure;

            public StubChatClient(ChatResponse response)
            {
                m_response = response;
            }

            public StubChatClient(Exception failure)
            {
                m_failure = failure;
            }

            public List<ChatMessage>? LastMessages { get; private set; }

            public ChatOptions? LastOptions { get; private set; }

            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                LastMessages = new List<ChatMessage>(messages);
                LastOptions = options;
                if (m_failure != null)
                {
                    throw m_failure;
                }
                return Task.FromResult(m_response!);
            }

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public object? GetService(Type serviceType, object? serviceKey = null)
            {
                return null;
            }

            public void Dispose()
            {
            }
        }
    }
}
