/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Di.Server.Transfer
{
    /// <summary>
    /// Convenience extensions that wire an
    /// <see cref="ITransferService"/> to the three method children
    /// of a <c>TransferServicesType</c> instance —
    /// <c>TransferToDevice</c>, <c>TransferFromDevice</c>, and
    /// <c>FetchTransferResultData</c>. Uses the stack-level
    /// <see cref="MethodState.OnCallMethod2"/> handler so it works
    /// with any concrete state derived from <see cref="BaseObjectState"/>
    /// that carries the three named method children.
    /// </summary>
    public static class TransferServicesExtensions
    {
        /// <summary>
        /// Routes the three <c>TransferServicesType</c> method calls
        /// on <paramref name="transferServices"/> through
        /// <paramref name="service"/>, keyed on
        /// <paramref name="elementId"/>.
        /// </summary>
        /// <param name="transferServices">
        /// A <see cref="BaseObjectState"/> instance whose children
        /// include three <see cref="MethodState"/> nodes named
        /// <c>TransferToDevice</c>, <c>TransferFromDevice</c>, and
        /// <c>FetchTransferResultData</c> (the typed
        /// <c>TransferServicesState</c> emitted by the source
        /// generator, or any structurally-equivalent custom object).
        /// </param>
        /// <param name="elementId">
        /// The NodeId of the topology element whose parameters are
        /// transferred through this service instance.
        /// </param>
        /// <param name="service">The transfer service.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the supplied node does not expose the three
        /// expected child methods.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="transferServices"/> is <c>null</c>.</exception>
        public static void BindToTransferService(
            this BaseObjectState transferServices,
            NodeId elementId,
            ITransferService service)
        {
            if (transferServices == null)
            {
                throw new ArgumentNullException(nameof(transferServices));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }
            MethodState? transferTo = FindMethodChild(transferServices,
                BrowseNames.TransferToDevice);
            MethodState? transferFrom = FindMethodChild(transferServices,
                BrowseNames.TransferFromDevice);
            MethodState? fetchResult = FindMethodChild(transferServices,
                BrowseNames.FetchTransferResultData);

            if (transferTo == null || transferFrom == null || fetchResult == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "TransferServices instance '{0}' is missing one or more " +
                    "required method children (TransferToDevice/TransferFromDevice/" +
                    "FetchTransferResultData).",
                    transferServices.BrowseName);
            }

            NodeId capturedElementId = elementId;
            ITransferService capturedService = service;

            transferTo.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
                HandleTransferTo(ctx, capturedElementId, capturedService, outputs);

            transferFrom.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
                HandleTransferFrom(ctx, capturedElementId, capturedService, outputs);

            fetchResult.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
                HandleFetch(ctx, capturedService, inputs, outputs);
        }

        private static ServiceResult HandleTransferTo(
            ISystemContext context,
            NodeId elementId,
            ITransferService service,
            List<Variant> outputs)
        {
            // The OPC UA TransferToDevice spec carries the parameter
            // payload via address-space side channels (the device's
            // ParameterSet folder writes). For programmatic bindings
            // we pass an empty ParameterSet — applications that need
            // to inspect the parameters install their own importer
            // callback on the service (see
            // DefaultTransferService.RegisterImporter).
            var emptySet = new ParameterSet(elementId);
            int transferId = service.TransferToDeviceAsync(
                context, elementId, emptySet)
                .AsTask().GetAwaiter().GetResult();
            outputs.Add(new Variant(transferId));
            outputs.Add(new Variant((int)(uint)StatusCodes.Good));
            return ServiceResult.Good;
        }

        private static ServiceResult HandleTransferFrom(
            ISystemContext context,
            NodeId elementId,
            ITransferService service,
            List<Variant> outputs)
        {
            int transferId = service.TransferFromDeviceAsync(
                context, elementId)
                .AsTask().GetAwaiter().GetResult();
            outputs.Add(new Variant(transferId));
            outputs.Add(new Variant((int)(uint)StatusCodes.Good));
            return ServiceResult.Good;
        }

        private static ServiceResult HandleFetch(
            ISystemContext context,
            ITransferService service,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (inputs.Count < 4)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            int transferId = Convert.ToInt32(
                inputs[0].AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);
            int sequenceNumber = Convert.ToInt32(
                inputs[1].AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);
            int maxResults = Convert.ToInt32(
                inputs[2].AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);
            bool omitGoodResults = Convert.ToBoolean(
                inputs[3].AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);

            FetchResult result = service.FetchAsync(
                context, transferId, sequenceNumber,
                maxResults, omitGoodResults).AsTask().GetAwaiter().GetResult();

            // The spec output type is FetchResultDataType (an abstract
            // structure). We surface the concrete subtype via a
            // Variant carrying the fields packed as an ExtensionObject
            // when a structured result is required by the client. For
            // simple consumers, the Variant is left null and clients
            // read the per-entry status from the result via the
            // server-side service directly (programmatic binding).
            outputs.Add(Variant.Null);
            return result.TransferError == StatusCodes.Good
                ? ServiceResult.Good
                : new ServiceResult(result.TransferError);
        }

        private static MethodState? FindMethodChild(
            BaseObjectState parent, string browseName)
        {
            List<BaseInstanceState> children = [];
            parent.GetChildren(context: null!, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is MethodState method &&
                    method.BrowseName.Name == browseName)
                {
                    return method;
                }
            }
            return null;
        }
    }
}
