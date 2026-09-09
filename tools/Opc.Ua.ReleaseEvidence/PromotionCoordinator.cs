// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal interface IPromotionEligibility
    {
        Task<VerifiedPromotion> VerifyAsync(CancellationToken cancellationToken);
    }

    internal sealed class PromotionCoordinator(
        IPromotionEligibility eligibility,
        IPromotionTransport transport,
        IPromotionJournal journal,
        TimeProvider clock)
    {
        public async Task<PromotionResult> PromoteAsync(CancellationToken cancellationToken)
        {
            VerifiedPromotion verified = await eligibility.VerifyAsync(cancellationToken).ConfigureAwait(false);
            PromotionRequest request = verified.Request;
            if (transport.IsOfficial && !verified.OfficialTransportAuthorized)
            {
                throw new PromotionRejectedException("Official transport authorization is not configured.");
            }
            IPromotionLease lease = await transport.AcquireLeaseAsync(
                request.Group, request.Destination, cancellationToken).ConfigureAwait(false);
            await using System.Runtime.CompilerServices.ConfiguredAsyncDisposable leaseLifetime =
                lease.ConfigureAwait(false);
            // Preflight the entire approved group before making any destination mutation.
            foreach (PromotionMember member in request.Members)
            {
                PromotionObservation observed = await transport.ReadAsync(member, cancellationToken)
                    .ConfigureAwait(false);
                CheckCollision(member, observed);
                await CheckAliasesAsync(member, false, cancellationToken).ConfigureAwait(false);
            }
            int completed = 0;
            foreach (PromotionMember member in request.Members)
            {
                await RefreshAsync(verified, lease, cancellationToken).ConfigureAwait(false);
                PromotionObservation observed = await transport.ReadAsync(member, cancellationToken)
                    .ConfigureAwait(false);
                CheckCollision(member, observed);
                if (observed.ContentDigest == null)
                {
                    await AppendAsync(verified, member, "create-started", observed, lease, null, cancellationToken)
                        .ConfigureAwait(false);
                    await RefreshAsync(verified, lease, cancellationToken).ConfigureAwait(false);
                    await transport.CreateImmutableAsync(
                        member, verified.CandidateRoot, lease, cancellationToken).ConfigureAwait(false);
                }
                observed = await transport.ReadAsync(member, cancellationToken).ConfigureAwait(false);
                CheckContent(member, observed);
                await AppendAsync(verified, member, "content-readback", observed, lease, null, cancellationToken)
                    .ConfigureAwait(false);
                if (!HasEvidence(member, observed))
                {
                    await AppendAsync(verified, member, "evidence-started", observed, lease, null, cancellationToken)
                        .ConfigureAwait(false);
                    await RefreshAsync(verified, lease, cancellationToken).ConfigureAwait(false);
                    await transport.RestoreEvidenceAsync(
                        member, verified.CandidateRoot, lease, cancellationToken).ConfigureAwait(false);
                }
                observed = await transport.ReadAsync(member, cancellationToken).ConfigureAwait(false);
                CheckContent(member, observed);
                if (!HasEvidence(member, observed))
                {
                    throw new PromotionRejectedException(
                        "Destination evidence/referrers are not discoverable after transfer.");
                }
                await AppendAsync(verified, member, "evidence-readback", observed, lease, null, cancellationToken)
                    .ConfigureAwait(false);
                foreach (PromotionAlias alias in PromotionRequestValidator.GetAliases(member))
                {
                    PromotionMember target = SelectAlias(member, alias);
                    observed = await transport.ReadAsync(target, cancellationToken).ConfigureAwait(false);
                    CheckContent(member, observed);
                    if (!HasEvidence(member, observed))
                    {
                        throw new PromotionRejectedException("Required evidence disappeared before alias mutation.");
                    }
                    CheckAliasPrecondition(member, alias, observed);
                    if (observed.AliasDigest != member.Content.Digest)
                    {
                        await AppendAsync(verified, member, "alias-started", observed, lease, alias, cancellationToken)
                            .ConfigureAwait(false);
                        await RefreshAsync(verified, lease, cancellationToken).ConfigureAwait(false);
                        await transport.CompareExchangeAliasAsync(
                            target, alias.ExpectedDigest, lease, cancellationToken).ConfigureAwait(false);
                    }
                    observed = await transport.ReadAsync(target, cancellationToken).ConfigureAwait(false);
                    CheckContent(member, observed);
                    if (!HasEvidence(member, observed) || observed.AliasDigest != member.Content.Digest)
                    {
                        throw new PromotionRejectedException("An alias readback lost content or required evidence.");
                    }
                    await AppendAsync(verified, member, "alias-readback", observed, lease, alias, cancellationToken)
                        .ConfigureAwait(false);
                }
                observed = await transport.ReadAsync(member, cancellationToken).ConfigureAwait(false);
                CheckContent(member, observed);
                if (!HasEvidence(member, observed))
                {
                    throw new PromotionRejectedException("Delivery readback lost evidence or the authorized alias.");
                }
                await AppendAsync(verified, member, "member-verified", observed, lease, null, cancellationToken)
                    .ConfigureAwait(false);
                completed++;
            }
            await RefreshAsync(verified, lease, cancellationToken).ConfigureAwait(false);
            foreach (PromotionMember member in request.Members)
            {
                PromotionObservation observed = await transport.ReadAsync(member, cancellationToken)
                    .ConfigureAwait(false);
                CheckContent(member, observed);
                if (!HasEvidence(member, observed))
                {
                    throw new PromotionRejectedException("The group changed before its final readback.");
                }
                await CheckAliasesAsync(member, true, cancellationToken).ConfigureAwait(false);
            }
            return new PromotionResult(
                1, "complete", verified.RequestDigest, completed, transport.IsOfficial);
        }

        private async Task RefreshAsync(
            VerifiedPromotion original,
            IPromotionLease lease,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifiedPromotion current = await eligibility.VerifyAsync(cancellationToken).ConfigureAwait(false);
            if (current.RequestDigest != original.RequestDigest ||
                current.CandidateRoot != original.CandidateRoot ||
                (transport.IsOfficial && !current.OfficialTransportAuthorized))
            {
                throw new PromotionRejectedException("Candidate or current promotion authorization changed.");
            }
            await lease.AssertHeldAsync(cancellationToken).ConfigureAwait(false);
        }

        private Task AppendAsync(
            VerifiedPromotion verified,
            PromotionMember member,
            string operation,
            PromotionObservation observed,
            IPromotionLease lease,
            PromotionAlias? alias,
            CancellationToken cancellationToken)
        {
            PromotionRequest request = verified.Request;
            return journal.AppendAsync(new PromotionEvent(
                1, Guid.NewGuid().ToString("N"), verified.RequestDigest,
                request.CandidateDigest, request.EvidenceDigest, request.IntentDigest, request.PolicyDigest,
                request.SourceSha, request.RunId, request.Attempt, request.Group, member.Destination,
                member.Id, operation, observed.ContentDigest ?? member.Content.Digest,
                observed.EvidenceDigests, lease.Fence, clock.GetUtcNow().ToString("O"),
                member.Kind, member.Platform, alias?.Name, alias?.ExpectedDigest, alias?.Immutable), cancellationToken);
        }

        private async Task CheckAliasesAsync(
            PromotionMember member,
            bool requireDelivery,
            CancellationToken cancellationToken)
        {
            foreach (PromotionAlias alias in PromotionRequestValidator.GetAliases(member))
            {
                PromotionObservation observed = await transport.ReadAsync(
                    SelectAlias(member, alias), cancellationToken).ConfigureAwait(false);
                CheckCollision(member, observed);
                if (requireDelivery)
                {
                    CheckContent(member, observed);
                    if (observed.AliasDigest != member.Content.Digest || !HasEvidence(member, observed))
                    {
                        throw new PromotionRejectedException("A required alias changed before final group readback.");
                    }
                }
                else
                {
                    CheckAliasPrecondition(member, alias, observed);
                }
            }
        }

        private static PromotionMember SelectAlias(PromotionMember member, PromotionAlias alias)
        {
            return member with { Alias = alias.Name, ExpectedAliasDigest = alias.ExpectedDigest, Aliases = null };
        }

        private static void CheckAliasPrecondition(
            PromotionMember member,
            PromotionAlias alias,
            PromotionObservation observed)
        {
            if (alias.Immutable && observed.AliasDigest != null && observed.AliasDigest != member.Content.Digest)
            {
                throw new PromotionRejectedException("An immutable version alias already identifies different bytes.");
            }
            if (observed.AliasDigest != member.Content.Digest && observed.AliasDigest != alias.ExpectedDigest)
            {
                throw new PromotionRejectedException("Alias precondition does not match destination state.");
            }
        }

        private static bool HasEvidence(PromotionMember member, PromotionObservation observed)
        {
            return member.Evidence.Select(e => e.Digest).Order(StringComparer.Ordinal)
                .SequenceEqual(observed.EvidenceDigests.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        }

        private static void CheckContent(PromotionMember member, PromotionObservation observed)
        {
            if (observed.ContentDigest != member.Content.Digest)
            {
                throw new PromotionRejectedException("Destination readback differs from the exact approved digest.");
            }
        }

        private static void CheckCollision(PromotionMember member, PromotionObservation observed)
        {
            if (observed.ContentDigest != null && observed.ContentDigest != member.Content.Digest)
            {
                throw new PromotionRejectedException("Immutable version collision; overwriting is prohibited.");
            }
        }
    }
}
