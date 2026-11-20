/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Logger event identifiers used by the Vision server surface.
    /// </summary>
    internal static class VisionServerEventIds
    {
        /// <summary>
        /// Released Machine Vision runtime messages.
        /// </summary>
        public const int MachineVisionRuntime = 16010;

        /// <summary>
        /// The Vision node manager finished creating its address space.
        /// </summary>
        public const int NodeManagerReady = 16000;

        /// <summary>
        /// A Vision media provider is missing for a sensor.
        /// </summary>
        public const int MediaProviderMissing = 16001;

        /// <summary>
        /// A Vision inference provider is missing for a pipeline.
        /// </summary>
        public const int InferenceProviderMissing = 16002;

        /// <summary>
        /// A Vision feedback sink is missing for a pipeline.
        /// </summary>
        public const int FeedbackSinkMissing = 16003;

        /// <summary>
        /// A Vision method failed and produced a Bad_ status.
        /// </summary>
        public const int MethodFailed = 16004;

        /// <summary>
        /// A published result was accepted from an off-server caller.
        /// </summary>
        public const int FeedbackAccepted = 16005;
    }
}
