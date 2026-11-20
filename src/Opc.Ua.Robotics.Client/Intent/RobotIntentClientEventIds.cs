// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Robotics.Client.Intent
{
    internal static class RobotIntentClientEventIds
    {
        public const int IntentSubmitted = 7200;
        public const int IntentRefused = 7201;
        public const int OperationTerminal = 7202;
        public const int OperationReconnectRead = 7203;
        public const int OperationSubscriptionReestablished = 7204;
        public const int AuthorityGranted = 7205;
        public const int AuthorityLost = 7206;
        public const int AuthorityReleased = 7207;
        public const int ChannelLeaseGranted = 7208;
        public const int ChannelLeaseRenewed = 7209;
        public const int ChannelLeaseLapsed = 7210;
        public const int MissionUpdateRefused = 7211;
        public const int ChannelLeaseRenewalFailed = 7212;
        public const int AuthorityReleaseFailed = 7213;
        public const int OperationSubscriptionFailed = 7214;
        public const int MissionSubscriptionFailed = 7215;
    }
}
