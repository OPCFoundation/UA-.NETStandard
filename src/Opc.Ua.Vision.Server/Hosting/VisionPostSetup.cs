/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Vision.Perception;

namespace Opc.Ua.Vision.Server.Hosting
{
    /// <summary>
    /// Runs Vision startup configurators.
    /// </summary>
    public interface IVisionPostSetupRunner
    {
        /// <summary>
        /// Runs configurators for a node manager.
        /// </summary>
        ValueTask RunAsync(
            AsyncCustomNodeManager manager,
            VisionRootState root,
            VisionServerOptions options,
            CancellationToken cancellationToken);
    }

    internal interface IVisionPostSetupConfigurator
    {
        Type TargetManagerType { get; }

        ValueTask RunAsync(IVisionBuildContext context);
    }

    internal sealed class VisionPostSetupRunner : IVisionPostSetupRunner
    {
        public VisionPostSetupRunner(
            IServiceProvider services,
            IEnumerable<IVisionPostSetupConfigurator> configurators,
            IEnumerable<VisionMediaProviderRegistration> mediaRegistrations,
            IEnumerable<VisionInferenceProviderRegistration> inferenceRegistrations,
            IEnumerable<VisionFeedbackSinkRegistration> feedbackRegistrations)
        {
            m_services = services;
            m_configurators = configurators.ToArray().ToArrayOf();
            MediaRegistrations = mediaRegistrations.ToArray().ToArrayOf();
            InferenceRegistrations = inferenceRegistrations.ToArray().ToArrayOf();
            FeedbackRegistrations = feedbackRegistrations.ToArray().ToArrayOf();
        }

        public async ValueTask RunAsync(
            AsyncCustomNodeManager manager,
            VisionRootState root,
            VisionServerOptions options,
            CancellationToken cancellationToken)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (manager is not VisionNodeManager visionManager)
            {
                return;
            }
            VisionBuildContext context = visionManager.CreateBuildContextCore(cancellationToken);
            for (int ii = 0; ii < m_configurators.Count; ii++)
            {
                IVisionPostSetupConfigurator configurator = m_configurators[ii];
                if (configurator.TargetManagerType.IsAssignableFrom(manager.GetType()))
                {
                    await configurator.RunAsync(context).ConfigureAwait(false);
                    await context.FlushPendingRegistrationsAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            await context.FlushPendingRegistrationsAsync(cancellationToken).ConfigureAwait(false);
        }

        internal ArrayOf<VisionMediaProviderRegistration> MediaRegistrations { get; }

        internal ArrayOf<VisionInferenceProviderRegistration> InferenceRegistrations { get; }

        internal ArrayOf<VisionFeedbackSinkRegistration> FeedbackRegistrations { get; }

        private readonly IServiceProvider m_services;
        private readonly ArrayOf<IVisionPostSetupConfigurator> m_configurators;
    }

    internal sealed class VisionMediaProviderRegistration
    {
        public VisionMediaProviderRegistration(string sensorBrowseName, IVisionMediaProvider provider)
        {
            SensorBrowseName = sensorBrowseName ?? throw new ArgumentNullException(nameof(sensorBrowseName));
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public string SensorBrowseName { get; }

        public IVisionMediaProvider Provider { get; }
    }

    internal sealed class VisionInferenceProviderRegistration
    {
        public VisionInferenceProviderRegistration(
            string pipelineBrowseName,
            IVisionInferenceProvider provider,
            bool onServer)
        {
            PipelineBrowseName = pipelineBrowseName ??
                throw new ArgumentNullException(nameof(pipelineBrowseName));
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            OnServer = onServer;
        }

        public string PipelineBrowseName { get; }

        public IVisionInferenceProvider Provider { get; }

        public bool OnServer { get; }
    }

    internal sealed class VisionFeedbackSinkRegistration
    {
        public VisionFeedbackSinkRegistration(string pipelineBrowseName, IVisionFeedbackSink sink)
        {
            PipelineBrowseName = pipelineBrowseName ??
                throw new ArgumentNullException(nameof(pipelineBrowseName));
            Sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public string PipelineBrowseName { get; }

        public IVisionFeedbackSink Sink { get; }
    }
}
