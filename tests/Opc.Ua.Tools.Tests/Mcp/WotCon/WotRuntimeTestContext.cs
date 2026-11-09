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

#if NET10_0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpRuntime;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Uses real planning, policy, registry, binding and result projection with injectable executor I/O.
    /// </summary>
    internal sealed class WotRuntimeTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates one isolated host with deterministic leases and optional real protocol executors.
        /// </summary>
        internal WotRuntimeTestContext(
            Action<McpExecutionOptions>? configure = null,
            WotRuntimeOptions? options = null,
            bool realExecutors = false,
            Action<IServiceCollection>? register = null,
            bool files = false)
        {
            Root = files
                ? Path.Combine(TestContext.CurrentContext.WorkDirectory, "wot-runtime-" + Guid.NewGuid().ToString("N"))
                : null;
            if (Root is not null)
            {
                Directory.CreateDirectory(Root);
            }
            Execution = new McpExecutionOptions
            {
                EnabledCapabilities = Capabilities,
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = Owner, Capabilities = Capabilities,
                        SessionNames = ["selected", "replacement"], SecretAliases = ["password", "username", "token"]
                    },
                    new McpCallerGrant
                    {
                        Caller = Other, Capabilities = Capabilities,
                        SessionNames = ["selected"], SecretAliases = ["password", "username", "token"]
                    }
                ],
                NetworkRules =
                [
                    Rule(McpCapability.WotRuntime, ["GET"], ["/value", "/other", "/action"]),
                    Rule(McpCapability.WotWrite, ["PUT", "POST"], ["/value", "/other"]),
                    Rule(McpCapability.WotInvoke, ["POST"], ["/action"])
                ]
            };
            configure?.Invoke(Execution);
            Options = options ?? new WotRuntimeOptions();
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions
            {
                TransferRoot = Root,
                MaxTransferBytes = 1_048_576,
                Execution = Execution
            });
            services.AddSingleton<TimeProvider>(Clock);
            Dns.Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ArrayOf<IPAddress>>(
                    Task.FromResult<ArrayOf<IPAddress>>([IPAddress.Loopback])));
            services.AddSingleton(Dns.Object);
            if (!realExecutors)
            {
                services.AddSingleton<IWotRuntimeExecutorFactory>(Factory);
            }
            register?.Invoke(services);
            services.AddOpcUaMcpWotRuntime(Options);
            services.AddMcpServer().WithStreamServerTransport(Stream.Null, Stream.Null)
                .WithOpcUaWotRuntimeTools(McpToolProfile.WotRuntime);
            Services = services.BuildServiceProvider();
            Tools = Services.GetRequiredService<WotRuntimeTools>();
        }

        /// <summary>
        /// Gets the exact trusted baseline identity.
        /// </summary>
        internal static McpCallerIdentity Owner { get; } = new("wot-owner", "urn:test:wot");

        /// <summary>
        /// Gets an independently authorized foreign owner.
        /// </summary>
        internal static McpCallerIdentity Other { get; } = new("wot-other", "urn:test:wot");

        /// <summary>
        /// Gets all capabilities granted by the baseline fixture.
        /// </summary>
        internal static ArrayOf<McpCapability> Capabilities { get; } =
        [
            McpCapability.WotRuntime, McpCapability.WotWrite, McpCapability.WotInvoke, McpCapability.PubSubRuntime
        ];

        /// <summary>
        /// Gets the baseline property document with two deliberately different endpoints.
        /// </summary>
        internal const string PropertyDocument = /*lang=json,strict*/ """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "id": "urn:test:wot:property",
              "title": "Runtime property",
              "securityDefinitions": {"nosec_sc": {"scheme": "nosec"}},
              "security": "nosec_sc",
              "properties": {
                "temperature": {
                  "type": "number",
                  "forms": [
                    {"href": "http://device.example/value", "op": ["readproperty", "writeproperty", "observeproperty"]},
                    {"href": "http://device.example/other", "op": "readproperty"}
                  ]
                }
              }
            }
            """;

        /// <summary>
        /// Gets the real DI service provider.
        /// </summary>
        internal ServiceProvider Services { get; }

        /// <summary>
        /// Gets the actual tools under test.
        /// </summary>
        internal WotRuntimeTools Tools { get; }

        /// <summary>
        /// Gets initial host authority options, snapshotted by production registration.
        /// </summary>
        internal McpExecutionOptions Execution { get; }

        /// <summary>
        /// Gets the initial live WoT host bounds.
        /// </summary>
        internal WotRuntimeOptions Options { get; }

        /// <summary>
        /// Gets the test-owned transfer root, when requested.
        /// </summary>
        internal string? Root { get; }

        /// <summary>
        /// Gets the strict injectable DNS seam; fake executor tests must never call it.
        /// </summary>
        internal Mock<IMcpDnsResolver> Dns { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// Gets the no-network executor factory and its concrete operation counters.
        /// </summary>
        internal WotRuntimeTestExecutors Factory { get; } = new();

        /// <summary>
        /// Gets the existing shared manual clock rather than introducing another timer implementation.
        /// </summary>
        internal McpRuntimeTestClock Clock { get; } = new(McpRuntimeTestContext.Epoch);

        /// <summary>
        /// Gets the real shared named-runtime registry.
        /// </summary>
        internal McpRuntimeRegistry Registry => Services.GetRequiredService<McpRuntimeRegistry>();

        /// <summary>
        /// Enters a trusted host caller scope.
        /// </summary>
        internal IDisposable Enter(McpCallerIdentity? caller = null)
        {
            return Services.GetRequiredService<McpCallerContext>().EnterTrustedCaller(caller ?? Owner);
        }

        /// <summary>
        /// Opens a real, pinned property plan and returns its public handle.
        /// </summary>
        internal async Task<string> OpenAsync(
            string name = "property", ArrayOf<string> operations = default, string document = PropertyDocument)
        {
            JsonObject result = Payload(await Tools.OpenAsync(name, new WotDocumentSource { Text = document },
                Selection(document), operations.IsNull ? ["readproperty"] : operations).ConfigureAwait(false));
            return result["runtime"]!["handle"]!.GetValue<string>();
        }

        /// <summary>
        /// Creates an exact document selection from original UTF-8 bytes, without canonicalizing the JSON.
        /// </summary>
        internal static WotRuntimeSelection Selection(string document = PropertyDocument)
        {
            return new WotRuntimeSelection
            {
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document))),
                Affordance = "temperature",
                BindingId = "w3c.http",
                BindingVersion = "1.1"
            };
        }

        /// <summary>
        /// Creates a simple explicit typed value.
        /// </summary>
        internal static WotRuntimeValue Value(BuiltInType type, string json)
        {
            using var document = JsonDocument.Parse(json);
            return new WotRuntimeValue { Type = type, Value = document.RootElement.Clone() };
        }

        /// <summary>
        /// Creates a precise host rule whose private network is deliberately explicit.
        /// </summary>
        internal static McpNetworkRule Rule(
            McpCapability capability, ArrayOf<string> operations, ArrayOf<string> resources,
            string scheme = "http", string host = "device.example", int port = 80)
        {
            return new McpNetworkRule
            {
                Capability = capability,
                Scheme = scheme,
                Host = host,
                Port = port,
                Networks = ["127.0.0.0/8"],
                Operations = operations,
                Resources = resources
            };
        }

        /// <summary>
        /// Asserts matching text/structured payloads and the exact success/error flag.
        /// </summary>
        internal static JsonObject Payload(CallToolResult result, bool error = false)
        {
            Assert.That(result.IsError, Is.EqualTo(error), result.StructuredContent?.GetRawText());
            Assert.That(result.StructuredContent.HasValue, Is.True);
            string text = result.StructuredContent!.Value.GetRawText();
            Assert.That(result.Content.OfType<TextContentBlock>().Single().Text, Is.EqualTo(text));
            return JsonNode.Parse(text)!.AsObject();
        }

        /// <summary>
        /// Invokes the actual MCP serializer and schema wrapper rather than only the direct tool method.
        /// </summary>
        internal async Task<CallToolResult> InvokeAsync(string name, JsonObject arguments)
        {
            McpServerTool tool = Services.GetServices<McpServerTool>()
                .Single(value => value.ProtocolTool.Name == name);
            using var document = JsonDocument.Parse(arguments.ToJsonString());
            var parameters = new CallToolRequestParams
            {
                Name = name,
                Arguments = document.RootElement.EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value.Clone())
            };
            var request = new RequestContext<CallToolRequestParams>(Services.GetRequiredService<McpServer>(),
                new JsonRpcRequest { Method = "tools/call", Id = new RequestId("wot-runtime-test") }, parameters)
            {
                MatchedPrimitive = tool
            };
            return await tool.InvokeAsync(request).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync().ConfigureAwait(false);
            if (Root is not null)
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Records only executor effects while retaining the real shipped planners.
    /// </summary>
    internal sealed class WotRuntimeTestExecutors : IWotRuntimeExecutorFactory
    {
        /// <summary>
        /// Advertises the four shipped identities to the real offline registry.
        /// </summary>
        internal WotRuntimeTestExecutors()
        {
            var executors = new List<IWotBindingExecutor>();
            foreach (IWotProtocolBinder binder in WotBuiltInBinders.CreateAll())
            {
                if (binder.Identity.Id is "w3c.http" or "w3c.mqtt" or "w3c.modbus" or "opc.opcua")
                {
                    var executor = new Mock<IWotBindingExecutor>(MockBehavior.Strict);
                    executor.SetupGet(value => value.Identity).Returns(binder.Identity);
                    executors.Add(executor.Object);
                }
            }
            Binders = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll(), executors);
        }

        /// <inheritdoc/>
        public IWotBinderRegistry Binders { get; }

        /// <summary>
        /// Gets the exact compiled operations sent to activation.
        /// </summary>
        internal ConcurrentQueue<WotCompiledForm> OpenedForms { get; } = new();

        /// <summary>
        /// Gets property values actually sent to the executor.
        /// </summary>
        internal ConcurrentQueue<DataValue> Writes { get; } = new();

        /// <summary>
        /// Gets ordered action argument batches actually dispatched.
        /// </summary>
        internal ConcurrentQueue<ArrayOf<Variant>> Invocations { get; } = new();

        /// <summary>
        /// Gets a deterministic signal that subscription registration completed.
        /// </summary>
        internal TaskCompletionSource<bool> Subscribed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets a deterministic signal that an owned channel has been disposed.
        /// </summary>
        internal TaskCompletionSource<bool> Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets the number of actual read operations.
        /// </summary>
        internal int ReadCalls => Volatile.Read(ref m_readCalls);

        /// <summary>
        /// Gets the last test-only resolved credential for protocol-header assertions.
        /// </summary>
        internal WotCredential? LastCredential { get; private set; }

        /// <summary>
        /// Gets or sets one bounded batch emitted synchronously during subscription creation.
        /// </summary>
        internal ArrayOf<WotNotification> Notifications { get; set; } = [];

        /// <summary>
        /// Gets or sets read behavior at the existing channel seam.
        /// </summary>
        internal Func<CancellationToken, ValueTask<WotReadResult>> Read { get; set; } =
            _ => ValueTask.FromResult(new WotReadResult(StatusCodes.Good, new DataValue(Variant.From(42))));

        /// <summary>
        /// Gets or sets write behavior, including deterministic late-completion tests.
        /// </summary>
        internal Func<DataValue, CancellationToken, ValueTask<WotWriteResult>> Write { get; set; } =
            (_, _) => ValueTask.FromResult(new WotWriteResult(StatusCodes.Good));

        /// <summary>
        /// Gets or sets ordered action results.
        /// </summary>
        internal Func<IReadOnlyList<Variant>, CancellationToken, ValueTask<WotInvokeResult>> Invoke { get; set; } =
            (_, _) => ValueTask.FromResult(new WotInvokeResult(StatusCodes.Good, []));

        /// <summary>
        /// Gets or sets a deterministic activation gate or failure.
        /// </summary>
        internal Func<CancellationToken, ValueTask>? Activating { get; set; }

        /// <summary>
        /// Gets or sets a cleanup failure or retry outcome for a returned subscription.
        /// </summary>
        internal Func<ValueTask>? DisposingSubscription { get; set; }

        /// <summary>
        /// Gets the number of channel disposal attempts, without fake-side deduplication.
        /// </summary>
        internal int DisposeCalls => Volatile.Read(ref m_disposeCalls);

        /// <summary>
        /// Gets the number of subscription disposal attempts.
        /// </summary>
        internal int SubscriptionDisposeCalls => Volatile.Read(ref m_subscriptionDisposeCalls);

        /// <inheritdoc/>
        public async ValueTask<WotRuntimeConnection> OpenAsync(
            WotCompiledForm form, McpNetworkGrant grant, IWotCredentialProvider credentials,
            ISession? session = null, CancellationToken ct = default)
        {
            if (Activating is not null)
            {
                await Activating(ct).ConfigureAwait(false);
            }
            if (!form.Security.IsEmpty && form.Security[0].Scheme != WotSecurityScheme.NoSecurity)
            {
                LastCredential = await credentials.ResolveAsync(form.Security[0], ct).ConfigureAwait(false);
            }
            OpenedForms.Enqueue(form);
            var channel = new Mock<IWotBindingChannel>(MockBehavior.Strict);
            channel.SetupGet(value => value.Form).Returns(form);
            channel.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    Interlocked.Increment(ref m_readCalls);
                    return Read(token);
                });
            channel.Setup(value => value.WriteAsync(It.IsAny<DataValue>(), It.IsAny<CancellationToken>()))
                .Returns((DataValue value, CancellationToken token) =>
                {
                    Writes.Enqueue(value);
                    return Write(value, token);
                });
            channel.Setup(value => value.InvokeAsync(It.IsAny<IReadOnlyList<Variant>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<Variant> values, CancellationToken token) =>
                {
                    Invocations.Enqueue(values.ToArrayOf());
                    return Invoke(values, token);
                });
            channel.Setup(value => value.ObserveAsync(It.IsAny<Action<WotNotification>>(),
                It.IsAny<CancellationToken>())).Returns((Action<WotNotification> callback, CancellationToken _) =>
                    Subscribe(form, callback));
            channel.Setup(value => value.SubscribeEventAsync(It.IsAny<Action<WotNotification>>(),
                It.IsAny<CancellationToken>())).Returns((Action<WotNotification> callback, CancellationToken _) =>
                    Subscribe(form, callback));
            channel.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                Interlocked.Increment(ref m_disposeCalls);
                Disposed.TrySetResult(true);
                return ValueTask.CompletedTask;
            });
            return new WotRuntimeConnection(channel.Object);
        }

        /// <summary>
        /// Creates a strict subscription and emits only explicitly supplied test notifications.
        /// </summary>
        private ValueTask<IWotSubscription> Subscribe(WotCompiledForm form, Action<WotNotification> callback)
        {
            var subscription = new Mock<IWotSubscription>(MockBehavior.Strict);
            subscription.SetupGet(value => value.Form).Returns(form);
            subscription.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                Interlocked.Increment(ref m_subscriptionDisposeCalls);
                return DisposingSubscription?.Invoke() ?? ValueTask.CompletedTask;
            });
            foreach (WotNotification notification in Notifications)
            {
                callback(notification);
            }
            Subscribed.TrySetResult(true);
            return ValueTask.FromResult(subscription.Object);
        }

        private int m_disposeCalls;
        private int m_subscriptionDisposeCalls;
        private int m_readCalls;
    }
}
#endif
