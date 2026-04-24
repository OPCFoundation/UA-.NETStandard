// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions.Obsolete
{
    using FluentAssertions;
    using Opc.Ua;
    using System;
    using Xunit;

    public class SessionClientTests
    {
        [Fact]
        public void CloseShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Close();

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Close deprecated");
        }

        [Fact]
        public void CreateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CreateSession(null, null!, null!,
                null!, null!, null!, null!, 0, 0, out _, out _, out _, out _, out _, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("CreateSession deprecated");
        }

        [Fact]
        public void BeginCreateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCreateSession(null, null!, null!, null!, null!, null!, null!, 0, 0, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginCreateSession deprecated");
        }

        [Fact]
        public void EndCreateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCreateSession(null!, out _, out _, out _, out _, out _, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndCreateSession deprecated");
        }

        [Fact]
        public void ActivateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.ActivateSession(null, null!, null!, null!, null!, null!, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("ActivateSession deprecated");
        }

        [Fact]
        public void BeginActivateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginActivateSession(null, null!, null, null!, null!, null!, null, null);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginActivateSession deprecated");
        }

        [Fact]
        public void EndActivateSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndActivateSession(null!, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndActivateSession deprecated");
        }

        [Fact]
        public void CloseSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CloseSession(null, false);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("CloseSession deprecated");
        }

        [Fact]
        public void BeginCloseSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCloseSession(null, false, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginCloseSession deprecated");
        }

        [Fact]
        public void EndCloseSessionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCloseSession(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndCloseSession deprecated");
        }

        [Fact]
        public void CancelShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Cancel(null, 0, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Cancel deprecated");
        }

        [Fact]
        public void BeginCancelShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCancel(null, 0, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginCancel deprecated");
        }

        [Fact]
        public void EndCancelShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCancel(null!, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndCancel deprecated");
        }

        [Fact]
        public void AddNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.AddNodes(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("AddNodes deprecated");
        }

        [Fact]
        public void BeginAddNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginAddNodes(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginAddNodes deprecated");
        }

        [Fact]
        public void EndAddNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndAddNodes(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndAddNodes deprecated");
        }

        [Fact]
        public void AddReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.AddReferences(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("AddReferences deprecated");
        }

        [Fact]
        public void BeginAddReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginAddReferences(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginAddReferences deprecated");
        }

        [Fact]
        public void EndAddReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndAddReferences(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndAddReferences deprecated");
        }

        [Fact]
        public void DeleteNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteNodes(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("DeleteNodes deprecated");
        }

        [Fact]
        public void BeginDeleteNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteNodes(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginDeleteNodes deprecated");
        }

        [Fact]
        public void EndDeleteNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteNodes(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndDeleteNodes deprecated");
        }

        [Fact]
        public void DeleteReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteReferences(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("DeleteReferences deprecated");
        }

        [Fact]
        public void BeginDeleteReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteReferences(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginDeleteReferences deprecated");
        }

        [Fact]
        public void EndDeleteReferencesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteReferences(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndDeleteReferences deprecated");
        }

        [Fact]
        public void BrowseShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Browse(null, null!, 0, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Browse deprecated");
        }

        [Fact]
        public void BeginBrowseShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginBrowse(null, null!, 0, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginBrowse deprecated");
        }

        [Fact]
        public void EndBrowseShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndBrowse(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndBrowse deprecated");
        }

        [Fact]
        public void BrowseNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BrowseNext(null, false, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BrowseNext deprecated");
        }

        [Fact]
        public void BeginBrowseNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginBrowseNext(null, false, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginBrowseNext deprecated");
        }

        [Fact]
        public void EndBrowseNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndBrowseNext(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndBrowseNext deprecated");
        }

        [Fact]
        public void TranslateBrowsePathsToNodeIdsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.TranslateBrowsePathsToNodeIds(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("TranslateBrowsePathsToNodeIds deprecated");
        }

        [Fact]
        public void BeginTranslateBrowsePathsToNodeIdsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginTranslateBrowsePathsToNodeIds(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginTranslateBrowsePathsToNodeIds deprecated");
        }

        [Fact]
        public void EndTranslateBrowsePathsToNodeIdsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndTranslateBrowsePathsToNodeIds(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndTranslateBrowsePathsToNodeIds deprecated");
        }

        [Fact]
        public void RegisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.RegisterNodes(null, null!, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("RegisterNodes deprecated");
        }

        [Fact]
        public void BeginRegisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginRegisterNodes(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginRegisterNodes deprecated");
        }

        [Fact]
        public void EndRegisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndRegisterNodes(null!, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndRegisterNodes deprecated");
        }

        [Fact]
        public void UnregisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.UnregisterNodes(null, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("UnregisterNodes deprecated");
        }

        [Fact]
        public void BeginUnregisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginUnregisterNodes(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginUnregisterNodes deprecated");
        }

        [Fact]
        public void EndUnregisterNodesShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndUnregisterNodes(null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndUnregisterNodes deprecated");
        }

        [Fact]
        public void QueryFirstShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.QueryFirst(null, null!, null!, null!, 0, 0, out _, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("QueryFirst deprecated");
        }

        [Fact]
        public void BeginQueryFirstShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginQueryFirst(null, null!, null!, null!, 0, 0, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginQueryFirst deprecated");
        }

        [Fact]
        public void EndQueryFirstShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndQueryFirst(null!, out _, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndQueryFirst deprecated");
        }

        [Fact]
        public void QueryNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.QueryNext(null, false, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("QueryNext deprecated");
        }

        [Fact]
        public void BeginQueryNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginQueryNext(null, false, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginQueryNext deprecated");
        }

        [Fact]
        public void EndQueryNextShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndQueryNext(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndQueryNext deprecated");
        }

        [Fact]
        public void HistoryReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.HistoryRead(null, null!, 0, false, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("HistoryRead deprecated");
        }

        [Fact]
        public void BeginHistoryReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginHistoryRead(null, null!, 0, false, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginHistoryRead deprecated");
        }

        [Fact]
        public void EndHistoryReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndHistoryRead(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndHistoryRead deprecated");
        }
        [Fact]
        public void WriteShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Write(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Write deprecated");
        }

        [Fact]
        public void BeginWriteShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginWrite(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginWrite deprecated");
        }

        [Fact]
        public void EndWriteShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndWrite(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndWrite deprecated");
        }

        [Fact]
        public void HistoryUpdateShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.HistoryUpdate(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("HistoryUpdate deprecated");
        }

        [Fact]
        public void BeginHistoryUpdateShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginHistoryUpdate(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginHistoryUpdate deprecated");
        }

        [Fact]
        public void EndHistoryUpdateShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndHistoryUpdate(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndHistoryUpdate deprecated");
        }

        [Fact]
        public void CallShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Call(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Call deprecated");
        }

        [Fact]
        public void BeginCallShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCall(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginCall deprecated");
        }

        [Fact]
        public void EndCallShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCall(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndCall deprecated");
        }

        [Fact]
        public void CreateMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CreateMonitoredItems(null, 0, 0, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("CreateMonitoredItems deprecated");
        }

        [Fact]
        public void BeginCreateMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCreateMonitoredItems(null, 0, 0, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginCreateMonitoredItems deprecated");
        }

        [Fact]
        public void EndCreateMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCreateMonitoredItems(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndCreateMonitoredItems deprecated");
        }

        [Fact]
        public void ModifyMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.ModifyMonitoredItems(null, 0, 0, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("ModifyMonitoredItems deprecated");
        }

        [Fact]
        public void BeginModifyMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginModifyMonitoredItems(null, 0, 0, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginModifyMonitoredItems deprecated");
        }

        [Fact]
        public void EndModifyMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndModifyMonitoredItems(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndModifyMonitoredItems deprecated");
        }

        [Fact]
        public void SetMonitoringModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.SetMonitoringMode(null, 0, 0, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("SetMonitoringMode deprecated");
        }

        [Fact]
        public void BeginSetMonitoringModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginSetMonitoringMode(null, 0, 0, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginSetMonitoringMode deprecated");
        }

        [Fact]
        public void EndSetMonitoringModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndSetMonitoringMode(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndSetMonitoringMode deprecated");
        }

        [Fact]
        public void SetTriggeringShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.SetTriggering(null, 0, 0, null!, null!, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("SetTriggering deprecated");
        }

        [Fact]
        public void BeginSetTriggeringShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginSetTriggering(null, 0, 0, null!, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginSetTriggering deprecated");
        }

        [Fact]
        public void EndSetTriggeringShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndSetTriggering(null!, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndSetTriggering deprecated");
        }

        [Fact]
        public void DeleteMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteMonitoredItems(null, 0, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("DeleteMonitoredItems deprecated");
        }

        [Fact]
        public void BeginDeleteMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteMonitoredItems(null, 0, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginDeleteMonitoredItems deprecated");
        }

        [Fact]
        public void EndDeleteMonitoredItemsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteMonitoredItems(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndDeleteMonitoredItems deprecated");
        }

        [Fact]
        public void CreateSubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.CreateSubscription(null, 0, 0, 0, 0, false, 0, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("CreateSubscription deprecated");
        }

        [Fact]
        public void BeginCreateSubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginCreateSubscription(null, 0, 0, 0, 0, false, 0, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginCreateSubscription deprecated");
        }

        [Fact]
        public void EndCreateSubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndCreateSubscription(null!, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndCreateSubscription deprecated");
        }

        [Fact]
        public void ModifySubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.ModifySubscription(null, 0, 0, 0, 0, 0, 0, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("ModifySubscription deprecated");
        }

        [Fact]
        public void BeginModifySubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginModifySubscription(null, 0, 0, 0, 0, 0, 0, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginModifySubscription deprecated");
        }

        [Fact]
        public void EndModifySubscriptionShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndModifySubscription(null!, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndModifySubscription deprecated");
        }

        [Fact]
        public void SetPublishingModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.SetPublishingMode(null, false, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("SetPublishingMode deprecated");
        }

        [Fact]
        public void BeginSetPublishingModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginSetPublishingMode(null, false, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginSetPublishingMode deprecated");
        }

        [Fact]
        public void EndSetPublishingModeShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndSetPublishingMode(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndSetPublishingMode deprecated");
        }

        [Fact]
        public void PublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Publish(null, null!, out _, out _, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Publish deprecated");
        }

        [Fact]
        public void BeginPublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginPublish(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginPublish deprecated");
        }

        [Fact]
        public void EndPublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndPublish(null!, out _, out _, out _, out _, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndPublish deprecated");
        }

        [Fact]
        public void BeginReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginRead(null, 0, 0, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginRead deprecated");
        }

        [Fact]
        public void EndReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndRead(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndRead deprecated");
        }

        [Fact]
        public void ReadShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Read(null, 0, 0, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Read deprecated");
        }

        [Fact]
        public void RepublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.Republish(null, 0, 0, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("Republish deprecated");
        }

        [Fact]
        public void BeginRepublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginRepublish(null, 0, 0, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginRepublish deprecated");
        }

        [Fact]
        public void EndRepublishShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndRepublish(null!, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndRepublish deprecated");
        }

        [Fact]
        public void TransferSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.TransferSubscriptions(null, null!, false, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("TransferSubscriptions deprecated");
        }

        [Fact]
        public void BeginTransferSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginTransferSubscriptions(null, null!, false, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginTransferSubscriptions deprecated");
        }

        [Fact]
        public void EndTransferSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndTransferSubscriptions(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndTransferSubscriptions deprecated");
        }

        [Fact]
        public void DeleteSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.DeleteSubscriptions(null, null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("DeleteSubscriptions deprecated");
        }

        [Fact]
        public void BeginDeleteSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.BeginDeleteSubscriptions(null, null!, null!, null!);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("BeginDeleteSubscriptions deprecated");
        }

        [Fact]
        public void EndDeleteSubscriptionsShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new SessionClient();

            // Act
            Action act = () => sessionClient.EndDeleteSubscriptions(null!, out _, out _);

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("EndDeleteSubscriptions deprecated");
        }

        [Fact]
        public void UpdateRequestHeaderShouldThrowNotSupportedException()
        {
            // Arrange
            var sessionClient = new TestSessionClient();

            // Act
            Action act = sessionClient.TestUpdateRequestHeader;

            // Assert
            act.Should().Throw<ServiceResultException>()
                .WithMessage("UpdateRequestHeader deprecated");
        }

        internal sealed class TestSessionClient : SessionClient
        {
            public void TestUpdateRequestHeader()
            {
                base.UpdateRequestHeader(null!, false);
            }
        }
    }
}
