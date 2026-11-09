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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
