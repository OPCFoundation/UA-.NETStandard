/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;

namespace UaLens.Plugins.PubSub;

/// <summary>
/// Separates reaching a source-sampling limit from completing its last publication.
/// The runtime owns the writer; this adapter only observes its existing publish hook.
/// </summary>
internal sealed class PubSubPublicationDrain
{
    public PubSubPublicationDrain(
        WriterGroup writer,
        PubSubBoundedSource source,
        PubSubConfiguration configuration,
        PubSubObservationStore observations,
        TimeProvider clock)
    {
        m_writer = writer ?? throw new ArgumentNullException(nameof(writer));
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentNullException.ThrowIfNull(configuration);
        m_observations = observations ?? throw new ArgumentNullException(nameof(observations));
        m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
        m_publish = writer.PublishSink ?? throw new InvalidOperationException("The writer has no transport sink.");
        m_drainLocalReader = configuration.Publication == PubSubPublication.Synthetic &&
            configuration.ReceiveEnabled && !configuration.WriteBackEnabled &&
            PubSubIdentity.Local(configuration) == PubSubIdentity.Filter(configuration) &&
            configuration.Profile == PubSubProfile.UdpUadp &&
            Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out Uri? endpoint) && endpoint.IsLoopback;
        writer.PublishSink = PublishAsync;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(s_drainTimeout, m_clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await m_published.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            await m_writer.DisableAsync(linked.Token).ConfigureAwait(false);
            if (m_drainLocalReader)
            {
                await m_observations.WaitForDataSetsAsync(m_source.Samples, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            m_observations.RecordEvidence("Publication drain", StatusCodes.BadTimeout,
                "The bounded final-publication/loopback drain expired. Sent or sampled does not prove delivery.");
        }
    }

    private async ValueTask PublishAsync(PubSubNetworkMessage message, CancellationToken cancellationToken)
    {
        await m_publish(message, cancellationToken).ConfigureAwait(false);
        if (m_source.Completed.IsCompletedSuccessfully)
        {
            m_published.TrySetResult();
        }
    }

    private readonly WriterGroup m_writer;
    private readonly PubSubBoundedSource m_source;
    private readonly PubSubObservationStore m_observations;
    private readonly TimeProvider m_clock;
    private readonly Func<PubSubNetworkMessage, CancellationToken, ValueTask> m_publish;
    private readonly bool m_drainLocalReader;
    private readonly TaskCompletionSource m_published = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TimeSpan s_drainTimeout = TimeSpan.FromSeconds(2);
}
