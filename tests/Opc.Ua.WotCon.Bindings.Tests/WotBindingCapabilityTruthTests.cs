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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// A Client reads a binding's advertised <c>Capabilities</c> as what it can
    /// do through that binding. A binder that validates and compiles plans but
    /// has no runtime executor can do none of them, so it must not advertise
    /// any: a promise that fails only when a Client tries is worse than an
    /// absent one.
    /// </summary>
    [TestFixture]
    public sealed class WotBindingCapabilityTruthTests
    {
        [Test]
        public void APlannerOnlyBinderAdvertisesNoOperation()
        {
            WotBindingCapability capability = Capability(isExecutable: false);

            WoTBindingCapabilityDataType projected = capability.ToDataType();

            Assert.Multiple(() =>
            {
                Assert.That(projected.Capabilities.Count, Is.Zero);
                Assert.That(
                    projected.DraftMaturity,
                    Does.EndWith(WotBindingCapability.PlannerOnlySuffix),
                    "The empty list has to read as a statement rather than as a " +
                    "binding that happens to declare nothing.");
                Assert.That(projected.BindingUri, Is.EqualTo("http://example.com/binding"));
                Assert.That(projected.Title, Is.EqualTo("Example"));
            });
        }

        [Test]
        public void AnExecutableBinderAdvertisesItsOperations()
        {
            WotBindingCapability capability = Capability(isExecutable: true);

            WoTBindingCapabilityDataType projected = capability.ToDataType();

            Assert.Multiple(() =>
            {
                Assert.That(projected.Capabilities.Count, Is.EqualTo(2));
                Assert.That(
                    projected.DraftMaturity,
                    Does.Not.Contain(WotBindingCapability.PlannerOnlySuffix));
            });
        }

        /// <summary>
        /// A binder executable in principle still advertises nothing where the
        /// host registered no executor for it: the capability describes the
        /// host a Client is talking to, not the binder's ambitions.
        /// </summary>
        [Test]
        public void AnExecutableBinderWithoutAnExecutorAdvertisesNoOperation()
        {
            WotBindingCapability capability = Capability(isExecutable: true);

            WoTBindingCapabilityDataType projected =
                capability.ToDataType(executorPresent: false);

            Assert.Multiple(() =>
            {
                Assert.That(projected.Capabilities.Count, Is.Zero);
                Assert.That(
                    projected.DraftMaturity,
                    Does.EndWith(WotBindingCapability.PlannerOnlySuffix));
            });
        }

        /// <summary>
        /// The registry advertises what this host can do. A registry built with
        /// binders and no executors advertises no operation at all, however
        /// many binders it holds.
        /// </summary>
        [Test]
        public void TheRegistryAdvertisesNothingExecutableWithoutExecutors()
        {
            var registry = new WotProtocolBinderRegistry(AllBinders());

            Assert.Multiple(() =>
            {
                Assert.That(registry.Capabilities, Is.Not.Empty);
                foreach (WoTBindingCapabilityDataType capability in registry.Capabilities)
                {
                    Assert.That(
                        capability.Capabilities.Count,
                        Is.Zero,
                        capability.BindingUri);
                    Assert.That(
                        capability.DraftMaturity,
                        Does.EndWith(WotBindingCapability.PlannerOnlySuffix),
                        capability.BindingUri);
                }
            });
        }

        /// <summary>
        /// The binders this build ships that have no executor at all stay
        /// planner-only however the host is composed, so no arrangement of
        /// executors can make them advertise an operation.
        /// </summary>
        [Test]
        public void ThePlannerOnlyBindersAreExactlyTheOnesRecorded()
        {
            List<string> plannerOnly = [.. AllBinders()
                .Where(b => !b.Capability.IsExecutable)
                .Select(b => b.Identity.Id)
                .OrderBy(id => id, System.StringComparer.Ordinal)];

            Assert.That(
                plannerOnly,
                Is.EqualTo(s_plannerOnlyBinderIds).AsCollection);
        }

        private static List<IWotProtocolBinder> AllBinders()
        {
            return [.. WotBuiltInBinders.CreateAll()];
        }

        private static WotBindingCapability Capability(bool isExecutable)
        {
            return new WotBindingCapability(
                "http://example.com/binding",
                "Example",
                new WotBindingSource(
                    "http://example.com/spec", "1.0", WotBindingMaturity.WorkingDraft),
                [WoTBindingCapabilityEnum.ReadProperty, WoTBindingCapabilityEnum.WriteProperty],
                ["application/json"],
                isExecutable);
        }

        /// <summary>
        /// The binders this build ships without an executor, in ascending
        /// ordinal order.
        /// </summary>
        private static readonly string[] s_plannerOnlyBinderIds =
            ["w3c.bacnet", "w3c.coap", "w3c.lorawan", "w3c.profinet"];
    }
}