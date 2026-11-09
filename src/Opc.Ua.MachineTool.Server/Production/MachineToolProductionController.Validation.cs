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

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolProductionController
    {
        /// <summary>
        /// Validates one full coherent observation before any generated factory or live write is invoked.
        /// </summary>
        private void ValidateProduction(MachineToolSnapshot snapshot)
        {
            ArrayOf<MachineToolProductionJob> jobs = snapshot.Production.Jobs;
            if (!Has(MachineToolFeatures.Production) && jobs.Count != 0)
            {
                throw Invalid("A production plan was supplied without enabling Production.");
            }
            if (Has(MachineToolFeatures.Production) &&
                !Has(MachineToolFeatures.DynamicProductionPlan) && jobs.Count == 0)
            {
                throw Invalid("A static production plan requires at least one job.");
            }
            Bound(jobs.Count, m_options.MaximumJobs);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var parts = new HashSet<string>(StringComparer.Ordinal);
            int total = jobs.Count;
            bool activeFound = false;
            foreach (MachineToolProductionJob job in jobs)
            {
                if (job == null)
                {
                    throw Invalid("The production plan contains a null job.");
                }
                Unique(keys, Key(job));
                Unique(identifiers, job.Identifier);
                ValidateText(job.CustomerOrderIdentifier, false);
                ValidateText(job.OrderIdentifier, false);
                ValidatePhase(job.Phase);
                ValidateSample(job.Sample);
                if (job.RunsPlannedIsValid && (job.RunsCompleted > job.RunsPlanned ||
                    (job.Phase == MachineToolProductionPhase.Running && job.RunsCompleted == job.RunsPlanned)))
                {
                    throw Invalid("Completed runs are inconsistent with the planned job.");
                }
                if (job.Phase == MachineToolProductionPhase.Ended &&
                    (!job.RunsPlannedIsValid || job.RunsCompleted != job.RunsPlanned))
                {
                    throw Invalid("An ended job must have completed all of its known planned runs.");
                }
                Bound(job.Programs.Count, m_options.MaximumProgramsPerJob);
                if (job.Programs.Count == 0)
                {
                    throw Invalid("Every production job requires at least one program.");
                }
                var programKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (MachineToolProgram program in job.Programs)
                {
                    if (program == null)
                    {
                        throw Invalid("A job contains a null program.");
                    }
                    Unique(programKeys, Key(program));
                    ValidateText(program.Name, true);
                    ValidatePhase(program.Phase);
                    ValidateSample(program.Sample);
                    if (Terminal(job.Phase) && program.Phase == MachineToolProductionPhase.Running)
                    {
                        throw Invalid("A terminal job cannot contain a running program.");
                    }
                    if (!string.IsNullOrEmpty(program.JobIdentifier) && program.JobIdentifier != job.Identifier)
                    {
                        throw Invalid("A program names a different containing job.");
                    }
                    if (snapshot.ActiveProgram.JobIdentifier == job.Identifier &&
                        Key(snapshot.ActiveProgram) == Key(program))
                    {
                        if (snapshot.ActiveProgram.Name != program.Name ||
                            snapshot.ActiveProgram.Phase != program.Phase ||
                            snapshot.ActiveProgram.TransitionSequence != program.TransitionSequence ||
                            snapshot.ActiveProgram.Sample != program.Sample)
                        {
                            throw Invalid("ActiveProgram must match the selected modeled program observation.");
                        }
                        activeFound = true;
                    }
                }
                total = checked(total + job.Programs.Count + job.PartSets.Count + job.Interruptions.Count);
                ValidateParts(job, parts, ref total);
                ValidateInterruptions(job);
                Bound(total, m_options.MaximumElements);
                if (m_jobs.TryGetValue(Key(job), out JobNodes? prior))
                {
                    ValidateJobDelta(prior, job);
                }
            }
            if (jobs.Count != 0 && !string.IsNullOrEmpty(snapshot.ActiveProgram.Name) && !activeFound)
            {
                throw Invalid("ActiveProgram does not identify a modeled job and program.");
            }
            if (!activeFound && !string.IsNullOrEmpty(snapshot.ActiveProgram.JobIdentifier))
            {
                throw Invalid("ActiveProgram identifies an absent job.");
            }
            foreach ((string key, JobNodes previous) in m_jobs)
            {
                if (!keys.Contains(key) && (!Has(MachineToolFeatures.DynamicProductionPlan) ||
                    !Terminal(previous.Observation.Phase) || HasActiveInterruption(previous.Observation)))
                {
                    throw Invalid("Only a finally observed terminal job without active interruptions can be removed.");
                }
            }
            if (m_observation != null && !Has(MachineToolFeatures.DynamicProductionPlan) &&
                jobs.Count != m_jobs.Count)
            {
                throw Invalid("A static plan cannot add or remove job slots.");
            }
            MachineToolProductionStatistics? statistics = snapshot.Production.Statistics;
            if (Has(MachineToolFeatures.ProductionStatistics) != (statistics != null))
            {
                throw Invalid("Statistics must be supplied exactly when the retained statistics feature is enabled.");
            }
            if (statistics != null)
            {
                ValidateSample(statistics.Sample);
                if (m_observation?.Production.Statistics is MachineToolProductionStatistics previous &&
                    statistics.PartsProducedInLifetime < previous.PartsProducedInLifetime)
                {
                    throw Invalid("Lifetime produced parts cannot decrease.");
                }
                if (snapshot.Operation.PartsProducedInLifetime is ulong modern &&
                    modern != statistics.PartsProducedInLifetime)
                {
                    throw Invalid(
                        "The retained and modern lifetime counters describe different equipment observations.");
                }
            }
        }

        /// <summary>
        /// Checks part inventories, quality categories and cumulative/current-run counter coherence.
        /// </summary>
        private void ValidateParts(MachineToolProductionJob job, HashSet<string> identifiers, ref int total)
        {
            Bound(job.PartSets.Count, m_options.MaximumPartSetsPerJob);
            if (!Has(MachineToolFeatures.ProductionParts) && job.PartSets.Count != 0)
            {
                throw Invalid("Part sets require ProductionParts.");
            }
            if (job.PartsGood > job.PartsCompleted ||
                job.PartsGoodBeforeCurrentRun > job.PartsCompletedBeforeCurrentRun)
            {
                throw Invalid("Good parts cannot exceed completed parts.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            ulong completed = job.PartsCompletedBeforeCurrentRun;
            ulong good = job.PartsGoodBeforeCurrentRun;
            bool allPartsModeled = job.PartSets.Count != 0;
            foreach (MachineToolProductionPartSet set in job.PartSets)
            {
                if (set == null)
                {
                    throw Invalid("A job contains a null part set.");
                }
                Unique(keys, set.Key);
                ValidateText(set.Name, false);
                ValidateSample(set.Sample);
                Bound(set.Parts.Count, m_options.MaximumPartsPerSet);
                total = checked(total + set.Parts.Count);
                if (set.PartsCompletedPerRun > set.PartsPlannedPerRun ||
                    (set.Parts.Count != 0 && set.Parts.Count != set.PartsPlannedPerRun))
                {
                    throw Invalid("The part set does not match its planned current-run quantity.");
                }
                uint modeledCompleted = 0;
                var partKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (MachineToolProductionPart part in set.Parts)
                {
                    if (part == null)
                    {
                        throw Invalid("A part set contains a null part.");
                    }
                    Unique(partKeys, part.Key);
                    ValidateText(part.Name, true);
                    ValidateText(part.Identifier, false);
                    ValidateText(part.CustomerOrderIdentifier, false);
                    if (!string.IsNullOrEmpty(part.Identifier))
                    {
                        Unique(identifiers, part.Identifier);
                    }
                    ValidatePhase(part.Phase);
                    ValidateSample(part.Sample);
                    if ((int)part.Quality is < 0 or > 4 || (int)part.Irregularity is < 0 or > 3)
                    {
                        throw Invalid("An unknown part-quality or process-irregularity category was supplied.");
                    }
                    if (Terminal(job.Phase) && part.Phase == MachineToolProductionPhase.Running)
                    {
                        throw Invalid("A terminal job cannot contain a running part.");
                    }
                    if (Terminal(part.Phase))
                    {
                        modeledCompleted++;
                        if (GoodPart(part.Quality))
                        {
                            good++;
                        }
                    }
                }
                if (set.Parts.Count == 0)
                {
                    allPartsModeled = false;
                }
                else if (modeledCompleted != set.PartsCompletedPerRun)
                {
                    throw Invalid("PartsCompletedPerRun must count the terminal individual parts.");
                }
                completed += set.PartsCompletedPerRun;
            }
            if (job.PartSets.Count != 0 && (completed != job.PartsCompleted ||
                (allPartsModeled && good != job.PartsGood) || good > job.PartsGood))
            {
                throw Invalid("Job counters disagree with the part sets and the specified good-quality categories.");
            }
        }

        /// <summary>
        /// Checks known reasons and prohibits resuming while any interruption remains active.
        /// </summary>
        private void ValidateInterruptions(MachineToolProductionJob job)
        {
            Bound(job.Interruptions.Count, m_options.MaximumInterruptionsPerJob);
            if (!Has(MachineToolFeatures.ProductionInterruptions) && job.Interruptions.Count != 0)
            {
                throw Invalid("Known interruption observations require ProductionInterruptions.");
            }
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (MachineToolProductionInterruption interruption in job.Interruptions)
            {
                if (interruption == null)
                {
                    throw Invalid("A job contains a null interruption.");
                }
                Unique(identifiers, interruption.Identifier);
                ValidateText(interruption.Message.Text, true);
                ValidateSample(interruption.Sample);
                if (interruption.Severity is < 1 or > 1000 ||
                    !m_context.Context.TypeTable.IsTypeOf(interruption.ConditionClassId,
                        Opc.Ua.ObjectTypeIds.BaseConditionClassType) ||
                    m_context.Manager.FindPredefinedNode(interruption.ConditionClassId) is not BaseObjectTypeState)
                {
                    throw Invalid("An interruption requires a registered ConditionClass subtype and valid severity.");
                }
            }
            if (job.Phase == MachineToolProductionPhase.Running && HasActiveInterruption(job))
            {
                throw Invalid("A job cannot run while an interruption remains active.");
            }
        }

        /// <summary>
        /// Checks exact transition/counter relationships and immutable final observations.
        /// </summary>
        private void ValidateJobDelta(JobNodes prior, MachineToolProductionJob job)
        {
            MachineToolProductionJob old = prior.Observation;
            uint transition = ValidateTransition(old.Phase, old.TransitionSequence, job.Phase, job.TransitionSequence);
            bool reset = transition is 2 or 8;
            if (reset && Has(MachineToolFeatures.DynamicProductionPlan))
            {
                throw Invalid("Terminal-to-initializing job replacement is only valid in a static plan.");
            }
            if (job.Identifier != old.Identifier && !reset)
            {
                throw Invalid("A job identifier can change only when a static terminal slot is reinitialized.");
            }
            ulong expectedRuns = reset ? 0 : (ulong)old.RunsCompleted + (transition is 1 or 3 ? 1u : 0u);
            if (job.RunsCompleted != expectedRuns)
            {
                throw Invalid("RunsCompleted must increase exactly once for RunningToRunning or RunningToEnded.");
            }
            if (!reset && old.Phase == MachineToolProductionPhase.Ended &&
                (job.PartsCompleted != old.PartsCompleted || job.PartsGood != old.PartsGood ||
                    job.RunsPlanned != old.RunsPlanned || job.RunsPlannedIsValid != old.RunsPlannedIsValid ||
                    job.OrderIdentifier != old.OrderIdentifier ||
                    job.CustomerOrderIdentifier != old.CustomerOrderIdentifier ||
                    OrderChanged(old.Programs, job.Programs, static program => Key(program))))
            {
                throw Invalid("An ended job's final values cannot be revised.");
            }
            if (transition == 3)
            {
                if (job.PartsCompletedBeforeCurrentRun != old.PartsCompleted ||
                    job.PartsGoodBeforeCurrentRun != old.PartsGood)
                {
                    throw Invalid("A new run must carry the prior run's final totals into the cumulative baseline.");
                }
            }
            else if (!reset && (job.PartsCompletedBeforeCurrentRun != old.PartsCompletedBeforeCurrentRun ||
                job.PartsGoodBeforeCurrentRun != old.PartsGoodBeforeCurrentRun))
            {
                throw Invalid("Historical part totals cannot change during a run.");
            }
            ValidateProgramDelta(prior, job, reset);
            ValidatePartDelta(prior, job, reset);
            foreach (MachineToolProductionInterruption previous in old.Interruptions)
            {
                bool found = false;
                foreach (MachineToolProductionInterruption current in job.Interruptions)
                {
                    found |= current.Identifier == previous.Identifier;
                }
                if (!found && previous.IsActive)
                {
                    throw Invalid("An active interruption must be cleared in an observation before it is removed.");
                }
            }
        }

        /// <summary>
        /// Validates program slot retention and state transitions.
        /// </summary>
        private void ValidateProgramDelta(JobNodes prior, MachineToolProductionJob job, bool reset)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (MachineToolProgram program in job.Programs)
            {
                keys.Add(Key(program));
                if (prior.Programs.TryGetValue(Key(program), out ProgramNodes? old))
                {
                    ValidateTransition(old.Observation.Phase, old.Observation.TransitionSequence,
                        program.Phase, program.TransitionSequence);
                    if (!reset && prior.Observation.Phase == MachineToolProductionPhase.Ended &&
                        (program.Name != old.Observation.Name || program.Phase != old.Observation.Phase ||
                            program.TransitionSequence != old.Observation.TransitionSequence))
                    {
                        throw Invalid("Programs of an ended job cannot change.");
                    }
                }
                else if (!Has(MachineToolFeatures.DynamicProductionPlan))
                {
                    throw Invalid("A static production plan cannot add program slots.");
                }
            }
            foreach ((string key, ProgramNodes old) in prior.Programs)
            {
                if (!keys.Contains(key) &&
                    (!Has(MachineToolFeatures.DynamicProductionPlan) || !Terminal(old.Observation.Phase)))
                {
                    throw Invalid("Only finally observed terminal programs can be removed from a dynamic plan.");
                }
            }
        }

        /// <summary>
        /// Validates part reset, final-quality retention and safe removal of current-run slots.
        /// </summary>
        private void ValidatePartDelta(JobNodes prior, MachineToolProductionJob job, bool reset)
        {
            var sets = new HashSet<string>(StringComparer.Ordinal);
            foreach (MachineToolProductionPartSet set in job.PartSets)
            {
                sets.Add(set.Key);
                if (!prior.PartSets.TryGetValue(set.Key, out PartSetNodes? oldSet))
                {
                    if (!Has(MachineToolFeatures.DynamicProductionPlan) ||
                        (!reset && prior.Observation.Phase == MachineToolProductionPhase.Ended))
                    {
                        throw Invalid("A static production plan cannot add part sets.");
                    }
                    continue;
                }
                if (!reset && prior.Observation.Phase == MachineToolProductionPhase.Ended &&
                    (set.Name != oldSet.Observation.Name ||
                        set.ContainsMixedParts != oldSet.Observation.ContainsMixedParts ||
                        set.PartsPlannedPerRun != oldSet.Observation.PartsPlannedPerRun ||
                        set.PartsCompletedPerRun != oldSet.Observation.PartsCompletedPerRun))
                {
                    throw Invalid("An ended job's final part-set values cannot be revised.");
                }
                var parts = new HashSet<string>(StringComparer.Ordinal);
                foreach (MachineToolProductionPart part in set.Parts)
                {
                    parts.Add(part.Key);
                    if (oldSet.Parts.TryGetValue(part.Key, out PartNodes? old))
                    {
                        uint transition = ValidateTransition(old.Observation.Phase, old.Observation.TransitionSequence,
                            part.Phase, part.TransitionSequence);
                        if (part.Identifier != old.Observation.Identifier && transition is not (2 or 8))
                        {
                            throw Invalid("A physical part identifier can change only when its slot is reinitialized.");
                        }
                        if (!reset && prior.Observation.Phase == MachineToolProductionPhase.Ended &&
                            (part with { Sample = old.Observation.Sample }) != old.Observation)
                        {
                            throw Invalid("A finished job's final part observations cannot be revised.");
                        }
                    }
                    else if (!Has(MachineToolFeatures.DynamicProductionPlan))
                    {
                        throw Invalid("A static production plan cannot add part slots.");
                    }
                }
                foreach ((string key, PartNodes old) in oldSet.Parts)
                {
                    if (!parts.Contains(key) && (!Has(MachineToolFeatures.DynamicProductionPlan) ||
                        !Terminal(old.Observation.Phase)))
                    {
                        throw Invalid("Only finally observed terminal parts can be removed from a dynamic plan.");
                    }
                }
            }
            foreach ((string key, PartSetNodes oldSet) in prior.PartSets)
            {
                if (!sets.Contains(key))
                {
                    if (!Has(MachineToolFeatures.DynamicProductionPlan) ||
                        oldSet.Observation.PartsCompletedPerRun != oldSet.Observation.PartsPlannedPerRun)
                    {
                        throw Invalid("Only a finally observed completed part set can be removed from a dynamic plan.");
                    }
                }
            }
        }

        /// <summary>
        /// Counts only the quality categories explicitly named in section 8.4.3.
        /// </summary>
        private static bool GoodPart(PartQuality quality) =>
            quality is PartQuality.Good or PartQuality.CapabilityUnavailable or PartQuality.WillNotBeMeasured;

        /// <summary>
        /// Tests whether an operation has a final observable state.
        /// </summary>
        private static bool Terminal(MachineToolProductionPhase phase) =>
            phase is MachineToolProductionPhase.Ended or MachineToolProductionPhase.Aborted;

        /// <summary>
        /// Tests all known reasons rather than only the interruption that first stopped the job.
        /// </summary>
        private static bool HasActiveInterruption(MachineToolProductionJob job)
        {
            foreach (MachineToolProductionInterruption interruption in job.Interruptions)
            {
                if (interruption.IsActive)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rejects duplicate identities instead of silently selecting an arbitrary node.
        /// </summary>
        private void Unique(HashSet<string> values, string value)
        {
            ValidateText(value, true);
            if (!values.Add(value))
            {
                throw Invalid($"Duplicate production identity '{value}'.");
            }
        }

        /// <summary>
        /// Rejects observations that exceed a declared bound.
        /// </summary>
        private static void Bound(int count, int maximum)
        {
            if (count > maximum)
            {
                throw Invalid("A production inventory exceeds its configured bound.");
            }
        }
    }
}
