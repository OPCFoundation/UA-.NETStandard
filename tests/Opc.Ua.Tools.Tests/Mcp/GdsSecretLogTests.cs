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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Proves arbitrary third-party exception text never reaches public errors or structured logging.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsSecretLogTests
    {
        [Test]
        public async Task ArbitraryProviderExceptionCannotLeakSecretThroughPersistenceErrorOrLogsAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.SetFailure = new MaliciousSecretProviderException("malicious-provider-secret-material");
            fixture.Store.RemoveFailure = new MaliciousSecretProviderException("malicious-cleanup-secret-material");

            McpSecretPersistenceException? failure = Assert.ThrowsAsync<McpSecretPersistenceException>(async () =>
                await fixture.Secrets.StoreIssuedAsync(ByteString.From([1, 2, 3]),
                    McpCapability.CredentialIssuance, "key-credential", "urn:resource",
                    fixture.Clock.GetUtcNow().AddMinutes(1)).ConfigureAwait(false));

            Assert.That(failure!.CleanupFailed, Is.True);
            Assert.That(failure.ProviderStatus, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(failure.InnerException, Is.Null);
            Assert.That(failure.ToString(), Does.Not.Contain("malicious-provider-secret-material")
                .And.Not.Contain("malicious-cleanup-secret-material"));
            AssertSecretLogs(fixture, "Set", "RemoveAfterFailedSet");
            Assert.That(fixture.Store.Values, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ArbitraryProviderReadExceptionCannotLeakMessageOrInnerExceptionAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.GetFailure = new MaliciousSecretProviderException("malicious-provider-secret-material",
                new MaliciousSecretProviderException("malicious-inner-secret-material"));

            ServiceResultException? failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Secrets.ResolveAsync(
                    "alias:proof", "urn:resource", McpCapability.CredentialIssuance).ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(failure.InnerException, Is.Null);
            Assert.That(failure.ToString(), Does.Not.Contain("malicious-provider-secret-material")
                .And.Not.Contain("malicious-inner-secret-material"));
            AssertSecretLogs(fixture, "GetAlias");
        }

        [Test]
        public async Task ProviderCancellationRemainsCanceledWithoutLeakingItsMessageAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            fixture.Store.GetFailure = new OperationCanceledException(
                "malicious-provider-secret-material", cancellation.Token);
            Task<ISecret> operation = fixture.Secrets.ResolveAsync(
                "alias:proof", "urn:resource", McpCapability.CredentialIssuance).AsTask();

            OperationCanceledException? failure = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await operation.ConfigureAwait(false));

            Assert.That(operation.IsCanceled, Is.True);
            Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(failure.InnerException, Is.Null);
            Assert.That(failure.ToString(), Does.Not.Contain("malicious-provider-secret-material"));
            Assert.That(fixture.Logs.SecretEntries.ToArray()!.Single().Message,
                Does.Contain("OperationCanceledException"));
            Assert.That(fixture.Logs.SecretEntries.ToArray()!.Single().HasExceptionArgument, Is.False);
        }

        [Test]
        public async Task PersistenceCancellationAfterRemoteIssuanceRetainsTheRemoteResultAndSafeLocalErrorAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Store.SetFailure = new OperationCanceledException("malicious-provider-secret-material");
            fixture.Remote.Reply(StatusCodes.Good, Variant.From("credential-17"), Variant.From(ByteString.From([7])),
                Variant.From("thumbprint"), Variant.From(SecurityPolicies.Basic256Sha256),
                Variant.From(ArrayOf<NodeId>.Empty));

            CallToolResult result = await fixture.Services.GetRequiredService<GdsCredentialTools>()
                .FinishKeyRequestAsync(fixture.Remote.Id("key-service").ToString(),
                    fixture.Remote.Id("request").ToString(), "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(json.GetProperty("remoteIssuance").GetString(), Is.EqualTo("issued"));
            Assert.That(json.GetProperty("requestId").GetString(), Is.EqualTo(fixture.Remote.Id("request").ToString()));
            Assert.That(json.GetProperty("key-credentialPersistence").GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadRequestCancelledByClient.Code));
            Assert.That(json.GetRawText(), Does.Not.Contain("malicious-provider-secret-material"));
            Assert.That(fixture.Store.Values, Is.Empty);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task StoreCancellationHasTypedRecoveryMetadataAndPreservesCanceledTaskStateAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            fixture.Store.SetFailure = new OperationCanceledException(
                "malicious-provider-secret-material", cancellation.Token);
            Task<McpSecretReference> operation = fixture.Secrets.StoreIssuedAsync(
                ByteString.From([5]), McpCapability.CredentialIssuance, "key-credential", "urn:resource",
                fixture.Clock.GetUtcNow().AddMinutes(1)).AsTask();

            McpSecretPersistenceCanceledException? failure =
                Assert.ThrowsAsync<McpSecretPersistenceCanceledException>(async () =>
                    await operation.ConfigureAwait(false));

            Assert.That(operation.IsCanceled, Is.True);
            Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(failure.ProviderStatus, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(failure.CleanupFailed, Is.False);
            Assert.That(failure.Reference.Reference, Does.StartWith("issued:"));
            Assert.That(failure.InnerException, Is.Null);
            Assert.That(failure.ToString(), Does.Not.Contain("malicious-provider-secret-material"));
            Assert.That(fixture.Store.Values, Is.Empty);
        }

        /// <summary>
        /// Standard exception constructors cannot turn provider diagnostics into public credential material.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void PersistenceConstructorsDiscardProviderDiagnostics(int constructor)
        {
            var inner = new InvalidOperationException("malicious-inner-secret-material");
            McpSecretPersistenceException failure = constructor switch
            {
                0 => new McpSecretPersistenceException(),
                1 => new McpSecretPersistenceException("malicious-provider-secret-material"),
                _ => new McpSecretPersistenceException("malicious-provider-secret-material", inner)
            };

            Assert.That(failure.ToString(), Does.Not.Contain("secret-material"));
            Assert.That(failure.InnerException, Is.Null);
            Assert.That(failure.Reference.Reference, Is.Empty);
            Assert.That(failure.Reference.ExpiresAt, Is.EqualTo(DateTimeOffset.MinValue));
            Assert.That(failure.ProviderStatus, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(failure.CleanupFailed, Is.False);
        }

        /// <summary>
        /// Cancellation overloads preserve the supplied token without retaining messages or inner exceptions.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        [TestCase(4, true)]
        [TestCase(5, false)]
        [TestCase(5, true)]
        public async Task CancellationConstructorsPreserveOnlyExplicitTokenAsync(int constructor, bool canceled)
        {
            using var cancellation = new CancellationTokenSource();
            if (canceled)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            var inner = new InvalidOperationException("malicious-inner-secret-material");
            McpSecretPersistenceCanceledException failure = constructor switch
            {
                0 => new McpSecretPersistenceCanceledException(),
                1 => new McpSecretPersistenceCanceledException("malicious-provider-secret-material"),
                2 => new McpSecretPersistenceCanceledException("malicious-provider-secret-material", inner),
                3 => new McpSecretPersistenceCanceledException(cancellation.Token),
                4 => new McpSecretPersistenceCanceledException(
                    "malicious-provider-secret-material", cancellation.Token),
                _ => new McpSecretPersistenceCanceledException(
                    "malicious-provider-secret-material", inner, cancellation.Token)
            };

            Assert.That(failure.CancellationToken, Is.EqualTo(
                constructor >= 3 ? cancellation.Token : CancellationToken.None));
            Assert.That(failure.CancellationToken.IsCancellationRequested, Is.EqualTo(constructor >= 3 && canceled));
            Assert.That(failure.ToString(), Does.Not.Contain("secret-material"));
            Assert.That(failure.InnerException, Is.Null);
            Assert.That(failure.Reference.Reference, Is.Empty);
            Assert.That(failure.Reference.ExpiresAt, Is.EqualTo(DateTimeOffset.MinValue));
            Assert.That(failure.ProviderStatus, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(failure.CleanupFailed, Is.False);
        }

        /// <summary>
        /// Checks exact provider operation/type-only diagnostics and absence of exception objects.
        /// </summary>
        private static void AssertSecretLogs(GdsTestContext fixture, params string[] operations)
        {
            ArrayOf<GdsTestLogEntry> entries = fixture.Logs.SecretEntries;
            Assert.That(entries, Has.Count.EqualTo(operations.Length));
            for (int index = 0; index < operations.Length; index++)
            {
                Assert.That(entries[index].Message,
                    Is.EqualTo($"MCP secret provider operation {operations[index]} failed with " +
                        $"{typeof(MaliciousSecretProviderException).FullName}."));
                Assert.That(entries[index].HasExceptionArgument, Is.False);
            }
            Assert.That(entries.ToArray()!.All(entry =>
                !entry.Message.Contains("secret-material", StringComparison.Ordinal)), Is.True);
        }
    }

    /// <summary>
    /// Simulates a third-party provider exception outside the original operational exception allowlist.
    /// Its deliberately unsafe message and inner exception must never escape the production trust boundary.
    /// </summary>
    internal sealed class MaliciousSecretProviderException : Exception
    {
        /// <summary>
        /// Creates an empty provider-specific exception.
        /// </summary>
        public MaliciousSecretProviderException()
        {
        }

        /// <summary>
        /// Preserves malicious provider text so the gateway, rather than the fixture, must sanitize it.
        /// </summary>
        public MaliciousSecretProviderException(string? message)
            : base(message)
        {
        }

        /// <summary>
        /// Preserves nested provider diagnostics to verify the gateway does not copy them into public failures.
        /// </summary>
        public MaliciousSecretProviderException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Captures formatted logging fields and whether an exception was supplied, without storing that exception.
    /// </summary>
    internal sealed class GdsTestLogProvider : ILoggerProvider
    {
        /// <summary>
        /// Gets only diagnostics from the production secret gateway.
        /// </summary>
        internal ArrayOf<GdsTestLogEntry> SecretEntries
        {
            get
            {
                lock (m_state)
                {
                    return [.. m_entries.Where(entry => entry.Category == typeof(McpSecretReferences).FullName)];
                }
            }
        }

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
        {
            return new Recorder(this, categoryName);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <summary>
        /// Stores a safe logging observation for assertions.
        /// </summary>
        private void Add(GdsTestLogEntry entry)
        {
            lock (m_state)
            {
                m_entries.Add(entry);
            }
        }

        /// <summary>
        /// Implements the standard logging contract without retaining secret-bearing exception objects.
        /// </summary>
        private sealed class Recorder(GdsTestLogProvider owner, string category) : ILogger
        {
            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                owner.Add(new GdsTestLogEntry(category, formatter(state, exception), exception is not null));
            }
        }

        private readonly List<GdsTestLogEntry> m_entries = [];
        private readonly Lock m_state = new();
    }

    /// <summary>
    /// A logging observation containing no exception object.
    /// </summary>
    internal sealed record GdsTestLogEntry(string Category, string Message, bool HasExceptionArgument);
}
#endif
