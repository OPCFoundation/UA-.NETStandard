// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        public virtual async ValueTask CallAsync(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation operation = BeginNodeManagerOperation();
            await CallInternalAsync(
                context,
                methodsToCall,
                results,
                errors,
                sync: false,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispatches validated requests through synchronous method callbacks and records their results.
        /// </summary>
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

        /// <summary>
        /// Invokes a synchronous method callback and translates its argument and output results.
        /// </summary>
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

        /// <summary>
        /// Claims this manager's method requests and yields those passing node, method and permission checks.
        /// </summary>
        private IEnumerable<(int Index, CallMethodRequest Request, MethodState Method)> GetMethodCalls(
            OperationContext context,
            ServerSystemContext systemContext,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<ServiceResult> errors)
        {
            IDictionary<NodeId, NodeState> operationCache = new Dictionary<NodeId, NodeState>();
            for (int ii = 0; ii < methodsToCall.Count; ii++)
            {
                // the call fails as a whole once its results exceed the response byte budget.
                if (context.ResponseBudgetExceeded)
                {
                    yield break;
                }
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
                    // Part 3 §8.55 Call: required on the Object and on the Method.
                    errors[ii] = ValidateRolePermissions(context, request.ObjectId, PermissionType.Call);
                    if (ServiceResult.IsBad(errors[ii]))
                    {
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

        /// <summary>
        /// Populates failed input-argument diagnostics or successful outputs while preserving the callback result.
        /// </summary>
        /// <param name="context">The context for the method invocation.</param>
        /// <param name="result">The result populated for the Call service response.</param>
        /// <param name="callResult">The operation result returned by the callback.</param>
        /// <param name="argumentErrors">The ordered per-input results returned by the callback.</param>
        /// <param name="outputArguments">The ordered output arguments returned by the callback.</param>
        private ServiceResult CompleteMethodCall(
            ISystemContext context,
            CallMethodResult result,
            ServiceResult callResult,
            List<ServiceResult> argumentErrors,
            List<Variant> outputArguments)
        {
            var systemContext = context as ServerSystemContext;
            return MethodCallResultBuilder.Apply(
                callResult, argumentErrors, outputArguments, result, systemContext?.OperationContext, m_logger);
        }
    }
}
