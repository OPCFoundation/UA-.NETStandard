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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Covers role-based system identities independently of routing-table list positions.
    /// </summary>
    [TestFixture]
    public sealed class NodeManagerSystemSlotTests
    {
        /// <summary>
        /// System access follows the exact slot's current generation, including withdrawal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SystemAccessorsFollowTheirCurrentSlot(bool core)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, []))
            {
                Func<IAsyncNodeManager?> read = core
                    ? () => master.CoreNodeManager
                    : () => master.ConfigurationNodeManager;
                IAsyncNodeManager original = read()!;
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(original);
                var configuration = new Mock<IConfigurationNodeManager>();
                configuration.As<IDiagnosticsNodeManager>();
                IAsyncNodeManager replacement = core
                    ? new Mock<ICoreNodeManager>().Object
                    : configuration.Object;
                var next = new NodeManagerGeneration(
                    generation.Slot, generation.Number + 1, replacement, NodeManagerGenerationState.Live);
                try
                {
                    generation.Slot.Unpublish(generation);
                    Assert.That(read(), Is.Null);
                    if (!core)
                    {
                        Assert.That(master.DiagnosticsNodeManager, Is.Null);
                    }
                    generation.Slot.Publish(next);
                    Assert.That(read(), Is.SameAs(replacement));
                    if (!core)
                    {
                        Assert.That(master.DiagnosticsNodeManager, Is.SameAs(replacement));
                    }
                }
                finally
                {
                    generation.Slot.Publish(generation);
                }
            }
        }
    }
}
