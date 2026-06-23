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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.Server;

namespace Opc.Ua.PubSub.Server
{
    /// <summary>
    /// PubSub Action handler that invokes an OPC UA server Method.
    /// </summary>
    public sealed class ServerMethodActionHandler : IPubSubActionHandler
    {
        private readonly IMasterNodeManager m_nodeManager;
        private readonly NodeId m_objectId;
        private readonly NodeId m_methodId;
        private readonly IUserIdentity m_serviceIdentity;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes a new <see cref="ServerMethodActionHandler"/>.
        /// </summary>
        /// <param name="nodeManager">Master node manager used to call the Method.</param>
        /// <param name="method">PublishedActionMethod metadata to bind.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="serviceIdentity">
        /// Identity the bound Method executes under (SA-ACT-02). PubSub Action
        /// requests do not arrive over an OPC UA session, so there is no
        /// session-derived user. When <see langword="null"/> an explicit
        /// <em>Anonymous</em> identity is used and the Method is invoked as
        /// Anonymous; node <c>RolePermissions</c> for the Anonymous role then
        /// apply. Supply a configured service identity to run the Method under a
        /// specific principal instead of bypassing user-auth/role mapping.
        /// </param>
        public ServerMethodActionHandler(
            IMasterNodeManager nodeManager,
            ActionMethodDataType method,
            ITelemetryContext telemetry,
            IUserIdentity? serviceIdentity = null)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            if (method is null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (method.ObjectId.IsNull)
            {
                throw new ArgumentException("ObjectId must not be null.", nameof(method));
            }
            if (method.MethodId.IsNull)
            {
                throw new ArgumentException("MethodId must not be null.", nameof(method));
            }

            m_nodeManager = nodeManager;
            m_objectId = method.ObjectId;
            m_methodId = method.MethodId;
            m_serviceIdentity = serviceIdentity ?? new UserIdentity();
            m_logger = telemetry.CreateLogger<ServerMethodActionHandler>();
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubActionHandlerResult> HandleAsync(
            PubSubActionInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (invocation is null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }

            try
            {
                OperationContext context = CreateOperationContext(invocation, m_serviceIdentity);
                var methodToCall = new CallMethodRequest
                {
                    ObjectId = m_objectId,
                    MethodId = m_methodId,
                    InputArguments = MapInputArguments(invocation.InputFields)
                };

                (ArrayOf<CallMethodResult> results, _) = await m_nodeManager
                    .CallAsync(context, [methodToCall], cancellationToken)
                    .ConfigureAwait(false);

                if (results.Count == 0 || results[0] is null)
                {
                    return new PubSubActionHandlerResult
                    {
                        StatusCode = (StatusCode)StatusCodes.BadUnexpectedError
                    };
                }

                CallMethodResult result = results[0];
                return new PubSubActionHandlerResult
                {
                    StatusCode = result.StatusCode,
                    OutputFields = MapOutputFields(result.OutputArguments)
                };
            }
            catch (ServiceResultException ex)
            {
                m_logger.LogWarning(ex, "PubSub Action server Method call failed.");
                return new PubSubActionHandlerResult
                {
                    StatusCode = ex.StatusCode
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.LogWarning(ex, "PubSub Action server Method call failed unexpectedly.");
                return new PubSubActionHandlerResult
                {
                    StatusCode = (StatusCode)StatusCodes.BadUnexpectedError
                };
            }
        }

        private static OperationContext CreateOperationContext(
            PubSubActionInvocation invocation,
            IUserIdentity serviceIdentity)
        {
            var header = new RequestHeader
            {
                RequestHandle = invocation.RequestId,
                Timestamp = DateTime.UtcNow,
                TimeoutHint = ToTimeoutHint(invocation.TimeoutHint),
                AuditEntryId = invocation.Target.ActionName
            };

            // SA-ACT-02: PubSub Action requests do not arrive over an OPC UA
            // secure channel / session, so there is no secure-channel context to
            // attach. Permission evaluation therefore relies on the explicitly
            // configured service identity and the node RolePermissions that apply
            // to it, rather than a session-mapped user.
            return new OperationContext(
                header,
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None,
                serviceIdentity);
        }

        private static uint ToTimeoutHint(double timeoutHint)
        {
            if (timeoutHint <= 0 || double.IsNaN(timeoutHint))
            {
                return 0;
            }
            if (timeoutHint >= uint.MaxValue)
            {
                return uint.MaxValue;
            }
            return (uint)timeoutHint;
        }

        private static ArrayOf<Variant> MapInputArguments(ArrayOf<DataSetField> inputFields)
        {
            if (inputFields.IsNull || inputFields.Count == 0)
            {
                return [];
            }

            var arguments = new Variant[inputFields.Count];
            for (int i = 0; i < inputFields.Count; i++)
            {
                arguments[i] = inputFields[i].Value;
            }
            return new ArrayOf<Variant>(arguments);
        }

        private static ArrayOf<DataSetField> MapOutputFields(ArrayOf<Variant> outputArguments)
        {
            if (outputArguments.IsNull || outputArguments.Count == 0)
            {
                return [];
            }

            var fields = new DataSetField[outputArguments.Count];
            for (int i = 0; i < outputArguments.Count; i++)
            {
                fields[i] = new DataSetField
                {
                    Name = "OutputArgument" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Value = outputArguments[i],
                    StatusCode = (StatusCode)StatusCodes.Good,
                    Encoding = PubSubFieldEncoding.Variant
                };
            }
            return new ArrayOf<DataSetField>(fields);
        }
    }
}
