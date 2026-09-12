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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Second part of the <see cref="CustomNodeManager2"/>
    /// Implementation of the <see cref="ICallAsyncNodeManager"/> interface for custom node management.
    /// The interface is not implmented by default to ensure consistent behaviour in existing NodeManagers, but can be implemented in a derived class.
    /// </summary>
    public partial class CustomNodeManager2
    {
        /// <summary>
        /// Asycnhronously calls a method defined on an object.
        /// </summary>
        public virtual ValueTask CallAsync(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            return CallInternalAsync(
                context,
                methodsToCall,
                results,
                errors,
                sync: false,
                cancellationToken);
        }

        private void CallSynchronousBatch(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            foreach ((int index, CallMethodRequest request, MethodState method) in
                GetMethodCalls(context, systemContext, methodsToCall, errors))
            {
                CallMethodResult result = results[index] = new CallMethodResult();
                errors[index] = Call(systemContext, request, method, result);
            }
        }

        private ServiceResult CallSynchronousMethod(
            ISystemContext context,
            CallMethodRequest request,
            MethodState method,
            CallMethodResult result)
        {
            var argumentErrors = new List<ServiceResult>();
            var outputArguments = new List<Variant>();
            ServiceResult callResult = method.Call(
                context, request.ObjectId, request.InputArguments, argumentErrors, outputArguments);
            return CompleteMethodCall(context, result, callResult, argumentErrors, outputArguments);
        }

        private IEnumerable<(int Index, CallMethodRequest Request, MethodState Method)> GetMethodCalls(
            OperationContext context,
            ServerSystemContext systemContext,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<ServiceResult> errors)
        {
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();
            for (int ii = 0; ii < methodsToCall.Count; ii++)
            {
                CallMethodRequest request = methodsToCall[ii];
                if (request.Processed)
                {
                    continue;
                }
                NodeHandle? handle = GetManagerHandle(systemContext, request.ObjectId, operationCache);
                if (handle == null)
                {
                    continue;
                }

                MethodState? method;
                lock (Lock)
                {
                    request.Processed = true;
                    NodeState? source = ValidateNode(systemContext, handle, operationCache);
                    if (source == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        continue;
                    }
                    method = FindMethodState(context, request);
                    if (method == null)
                    {
                        errors[ii] = StatusCodes.BadMethodInvalid;
                        continue;
                    }
                    errors[ii] = ValidateRolePermissions(context, method.NodeId, PermissionType.Call);
                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }
                }
                yield return (ii, request, method);
            }
        }

        private ServiceResult CompleteMethodCall(
            ISystemContext context,
            CallMethodResult result,
            ServiceResult callResult,
            List<ServiceResult> argumentErrors,
            List<Variant> outputArguments)
        {
            if (ServiceResult.IsBad(callResult))
            {
                return callResult;
            }
            var systemContext = context as ServerSystemContext;
            bool argumentsValid = true;
            var inputArgumentResults = new List<StatusCode>();
            var inputArgumentDiagnosticInfos = new List<DiagnosticInfo>();
            for (int ii = 0; ii < argumentErrors.Count; ii++)
            {
                ServiceResult argumentError = argumentErrors[ii];
                if (argumentError != null)
                {
                    inputArgumentResults.Add(argumentError.StatusCode);
                    if (ServiceResult.IsBad(argumentError))
                    {
                        argumentsValid = false;
                    }
                    if (systemContext!.OperationContext != null &&
                        (systemContext.OperationContext.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        inputArgumentDiagnosticInfos.Add(ServiceResult.IsBad(argumentError)
                            ? new DiagnosticInfo(
                                argumentError,
                                systemContext.OperationContext.DiagnosticsMask,
                                false,
                                systemContext.OperationContext.StringTable,
                                m_logger)
                            : null!);
                    }
                }
            }
            if (!argumentsValid)
            {
                result.InputArgumentResults = inputArgumentResults;
                result.InputArgumentDiagnosticInfos = inputArgumentDiagnosticInfos;
                result.StatusCode = StatusCodes.BadInvalidArgument;
                return result.StatusCode;
            }
            result.OutputArguments = outputArguments;
            return callResult;
        }
    }
}
