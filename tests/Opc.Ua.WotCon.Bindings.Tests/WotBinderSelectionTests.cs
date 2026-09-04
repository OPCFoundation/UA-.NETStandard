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
 *
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
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Selection among binders and executors is decided, not left to whichever
    /// registration happened to come first.
    /// </summary>
    /// <remarks>
    /// Two binders that match a form equally well, an executor registered under
    /// a bare binding id beside one registered under an exact key, and a
    /// compilation that reports success and errors at the same time are all
    /// situations where "it worked on my machine" and "it worked in the field"
    /// can differ solely by registration order. Each is pinned here so the
    /// answer is a property of the registry rather than of the container.
    /// </remarks>
    [TestFixture]
    public sealed class WotBinderSelectionTests
    {
        /// <summary>
        /// Two binders whose match priority is identical are separated by
        /// ordinal <c>id@version</c>, and the registry evaluates that order, so
        /// the first one wins whichever order they were registered in.
        /// </summary>
        [TestCase(false, TestName = "AnEqualPriorityTieIsBrokenByOrdinalOrder")]
        [TestCase(true, TestName = "AnEqualPriorityTieIgnoresRegistrationOrder")]
        public void AnEqualPriorityTieIsBrokenDeterministically(bool reverseRegistration)
        {
            var alpha = new StubBinder("stub.alpha", "1.0");
            var beta = new StubBinder("stub.beta", "1.0");
            IWotProtocolBinder[] binders = reverseRegistration
                ? [beta, alpha]
                : [alpha, beta];

            var registry = new WotProtocolBinderRegistry(binders);
            WotBindingPlan plan = registry.Prepare(Request());

            Assert.Multiple(() =>
            {
                Assert.That(plan.CompiledForms, Has.Length.EqualTo(1));
                Assert.That(
                    plan.CompiledForms[0].Binding.Id,
                    Is.EqualTo("stub.alpha"),
                    "'stub.alpha@1.0' sorts before 'stub.beta@1.0', and the " +
                    "registry evaluates that order, so registration order " +
                    "cannot change the winner.");
            });
        }

        /// <summary>
        /// A tie between two versions of one binding is broken the same way,
        /// which is what lets two versions of a binder coexist without the
        /// container deciding which one runs.
        /// </summary>
        [Test]
        public void AnEqualPriorityTieBetweenTwoVersionsIsAlsoOrdinal()
        {
            var registry = new WotProtocolBinderRegistry(
                [new StubBinder("stub.x", "2.0"), new StubBinder("stub.x", "1.0")]);

            WotBindingPlan plan = registry.Prepare(Request());

            Assert.That(plan.CompiledForms[0].Binding.Version, Is.EqualTo("1.0"));
        }

        /// <summary>
        /// A compilation that reports success and an error at once is rejected.
        /// </summary>
        /// <remarks>
        /// A planner that answers "supported" while also reporting an error has
        /// contradicted itself, and the only safe reading is the error: taking
        /// the entries would materialize a binding whose own planner said
        /// something about it was wrong, and the diagnostic beside it is
        /// something a caller reading the compiled forms never has to look at.
        /// </remarks>
        [Test]
        public void ASupportedCompilationThatCarriesAnErrorIsRejected()
        {
            var registry = new WotProtocolBinderRegistry(
                [new StubBinder("stub.err", "1.0", withError: true)]);

            WotBindingPlan plan = registry.Prepare(Request());

            Assert.Multiple(() =>
            {
                Assert.That(plan.CompiledForms, Is.Empty);
                Assert.That(plan.UnsupportedForms, Has.Length.EqualTo(1));
                Assert.That(
                    plan.Diagnostics.Any(d => d.Severity == WotDiagnosticSeverity.Error),
                    Is.True);
            });
        }

        [Test]
        public void ASupportedCompilationWithoutErrorsIsAccepted()
        {
            var registry = new WotProtocolBinderRegistry(
                [new StubBinder("stub.ok", "1.0")]);

            WotBindingPlan plan = registry.Prepare(Request());

            Assert.Multiple(() =>
            {
                Assert.That(plan.CompiledForms, Has.Length.EqualTo(1));
                Assert.That(plan.UnsupportedForms, Is.Empty);
            });
        }

        /// <summary>
        /// An executor registered for the exact <c>id@version</c> outranks one
        /// registered for the bare id.
        /// </summary>
        /// <remarks>
        /// The id fallback exists so a binder version that ships without its
        /// own executor still runs. It must not shadow an executor written for
        /// exactly this version, or a deployment that adds a general executor
        /// silently replaces a specific one.
        /// </remarks>
        [Test]
        public void AnExactBindingKeyOutranksTheBindingIdFallback()
        {
            var exact = new NamedExecutor(new WotBindingIdentity("stub.e", "2.0", "urn:x"));
            var fallback = new NamedExecutor(new WotBindingIdentity("stub.e", "1.0", "urn:x"));
            var registry = new WotProtocolBinderRegistry(
                [new StubBinder("stub.e", "2.0")], [fallback, exact]);

            bool found = registry.TryGetExecutor(
                new WotBindingIdentity("stub.e", "2.0", "urn:x"), out IWotBindingExecutor executor);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(executor, Is.SameAs(exact));
            });
        }

        /// <summary>
        /// With no exact match the id fallback is still used, so the ranking
        /// above is a preference and not a restriction.
        /// </summary>
        [Test]
        public void TheBindingIdFallbackIsUsedWhenNoExactKeyIsRegistered()
        {
            var fallback = new NamedExecutor(new WotBindingIdentity("stub.f", "1.0", "urn:x"));
            var registry = new WotProtocolBinderRegistry(
                [new StubBinder("stub.f", "9.9")], [fallback]);

            bool found = registry.TryGetExecutor(
                new WotBindingIdentity("stub.f", "9.9", "urn:x"), out IWotBindingExecutor executor);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(executor, Is.SameAs(fallback));
            });
        }

        [Test]
        public void AnUnrelatedBindingIdResolvesNoExecutor()
        {
            var registry = new WotProtocolBinderRegistry(
                [new StubBinder("stub.g", "1.0")],
                [new NamedExecutor(new WotBindingIdentity("stub.h", "1.0", "urn:x"))]);

            Assert.That(
                registry.TryGetExecutor(
                    new WotBindingIdentity("stub.g", "1.0", "urn:x"), out _),
                Is.False);
        }

        /// <summary>
        /// An HTTP form is validated against the address a request is actually
        /// sent to.
        /// </summary>
        /// <remarks>
        /// For HTTP the request goes to the addressing target, not to the
        /// endpoint's base URI: those differ whenever the form's href carries a
        /// path or a different host, and validating the base URI would approve
        /// a request to somewhere the policy refuses. The metadata address here
        /// is the canonical example - it is the one a server-side request
        /// forgery is aimed at.
        /// </remarks>
        [Test]
        public void HttpValidationUsesTheAddressingTargetNotTheBaseUri()
        {
            var executor = new NamedExecutor(new HttpBindingPlanner().Identity);
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()], [executor]);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await registry.OpenChannelAsync(HttpForm(
                    baseUri: "http://example.com",
                    target: "http://169.254.169.254/latest/meta-data/")).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(
                    executor.Activations,
                    Is.Zero,
                    "The refusal happens before the executor is asked for a channel.");
            });
        }

        /// <summary>
        /// The converse: a base URI the policy refuses does not condemn a form
        /// whose request goes somewhere allowed, because for HTTP the base URI
        /// is not the address anything is sent to.
        /// </summary>
        [Test]
        public async Task HttpValidationIgnoresABaseUriThatIsNotTheRequestTarget()
        {
            var executor = new NamedExecutor(new HttpBindingPlanner().Identity);
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()], [executor]);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(HttpForm(
                baseUri: "http://169.254.169.254",
                target: "http://example.com/p")).ConfigureAwait(false);

            Assert.That(executor.Activations, Is.EqualTo(1));
        }

        private static WotBindingPlanRequest Request()
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"T\",\"properties\":{\"p\":{\"forms\":[{" +
                "\"href\":\"stub://example.com/p\",\"op\":\"readproperty\"}]}}}";
            return WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td));
        }

        private static WotCompiledForm HttpForm(string baseUri, string target)
        {
            return new WotCompiledForm(
                new HttpBindingPlanner().Identity,
                WotAffordanceKind.Property,
                "p",
                "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty,
                "readproperty",
                new WotEndpointDescriptor("http", "h", 80, baseUri),
                new WotAddressingDescriptor(target),
                new WotOperationDescriptor(
                    WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                new WotPayloadDescriptor("application/json", "json"),
                [],
                isExecutable: true);
        }

        /// <summary>
        /// A binder that matches every form at one fixed priority, so a tie is
        /// the only thing that can decide between two of them.
        /// </summary>
        private sealed class StubBinder : WotProtocolBinderBase
        {
            public StubBinder(string id, string version, bool withError = false)
            {
                Identity = new WotBindingIdentity(id, version, "urn:stub:" + id, id);
                Capability = new WotBindingCapability(
                    "urn:stub:" + id,
                    id,
                    WotBindingSources.Http,
                    [WoTBindingCapabilityEnum.ReadProperty],
                    ["application/json"],
                    isExecutable: false);
                m_withError = withError;
            }

            public override WotBindingIdentity Identity { get; }

            public override WotBindingCapability Capability { get; }

            protected override IReadOnlyCollection<string> Schemes { get; } = ["stub"];

            public override WotBindingMatch Match(
                WotAffordanceForm form, WotBindingSelectionContext context)
            {
                return WotBindingMatch.Match(WotBindingMatchKind.Scheme);
            }

            public override WotBindingCompilation Compile(
                WotAffordanceForm form, WotBindingPlanContext context)
            {
                var entry = new WotCompiledForm(
                    Identity,
                    form.Kind,
                    form.AffordanceName,
                    form.JsonPointer,
                    WoTBindingCapabilityEnum.ReadProperty,
                    "readproperty",
                    new WotEndpointDescriptor("stub", "example.com", -1, "stub://example.com"),
                    new WotAddressingDescriptor("stub://example.com/p"),
                    new WotOperationDescriptor(
                        WoTBindingCapabilityEnum.ReadProperty, "readproperty", string.Empty),
                    new WotPayloadDescriptor("application/json", "json"),
                    [],
                    isExecutable: false);
                ImmutableArray<WotBindingDiagnostic> diagnostics = m_withError
                    ? [WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidFieldValue,
                        "The stub binder reports a defect it also claims to have compiled.",
                        form.JsonPointer)]
                    : [];
                return WotBindingCompilation.Supported([entry], diagnostics);
            }

            private readonly bool m_withError;
        }

        private sealed class NamedExecutor : IWotBindingExecutor
        {
            public NamedExecutor(WotBindingIdentity identity)
            {
                Identity = identity;
            }

            public WotBindingIdentity Identity { get; }

            public int Activations { get; private set; }

            public bool CanExecute(WotCompiledForm form)
            {
                return string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);
            }

            public ValueTask<IWotBindingChannel> ActivateAsync(
                WotCompiledForm form,
                WotExecutorContext context,
                CancellationToken cancellationToken = default)
            {
                Activations++;
                return new ValueTask<IWotBindingChannel>(new NullChannel(form));
            }
        }

        private sealed class NullChannel : IWotBindingChannel
        {
            public NullChannel(WotCompiledForm form)
            {
                Form = form;
            }

            public WotCompiledForm Form { get; }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            public ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotReadResult>(
                    new WotReadResult(StatusCodes.Good, new DataValue(Variant.Null)));
            }

            public ValueTask<WotWriteResult> WriteAsync(
                DataValue value, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotWriteResult>(new WotWriteResult(StatusCodes.Good));
            }

            public ValueTask<WotInvokeResult> InvokeAsync(
                IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
            }

            public ValueTask<IWotSubscription> ObserveAsync(
                Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<IWotSubscription> SubscribeEventAsync(
                Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}