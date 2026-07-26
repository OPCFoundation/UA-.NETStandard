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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WotProtocolBinderRegistry"/>: plan preparation,
    /// target-mapping validation, activate / deactivate lifecycle and executor
    /// resolution.
    /// </summary>
    [TestFixture]
    public sealed class WotProtocolBinderRegistryTests
    {
        private static WotBindingPlanRequest HttpRequest(string propertiesJson)
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "\"title\":\"T\",\"properties\":{\"p\":{" + propertiesJson + "}}}";
            return WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td));
        }

        [Test]
        public void PrepareWithNullRequestThrows()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            Assert.Throws<ArgumentNullException>(() => registry.Prepare(null!));
        }

        [Test]
        public void PrepareWithNoFormsReturnsEmptyPlan()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var request = new WotBindingPlanRequest("xid", WoTDocumentKindEnum.ThingDescription, []);

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.CompiledForms.IsEmpty, Is.True);
            Assert.That(plan.UnsupportedForms.IsEmpty, Is.True);
        }

        [Test]
        public void PrepareWithNoMatchingBinderYieldsUnsupportedForm()
        {
            var registry = new WotProtocolBinderRegistry([]);
            WotBindingPlanRequest request = HttpRequest(
                "\"type\":\"number\",\"forms\":[{\"href\":\"http://example.com/p\"}]");

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
        }

        [Test]
        public void PrepareWithNoExecutorProducesNonExecutableForm()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            WotBindingPlanRequest request = HttpRequest(
                "\"type\":\"number\",\"forms\":[{\"href\":\"http://example.com/p\"}]");

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.CompiledForms, Is.Not.Empty);
            Assert.That(plan.CompiledForms.All(f => !f.IsExecutable), Is.True);
            Assert.That(plan.HasNonExecutableForms, Is.True);
        }

        [Test]
        public void PrepareTargetMappingAuthoredOnFormIsRejected()
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"T\"," +
                "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{" +
                "\"href\":\"http://example.com/p\",\"uav:mapToNodeId\":\"ns=2;i=1\"}]}}}";
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var request = WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td));

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.TargetMappingAuthoredOnForm), Is.True);
        }

        [Test]
        public void PrepareTargetMappingOnActionAffordanceIsRejected()
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"T\"," +
                "\"actions\":{\"reset\":{\"uav:mapToNodeId\":\"ns=2;i=1\"," +
                "\"forms\":[{\"href\":\"http://example.com/reset\"}]}}}";
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var request = WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td));

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.TargetMappingNotOnProperty), Is.True);
        }

        [Test]
        public void PrepareTargetMappingFieldPathWithoutTypeIsRejected()
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"T\"," +
                "\"properties\":{\"p\":{\"uav:mapByFieldPath\":\"Value.Temperature\"," +
                "\"forms\":[{\"href\":\"http://example.com/p\"}]}}}";
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var request = WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td));

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.UnsupportedForms, Is.Not.Empty);
            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.TargetMappingFieldPathRequiresType), Is.True);
        }

        [Test]
        public void PrepareValidTargetMappingIsAccepted()
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"T\"," +
                "\"properties\":{\"p\":{\"uav:mapToNodeId\":\"ns=2;i=1000\"," +
                "\"uav:mapToType\":\"ns=1;i=5003\",\"uav:mapByFieldPath\":\"Body.Value\"," +
                "\"forms\":[{\"href\":\"http://example.com/p\"}]}}}";
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var request = WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td));

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.TargetMappingFieldPathRequiresType ||
                d.Code == WotBindingDiagnosticCode.TargetMappingAuthoredOnForm), Is.False);
        }

        [Test]
        public async Task ActivateAndDeactivateTracksActivePlan()
        {
            var registry = new WotProtocolBinderRegistry([]);
            var plan = WotBindingPlan.Empty;

            await registry.ActivateAsync(plan).ConfigureAwait(false);
            Assert.That(registry.IsActive(plan.ResourceXid), Is.True);

            await registry.DeactivateAsync(plan).ConfigureAwait(false);
            Assert.That(registry.IsActive(plan.ResourceXid), Is.False);
        }

        [Test]
        public async Task ActivateNullThrows()
        {
            var registry = new WotProtocolBinderRegistry([]);
            await Task.CompletedTask.ConfigureAwait(false);
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await registry.ActivateAsync(null!).ConfigureAwait(false));
        }

        [Test]
        public async Task DeactivateNullThrows()
        {
            var registry = new WotProtocolBinderRegistry([]);
            await Task.CompletedTask.ConfigureAwait(false);
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await registry.DeactivateAsync(null!).ConfigureAwait(false));
        }

        [Test]
        public void TryGetExecutorReturnsFalseWhenNoneRegistered()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var identity = new HttpBindingPlanner().Identity;

            bool found = registry.TryGetExecutor(identity, out _);

            Assert.That(found, Is.False);
        }

        [Test]
        public void TryGetExecutorNullIdentityThrows()
        {
            var registry = new WotProtocolBinderRegistry([]);
            Assert.Throws<ArgumentNullException>(() => registry.TryGetExecutor(null!, out _));
        }

        [Test]
        public void OpenChannelAsyncWithoutExecutorThrows()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var form = new WotCompiledForm(
                new WotBindingIdentity("w3c.http", "1.1", "urn:x"),
                WotAffordanceKind.Property, "p", "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty, "readproperty",
                new WotEndpointDescriptor("http", "h", 80, "http://h"),
                new WotAddressingDescriptor("path"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "GET"),
                new WotPayloadDescriptor("application/json", "json"),
                [], isExecutable: false);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await registry.OpenChannelAsync(form).ConfigureAwait(false));
        }

        [Test]
        public void OpenChannelAsyncWithNullFormThrows()
        {
            var registry = new WotProtocolBinderRegistry([]);
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await registry.OpenChannelAsync(null!).ConfigureAwait(false));
        }

        [Test]
        public void RegistryConstructorIgnoresNullBinders()
        {
            var registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { null!, new HttpBindingPlanner() });

            Assert.That(registry.Binders, Has.Count.EqualTo(1));
        }

        [Test]
        public void RegistryConstructorDeduplicatesSameIdAndVersion()
        {
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner(), new HttpBindingPlanner()]);

            Assert.That(registry.Binders, Has.Count.EqualTo(1));
        }

        [Test]
        public void RegistryBindersAreInDeterministicOrdinalOrder()
        {
            var registry = new WotProtocolBinderRegistry(WotBuiltInBinders.CreateAll());

            var keys = registry.Binders.Select(b => b.Identity.Key).ToList();
            var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.That(keys, Is.EqualTo(sorted));
        }

        [Test]
        public void RegistryCapabilitiesArePopulated()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            Assert.That(registry.Capabilities, Has.Count.EqualTo(1));
        }

        [Test]
        public void RegistryConstructorNullBindersThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new WotProtocolBinderRegistry(null!));
        }

        [Test]
        public void PrepareFullySupported()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            WotBindingPlanRequest request = HttpRequest(
                "\"type\":\"number\",\"forms\":[{\"href\":\"http://example.com/p\"}]");

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.FullySupported, Is.True);
        }

        [Test]
        public void PrepareResourceXidIsPreservedInPlan()
        {
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            WotBindingPlanRequest request = HttpRequest(
                "\"type\":\"number\",\"forms\":[{\"href\":\"http://example.com/p\"}]");

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.ResourceXid, Is.EqualTo("xid"));
        }

        [Test]
        public void PrepareTargetMappingPresentOnEventAffordanceIsRejected()
        {
            string td =
                "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"T\"," +
                "\"events\":{\"alarm\":{\"uav:mapToNodeId\":\"ns=2;i=1\"," +
                "\"forms\":[{\"href\":\"http://example.com/alarm\"}]}}}";
            var registry = new WotProtocolBinderRegistry([new HttpBindingPlanner()]);
            var request = WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td));

            WotBindingPlan plan = registry.Prepare(request);

            Assert.That(plan.Diagnostics.Any(d =>
                d.Code == WotBindingDiagnosticCode.TargetMappingNotOnProperty), Is.True);
        }
    }
}
