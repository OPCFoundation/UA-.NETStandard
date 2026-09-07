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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

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
