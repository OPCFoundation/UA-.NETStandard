/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for the asynchronous change-notification API on <see cref="NodeState"/>:
    /// <see cref="NodeState.ClearChangeMasksAsync"/> / <see cref="NodeState.OnStateChangedAsync"/>
    /// and <see cref="NodeState.ReportEventAsync"/> / <see cref="NodeState.OnReportEventAsync"/>,
    /// including that the synchronous entry points still drive the asynchronous sinks.
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateChangeNotificationTests
    {
        private ITelemetryContext m_telemetry;
        private ServiceMessageContext m_messageContext;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (m_messageContext as System.IDisposable)?.Dispose();
        }

        private SystemContext CreateSystemContext()
        {
            return new SystemContext(m_telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                TypeTable = new TypeTable(m_messageContext.NamespaceUris)
            };
        }

        private static BaseDataVariableState CreateVariable()
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Var", 0),
                BrowseName = new QualifiedName("Var", 0),
                DisplayName = new LocalizedText("Var"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite
            };
        }

        [Test]
        public async Task ClearChangeMasksAsyncInvokesAsyncAndSyncSinks()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();

            // Flush any change masks set while constructing the node so the assertion below
            // observes only the mask raised by this test.
            v.ClearChangeMasks(ctx, includeChildren: false);

            NodeStateChangeMasks syncMask = NodeStateChangeMasks.None;
            NodeStateChangeMasks asyncMask = NodeStateChangeMasks.None;

            v.OnStateChanged = (c, n, mask) => syncMask = mask;
            v.OnStateChangedAsync = (c, n, mask, ct) =>
            {
                asyncMask = mask;
                return default;
            };

            v.UpdateChangeMasks(NodeStateChangeMasks.Value);
            await v.ClearChangeMasksAsync(ctx, includeChildren: false).ConfigureAwait(false);

            Assert.That(syncMask, Is.EqualTo(NodeStateChangeMasks.Value));
            Assert.That(asyncMask, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        [Test]
        public void ClearChangeMasksSyncDrivesAsyncSink()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();

            // Flush any change masks set while constructing the node.
            v.ClearChangeMasks(ctx, includeChildren: false);

            NodeStateChangeMasks asyncMask = NodeStateChangeMasks.None;
            v.OnStateChangedAsync = (c, n, mask, ct) =>
            {
                asyncMask = mask;
                return default;
            };

            v.UpdateChangeMasks(NodeStateChangeMasks.Value);
            v.ClearChangeMasks(ctx, includeChildren: false);

            Assert.That(asyncMask, Is.EqualTo(NodeStateChangeMasks.Value));
        }

        [Test]
        public async Task ClearChangeMasksAsyncAwaitsStateChangedAsyncEvent()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();

            bool invoked = false;
            v.StateChangedAsync += (c, n, mask, ct) =>
            {
                invoked = true;
                return default;
            };

            v.UpdateChangeMasks(NodeStateChangeMasks.Value);
            await v.ClearChangeMasksAsync(ctx, includeChildren: false).ConfigureAwait(false);

            Assert.That(invoked, Is.True);
        }

        [Test]
        public async Task ReportEventAsyncInvokesAsyncAndSyncSinks()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();
            var target = new BaseObjectState(null);

            IFilterTarget syncTarget = null;
            IFilterTarget asyncTarget = null;

            v.OnReportEvent = (c, n, e) => syncTarget = e;
            v.OnReportEventAsync = (c, n, e, ct) =>
            {
                asyncTarget = e;
                return default;
            };

            await v.ReportEventAsync(ctx, target).ConfigureAwait(false);

            Assert.That(syncTarget, Is.SameAs(target));
            Assert.That(asyncTarget, Is.SameAs(target));
        }

        [Test]
        public void ReportEventSyncDrivesAsyncSink()
        {
            SystemContext ctx = CreateSystemContext();
            BaseDataVariableState v = CreateVariable();
            var target = new BaseObjectState(null);

            IFilterTarget asyncTarget = null;
            v.OnReportEventAsync = (c, n, e, ct) =>
            {
                asyncTarget = e;
                return default;
            };

            v.ReportEvent(ctx, target);

            Assert.That(asyncTarget, Is.SameAs(target));
        }
    }
}
