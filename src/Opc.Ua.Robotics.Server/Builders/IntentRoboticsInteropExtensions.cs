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

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Annex B helpers for binding OPC 40010 topology to Robot Intent controllers.
    /// </summary>
    public static class IntentRoboticsInteropExtensions
    {
        /// <summary>
        /// Adds HasIntentController and IntentControllerOf between a MotionDeviceSystem and an intent controller.
        /// </summary>
        public static IMotionDeviceSystemBuilder HasIntentController(
            this IMotionDeviceSystemBuilder motionDeviceSystem,
            IIntentControllerBuilder intentController)
        {
            if (motionDeviceSystem == null)
            {
                throw new System.ArgumentNullException(nameof(motionDeviceSystem));
            }
            if (intentController == null)
            {
                throw new System.ArgumentNullException(nameof(intentController));
            }
            NamespaceTable namespaceUris = motionDeviceSystem.BuildContext.Context.NamespaceUris;
            var referenceTypeId = NodeId.Create(
                global::Opc.Ua.RobotIntent.ReferenceTypes.HasIntentController,
                global::Opc.Ua.RobotIntent.Namespaces.RobotIntent,
                namespaceUris);
            if (!motionDeviceSystem.State.ReferenceExists(referenceTypeId, false, intentController.State.NodeId))
            {
                motionDeviceSystem.State.AddReference(referenceTypeId, false, intentController.State.NodeId);
                intentController.State.AddReference(referenceTypeId, true, motionDeviceSystem.State.NodeId);
            }
            return motionDeviceSystem;
        }
    }
}
