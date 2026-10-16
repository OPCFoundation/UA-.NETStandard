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

import {
    fetchHistory,
    fetchHolds,
    fetchInspector,
    fetchMaterial,
    fetchSession,
    fetchState,
    openStateStream,
    postCommand
} from "./api.js";
import { hasValue, sumStageString } from "./format.js";
import {
    buildCommand,
    normalizeState,
    renderExceptionDock,
    renderHolds,
    renderInspector,
    renderMaterial,
    renderPlantQuality,
    renderProcessList,
    renderSchematic,
    renderSessionStatus,
    renderSystemBanner,
    renderTopSummary,
    renderTrends
} from "./render.js";

const app = document.querySelector("#app");
const connectForm = document.querySelector("#connect-form");
const connectMessage = document.querySelector("#connect-message");
const schematic = document.querySelector("#schematic");
const processList = document.querySelector("#process-list");
const topSummary = document.querySelector("#top-summary");
const sessionStatus = document.querySelector("#session-status");
const systemBanner = document.querySelector("#system-banner");
const plantQuality = document.querySelector("#plant-quality");
const inspector = document.querySelector("#inspector");
const trendsContent = document.querySelector("#trends-content");
const materialContent = document.querySelector("#material-content");
const holdsContent = document.querySelector("#holds-content");
const faultList = document.querySelector("#fault-list");
const holdList = document.querySelector("#hold-list");
const commandOutcomes = document.querySelector("#command-outcomes");
const commandDialog = document.querySelector("#command-dialog");
const commandDialogCopy = document.querySelector("#command-dialog-copy");
const commandDialogPayload = document.querySelector("#command-dialog-payload");

const state = {
    session: null,
    plant: null,
    selectedProcess: "Collection",
    inspector: null,
    eventSource: null,
    liveDelivery: false,
    streamGeneration: 0,
    streamRetryTimer: null,
    streamRetryDelay: 1000,
    browserStreamStatus: "Disconnected",
    browserStreamDetail: "",
    previousTransfers: new Map(),
    activeTransfers: new Set(),
    commandEnvelopes: new Map(),
    pendingCommandKeys: new Set(),
    activeConfirmation: null,
    materialPage: null,
    historyPage: null,
    materialAfter: "0",
    historyAfter: "0",
    holdsPage: null,
    holdsAfter: "0",
    holdsPrevious: [],
    holdsFilter: "Held",
    holdsLoading: false,
    holdsRequest: 0,
    holdsTimer: null,
    holdsError: ""
};

connectForm?.addEventListener("submit", event => {
    event.preventDefault();
    const token = new FormData(connectForm).get("bootstrapToken")?.toString().trim();
    if (!token) {
        connectMessage.textContent = "Paste the external bootstrap token file content.";
        return;
    }

    window.location.assign(`/bootstrap?token=${encodeURIComponent(token)}`);
});

document.querySelectorAll("[data-panel]").forEach(button => {
    button.addEventListener("click", () => showPanel(button.dataset.panel));
});

document.querySelector("[data-action='toggle-inspector']")?.addEventListener("click", () => {
    inspector?.classList.toggle("is-open");
});

document.querySelector("[data-action='load-material']")?.addEventListener("click", () => loadMaterialPage());
document.querySelector("[data-action='load-history']")?.addEventListener("click", () => loadHistoryPage());
document.querySelector("[data-action='holds-previous']")?.addEventListener("click", () => loadHoldsPage("previous"));
document.querySelector("[data-action='holds-next']")?.addEventListener("click", () => loadHoldsPage("next"));
document.querySelector("[data-action='holds-refresh']")?.addEventListener("click", () => loadHoldsPage("refresh"));
document.querySelector("#holds-disposition")?.addEventListener("change", event => {
    state.holdsFilter = event.target.value;
    loadHoldsPage("first");
});

await boot();

async function boot() {
    try {
        state.session = await fetchSession();
    } catch (error) {
        showUnauthenticated(error);
        return;
    }

    app.className = "app app--ready";
    await refreshState();
    if (!state.session) {
        return;
    }
    connectStateStream();
}

function connectStateStream() {
    const generation = ++state.streamGeneration;
    state.eventSource?.close();
    state.eventSource = openStateStream(nextState => {
        if (!state.session || generation !== state.streamGeneration) return;
        state.liveDelivery = true;
        state.streamRetryDelay = 1000;
        state.browserStreamStatus = "Connected";
        state.browserStreamDetail = "";
        applyState(nextState);
    }, (connection, detail, terminal = false) => {
        if (!state.session || generation !== state.streamGeneration) return;
        state.liveDelivery = false;
        state.browserStreamStatus = connection === "Connected" ? "Connecting" : connection;
        state.browserStreamDetail = detail ?? "";
        closeIncompatibleConfirmation("Browser live delivery is unavailable");
        if (state.plant) {
            applyState({ ...state.plant, available: false });
        } else {
            renderAll();
        }
        if (connection !== "Connected") {
            refreshState();
        }
        if (terminal && state.streamRetryTimer === null) {
            state.eventSource?.close();
            const delay = state.streamRetryDelay;
            state.streamRetryDelay = Math.min(delay * 2, 15000);
            state.streamRetryTimer = window.setTimeout(() => {
                state.streamRetryTimer = null;
                if (state.session && generation === state.streamGeneration) {
                    connectStateStream();
                }
            }, delay);
        }
    });
}

function showUnauthenticated(error) {
    state.streamGeneration++;
    window.clearTimeout(state.streamRetryTimer);
    state.streamRetryTimer = null;
    state.liveDelivery = false;
    state.eventSource?.close();
    state.eventSource = null;
    state.session = null;
    app.className = "app app--unauthenticated";
    if (connectMessage) {
        connectMessage.textContent = error?.status === 401
            ? "This browser is not authenticated. Use /bootstrap with the external key file content."
            : `Session status unavailable: ${error.message}`;
    }
}

async function refreshState() {
    const generation = state.streamGeneration;
    try {
        const observation = await fetchState();
        if (!state.session || generation !== state.streamGeneration || state.liveDelivery) {
            return;
        }
        applyState(observation);
    } catch (error) {
        if (!state.session || generation !== state.streamGeneration || state.liveDelivery) {
            return;
        }
        if (error.status === 401) {
            showUnauthenticated(error);
            return;
        }
        if (state.plant) {
            state.plant = {
                ...state.plant,
                connection: state.plant.connection,
                detail: `State refresh failed: ${error.message}`,
                available: false,
                quality: state.plant.quality ?? "Unknown"
            };
            renderAll();
            return;
        }

        applyState(emptyUnavailableState(error.message));
    }
}

function applyState(nextState) {
    const observation = normalizeState(mergeUnavailableState(nextState));
    const normalized = state.liveDelivery ? observation : { ...observation, available: false };
    const previousRunId = state.plant?.runId ?? "";
    const previousConfigurationId = state.plant?.configurationId ?? "";
    const changedContext = previousRunId &&
        (previousRunId !== (normalized.runId ?? "") || previousConfigurationId !== (normalized.configurationId ?? ""));

    if (changedContext) {
        clearRunScopedState();
        closeIncompatibleConfirmation();
    }

    state.activeTransfers = changedContext ? new Set() : detectTransferChanges(normalized);
    state.plant = normalized;
    if (!normalized.processes.some(process => process.name === state.selectedProcess)) {
        state.selectedProcess = normalized.processes[0]?.name ?? "Collection";
    }

    renderAll();
    if (document.querySelector("[data-panel-name='holds']")?.hidden === false && normalized.available &&
        state.holdsTimer === null) {
        state.holdsTimer = window.setTimeout(() => {
            state.holdsTimer = null;
            loadHoldsPage("refresh");
        }, 1000);
    }
}

function renderAll() {
    const restore = captureRenderState();
    renderTopSummary(topSummary, state.plant);
    renderSessionStatus(sessionStatus, state.session, state.plant);
    renderSystemBanner(systemBanner, state.plant);
    appendBrowserStreamStatus();
    renderPlantQuality(plantQuality, state.plant);
    renderSchematic(schematic, state.plant, state.selectedProcess, state.activeTransfers);
    renderProcessList(processList, state.plant, state.selectedProcess);
    renderInspector(inspector, state.plant, state.session, state.selectedProcess, state.inspector, submitCommand);
    renderExceptionDock(faultList, holdList, commandOutcomes, state.plant, retryCommand);
    renderTrends(trendsContent, state.plant);
    renderHolds(holdsContent, {
        ...state.plant,
        holds: state.holdsPage?.runId === state.plant?.runId ? state.holdsPage.items : []
    }, state.session, submitCommand);
    renderHoldsNavigation();
    renderMaterial(materialContent, state.materialPage, state.historyPage);
    bindProcessSelection();
    bindInspectorClose();
    restoreRenderState(restore);
}

function bindProcessSelection() {
    for (const element of document.querySelectorAll("[data-process]")) {
        if (element.dataset.boundSelect === "true") {
            continue;
        }

        element.dataset.boundSelect = "true";
        element.addEventListener("click", event => {
            const processName = event.currentTarget.dataset.process;
            if (processName) {
                selectProcess(processName);
            }
        });
        element.addEventListener("keydown", event => {
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                const processName = event.currentTarget.dataset.process;
                if (processName) {
                    selectProcess(processName);
                }
            }
        });
    }
}

function bindInspectorClose() {
    const closeButton = document.querySelector("[data-testid='inspector-close']");
    if (!closeButton || closeButton.dataset.boundClose === "true") {
        return;
    }

    closeButton.dataset.boundClose = "true";
    closeButton.addEventListener("click", () => {
        inspector?.classList.remove("is-open");
        document.querySelector("[data-testid='nav-inspector']")?.focus({ preventScroll: true });
    });
}

async function selectProcess(processName) {
    state.selectedProcess = processName;
    state.inspector = null;
    inspector?.classList.add("is-open");
    renderAll();
    const runId = state.plant?.runId ?? "";
    const configurationId = state.plant?.configurationId ?? "";
    try {
        const inspectorResult = await fetchInspector(processName);
        if (state.selectedProcess !== processName ||
            state.plant?.runId !== runId ||
            state.plant?.configurationId !== configurationId ||
            inspectorResult?.runId !== runId ||
            inspectorResult?.configurationId !== configurationId) {
            return;
        }

        state.inspector = inspectorResult;
    } catch (error) {
        if (state.selectedProcess !== processName ||
            state.plant?.runId !== runId ||
            state.plant?.configurationId !== configurationId) {
            return;
        }

        const process = state.plant?.processes?.find(item => item.name === processName);
        state.inspector = {
            process,
            runId: state.plant?.runId ?? "",
            configurationId: state.plant?.configurationId ?? "",
            nativeAvailable: false,
            nativeDetail: `Inspector read failed: ${error.message}`,
            readings: []
        };
    }

    renderAll();
}

function showPanel(panelName) {
    for (const panel of document.querySelectorAll("[data-panel-name]")) {
        panel.hidden = panel.dataset.panelName !== panelName;
    }

    for (const button of document.querySelectorAll("[data-panel]")) {
        button.classList.toggle("is-active", button.dataset.panel === panelName);
    }
    if (panelName === "holds") {
        loadHoldsPage(state.holdsPage ? "refresh" : "first");
    }
}

function renderHoldsNavigation() {
    const previous = document.querySelector("[data-action='holds-previous']");
    const next = document.querySelector("[data-action='holds-next']");
    const refresh = document.querySelector("[data-action='holds-refresh']");
    const filter = document.querySelector("#holds-disposition");
    if (previous) previous.disabled = state.holdsLoading || !state.plant?.available || state.holdsPrevious.length === 0;
    if (next) next.disabled = state.holdsLoading || !state.plant?.available || !state.holdsPage?.nextAfter;
    if (refresh) refresh.disabled = state.holdsLoading || !state.plant?.available;
    if (filter) filter.disabled = state.holdsLoading;
    const status = document.querySelector("#holds-page-status");
    if (status) {
        status.textContent = state.holdsError || (state.holdsLoading
            ? "Loading retained hold page…"
            : state.holdsPage
                ? `${state.holdsPage.items.length} holds · page ${state.holdsPrevious.length + 1} · revision ${state.holdsPage.revision}`
                : "Select a disposition to inspect retained holds.");
    }
}

async function loadHoldsPage(direction) {
    if (!state.plant?.runId || !state.plant.available || state.holdsLoading) return;
    let after = state.holdsAfter;
    if (direction === "first") after = "0";
    if (direction === "next") after = state.holdsPage?.nextAfter;
    if (direction === "previous") after = state.holdsPrevious.at(-1);
    if (after === null || after === undefined) return;
    const runId = state.plant.runId;
    const configurationId = state.plant.configurationId;
    const request = ++state.holdsRequest;
    state.holdsLoading = true;
    state.holdsError = "";
    renderHoldsNavigation();
    try {
        const page = await fetchHolds(runId, after, 50, state.holdsFilter);
        if (request !== state.holdsRequest || state.plant?.runId !== runId ||
            state.plant?.configurationId !== configurationId || page.runId !== runId) return;
        if (direction === "next") state.holdsPrevious.push(state.holdsAfter);
        if (direction === "previous") state.holdsPrevious.pop();
        if (direction === "first") state.holdsPrevious = [];
        state.holdsAfter = after;
        state.holdsPage = page;
    } catch (error) {
        if (request === state.holdsRequest) {
            state.holdsError = `Retained holds unavailable: ${error.message}`;
        }
    } finally {
        if (request === state.holdsRequest) {
            state.holdsLoading = false;
            renderAll();
        }
    }
}

async function loadMaterialPage() {
    if (!state.plant?.runId) {
        systemBanner.textContent = "Material cannot be loaded before a run ID is available.";
        return;
    }

    const runId = state.plant.runId;
    const configurationId = state.plant.configurationId ?? "";
    try {
        const page = await fetchMaterial(runId, state.materialAfter, 50);
        if (state.plant?.runId !== runId || (state.plant?.configurationId ?? "") !== configurationId || page?.runId !== runId) {
            return;
        }

        state.materialPage = page;
        state.materialAfter = state.materialPage?.nextAfter ?? state.materialAfter;
        renderMaterial(materialContent, state.materialPage, state.historyPage);
    } catch (error) {
        systemBanner.textContent = `Material page failed: ${error.message}`;
    }
}

async function loadHistoryPage() {
    if (!state.plant?.runId) {
        systemBanner.textContent = "Execution history cannot be loaded before a run ID is available.";
        return;
    }

    const runId = state.plant.runId;
    const configurationId = state.plant.configurationId ?? "";
    try {
        const page = await fetchHistory(runId, state.historyAfter, 50);
        if (state.plant?.runId !== runId || (state.plant?.configurationId ?? "") !== configurationId || page?.runId !== runId) {
            return;
        }

        state.historyPage = page;
        state.historyAfter = state.historyPage?.nextAfter ?? state.historyAfter;
        renderMaterial(materialContent, state.materialPage, state.historyPage);
    } catch (error) {
        systemBanner.textContent = `Execution history failed: ${error.message}`;
    }
}

async function submitCommand(candidate) {
    if (!state.plant?.available || !state.liveDelivery) {
        systemBanner.textContent = "Live state delivery is unavailable; reconnect before issuing a command.";
        return;
    }
    const envelope = candidate.requestId
        ? candidate
        : buildCommand(candidate.kind, state.plant, {
            process: candidate.process,
            holdId: candidate.holdId,
            disposition: candidate.disposition
        });
    const confirmedEnvelope = {
        ...envelope,
        confirmed: true,
        retry: false
    };
    const pendingKey = commandKey(confirmedEnvelope);
    if (state.pendingCommandKeys.has(pendingKey)) {
        systemBanner.textContent = "That exact command is already pending in this browser session.";
        return;
    }

    if (requiresConfirmation(confirmedEnvelope)) {
        const accepted = await confirmCommand(confirmedEnvelope);
        if (!accepted) {
            return;
        }
    }
    if (!state.plant?.available || !state.liveDelivery) {
        systemBanner.textContent = "Live state delivery changed; the command was not submitted.";
        return;
    }

    const originalEnvelope = Object.freeze({ ...confirmedEnvelope });
    const commandConfigurationId = state.plant?.configurationId ?? "";
    try {
        state.commandEnvelopes.set(originalEnvelope.requestId, originalEnvelope);
        boundMap(state.commandEnvelopes, 50);
        state.pendingCommandKeys.add(pendingKey);
        prependCommand({
            requestId: originalEnvelope.requestId,
            state: "Pending",
            kind: originalEnvelope.kind,
            runId: originalEnvelope.runId,
            expectedRevision: originalEnvelope.expectedRevision,
            revision: null,
            tick: null,
            status: "Pending",
            message: "Submitted to coordinator; awaiting authoritative result.",
            updatedUtc: null
        });
        const result = await postCommand(confirmedEnvelope, state.session.csrfToken);
        state.pendingCommandKeys.delete(pendingKey);
        if (state.plant?.runId !== originalEnvelope.runId || (state.plant?.configurationId ?? "") !== commandConfigurationId) {
            return;
        }

        applyState({
            ...state.plant,
            commands: [result, ...(state.plant?.commands ?? []).filter(item => item.requestId !== result.requestId)]
        });
    } catch (error) {
        state.pendingCommandKeys.delete(pendingKey);
        if (state.plant?.runId !== originalEnvelope.runId || (state.plant?.configurationId ?? "") !== commandConfigurationId) {
            return;
        }

        prependCommand({
            requestId: originalEnvelope.requestId,
            state: [401, 403, 409].includes(error.status) ? "Rejected" : "Unknown",
            kind: originalEnvelope.kind,
            runId: originalEnvelope.runId,
            expectedRevision: originalEnvelope.expectedRevision,
            revision: null,
            tick: null,
            status: `HTTP ${error.status ?? "error"}`,
            message: error.message,
            updatedUtc: null
        });
    }
}

async function retryCommand(requestId) {
    if (!state.plant?.available || !state.liveDelivery) {
        systemBanner.textContent = "Live state delivery is unavailable; the exact retry was not submitted.";
        return;
    }
    const original = state.commandEnvelopes.get(requestId);
    if (!original) {
        systemBanner.textContent = "The exact original command envelope is no longer available in this browser session.";
        return;
    }

    const retryEnvelope = {
        ...original,
        retry: true
    };
    if (requiresConfirmation(retryEnvelope)) {
        const accepted = await confirmCommand(retryEnvelope);
        if (!accepted) {
            return;
        }
    }
    if (!state.plant?.available || !state.liveDelivery) {
        systemBanner.textContent = "Live state delivery changed; the exact retry was not submitted.";
        return;
    }

    const pendingKey = commandKey(retryEnvelope);
    if (state.pendingCommandKeys.has(pendingKey)) {
        systemBanner.textContent = "That exact retry is already pending.";
        return;
    }

    const commandConfigurationId = state.plant?.configurationId ?? "";
    try {
        state.pendingCommandKeys.add(pendingKey);
        const result = await postCommand(retryEnvelope, state.session.csrfToken);
        state.pendingCommandKeys.delete(pendingKey);
        if (state.plant?.runId !== retryEnvelope.runId || (state.plant?.configurationId ?? "") !== commandConfigurationId) {
            return;
        }

        applyState({
            ...state.plant,
            commands: [result, ...(state.plant?.commands ?? []).filter(item => item.requestId !== result.requestId)]
        });
    } catch (error) {
        state.pendingCommandKeys.delete(pendingKey);
        if (state.plant?.runId !== retryEnvelope.runId || (state.plant?.configurationId ?? "") !== commandConfigurationId) {
            return;
        }

        systemBanner.textContent = `Retry failed: ${error.message}`;
    }
}

function confirmCommand(envelope) {
    commandDialog.returnValue = "";
    state.activeConfirmation = {
        runId: envelope.runId,
        configurationId: state.plant?.configurationId ?? "",
        requestId: envelope.requestId
    };
    commandDialogCopy.textContent = commandCopy(envelope);
    commandDialogPayload.textContent = JSON.stringify(envelope, null, 2);

    if (typeof commandDialog.showModal !== "function") {
        const accepted = window.confirm(commandCopy(envelope));
        state.activeConfirmation = null;
        return Promise.resolve(accepted);
    }

    return new Promise(resolve => {
        const onClose = () => {
            commandDialog.removeEventListener("close", onClose);
            state.activeConfirmation = null;
            resolve(commandDialog.returnValue === "confirm");
        };
        commandDialog.addEventListener("close", onClose);
        commandDialog.showModal();
    });
}

function commandCopy(envelope) {
    const target = envelope.process || envelope.holdId || envelope.runId;
    const consequence = envelope.kind === "Reset"
        ? "Reset requires stopped state and may abandon current simulator progress."
        : envelope.kind === "Stop"
            ? "Stop is a simulator command, not a physical emergency stop."
            : "The server remains authoritative; this browser will not update plant state optimistically.";
    return `${envelope.kind} for ${target || "the current run"}. ${consequence}`;
}

function detectTransferChanges(nextState) {
    const active = new Set();
    const nextTransfers = new Map();
    for (const process of nextState.processes ?? []) {
        const transferred = sumStageString(process, "transferredMg");
        nextTransfers.set(process.name, transferred);
        const previous = state.previousTransfers.get(process.name);
        if (previous !== undefined && transferred && previous !== transferred) {
            active.add(process.name);
        }
    }

    state.previousTransfers = nextTransfers;
    return active;
}

function mergeUnavailableState(nextState) {
    if (nextState?.available === false && !hasValue(nextState?.runId) && hasValue(state.plant?.runId)) {
        return {
            ...state.plant,
            connection: nextState.connection ?? state.plant.connection,
            detail: nextState.detail ?? state.plant.detail,
            available: false,
            receivedUtc: nextState.receivedUtc ?? state.plant.receivedUtc,
            sourceUtc: nextState.sourceUtc ?? state.plant.sourceUtc,
            quality: nextState.quality ?? state.plant.quality,
            lifecycle: nextState.lifecycle ?? state.plant.lifecycle,
            blockingReason: nextState.blockingReason ?? state.plant.blockingReason,
            commands: nextState.commands ?? state.plant.commands
        };
    }

    return nextState;
}

function emptyUnavailableState(detail) {
    return {
        connection: "Unavailable",
        detail,
        available: false,
        receivedUtc: null,
        sourceUtc: null,
        quality: "Unknown",
        lineId: "",
        runId: "",
        scenario: "",
        configurationId: "",
        revision: "",
        tick: "",
        logicalSeconds: "",
        lifecycle: "Unavailable",
        blockingReason: "",
        summary: {},
        processes: [],
        holds: [],
        trends: [],
        commands: []
    };
}

function clearRunScopedState() {
    window.clearTimeout(state.holdsTimer);
    state.holdsTimer = null;
    state.holdsRequest++;
    state.holdsLoading = false;
    state.holdsPage = null;
    state.holdsAfter = "0";
    state.holdsPrevious = [];
    state.holdsError = "";
    state.inspector = null;
    state.materialPage = null;
    state.historyPage = null;
    state.materialAfter = "0";
    state.historyAfter = "0";
    state.previousTransfers = new Map();
    state.activeTransfers = new Set();
    state.pendingCommandKeys.clear();
}

function closeIncompatibleConfirmation(reason = "The run or configuration changed") {
    if (!state.activeConfirmation) {
        return;
    }

    if (typeof commandDialog.close === "function" && commandDialog.open) {
        commandDialog.close("cancel");
    }

    systemBanner.textContent = `${reason}; the pending confirmation was canceled without retargeting.`;
}

function captureRenderState() {
    const active = document.activeElement;
    const activeTestId = commandDialog.open || !active?.dataset?.testid ? "" : active.dataset.testid;
    return {
        activeTestId,
        adminOpen: document.querySelector("[data-testid='admin-drawer']")?.open === true,
        inspectorOpen: inspector?.classList.contains("is-open") === true
    };
}

function restoreRenderState(snapshot) {
    const adminDrawer = document.querySelector("[data-testid='admin-drawer']");
    if (adminDrawer && snapshot.adminOpen) {
        adminDrawer.open = true;
    }

    if (snapshot.inspectorOpen) {
        inspector?.classList.add("is-open");
    }

    if (snapshot.activeTestId && !commandDialog.open) {
        document.querySelector(`[data-testid="${CSS.escape(snapshot.activeTestId)}"]`)?.focus({ preventScroll: true });
    }
}

function appendBrowserStreamStatus() {
    if (state.browserStreamStatus === "Connected" || state.browserStreamStatus === "Disconnected") {
        return;
    }

    const text = `Browser live stream: ${state.browserStreamStatus}. ${state.browserStreamDetail}`;
    systemBanner.textContent = systemBanner.textContent ? `${systemBanner.textContent} ${text}` : text;
}

function prependCommand(command) {
    applyState({
        ...state.plant,
        commands: [command, ...(state.plant?.commands ?? []).filter(item => item.requestId !== command.requestId)].slice(0, 50)
    });
}

function requiresConfirmation(envelope) {
    return ["DisableProcess", "DisposeHold", "Stop", "Reset"].includes(envelope.kind);
}

function commandKey(envelope) {
    return [
        envelope.kind,
        envelope.process,
        envelope.holdId,
        envelope.disposition,
        envelope.runId,
        envelope.expectedRevision,
        envelope.retry ? "retry" : "first"
    ].join("|");
}

function boundMap(map, maxSize) {
    while (map.size > maxSize) {
        const firstKey = map.keys().next().value;
        map.delete(firstKey);
    }
}
