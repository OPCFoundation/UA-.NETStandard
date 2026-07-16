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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// Registers PublishedActionMethod metadata as PubSub Action handlers.
    /// </summary>
    internal static class PubSubActionMethodRegistrar
    {
        public static void Register(
            IPubSubApplication application,
            IMasterNodeManager nodeManager,
            PubSubActionMethodRegistration registration,
            ITelemetryContext telemetry)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            ILogger logger = telemetry.CreateLogger<PubSubActionMethodRegistration>();
            PublishedActionMethodDataType action = registration.PublishedAction;

            // SA-ACT-02: bound Methods run under an explicitly configured service
            // identity instead of a silent Anonymous. Default to an explicit
            // Anonymous so behavior is unchanged unless an operator opts in, but
            // surface the choice so a privileged Method is not exposed unknowingly.
            IUserIdentity serviceIdentity = registration.ServiceIdentity ?? new UserIdentity();
            bool isAnonymous = serviceIdentity.TokenType == UserTokenType.Anonymous;

            if (action.ActionTargets.IsNull || action.ActionMethods.IsNull)
            {
                logger.PublishedActionMethodBindingSkippedNullTargetsOrMethods();
                return;
            }

            int count = Math.Min(action.ActionTargets.Count, action.ActionMethods.Count);
            if (action.ActionTargets.Count != action.ActionMethods.Count)
            {
                logger.PublishedActionMethodBindingCountMismatch(
                    action.ActionTargets.Count,
                    action.ActionMethods.Count);
            }

            for (int i = 0; i < count; i++)
            {
                ActionTargetDataType actionTarget = action.ActionTargets[i];
                ActionMethodDataType actionMethod = action.ActionMethods[i];
                if (actionTarget is null || actionMethod is null)
                {
                    logger.PublishedActionMethodBindingSkippedNullMetadata(i);
                    continue;
                }

                var target = new PubSubActionTarget
                {
                    ConnectionName = registration.ConnectionName,
                    DataSetWriterId = registration.DataSetWriterId,
                    ActionTargetId = actionTarget.ActionTargetId,
                    ActionName = actionTarget.Name ?? string.Empty
                };

                // Warn so an operator cannot unknowingly expose a privileged
                // Method anonymously over PubSub (SA-ACT-02). The Method executes
                // under the configured identity; node RolePermissions for that
                // identity (Anonymous here) govern whether the call is allowed.
                if (isAnonymous)
                {
                    logger.PubSubActionTargetInvokedAsAnonymous(
                        target.ActionName,
                        registration.DataSetWriterId,
                        actionTarget.ActionTargetId,
                        actionMethod.MethodId,
                        actionMethod.ObjectId);
                }

                application.RegisterActionHandler(
                    target,
                    new ServerMethodActionHandler(nodeManager, actionMethod, telemetry, serviceIdentity));
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for PubSubActionMethodRegistrar.
    /// </summary>
    internal static partial class PubSubActionMethodRegistrarLog
    {
        [LoggerMessage(EventId = PubSubServerEventIds.PubSubActionMethodRegistrar + 0, Level = LogLevel.Warning,
            Message = "PublishedActionMethod binding skipped because targets or methods are null.")]
        public static partial void PublishedActionMethodBindingSkippedNullTargetsOrMethods(this ILogger logger);

        [LoggerMessage(EventId = PubSubServerEventIds.PubSubActionMethodRegistrar + 1, Level = LogLevel.Warning,
            Message = "PublishedActionMethod binding count mismatch: {TargetCount} targets, {MethodCount} methods.")]
        public static partial void PublishedActionMethodBindingCountMismatch(
            this ILogger logger,
            int targetCount,
            int methodCount);

        [LoggerMessage(EventId = PubSubServerEventIds.PubSubActionMethodRegistrar + 2, Level = LogLevel.Warning,
            Message = "PublishedActionMethod binding {Index} skipped because metadata is null.")]
        public static partial void PublishedActionMethodBindingSkippedNullMetadata(this ILogger logger, int index);

        [LoggerMessage(EventId = PubSubServerEventIds.PubSubActionMethodRegistrar + 3, Level = LogLevel.Warning,
            Message = "PubSub Action target '{ActionName}' (writer {WriterId}, target {TargetId}) binds server " +
                "Method {MethodId} on object {ObjectId} and will be invoked as Anonymous over PubSub. Configure a " +
                "service identity if the Method requires user authentication or role-restricted RolePermissions.")]
        public static partial void PubSubActionTargetInvokedAsAnonymous(
            this ILogger logger,
            string actionName,
            ushort writerId,
            ushort targetId,
            NodeId? methodId,
            NodeId? objectId);
    }

}
