/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Adds only GMS result behavior to an existing typed Machinery builder; its owner registers the root once.
    /// </summary>
    public static class GmsResultBuilderExtensions
    {
        /// <summary>
        /// Binds the mandatory GMS result subtree to an explicit per-instance provider.
        /// After BuildAsync, obtain the publisher with binding.CreatePublisher(machine).
        /// No fallback resolves a provider from another machine or the application's service container.
        /// </summary>
        public static IMachineBuilder<GMSState> WithGmsResults(
            this IMachineBuilder<GMSState> builder,
            IGmsResultStore store,
            out GmsResultBinding binding,
            GmsResultBindingOptions? options = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (store == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "GMS result management requires its own result provider.");
            }
            GmsResultBindingOptions selected = options ?? new GmsResultBindingOptions();
            selected.Validate();
            selected = selected with
            {
                Corrections = selected.Corrections.IsNull
                    ? default
                    : new ArrayOf<GmsCorrection>(selected.Corrections.ToArray()!)
            };
            GMSResultManagementState management = builder.State.ResultManagement
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "The canonical GMS result-management descendant is missing.");
            var created = new GmsResultBinding(builder.State, builder.BuildContext.Context, store, selected);
            try
            {
                builder.WithResultManagement(management, results => created.Configure(results, builder.BuildContext.Context));
                builder.WithRegistrationBinding((_, _, cancellationToken) => created.ActivateAsync(cancellationToken));
            }
            catch
            {
                created.Dispose();
                throw;
            }
            binding = created;
            return builder;
        }
    }
}
