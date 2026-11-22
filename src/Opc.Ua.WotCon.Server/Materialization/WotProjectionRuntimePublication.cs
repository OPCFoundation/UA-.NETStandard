// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Receives the committed generation after a prepared projection runtime is published.
    /// </summary>
    internal interface IWotPublishedProjectionRuntime
    {
        /// <summary>
        /// Records the generation assigned to the published projection runtime.
        /// </summary>
        /// <param name="generation">The generation assigned to the committed publication.</param>
        void SetPublishedGeneration(long generation);
    }

    /// <summary>
    /// Forwards committed generation notifications to a projection runtime that supports publication tracking.
    /// </summary>
    internal sealed class WotProjectionRuntimePublication
    {
        /// <summary>
        /// Initializes a new <c>WotProjectionRuntimePublication</c> instance.
        /// </summary>
        /// <param name="options">The runtime node set options used to initialize the instance.</param>
        public WotProjectionRuntimePublication(RuntimeNodeSetOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.ConfigureAsync is { } configure)
            {
                options.ConfigureAsync = async (builder, cancellationToken) =>
                {
                    IAsyncDisposable? runtime = await configure(builder, cancellationToken).ConfigureAwait(false);
                    m_runtime = runtime as IWotPublishedProjectionRuntime;
                    return runtime;
                };
            }
        }

        /// <summary>
        /// Notifies the captured runtime that its projection has been published in the supplied generation.
        /// </summary>
        /// <param name="generation">The generation assigned to the committed publication.</param>
        public void Publish(long generation)
        {
            m_runtime?.SetPublishedGeneration(generation);
        }

        private IWotPublishedProjectionRuntime? m_runtime;
    }

    /// <summary>
    /// The per-generation OPC UA projection binding runtime wired onto a
    /// freshly imported NodeSet by <see cref="WotProjectionBindingRuntimeFactory"/>.
    /// It groups executable, target-mapped compiled forms by their resolved
    /// target variable, wires either a direct (whole-value) or a structured
    /// (field-by-field) handler per group, and owns every channel it
    /// opens for the lifetime of the generation. Local Method and EventType
    /// identities are resolved independently of those property target mappings.
    /// Event subscriptions and Condition occurrence routes share that ownership.
    /// </summary>
    public sealed partial class WotProjectionBindingRuntime
    {
        void IWotPublishedProjectionRuntime.SetPublishedGeneration(long generation)
        {
            foreach (WotProjectedEventBinding binding in m_events.Values)
            {
                binding.SetPublishedGeneration(generation);
            }
        }
    }
}
