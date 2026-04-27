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

using Opc.Ua;
using System;
using NUnit.Framework;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Opc.Ua.Client.Sessions.Obsolete
{
    [TestFixture]
    public class SessionClientTests
    {
        [Test]
        public void CreateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CreateSession(null, null!, null!,
                null!, null!, default, default, 0, 0, out _, out _, out _, out _, out _, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("CreateSession deprecated"));
        }

        [Test]
        public void BeginCreateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCreateSession(null, null!, null!, null!, null!, default, default, 0, 0, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginCreateSession deprecated"));
        }

        [Test]
        public void EndCreateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCreateSession(null!, out _, out _, out _, out _, out _, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndCreateSession deprecated"));
        }

        [Test]
        public void ActivateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.ActivateSession(null, null!, default, default, default, null!, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("ActivateSession deprecated"));
        }

        [Test]
        public void BeginActivateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginActivateSession(null, null!, default, default, default, null!, null, null);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginActivateSession deprecated"));
        }

        [Test]
        public void EndActivateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndActivateSession(null!, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndActivateSession deprecated"));
        }

        [Test]
        public void CloseSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CloseSession(null, false);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("CloseSession deprecated"));
        }

        [Test]
        public void BeginCloseSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCloseSession(null, false, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginCloseSession deprecated"));
        }

        [Test]
        public void EndCloseSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCloseSession(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndCloseSession deprecated"));
        }

        [Test]
        public void CancelShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Cancel(null, 0, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Cancel deprecated"));
        }

        [Test]
        public void BeginCancelShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCancel(null, 0, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginCancel deprecated"));
        }

        [Test]
        public void EndCancelShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCancel(null!, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndCancel deprecated"));
        }

        [Test]
        public void AddNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.AddNodes(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("AddNodes deprecated"));
        }

        [Test]
        public void BeginAddNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginAddNodes(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginAddNodes deprecated"));
        }

        [Test]
        public void EndAddNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndAddNodes(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndAddNodes deprecated"));
        }

        [Test]
        public void AddReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.AddReferences(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("AddReferences deprecated"));
        }

        [Test]
        public void BeginAddReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginAddReferences(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginAddReferences deprecated"));
        }

        [Test]
        public void EndAddReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndAddReferences(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndAddReferences deprecated"));
        }

        [Test]
        public void DeleteNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteNodes(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("DeleteNodes deprecated"));
        }

        [Test]
        public void BeginDeleteNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteNodes(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginDeleteNodes deprecated"));
        }

        [Test]
        public void EndDeleteNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteNodes(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndDeleteNodes deprecated"));
        }

        [Test]
        public void DeleteReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteReferences(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("DeleteReferences deprecated"));
        }

        [Test]
        public void BeginDeleteReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteReferences(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginDeleteReferences deprecated"));
        }

        [Test]
        public void EndDeleteReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteReferences(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndDeleteReferences deprecated"));
        }

        [Test]
        public void BrowseShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Browse(null, null!, 0, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Browse deprecated"));
        }

        [Test]
        public void BeginBrowseShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginBrowse(null, null!, 0, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginBrowse deprecated"));
        }

        [Test]
        public void EndBrowseShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndBrowse(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndBrowse deprecated"));
        }

        [Test]
        public void BrowseNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BrowseNext(null, false, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BrowseNext deprecated"));
        }

        [Test]
        public void BeginBrowseNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginBrowseNext(null, false, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginBrowseNext deprecated"));
        }

        [Test]
        public void EndBrowseNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndBrowseNext(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndBrowseNext deprecated"));
        }

        [Test]
        public void TranslateBrowsePathsToNodeIdsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.TranslateBrowsePathsToNodeIds(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("TranslateBrowsePathsToNodeIds deprecated"));
        }

        [Test]
        public void BeginTranslateBrowsePathsToNodeIdsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginTranslateBrowsePathsToNodeIds(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginTranslateBrowsePathsToNodeIds deprecated"));
        }

        [Test]
        public void EndTranslateBrowsePathsToNodeIdsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndTranslateBrowsePathsToNodeIds(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndTranslateBrowsePathsToNodeIds deprecated"));
        }

        [Test]
        public void RegisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.RegisterNodes(null, default, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("RegisterNodes deprecated"));
        }

        [Test]
        public void BeginRegisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginRegisterNodes(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginRegisterNodes deprecated"));
        }

        [Test]
        public void EndRegisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndRegisterNodes(null!, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndRegisterNodes deprecated"));
        }

        [Test]
        public void UnregisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.UnregisterNodes(null, default);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("UnregisterNodes deprecated"));
        }

        [Test]
        public void BeginUnregisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginUnregisterNodes(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginUnregisterNodes deprecated"));
        }

        [Test]
        public void EndUnregisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndUnregisterNodes(null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndUnregisterNodes deprecated"));
        }

        [Test]
        public void QueryFirstShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.QueryFirst(null, null!, default, null!, 0, 0, out _, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("QueryFirst deprecated"));
        }

        [Test]
        public void BeginQueryFirstShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginQueryFirst(null, null!, default, null!, 0, 0, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginQueryFirst deprecated"));
        }

        [Test]
        public void EndQueryFirstShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndQueryFirst(null!, out _, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndQueryFirst deprecated"));
        }

        [Test]
        public void QueryNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.QueryNext(null, false, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("QueryNext deprecated"));
        }

        [Test]
        public void BeginQueryNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginQueryNext(null, false, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginQueryNext deprecated"));
        }

        [Test]
        public void EndQueryNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndQueryNext(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndQueryNext deprecated"));
        }

        [Test]
        public void HistoryReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.HistoryRead(null, default, 0, false, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("HistoryRead deprecated"));
        }

        [Test]
        public void BeginHistoryReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginHistoryRead(null, default, 0, false, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginHistoryRead deprecated"));
        }

        [Test]
        public void EndHistoryReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndHistoryRead(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndHistoryRead deprecated"));
        }
        [Test]
        public void WriteShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Write(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Write deprecated"));
        }

        [Test]
        public void BeginWriteShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginWrite(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginWrite deprecated"));
        }

        [Test]
        public void EndWriteShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndWrite(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndWrite deprecated"));
        }

        [Test]
        public void HistoryUpdateShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.HistoryUpdate(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("HistoryUpdate deprecated"));
        }

        [Test]
        public void BeginHistoryUpdateShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginHistoryUpdate(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginHistoryUpdate deprecated"));
        }

        [Test]
        public void EndHistoryUpdateShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndHistoryUpdate(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndHistoryUpdate deprecated"));
        }

        [Test]
        public void CallShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Call(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Call deprecated"));
        }

        [Test]
        public void BeginCallShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCall(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginCall deprecated"));
        }

        [Test]
        public void EndCallShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCall(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndCall deprecated"));
        }

        [Test]
        public void CreateMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CreateMonitoredItems(null, 0, 0, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("CreateMonitoredItems deprecated"));
        }

        [Test]
        public void BeginCreateMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCreateMonitoredItems(null, 0, 0, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginCreateMonitoredItems deprecated"));
        }

        [Test]
        public void EndCreateMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCreateMonitoredItems(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndCreateMonitoredItems deprecated"));
        }

        [Test]
        public void ModifyMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.ModifyMonitoredItems(null, 0, 0, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("ModifyMonitoredItems deprecated"));
        }

        [Test]
        public void BeginModifyMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginModifyMonitoredItems(null, 0, 0, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginModifyMonitoredItems deprecated"));
        }

        [Test]
        public void EndModifyMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndModifyMonitoredItems(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndModifyMonitoredItems deprecated"));
        }

        [Test]
        public void SetMonitoringModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.SetMonitoringMode(null, 0, 0, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("SetMonitoringMode deprecated"));
        }

        [Test]
        public void BeginSetMonitoringModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginSetMonitoringMode(null, 0, 0, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginSetMonitoringMode deprecated"));
        }

        [Test]
        public void EndSetMonitoringModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndSetMonitoringMode(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndSetMonitoringMode deprecated"));
        }

        [Test]
        public void SetTriggeringShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.SetTriggering(null, 0, 0, default, default, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("SetTriggering deprecated"));
        }

        [Test]
        public void BeginSetTriggeringShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginSetTriggering(null, 0, 0, default, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginSetTriggering deprecated"));
        }

        [Test]
        public void EndSetTriggeringShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndSetTriggering(null!, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndSetTriggering deprecated"));
        }

        [Test]
        public void DeleteMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteMonitoredItems(null, 0, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("DeleteMonitoredItems deprecated"));
        }

        [Test]
        public void BeginDeleteMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteMonitoredItems(null, 0, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginDeleteMonitoredItems deprecated"));
        }

        [Test]
        public void EndDeleteMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteMonitoredItems(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndDeleteMonitoredItems deprecated"));
        }

        [Test]
        public void CreateSubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CreateSubscription(null, 0, 0, 0, 0, false, 0, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("CreateSubscription deprecated"));
        }

        [Test]
        public void BeginCreateSubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCreateSubscription(null, 0, 0, 0, 0, false, 0, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginCreateSubscription deprecated"));
        }

        [Test]
        public void EndCreateSubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCreateSubscription(null!, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndCreateSubscription deprecated"));
        }

        [Test]
        public void ModifySubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.ModifySubscription(null, 0, 0, 0, 0, 0, 0, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("ModifySubscription deprecated"));
        }

        [Test]
        public void BeginModifySubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginModifySubscription(null, 0, 0, 0, 0, 0, 0, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginModifySubscription deprecated"));
        }

        [Test]
        public void EndModifySubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndModifySubscription(null!, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndModifySubscription deprecated"));
        }

        [Test]
        public void SetPublishingModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.SetPublishingMode(null, false, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("SetPublishingMode deprecated"));
        }

        [Test]
        public void BeginSetPublishingModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginSetPublishingMode(null, false, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginSetPublishingMode deprecated"));
        }

        [Test]
        public void EndSetPublishingModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndSetPublishingMode(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndSetPublishingMode deprecated"));
        }

        [Test]
        public void PublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Publish(null, default, out _, out _, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Publish deprecated"));
        }

        [Test]
        public void BeginPublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginPublish(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginPublish deprecated"));
        }

        [Test]
        public void EndPublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndPublish(null!, out _, out _, out _, out _, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndPublish deprecated"));
        }

        [Test]
        public void BeginReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginRead(null, 0, 0, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginRead deprecated"));
        }

        [Test]
        public void EndReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndRead(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndRead deprecated"));
        }

        [Test]
        public void ReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Read(null, 0, 0, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Read deprecated"));
        }

        [Test]
        public void RepublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Republish(null, 0, 0, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("Republish deprecated"));
        }

        [Test]
        public void BeginRepublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginRepublish(null, 0, 0, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginRepublish deprecated"));
        }

        [Test]
        public void EndRepublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndRepublish(null!, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndRepublish deprecated"));
        }

        [Test]
        public void TransferSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.TransferSubscriptions(null, default, false, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("TransferSubscriptions deprecated"));
        }

        [Test]
        public void BeginTransferSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginTransferSubscriptions(null, default, false, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginTransferSubscriptions deprecated"));
        }

        [Test]
        public void EndTransferSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndTransferSubscriptions(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndTransferSubscriptions deprecated"));
        }

        [Test]
        public void DeleteSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteSubscriptions(null, default, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("DeleteSubscriptions deprecated"));
        }

        [Test]
        public void BeginDeleteSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteSubscriptions(null, default, null!, null!);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("BeginDeleteSubscriptions deprecated"));
        }

        [Test]
        public void EndDeleteSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteSubscriptions(null!, out _, out _);

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("EndDeleteSubscriptions deprecated"));
        }

        [Test]
        public void UpdateRequestHeaderShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new TestSessionClient();

            // Act
            Action act = sessionClient.TestUpdateRequestHeader;

            // Assert
            var ex = Assert.Throws<ServiceResultException>(() => act());
            Assert.That(ex.Message, Does.Match("UpdateRequestHeader deprecated"));
        }

        internal sealed class TestSessionClient : SessionClient
        {
            public void TestUpdateRequestHeader()
            {
                base.UpdateRequestHeader(null!, false);
            }
        }
    }

#pragma warning restore CS0618 // Type or member is obsolete
}
