/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.RobotIntent.Server
{
    internal static class RobotIntentServerEventIds
    {
        public const int Admission = 8000;
        public const int AdmissionRefused = 8001;
        public const int StateTransition = 8002;
        public const int ExecutorFault = 8003;
        public const int NodeAddRemove = 8004;
        public const int AuthorityGranted = 8005;
        public const int AuthorityReleased = 8006;
        public const int LeaseGranted = 8007;
        public const int LeaseExpired = 8008;
        public const int SafetyRefusal = 8009;
        public const int ShutdownTimedOut = 8010;
        public const int PumpFault = 8011;
    }
}
