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
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class ServerResourceIsolationOptionsTests
    {
        [Test]
        public void DefaultPlanReservesOnePooledMessagePerProtectedClass()
        {
            var options = new ServerResourceIsolationOptions();
            var budget = new ChunkReassemblyBudget(600, 300);

            ServerResourceIsolationPlan plan = options.CreatePlan(budget, 2, 3, 2, 50);

            Assert.That(options.Mode, Is.EqualTo(ServerResourceIsolationMode.Balanced));
            Assert.That(options.BootstrapReservedBytes, Is.Null);
            Assert.That(options.ReconnectReservedBytes, Is.Null);
            Assert.That(plan.Mode, Is.EqualTo(ServerResourceIsolationMode.Balanced));
            Assert.That(plan.MaxRetainedMessageBytes, Is.EqualTo(100));
            Assert.That(plan.BootstrapReservedBytes, Is.EqualTo(100));
            Assert.That(plan.ReconnectReservedBytes, Is.EqualTo(100));
            Assert.That(plan.ReservedReassemblyBytes, Is.EqualTo(200));
            Assert.That(plan.UnreservedReassemblyBytes, Is.EqualTo(400));
            Assert.That(plan.SecureChannelHeadroom, Is.EqualTo(1));
        }

        [TestCase(ServerResourceIsolationMode.SharedOnly)]
        [TestCase(ServerResourceIsolationMode.FairShare)]
        [TestCase(ServerResourceIsolationMode.Balanced)]
        public void ProfileSelectionIsIndependentBetweenOptionsInstances(ServerResourceIsolationMode mode)
        {
            var defaults = new ServerResourceIsolationOptions();
            var options = new ServerResourceIsolationOptions { Mode = mode };
            var budget = new ChunkReassemblyBudget(300, 100);

            ServerResourceIsolationPlan plan = options.CreatePlan(budget, 1, 2, 2, 50);
            ServerResourceIsolationPlan defaultPlan = defaults.CreatePlan(budget, 1, 2, 2, 50);

            Assert.That(plan.Mode, Is.EqualTo(mode));
            Assert.That(
                plan.BootstrapReservedBytes, Is.EqualTo(mode == ServerResourceIsolationMode.Balanced ? 100 : 0));
            Assert.That(
                plan.ReconnectReservedBytes, Is.EqualTo(mode == ServerResourceIsolationMode.Balanced ? 100 : 0));
            Assert.That(
                plan.UnreservedReassemblyBytes, Is.EqualTo(mode == ServerResourceIsolationMode.Balanced ? 100 : 300));
            Assert.That(defaultPlan.Mode, Is.EqualTo(ServerResourceIsolationMode.Balanced));
            Assert.That(defaultPlan.BootstrapReservedBytes, Is.EqualTo(100));
            Assert.That(defaultPlan.ReconnectReservedBytes, Is.EqualTo(100));
        }

        [TestCase(ServerResourceIsolationMode.SharedOnly, 0)]
        [TestCase(ServerResourceIsolationMode.SharedOnly, 100)]
        [TestCase(ServerResourceIsolationMode.FairShare, 0)]
        [TestCase(ServerResourceIsolationMode.FairShare, 100)]
        public void UnreservedProfilesPreserveLegacyThresholdExtremes(ServerResourceIsolationMode mode, long threshold)
        {
            var options = new ServerResourceIsolationOptions
            {
                Mode = mode,
                BootstrapReservedBytes = 0,
                ReconnectReservedBytes = 0
            };

            ServerResourceIsolationPlan plan = options.CreatePlan(
                new ChunkReassemblyBudget(100, threshold), 1, 2, 2, 50);

            Assert.That(plan.MaxBytesWithoutSession, Is.EqualTo(threshold));
            Assert.That(plan.ReservedReassemblyBytes, Is.Zero);
            Assert.That(plan.UnreservedReassemblyBytes, Is.EqualTo(100));
        }

        [Test]
        public void PlanningPreservesReferenceTotalsAndDoesNotReserveLegacyCapacity()
        {
            const long total = 64L * 1024 * 1024;
            const long threshold = 32L * 1024 * 1024;
            var budget = ChunkReassemblyBudget.CreateDefault(new EndpointConfiguration { MaxMessageSize = 4194304 });
            Assert.That(budget.TryReserve(total, true), Is.True);

            ServerResourceIsolationPlan plan = new ServerResourceIsolationOptions().CreatePlan(
                budget, 75, 1000, 65, 65536);

            Assert.That(plan.MaxReassemblyBytes, Is.EqualTo(total));
            Assert.That(plan.MaxBytesWithoutSession, Is.EqualTo(threshold));
            Assert.That(plan.MaxSessionCount, Is.EqualTo(75));
            Assert.That(plan.MaxChannelCount, Is.EqualTo(1000));
            Assert.That(plan.SecureChannelHeadroom, Is.EqualTo(925));
            Assert.That(plan.MaxRetainedMessageBytes, Is.EqualTo(4259840));
            Assert.That(plan.ReservedReassemblyBytes, Is.EqualTo(8519680));
            Assert.That(plan.UnreservedReassemblyBytes, Is.EqualTo(58589184));
            Assert.That(plan.ReservedReassemblyBytes + plan.UnreservedReassemblyBytes, Is.EqualTo(total));
            Assert.That(budget.MaxBytes, Is.EqualTo(total));
            Assert.That(budget.MaxBytesWithoutSession, Is.EqualTo(threshold));
            Assert.That(budget.ReservedBytes, Is.EqualTo(total));
            budget.Release(total);
            Assert.That(budget.TryReserve(total, true), Is.True, "Planning must not install reserved partitions.");
            budget.Release(total);
        }

        [Test]
        public void ManualFloorsOverrideAutoIndependentlyAndPlanIsImmutable()
        {
            var budget = new ChunkReassemblyBudget(1000, 600);
            var options = new ServerResourceIsolationOptions { BootstrapReservedBytes = 250 };
            ServerResourceIsolationPlan bootstrapPlan = options.CreatePlan(budget, 1, 3, 2, 50);
            options.BootstrapReservedBytes = null;
            options.ReconnectReservedBytes = 350;
            ServerResourceIsolationPlan reconnectPlan = options.CreatePlan(budget, 1, 3, 2, 50);
            options.BootstrapReservedBytes = 400;
            ServerResourceIsolationPlan bothPlan = options.CreatePlan(budget, 1, 3, 2, 50);
            options.Mode = ServerResourceIsolationMode.SharedOnly;
            options.ReconnectReservedBytes = 0;

            Assert.That(bootstrapPlan.BootstrapReservedBytes, Is.EqualTo(250));
            Assert.That(bootstrapPlan.ReconnectReservedBytes, Is.EqualTo(100));
            Assert.That(bootstrapPlan.UnreservedReassemblyBytes, Is.EqualTo(650));
            Assert.That(reconnectPlan.BootstrapReservedBytes, Is.EqualTo(100));
            Assert.That(reconnectPlan.ReconnectReservedBytes, Is.EqualTo(350));
            Assert.That(reconnectPlan.UnreservedReassemblyBytes, Is.EqualTo(550));
            Assert.That(bothPlan.Mode, Is.EqualTo(ServerResourceIsolationMode.Balanced));
            Assert.That(bothPlan.BootstrapReservedBytes, Is.EqualTo(400));
            Assert.That(bothPlan.ReconnectReservedBytes, Is.EqualTo(350));
            Assert.That(bothPlan.UnreservedReassemblyBytes, Is.EqualTo(250));
        }

        [TestCase(0, 0)]
        [TestCase(-1, 0)]
        [TestCase(100, -1)]
        [TestCase(100, 101)]
        public void InvalidTotalsAreRejectedBeforePlanning(long total, long threshold)
        {
            Assert.That(
                () => new ServerResourceIsolationOptions().CreatePlan(
                    new ChunkReassemblyBudget(total, threshold), 1, 2, 1, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0, 2, 1, 1, "maxSessionCount")]
        [TestCase(-1, 2, 1, 1, "maxSessionCount")]
        [TestCase(1, 0, 1, 1, "maxChannelCount")]
        [TestCase(1, -1, 1, 1, "maxChannelCount")]
        [TestCase(1, 2, 0, 1, "maxRetainedChunkCount")]
        [TestCase(1, 2, -1, 1, "maxRetainedChunkCount")]
        [TestCase(1, 2, 1, 0, "maxPooledBufferLength")]
        [TestCase(1, 2, 1, -1, "maxPooledBufferLength")]
        public void NonpositiveBoundsAreRejected(
            int sessions,
            int channels,
            int chunks,
            int bufferLength,
            string parameterName)
        {
            Assert.That(
                () => new ServerResourceIsolationOptions().CreatePlan(
                    new ChunkReassemblyBudget(100), sessions, channels, chunks, bufferLength),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo(parameterName));
        }

        [TestCase(2, 1)]
        [TestCase(2, 2)]
        [TestCase(int.MaxValue, int.MaxValue)]
        public void ChannelCapacityRequiresNPlusOneWithoutOverflow(int sessions, int channels)
        {
            Assert.That(
                () => new ServerResourceIsolationOptions().CreatePlan(
                    new ChunkReassemblyBudget(100), sessions, channels, 1, 1),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("maxChannelCount"));
        }

        [Test]
        public void MaximumRepresentableChannelHeadroomIsAccepted()
        {
            ServerResourceIsolationPlan plan = new ServerResourceIsolationOptions().CreatePlan(
                new ChunkReassemblyBudget(3), int.MaxValue - 1, int.MaxValue, 1, 1);

            Assert.That(plan.MaxSessionCount, Is.EqualTo(int.MaxValue - 1));
            Assert.That(plan.MaxChannelCount, Is.EqualTo(int.MaxValue));
            Assert.That(plan.SecureChannelHeadroom, Is.EqualTo(1));
        }

        [Test]
        public void NullBudgetIsRejected()
        {
            Assert.That(
                () => new ServerResourceIsolationOptions().CreatePlan(null!, 1, 2, 1, 1),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("budget"));
        }

        [TestCase(-1)]
        [TestCase(4)]
        public void UnknownProfileIsRejected(int mode)
        {
            var options = new ServerResourceIsolationOptions { Mode = (ServerResourceIsolationMode)mode };

            Assert.That(
                () => options.CreatePlan(new ChunkReassemblyBudget(300), 1, 2, 1, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("mode"));
        }

        [Test]
        public void TrustedReservationsFailExplicitlyRatherThanDowngrade()
        {
            var options = new ServerResourceIsolationOptions { Mode = ServerResourceIsolationMode.TrustedReservations };

            Assert.That(
                () => options.CreatePlan(new ChunkReassemblyBudget(300), 1, 2, 1, 1),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("trusted-owner provisioning"));
        }

        [TestCase(ServerResourceIsolationMode.SharedOnly, true)]
        [TestCase(ServerResourceIsolationMode.SharedOnly, false)]
        [TestCase(ServerResourceIsolationMode.FairShare, true)]
        [TestCase(ServerResourceIsolationMode.FairShare, false)]
        public void UnreservedProfilesRejectNonzeroFloorOverrides(ServerResourceIsolationMode mode, bool bootstrap)
        {
            var options = new ServerResourceIsolationOptions
            {
                Mode = mode,
                BootstrapReservedBytes = bootstrap ? 100 : 0,
                ReconnectReservedBytes = bootstrap ? 0 : 100
            };

            Assert.That(
                () => options.CreatePlan(new ChunkReassemblyBudget(300), 1, 2, 2, 50),
                Throws.TypeOf<ArgumentException>());
        }

        [TestCase(-1, 100)]
        [TestCase(100, -1)]
        [TestCase(0, 100)]
        [TestCase(100, 0)]
        [TestCase(99, 100)]
        [TestCase(100, 99)]
        public void ProtectedFloorsCannotBeSmallerThanOnePooledMessage(long bootstrap, long reconnect)
        {
            var options = new ServerResourceIsolationOptions
            {
                BootstrapReservedBytes = bootstrap,
                ReconnectReservedBytes = reconnect
            };

            Assert.That(
                () => options.CreatePlan(new ChunkReassemblyBudget(300), 1, 2, 2, 50),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(201, 100)]
        [TestCase(100, 201)]
        [TestCase(301, 100)]
        [TestCase(long.MaxValue, long.MaxValue)]
        public void FloorSumsCannotExceedTotalOrWrap(long bootstrap, long reconnect)
        {
            var options = new ServerResourceIsolationOptions
            {
                BootstrapReservedBytes = bootstrap,
                ReconnectReservedBytes = reconnect
            };

            Assert.That(
                () => options.CreatePlan(new ChunkReassemblyBudget(300, 300), 1, 2, 2, 50),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("floors exceed"));
        }

        [TestCase(200)]
        [TestCase(299)]
        public void BalancedRequiresRoomForAnUnreservedMessage(long total)
        {
            Assert.That(
                () => new ServerResourceIsolationOptions().CreatePlan(
                    new ChunkReassemblyBudget(total, 100), 1, 2, 2, 50),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("unreserved capacity"));
        }

        [TestCase(0)]
        [TestCase(99)]
        public void BootstrapMustFitExistingSessionlessThreshold(long threshold)
        {
            Assert.That(
                () => new ServerResourceIsolationOptions().CreatePlan(
                    new ChunkReassemblyBudget(300, threshold), 1, 2, 2, 50),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("sessionless occupancy threshold"));
        }

        [Test]
        public void ExactFloorAndSharedMessageBoundaryIsAccepted()
        {
            ServerResourceIsolationPlan plan = new ServerResourceIsolationOptions().CreatePlan(
                new ChunkReassemblyBudget(300, 100), 1, 2, 2, 50);

            Assert.That(plan.BootstrapReservedBytes, Is.EqualTo(100));
            Assert.That(plan.ReconnectReservedBytes, Is.EqualTo(100));
            Assert.That(plan.UnreservedReassemblyBytes, Is.EqualTo(100));
        }

        [TestCase(ServerResourceIsolationMode.SharedOnly)]
        [TestCase(ServerResourceIsolationMode.FairShare)]
        [TestCase(ServerResourceIsolationMode.Balanced)]
        public void ImpossiblePooledMessageFootprintIsRejected(ServerResourceIsolationMode mode)
        {
            var options = new ServerResourceIsolationOptions { Mode = mode };

            Assert.That(
                () => options.CreatePlan(new ChunkReassemblyBudget(99, 99), 1, 2, 2, 50),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("one maximum retained message"));
        }

        [Test]
        public void LargerBackingArraysChangeFloorsAndCanMakePlanImpossible()
        {
            var options = new ServerResourceIsolationOptions();
            var budget = new ChunkReassemblyBudget(600, 200);

            ServerResourceIsolationPlan smaller = options.CreatePlan(budget, 1, 2, 2, 50);
            ServerResourceIsolationPlan larger = options.CreatePlan(budget, 1, 2, 2, 100);

            Assert.That(smaller.MaxRetainedMessageBytes, Is.EqualTo(100));
            Assert.That(smaller.UnreservedReassemblyBytes, Is.EqualTo(400));
            Assert.That(larger.MaxRetainedMessageBytes, Is.EqualTo(200));
            Assert.That(larger.BootstrapReservedBytes, Is.EqualTo(200));
            Assert.That(larger.ReconnectReservedBytes, Is.EqualTo(200));
            Assert.That(larger.UnreservedReassemblyBytes, Is.EqualTo(200));
            Assert.That(
                () => options.CreatePlan(budget, 1, 2, 2, 101),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void FootprintMultiplicationAndFloorArithmeticUseFullLongRange()
        {
            var options = new ServerResourceIsolationOptions { Mode = ServerResourceIsolationMode.SharedOnly };
            var budget = new ChunkReassemblyBudget(long.MaxValue, long.MaxValue);

            ServerResourceIsolationPlan shared = options.CreatePlan(budget, 1, 2, int.MaxValue, int.MaxValue);

            Assert.That(shared.MaxRetainedMessageBytes, Is.EqualTo(4611686014132420609L));
            Assert.That(shared.MaxReassemblyBytes, Is.EqualTo(long.MaxValue));
            Assert.That(shared.UnreservedReassemblyBytes, Is.EqualTo(long.MaxValue));

            options.Mode = ServerResourceIsolationMode.Balanced;
            options.BootstrapReservedBytes = long.MaxValue - 2;
            options.ReconnectReservedBytes = 1;
            ServerResourceIsolationPlan balanced = options.CreatePlan(budget, 1, 2, 1, 1);

            Assert.That(balanced.ReservedReassemblyBytes, Is.EqualTo(long.MaxValue - 1));
            Assert.That(balanced.UnreservedReassemblyBytes, Is.EqualTo(1));
            options.BootstrapReservedBytes = long.MaxValue;
            Assert.That(
                () => options.CreatePlan(budget, 1, 2, 1, 1),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("floors exceed"));
        }
    }
}
