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

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Carries the typed parameters of SetCyclicJobData to the device authority.
    /// </summary>
    public sealed record CyclicJobConfiguration
    {
        /// <summary>
        /// Gets the common job name.
        /// </summary>
        public required string JobName { get; init; }

        /// <summary>
        /// Gets the free-form job description.
        /// </summary>
        public string? JobDescription { get; init; }

        /// <summary>
        /// Gets the customer name.
        /// </summary>
        public string? CustomerName { get; init; }

        /// <summary>
        /// Gets the production dataset identifier.
        /// </summary>
        public string? ProductionDatasetName { get; init; }

        /// <summary>
        /// Gets the production dataset description.
        /// </summary>
        public string? ProductionDatasetDescription { get; init; }

        /// <summary>
        /// Gets the material descriptions in declared order.
        /// </summary>
        public ArrayOf<string> Material { get; init; }

        /// <summary>
        /// Gets the product names in declared order.
        /// </summary>
        public ArrayOf<string> ProductName { get; init; }

        /// <summary>
        /// Gets the product descriptions in declared order.
        /// </summary>
        public ArrayOf<string> ProductDescription { get; init; }

        /// <summary>
        /// Gets whether the device continues after completing this job.
        /// </summary>
        public bool ContinueAtJobEnd { get; init; }

        /// <summary>
        /// Gets the nominal job quantity.
        /// </summary>
        public ulong NominalParts { get; init; }

        /// <summary>
        /// Gets the nominal box quantity.
        /// </summary>
        public ulong NominalBoxParts { get; init; }

        /// <summary>
        /// Gets the expected cycle duration in milliseconds.
        /// </summary>
        public double ExpectedCycleTime { get; init; }

        /// <summary>
        /// Gets the mould identifier.
        /// </summary>
        public string? MouldId { get; init; }

        /// <summary>
        /// Gets the declared cavity count.
        /// </summary>
        public uint NumCavities { get; init; }
    }

    /// <summary>
    /// Commits a complete cyclic-job configuration before it is published.
    /// </summary>
    public interface ICyclicJobConfigurationProvider
    {
        /// <summary>
        /// Applies the typed common job configuration at the device's durable control boundary.
        /// </summary>
        ValueTask SetAsync(
            PlasticsRubberRequestContext context,
            CyclicJobConfiguration configuration,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds the typed common SetCyclicJobData method and publishes only accepted configuration.
    /// </summary>
    public sealed class CyclicJobInformationBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds an actual generated cyclic-job information instance.
        /// </summary>
        public CyclicJobInformationBinding(
            CyclicJobInformationState node,
            ISystemContext context,
            ICyclicJobConfigurationProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            SetCyclicJobDataMethodState method = node.SetCyclicJobData ??
                throw new ArgumentException("The SetCyclicJobData method was not selected.", nameof(node));
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            BaseVariableState?[] publication =
            [
                node.JobName, node.JobDescription, node.CustomerName, node.ProductionDatasetName,
                node.ProductionDatasetDescription, node.Material, node.ProductName, node.ProductDescription,
                node.ContinueAtJobEnd, node.NominalParts, node.NominalBoxParts, node.ExpectedCycleTime,
                node.MouldId, node.NumCavities
            ];
            foreach (BaseVariableState? field in publication)
            {
                if (field != null)
                {
                    field.AccessLevel = AccessLevels.CurrentRead;
                    field.UserAccessLevel = AccessLevels.CurrentRead;
                }
            }
            method.OnCallAsync = async (
                call, called, target, name, description, customer, datasetName, datasetDescription,
                material, products, productDescriptions, continueAtEnd, nominalParts, nominalBoxParts,
                cycleTime, mouldId, cavities, ct) => new SetCyclicJobDataMethodStateResult
                {
                    ServiceResult = await m_binding.ExecuteAsync(
                        call, target, called.NodeId,
                        async (request, token) =>
                        {
                            if (string.IsNullOrWhiteSpace(name) || cycleTime < 0 ||
                                double.IsNaN(cycleTime) || double.IsInfinity(cycleTime))
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadInvalidArgument,
                                        "A job name and finite nonnegative cycle duration are required.");
                            }
                            var configuration = new CyclicJobConfiguration
                            {
                                JobName = name,
                                JobDescription = description,
                                CustomerName = customer,
                                ProductionDatasetName = datasetName,
                                ProductionDatasetDescription = datasetDescription,
                                Material = material.ToArray().ToArrayOf(),
                                ProductName = products.ToArray().ToArrayOf(),
                                ProductDescription = productDescriptions.ToArray().ToArrayOf(),
                                ContinueAtJobEnd = continueAtEnd,
                                NominalParts = nominalParts,
                                NominalBoxParts = nominalBoxParts,
                                ExpectedCycleTime = cycleTime,
                                MouldId = mouldId,
                                NumCavities = cavities
                            };
                            await provider.SetAsync(request, configuration, token).ConfigureAwait(false);
                            Set(node.JobName, name);
                            Set(node.JobDescription, description);
                            Set(node.CustomerName, customer);
                            Set(node.ProductionDatasetName, datasetName);
                            Set(node.ProductionDatasetDescription, datasetDescription);
                            Set(node.Material, configuration.Material);
                            Set(node.ProductName, configuration.ProductName);
                            Set(node.ProductDescription, configuration.ProductDescription);
                            Set(node.ContinueAtJobEnd, continueAtEnd);
                            Set(node.NominalParts, nominalParts);
                            Set(node.NominalBoxParts, nominalBoxParts);
                            Set(node.ExpectedCycleTime, cycleTime);
                            Set(node.MouldId, mouldId);
                            Set(node.NumCavities, cavities);
                            node.ClearChangeMasks(context, true);
                        },
                        ct).ConfigureAwait(false)
                };
            m_binding.Track(method);
        }

        /// <summary>
        /// Gets the generated cyclic-job information.
        /// </summary>
        public CyclicJobInformationState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Updates a selected generated property without creating absent optional metadata.
        /// </summary>
        /// <typeparam name="T">The declared property value type.</typeparam>
        private static void Set<T>(PropertyState<T>? property, T value)
        {
            if (property != null)
            {
                property.Value = value;
                property.StatusCode = StatusCodes.Good;
            }
        }

        /// <summary>
        /// The common operation guard and lifetime.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
