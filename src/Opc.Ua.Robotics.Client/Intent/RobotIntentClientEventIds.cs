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
