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
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;

namespace Opc.Ua.PubSub.Tests.Diagnostics
{
    /// <summary>
    /// Direct coverage for
    /// <see cref="AggregatingPubSubDiagnostics"/>: validates the
    /// component-resolver aggregation, level forwarding, and reset
    /// fan-out logic per Part 14 §9.1.11.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.11", Summary = "Aggregating PubSub diagnostics")]
    public class AggregatingPubSubDiagnosticsTests
    {
        [Test]
        public void ConstructorNullRootThrows()
        {
            Assert.That(
                () => new AggregatingPubSubDiagnostics(root: null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void LevelMirrorsRootAtConstruction()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            var agg = new AggregatingPubSubDiagnostics(root);
            Assert.That(agg.Level, Is.EqualTo(PubSubDiagnosticsLevel.High));
        }

        [Test]
        public void SetLevelUpdatesAggregateOnly()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var agg = new AggregatingPubSubDiagnostics(root);

            agg.SetLevel(PubSubDiagnosticsLevel.High);

            Assert.That(agg.Level, Is.EqualTo(PubSubDiagnosticsLevel.High));
            // The root level is the constructor-time value: aggregate should
            // not retroactively rewrite it.
            Assert.That(root.Level, Is.EqualTo(PubSubDiagnosticsLevel.Low));
        }

        [Test]
        public void IncrementForwardsToRoot()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var agg = new AggregatingPubSubDiagnostics(root);

            agg.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 3);
            agg.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages);

            Assert.That(
                root.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.EqualTo(4));
        }

        [Test]
        public void ReadSumsRootAndComponents()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var component = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var components = new List<IPubSubDiagnostics> { component };
            var agg = new AggregatingPubSubDiagnostics(root, () => components);

            root.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 5);
            component.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 7);

            Assert.That(
                agg.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.EqualTo(12));
        }

        [Test]
        public void ReadDoesNotDoubleCountIdenticalRootInComponents()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var components = new List<IPubSubDiagnostics> { root };
            var agg = new AggregatingPubSubDiagnostics(root, () => components);

            root.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 11);

            Assert.That(
                agg.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.EqualTo(11));
        }

        [Test]
        public void ReadWithNullComponentsResolverFallsBackToRootOnly()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var agg = new AggregatingPubSubDiagnostics(root, componentResolver: null);

            root.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages, 9);

            Assert.That(
                agg.Read(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages),
                Is.EqualTo(9));
        }

        [Test]
        public void RecordErrorForwardsToRoot()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            var agg = new AggregatingPubSubDiagnostics(root);

            agg.RecordError(StatusCodes.BadInvalidArgument, "boom");

            Assert.That(root.RecentErrors, Has.Count.EqualTo(1));
            Assert.That(
                root.RecentErrors[0].StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(root.RecentErrors[0].Message, Is.EqualTo("boom"));
        }

        [Test]
        public void ResetFansOutToRootAndComponents()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var component = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var components = new List<IPubSubDiagnostics> { component };
            var agg = new AggregatingPubSubDiagnostics(root, () => components);

            root.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 1);
            component.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 1);

            agg.Reset();

            Assert.That(
                root.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.Zero);
            Assert.That(
                component.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.Zero);
        }

        [Test]
        public void ResetWithRootInComponentsCallsResetOnlyOnce()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.High);
            var components = new List<IPubSubDiagnostics> { root };
            var agg = new AggregatingPubSubDiagnostics(root, () => components);

            root.RecordError(StatusCodes.BadInternalError, "first");
            root.RecordError(StatusCodes.BadInternalError, "second");

            agg.Reset();

            Assert.That(root.RecentErrors, Is.Empty);
        }

        [Test]
        public void ResolverReturningEmptyEnumerableIsHandled()
        {
            var root = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var agg = new AggregatingPubSubDiagnostics(
                root,
                () => Array.Empty<IPubSubDiagnostics>());

            root.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 4);

            Assert.That(
                agg.Read(PubSubDiagnosticsCounterKind.SentNetworkMessages),
                Is.EqualTo(4));
        }
    }
}
