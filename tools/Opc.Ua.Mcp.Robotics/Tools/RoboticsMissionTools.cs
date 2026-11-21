// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for Robot Intent missions.
    /// </summary>
    [McpServerToolType]
    public sealed class RoboticsMissionTools
    {
        /// <summary>
        /// Builds and submits a mission from typed step/transition DTOs.
        /// </summary>
        [McpServerTool(Name = "robotics_submit_mission")]
        [Description("Submits typed mission steps; returns the server result.")]
        public static async Task<MissionSubmissionResult> SubmitMissionAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("MissionId to submit.")] string missionId,
            [Description("MissionUpdateId for this submission.")] uint missionUpdateId,
            [Description("Array of mission steps. Each step has stepId, released, and intent with kind discriminator.")]
            MissionStepInput[] steps,
            [Description("Optional array of transitions; an omitted/empty array is a flat ordered mission.")]
            MissionTransitionInput[]? transitions = null,
            [Description("Optional localized label text.")] string? label = null,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RoboticsResolutionContext context = await RoboticsResolutionContext.CreateAsync(
                manager, controller, sessionName, ct).ConfigureAwait(false);
            MissionDataType mission = BuildMission(
                missionId, missionUpdateId, steps, transitions, label, context.Scope);
            return await context.Client.SubmitMissionAsync(mission, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates a mission horizon from typed step DTOs.
        /// </summary>
        [McpServerTool(Name = "robotics_update_mission")]
        [Description("Replaces a mission horizon; returns the server result.")]
        public static async Task<MissionUpdateOutcome> UpdateMissionAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("MissionId to update.")] string missionId,
            [Description("Strictly increasing MissionUpdateId.")] uint missionUpdateId,
            [Description("Array of replacement horizon steps.")]
            MissionStepInput[] horizonSteps,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RoboticsResolutionContext context = await RoboticsResolutionContext.CreateAsync(
                manager, controller, sessionName, ct).ConfigureAwait(false);
            ArrayOf<MissionStepDataType> steps = RoboticsIntentDtoConverter.ConvertMissionSteps(
                horizonSteps, context.Scope);
            return await context.Client.UpdateMissionAsync(missionId, missionUpdateId, steps, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels a mission.
        /// </summary>
        [McpServerTool(Name = "robotics_cancel_mission")]
        [Description("Requests cancellation of a mission. A server refusal such as NotPermittedInMode or " +
            "ControlNotOwned is returned by the client API; this tool never retries or submits compensating work.")]
        public static async Task<IntentCommandOutcome> CancelMissionAsync(
            RoboticsIntentManager manager,
            [Description(RoboticsControlTools.ControllerDescription)] string controller,
            [Description("MissionId to cancel.")] string missionId,
            [Description("Stop mode requested from the server.")] StopModeEnum stopMode = StopModeEnum.QuickStop,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            RobotIntentControllerClient resolved = await manager.ResolveControllerAsync(
                controller, sessionName, ct).ConfigureAwait(false);
            return await resolved.CancelMissionAsync(missionId, stopMode, ct).ConfigureAwait(false);
        }

        internal static MissionDataType BuildMission(
            string missionId,
            uint missionUpdateId,
            MissionStepInput[] steps,
            MissionTransitionInput[]? transitions,
            string? label,
            RoboticsScopeResolver? scope)
        {
            MissionBuilder builder = RobotIntentBuilder.Mission(missionId).WithMissionUpdateId(missionUpdateId);
            if (!string.IsNullOrEmpty(label))
            {
                builder.WithLabel(new LocalizedText(label));
            }

            ArrayOf<MissionStepDataType> convertedSteps =
                RoboticsIntentDtoConverter.ConvertMissionSteps(steps, scope);
            ArrayOf<MissionTransitionDataType> convertedTransitions =
                RoboticsIntentDtoConverter.ConvertMissionTransitions(transitions);
            builder.WithSteps(convertedSteps)
                .WithTransitions(convertedTransitions);
            return builder.Build();
        }
    }
}
