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
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotRegistryNodeManagerTests
    {
        [Test]
        public async Task ReconcileQueuePreservesCreateThenDeleteWhileOccupied()
        {
            TaskCompletionSource<bool> entered = NewSignal();
            TaskCompletionSource<bool> release = NewSignal();
            var generations = new List<long>();
            var interactions = new List<string>();
            bool resourceProjected = false;
            using var queue = new WotRegistryReconcileQueue(async change =>
            {
                generations.Add(change.Current.Generation);
                if (change.Current.Generation == 1)
                {
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                }

                bool existed = change.Previous.FindResource("g", "r") is not null;
                bool exists = change.Current.FindResource("g", "r") is not null;
                if (!existed && exists)
                {
                    interactions.Add("create");
                }
                else if (existed && !exists)
                {
                    interactions.Add("delete");
                }
                resourceProjected = exists;
            });

            WotRegistrySnapshot empty0 = Snapshot(0, hasResource: false);
            WotRegistrySnapshot empty1 = Snapshot(1, hasResource: false);
            WotRegistrySnapshot created2 = Snapshot(2, hasResource: true);
            WotRegistrySnapshot deleted3 = Snapshot(3, hasResource: false);
            queue.Enqueue(Change(empty0, empty1));
            await entered.Task.ConfigureAwait(false);

            queue.Enqueue(Change(empty1, created2));
            queue.Enqueue(Change(created2, deleted3));
            release.SetResult(true);
            await queue.WhenIdleAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(generations, Is.EqualTo(s_expectedGenerations));
                Assert.That(interactions, Is.EqualTo(s_expectedInteractions));
                Assert.That(resourceProjected, Is.False);
            });
        }

        private static WotRegistryChangedEventArgs Change(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot current)
        {
            return new WotRegistryChangedEventArgs(
                previous,
                current,
                ["/groups/g/resources/r"],
                projectionOnly: false);
        }

        private static WotRegistrySnapshot Snapshot(long generation, bool hasResource)
        {
            ImmutableDictionary<string, WotResourceGroup> groups =
                ImmutableDictionary<string, WotResourceGroup>.Empty;
            if (hasResource)
            {
                WotResourceVersion version = WotResourceVersion.CreatePlaceholder(
                    "v1",
                    s_unixEpoch);
                var resource = new WotResource(
                    "g",
                    "r",
                    WoTDocumentKindEnum.ThingDescription,
                    [version],
                    defaultVersionId: "v1",
                    epoch: 1);
                var group = new WotResourceGroup(
                    "g",
                    WoTDocumentKindEnum.ThingDescription,
                    ImmutableDictionary<string, WotResource>.Empty.Add("r", resource),
                    epoch: 1);
                groups = groups.Add("g", group);
            }
            return new WotRegistrySnapshot(generation, groups);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static readonly DateTime s_unixEpoch =
            new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly long[] s_expectedGenerations = [1, 2, 3];
        private static readonly string[] s_expectedInteractions = ["create", "delete"];
    }
}
