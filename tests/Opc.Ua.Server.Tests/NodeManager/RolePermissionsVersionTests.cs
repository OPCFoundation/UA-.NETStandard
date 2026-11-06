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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies which role permission writes retire the cached permission verdicts.
    /// </summary>
    /// <remarks>
    /// The version is process wide, so the fixture does not run alongside other tests.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class RolePermissionsVersionTests
    {
        /// <summary>
        /// Verifies that a new event copies its source's role permissions without retiring
        /// the cached verdicts of the server.
        /// </summary>
        [Test]
        public void InitializingEventFromSourceKeepsVersion()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = ServiceMessageContext.Create(telemetry).NamespaceUris
            };
            BaseObjectState source = CreateSource();
            long version = NodeState.RolePermissionsVersion;

            var e = new BaseEventState(null);
            e.Initialize(context, source, EventSeverity.Medium, new LocalizedText("event"));

            Assert.That(NodeState.RolePermissionsVersion, Is.EqualTo(version));
            Assert.That(e.SourceNode!.RolePermissions, Is.EqualTo(source.RolePermissions));
            Assert.That(e.SourceNode.UserRolePermissions, Is.EqualTo(source.UserRolePermissions));
        }

        /// <summary>
        /// Verifies that a copy takes the role permissions of its original without retiring
        /// the cached verdicts of the server.
        /// </summary>
        [Test]
        public void CloningNodeKeepsVersion()
        {
            BaseObjectState source = CreateSource();
            long version = NodeState.RolePermissionsVersion;

            var copy = (BaseObjectState)source.Clone();

            Assert.That(NodeState.RolePermissionsVersion, Is.EqualTo(version));
            Assert.That(copy.RolePermissions, Is.EqualTo(source.RolePermissions));
            Assert.That(copy.UserRolePermissions, Is.EqualTo(source.UserRolePermissions));
        }

        /// <summary>
        /// Verifies that changing the role permissions of a node still retires the cached
        /// verdicts.
        /// </summary>
        [Test]
        public void ChangingRolePermissionsMovesVersion()
        {
            BaseObjectState source = CreateSource();
            long version = NodeState.RolePermissionsVersion;
            source.RolePermissions = [new RolePermissionType
            {
                RoleId = ObjectIds.WellKnownRole_Operator,
                Permissions = (uint)PermissionType.ReceiveEvents
            }];
            Assert.That(NodeState.RolePermissionsVersion, Is.GreaterThan(version));

            version = NodeState.RolePermissionsVersion;
            source.UserRolePermissions = default;
            Assert.That(NodeState.RolePermissionsVersion, Is.GreaterThan(version));
        }

        private static BaseObjectState CreateSource()
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId("source", 1),
                BrowseName = new QualifiedName("source", 1),
                RolePermissions = [new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Observer,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.ReceiveEvents)
                }],
                UserRolePermissions = [new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Observer,
                    Permissions = (uint)PermissionType.ReceiveEvents
                }]
            };
        }
    }
}
