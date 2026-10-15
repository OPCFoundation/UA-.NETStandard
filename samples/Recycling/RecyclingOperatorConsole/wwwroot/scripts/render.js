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
    displayTimestamp,
    displayValue,
    escapeHtml,
    formatEnergy,
    formatMass,
    hasValue,
    isConstraintState,
    latestStage,
    lifecycleBadgeClass,
    processHasFault,
    processHasWarning,
    statusClass,
    sumStageString,
    testIdPart,
    toFiniteNumber
} from "./format.js";

export const processOrder = [
    "Collection",
    "Sorting",
    "Shredding",
    "Washing",
    "DensitySeparation",
    "Drying",
    "Extrusion",
    "MeltFiltration",
    "Pelletizing",
    "Bagging"
];

const processLabels = {
    Collection: "Collection",
    Sorting: "Sorting",
    Shredding: "Shredding",
    Washing: "Washing",
    DensitySeparation: "Density separation",
    Drying: "Drying",
    Extrusion: "Extrusion",
    MeltFiltration: "Melt filtration",
    Pelletizing: "Pelletizing",
    Bagging: "Bagging"
};

const processSymbols = {
    Collection: "symbol-collection",
    Sorting: "symbol-sorting",
    Shredding: "symbol-shredding",
    Washing: "symbol-washing",
    DensitySeparation: "symbol-density",
    Drying: "symbol-drying",
    Extrusion: "symbol-extrusion",
    MeltFiltration: "symbol-filtration",
    Pelletizing: "symbol-pelletizing",
    Bagging: "symbol-bagging"
};

const processPositions = {
    Collection: [56, 62],
    Sorting: [266, 62],
    Shredding: [476, 62],
    Washing: [686, 62],
    DensitySeparation: [686, 254],
    Drying: [476, 254],
    Extrusion: [266, 254],
    MeltFiltration: [56, 254],
    Pelletizing: [266, 446],
    Bagging: [476, 446]
};

const connectorPairs = [
    ["Collection", "Sorting"],
    ["Sorting", "Shredding"],
    ["Shredding", "Washing"],
    ["Washing", "DensitySeparation"],
    ["DensitySeparation", "Drying"],
    ["Drying", "Extrusion"],
    ["Extrusion", "MeltFiltration"],
    ["MeltFiltration", "Pelletizing"],
    ["Pelletizing", "Bagging"]
];

export function normalizeState(state) {
    const processMap = new Map((state?.processes ?? []).map(process => [process.name, process]));
    return {
        ...state,
        processes: processOrder.map(name => processMap.get(name) ?? {
            name,
            initialized: false,
            nativeConfigured: false,
            stages: []
        })
    };
}

export function renderTopSummary(root, state) {
    const summary = state?.summary ?? {};
    const noRun = !hasValue(state?.runId);
    const cells = [
        ["Input", summaryValue(summary.inputMg, formatMass, noRun)],
        ["Work in progress", summaryValue(summary.wipMg, formatMass, noRun)],
        ["Packaged", summaryValue(summary.packagedMg, formatMass, noRun)],
        ["Packages", summaryValue(summary.packageCount, displayValue, noRun)],
        ["Electrical", summaryValue(summary.electricalJoules, formatEnergy, noRun)],
        ["Thermal", summaryValue(summary.thermalJoules, formatEnergy, noRun)]
    ];

    root.innerHTML = cells.map(([label, value]) => `
        <div class="summary-cell" data-testid="summary-${testIdPart(label)}">
            <span>${escapeHtml(label)}</span>
            <strong title="${escapeHtml(value)}">${escapeHtml(value)}</strong>
        </div>
    `).join("");
}

export function renderSessionStatus(root, session, state) {
    const connection = state?.connection ?? "Connecting";
    const role = session?.role ?? "Unknown";
    const lifecycle = state?.lifecycle ?? "Unavailable";
    root.innerHTML = `
        <div class="status-line">
            <span class="status-pill status-pill--${statusClass(connection)}" data-testid="connection-status">${escapeHtml(connection)}</span>
            <span class="badge ${lifecycleBadgeClass(lifecycle)}" data-testid="lifecycle-status">${escapeHtml(lifecycle)}</span>
        </div>
        <div class="status-line">
            <strong data-testid="session-role">${escapeHtml(role)}</strong>
            <span data-testid="session-expires">${escapeHtml(displayTimestamp(session?.expiresUtc))}</span>
        </div>
        <div class="status-line">
            <span title="${escapeHtml(state?.configurationId ?? "")}">Run ${escapeHtml(state?.runId || "No run")}</span>
            <span>Tick ${escapeHtml(state?.tick || "—")}</span>
        </div>
    `;
}

export function renderSystemBanner(root, state) {
    const messages = [];
    if (!state?.available && !hasValue(state?.runId)) {
        messages.push("No coherent coordinator data is currently available.");
    } else if (!state?.available) {
        messages.push("Coordinator state is unavailable; showing retained snapshot values as stale.");
    }

    if (state?.blockingReason) {
        messages.push(state.blockingReason);
    }

    if (state?.detail && state.connection !== "Connected") {
        messages.push(state.detail);
    }

    root.textContent = messages.join(" ");
}

export function renderPlantQuality(root, state) {
    const chips = [
        ["Quality", state?.quality ?? "Unknown"],
        ["Source", displayTimestamp(state?.sourceUtc)],
        ["Received", displayTimestamp(state?.receivedUtc)]
    ];
    root.innerHTML = chips.map(([label, value]) => `
        <span class="quality-chip quality-chip--${statusClass(value)}" data-testid="quality-${testIdPart(label)}">
            ${escapeHtml(label)}: ${escapeHtml(value)}
        </span>
    `).join("");
}

export function renderSchematic(root, state, selectedProcess, activeTransfers) {
    const normalized = normalizeState(state);
    const unavailable = !normalized.available || normalized.connection === "Unavailable" || normalized.lifecycle === "Unavailable";
    const paused = normalized.lifecycle !== "Running" || normalized.connection !== "Connected";
    const connectors = connectorPairs.map(([from, to]) => connectorPath(from, to, activeTransfers.has(to) && !paused && !unavailable, unavailable)).join("");
    const nodes = normalized.processes.map(process => renderProcessNode(process, selectedProcess, unavailable)).join("");

    root.innerHTML = `
        <svg class="plant-svg" viewBox="0 0 890 620" role="img" aria-label="Serpentine recycling process schematic">
            <defs>
                <pattern id="staleHatch" patternUnits="userSpaceOnUse" width="12" height="12" patternTransform="rotate(45)">
                    <rect width="12" height="12" fill="#eef2f5"></rect>
                    <rect width="4" height="12" fill="#d8e0e7"></rect>
                </pattern>
                <marker id="arrow" markerWidth="12" markerHeight="12" refX="10" refY="6" orient="auto" markerUnits="strokeWidth">
                    <path d="M2 2 L10 6 L2 10 z" fill="#657687"></path>
                </marker>
            </defs>
            <g class="connectors" marker-end="url(#arrow)">${connectors}</g>
            <g class="processes">${nodes}</g>
        </svg>
    `;
}

export function renderProcessList(root, state, selectedProcess) {
    const normalized = normalizeState(state);
    root.innerHTML = normalized.processes.map(process => {
        const selected = process.name === selectedProcess;
        const stateLabel = processStateLabel(process);
        const inventory = sumStageString(process, "inventoryMg");
        return `
            <button type="button" class="${selected ? "is-selected" : ""}" data-process="${escapeHtml(process.name)}" data-testid="process-list-${testIdPart(process.name)}" aria-pressed="${selected}">
                <span>${escapeHtml(processLabels[process.name] ?? process.name)}</span>
                <span>${escapeHtml(inventory ? formatMass(inventory) : stateLabel)}</span>
            </button>
        `;
    }).join("");
}

export function renderInspector(root, state, session, selectedProcess, inspector, submitCommand) {
    const validInspector = inspector?.process?.name === selectedProcess &&
        inspector?.runId === state?.runId &&
        inspector?.configurationId === state?.configurationId;
    const process = normalizeState(state).processes.find(item => item.name === selectedProcess);
    if (!process) {
        root.innerHTML = `<div class="inspector__header"><h2>No process selected</h2></div>`;
        return;
    }

    const stage = latestStage(process);
    const readings = validInspector ? inspector?.readings ?? [] : [];
    const title = processLabels[process.name] ?? process.name;
    root.innerHTML = `
        <div class="inspector__header" data-testid="inspector-header">
            <div class="inspector__title-row">
                <div>
                    <p class="surface-label">Selected equipment</p>
                    <h2 data-testid="inspector-name">${escapeHtml(title)}</h2>
                </div>
                <div class="inspector__tools">
                    <span class="badge ${processHasFault(process) ? "badge--fault" : processHasWarning(process) ? "badge--constraint" : "badge--running"}" data-testid="inspector-state">${escapeHtml(processStateLabel(process))}</span>
                    <button class="button inspector__close" type="button" data-testid="inspector-close" aria-label="Close inspector">Close</button>
                </div>
            </div>
        </div>
        <div class="inspector__body">
            ${renderStageMetrics(process, stage)}
            ${renderNativeSection(process, validInspector ? inspector : null)}
            ${renderReadings(readings)}
            ${renderStageList(process)}
            ${renderCommandActions(process, state, session)}
        </div>
    `;

    root.querySelectorAll("[data-command]").forEach(button => {
        button.addEventListener("click", () => submitCommand(JSON.parse(button.dataset.command ?? "{}")));
    });
}

export function renderExceptionDock(faultRoot, holdRoot, commandRoot, state, retryCommand) {
    const normalized = normalizeState(state);
    const faults = [];
    for (const process of normalized.processes) {
        for (const stage of process.stages ?? []) {
            if (stage.jamAlarm || stage.strandBreakAlarm || stage.enabled === false || isConstraintState(stage.state)) {
                faults.push({ process: process.name, stage, reason: faultReason(stage) });
            }
        }

        if (!process.initialized || !process.nativeConfigured) {
            faults.push({ process: process.name, stage: null, reason: !process.initialized ? "Participant not initialized" : "Native endpoint unavailable" });
        }
    }

    faultRoot.innerHTML = faults.length === 0
        ? `<div class="empty-state" data-testid="fault-empty">No active equipment faults or constraints.</div>`
        : faults.map(item => `
            <div class="exception-item exception-item--fault" data-testid="fault-row-${testIdPart(item.process)}">
                <button type="button" data-process="${escapeHtml(item.process)}">${escapeHtml(processLabels[item.process] ?? item.process)}</button>
                <strong>${escapeHtml(item.reason)}</strong>
                <span>${item.stage ? `Stage ${escapeHtml(item.stage.index)} · ${escapeHtml(item.stage.name)}` : "Process membership / native data"}</span>
            </div>
        `).join("");

    const holds = state?.holds ?? [];
    holdRoot.innerHTML = holds.length === 0
        ? `<div class="empty-state" data-testid="hold-empty">No quality holds in the bounded state view.</div>`
        : holds.map(hold => renderHoldRow(hold, true)).join("");

    const commands = state?.commands ?? [];
    commandRoot.innerHTML = commands.length === 0
        ? `<div class="empty-state" data-testid="command-empty">No commands submitted in this workstation session.</div>`
        : commands.map(command => renderCommandResult(command)).join("");

    commandRoot.querySelectorAll("button[data-retry]").forEach(button => {
        button.addEventListener("click", () => retryCommand(button.dataset.retry));
    });
}

export function renderHolds(root, state, session, submitCommand) {
    root.closest(".panel--secondary")?.setAttribute("tabindex", "0");
    const holds = state?.holds ?? [];
    const canDispose = (session?.role === "Operator" || session?.role === "SimulatorAdministrator") && canMutate(state);
    root.innerHTML = holds.length === 0
        ? `<div class="empty-state" data-testid="holds-empty">No quality holds are present in the latest bounded state.</div>`
        : `<div class="table-like">${holds.map(hold => renderHoldRow(hold, false, canDispose)).join("")}</div>`;

    root.querySelectorAll("[data-command]").forEach(button => {
        button.addEventListener("click", () => submitCommand(JSON.parse(button.dataset.command ?? "{}")));
    });
}

export function renderTrends(root, state) {
    root.closest(".panel--secondary")?.setAttribute("tabindex", "0");
    const trends = state?.trends ?? [];
    if (trends.length === 0) {
        root.innerHTML = `<div class="empty-state" data-testid="trends-empty">No trend observations have arrived since this console connected.</div>`;
        return;
    }

    root.innerHTML = `
        <div class="trend-board">
            ${renderTrendSvg(trends)}
            <div class="table-like" data-testid="trend-rows">
                ${trends.slice(-12).reverse().map(trend => `
                    <div class="history-card" data-testid="trend-row-${testIdPart(trend.tick)}">
                        <strong>Tick ${escapeHtml(trend.tick)}</strong>
                        <span>WIP ${escapeHtml(formatMass(trend.wipMg))}</span>
                        <span>Packaged ${escapeHtml(formatMass(trend.packagedMg))}</span>
                        <span>${trend.gap ? "Gap" : "Observed"}</span>
                        <span>${escapeHtml(displayTimestamp(trend.receivedUtc))}</span>
                    </div>
                `).join("")}
            </div>
        </div>
    `;
}

export function renderMaterial(root, materialPage, historyPage) {
    root.closest(".panel--secondary")?.setAttribute("tabindex", "0");
    const materialItems = materialPage?.items ?? [];
    const historyItems = historyPage?.items ?? [];
    root.innerHTML = `
        <div class="trend-board">
            <section class="section-card">
                <h3>Material page</h3>
                <div class="material-list" data-testid="material-rows">
                    ${materialItems.length === 0 ? `<div class="empty-state" data-testid="material-empty">No material page loaded yet.</div>` : materialItems.map(renderMaterialRow).join("")}
                </div>
                <p class="connect-form__note" data-testid="material-next-after">Next page: ${escapeHtml(materialPage?.nextAfter ?? "none")}</p>
            </section>
            <section class="section-card">
                <h3>Execution history page</h3>
                <div class="history-list" data-testid="history-rows">
                    ${historyItems.length === 0 ? `<div class="empty-state" data-testid="history-empty">No history page loaded yet.</div>` : historyItems.map(renderHistoryRow).join("")}
                </div>
                <p class="connect-form__note" data-testid="history-next-after">Next page: ${escapeHtml(historyPage?.nextAfter ?? "none")}</p>
            </section>
        </div>
    `;
}

export function buildCommand(kind, state, values = {}) {
    return {
        requestId: crypto.randomUUID(),
        runId: state?.runId ?? "",
        expectedRevision: state?.revision ?? "",
        kind,
        process: values.process ?? "",
        holdId: values.holdId ?? "0",
        disposition: values.disposition ?? "",
        confirmed: false,
        retry: false
    };
}

function renderProcessNode(process, selectedProcess, unavailable) {
    const [x, y] = processPositions[process.name];
    const selected = process.name === selectedProcess;
    const fault = processHasFault(process);
    const warning = processHasWarning(process);
    const stateClass = unavailable ? "is-unavailable" : fault ? "is-fault" : warning ? "is-warning" : "";
    const inventory = sumStageString(process, "inventoryMg");
    const transferred = sumStageString(process, "transferredMg");
    const chips = selected ? renderSubstepChips(process) : "";
    const dotClass = unavailable ? "state-dot--stale" : fault ? "state-dot--fault" : warning ? "state-dot--warning" : "state-dot--running";
    return `
        <g class="process-node ${selected ? "is-selected" : ""} ${stateClass}" data-process="${escapeHtml(process.name)}" data-testid="process-${testIdPart(process.name)}" tabindex="0" role="button" aria-label="${escapeHtml(processLabels[process.name] ?? process.name)}">
            <rect class="process-card" x="${x}" y="${y}" width="150" height="132" rx="16"></rect>
            <circle class="state-dot ${dotClass}" cx="${x + 130}" cy="${y + 20}" r="7"></circle>
            <svg class="process-symbol" x="${x + 12}" y="${y + 15}" width="56" height="44" viewBox="0 0 96 72"><use href="assets/symbols.svg#${processSymbols[process.name]}"></use></svg>
            <text class="process-title" x="${x + 14}" y="${y + 75}">${escapeHtml(processLabels[process.name] ?? process.name)}</text>
            <text class="process-state" x="${x + 14}" y="${y + 92}">${escapeHtml(processStateLabel(process))}</text>
            <text class="process-amount" x="${x + 14}" y="${y + 109}">${escapeHtml(inventory ? `Inventory ${formatMass(inventory)}` : "Inventory unavailable")}</text>
            <text class="process-meta" x="${x + 14}" y="${y + 126}">${escapeHtml(transferred ? `Transferred ${formatMass(transferred)}` : "No transfer reading")}</text>
            ${chips}
            <rect class="process-hitbox" x="${x}" y="${y}" width="150" height="${selected ? 190 : 132}" rx="16"></rect>
        </g>
    `;
}

function connectorPath(from, to, active, stale) {
    const [x1, y1] = processPositions[from];
    const [x2, y2] = processPositions[to];
    const startX = x1 + (x2 >= x1 ? 150 : 0);
    const startY = y1 + 54;
    const endX = x2 + (x2 >= x1 ? 0 : 150);
    const endY = y2 + 54;
    const midX = (startX + endX) / 2;
    const d = Math.abs(startY - endY) > 40
        ? `M${startX} ${startY} L${midX} ${startY} L${midX} ${endY} L${endX} ${endY}`
        : `M${startX} ${startY} L${endX} ${endY}`;
    return `<path class="connector ${active ? "connector--active" : ""} ${stale ? "connector--stale" : ""}" data-testid="connector-${testIdPart(from)}-${testIdPart(to)}" d="${d}"></path>`;
}

function renderSubstepChips(process) {
    const stages = process.stages ?? [];
    return stages.slice(0, 4).map((stage, index) => {
        const x = processPositions[process.name][0] + 12 + (index % 2) * 62;
        const y = processPositions[process.name][1] + 144 + Math.floor(index / 2) * 24;
        const label = `${stage.index}: ${stage.name ?? "stage"}`.slice(0, 16);
        return `<g class="substep-chip" data-testid="substep-${testIdPart(process.name)}-${testIdPart(stage.index)}"><rect x="${x}" y="${y}" width="58" height="18" rx="9"></rect><text x="${x + 7}" y="${y + 13}">${escapeHtml(label)}</text></g>`;
    }).join("");
}

function processStateLabel(process) {
    if (!process?.initialized) {
        return "Waiting for participant";
    }

    if (processHasFault(process)) {
        return "Fault observed";
    }

    if (process?.stages?.some(stage => stage.enabled === false)) {
        return "Disabled";
    }

    const state = latestStage(process)?.state;
    return state || "Not inspected";
}

function renderStageMetrics(process, stage) {
    return `
        <section class="section-card" data-testid="inspector-process">
            <h3>Process state</h3>
            <div class="metrics-grid">
                <div class="metric"><span>Inventory</span><strong>${escapeHtml(formatMass(sumStageString(process, "inventoryMg") || stage?.inventoryMg))}</strong></div>
                <div class="metric"><span>Transferred</span><strong>${escapeHtml(formatMass(sumStageString(process, "transferredMg")))}</strong></div>
                <div class="metric"><span>Electrical</span><strong>${escapeHtml(formatEnergy(sumStageString(process, "electricalJoules")))}</strong></div>
                <div class="metric"><span>Thermal</span><strong>${escapeHtml(formatEnergy(sumStageString(process, "thermalJoules")))}</strong></div>
                <div class="metric"><span>Executions</span><strong>${escapeHtml(displayValue(sumStageString(process, "executions")))}</strong></div>
                <div class="metric"><span>Lease expires</span><strong>${escapeHtml(displayTimestamp(process?.leaseExpiresUtc))}</strong></div>
            </div>
        </section>
    `;
}

function renderNativeSection(process, inspector) {
    const inspected = inspector !== null && inspector !== undefined;
    const available = inspected && inspector?.nativeAvailable === true;
    return `
        <section class="section-card" data-testid="native-section">
            <h3>Native companion source</h3>
            <div class="meta-grid">
                <div class="meta-row"><span>Configured</span><strong>${escapeHtml(process?.nativeConfigured ? "Yes" : "No")}</strong></div>
                <div class="meta-row"><span>Available</span><strong>${escapeHtml(inspected ? available ? "Yes" : "No" : "Not inspected")}</strong></div>
                <div class="meta-row"><span>Run</span><strong>${escapeHtml(inspector?.runId ?? "Not inspected")}</strong></div>
                <div class="meta-row"><span>Configuration</span><strong>${escapeHtml(inspector?.configurationId ?? "Not inspected")}</strong></div>
            </div>
            ${inspector?.nativeDetail ? `<p class="connect-form__note">${escapeHtml(inspector.nativeDetail)}</p>` : ""}
        </section>
    `;
}

function renderReadings(readings) {
    return `
        <section class="section-card" data-testid="readings-section">
            <h3>Measurements with provenance</h3>
            <div class="reading-list">
                ${readings.length === 0 ? `<div class="empty-state" data-testid="readings-empty">No native readings loaded for this process.</div>` : readings.map(reading => `
                    <div class="reading-row" data-testid="reading-row-${testIdPart(reading.name)}">
                        <div><span>${escapeHtml(reading.name)}</span><strong>${escapeHtml(displayValue(reading.value, reading.unit))}</strong></div>
                        <span class="quality-chip quality-chip--${statusClass(reading.quality)}">${escapeHtml(reading.quality ?? "Unknown")}</span>
                        <div class="reading-row__meta">${escapeHtml(reading.origin ?? "Unknown origin")} · value quality ${escapeHtml(reading.quality ?? "Unknown")} · unit quality ${escapeHtml(reading.unitQuality ?? "MetadataUnavailable")} · source ${escapeHtml(displayTimestamp(reading.sourceUtc))} · run ${escapeHtml(reading.runId || "—")} · tick ${escapeHtml(reading.tick || "—")} · lag ${escapeHtml(reading.projectionLagTicks ?? "—")}</div>
                    </div>
                `).join("")}
            </div>
        </section>
    `;
}

function renderStageList(process) {
    const stages = process?.stages ?? [];
    return `
        <section class="section-card" data-testid="substeps-section">
            <h3>Substeps (${stages.length})</h3>
            <div class="reading-list">
                ${stages.length === 0 ? `<div class="empty-state">No substeps reported by the feed.</div>` : stages.map(stage => `
                    <div class="reading-row" data-testid="stage-row-${testIdPart(process.name)}-${testIdPart(stage.index)}">
                        <div><span>${escapeHtml(stage.name ?? `Stage ${stage.index}`)}</span><strong>${escapeHtml(stage.state ?? "Unknown")}</strong></div>
                        <span class="badge ${stage.jamAlarm || stage.strandBreakAlarm ? "badge--fault" : stage.enabled === false ? "badge--constraint" : "badge--running"}">${stage.enabled === false ? "Disabled" : stage.jamAlarm || stage.strandBreakAlarm ? "Fault" : "Enabled"}</span>
                        <div class="reading-row__meta">pH ${escapeHtml(displayValue(stage.ph))} · pH source ${escapeHtml(displayTimestamp(stage.phSourceUtc))} · ${escapeHtml(stage.origin ?? "Unknown origin")}</div>
                    </div>
                `).join("")}
            </div>
        </section>
    `;
}

function renderCommandActions(process, state, session) {
    const role = session?.role ?? "Observer";
    const mutationAllowed = canMutate(state);
    const lifecycle = state?.lifecycle ?? "Unavailable";
    const canOperate = (role === "Operator" || role === "SimulatorAdministrator") && mutationAllowed;
    const canAdmin = role === "SimulatorAdministrator";
    const disabledText = canOperate
        ? ""
        : `<p class="connect-form__note" data-testid="commands-readonly">${mutationAllowed ? "Observer or unknown role: commands are read-only." : "Coordinator state is stale or unavailable; mutations are disabled."}</p>`;
    const actions = [];
    const hasDisabledStage = Boolean(process?.stages?.some(stage => stage.enabled === false));
    actions.push(commandButton("EnableProcess", "Enable process", canOperate && hasDisabledStage, { process: process.name }));
    actions.push(commandButton("DisableProcess", "Disable process", canOperate && !hasDisabledStage && lifecycle !== "Stopped", { process: process.name }, "button--warning"));
    const adminActions = [];
    if (canAdmin) {
        adminActions.push(commandButton("Pause", "Pause", mutationAllowed && (lifecycle === "Running" || lifecycle === "ResourceLimited"), {}));
        adminActions.push(commandButton("Resume", "Resume", mutationAllowed && ["Paused", "Stopped"].includes(lifecycle), {}));
        adminActions.push(commandButton("Step", "Step one tick", mutationAllowed && lifecycle === "Paused", {}));
        adminActions.push(commandButton("Stop", "Stop", mutationAllowed && !["Stopped", "Unavailable"].includes(lifecycle), {}, "button--danger"));
        adminActions.push(commandButton("Reset", "Reset", mutationAllowed && lifecycle === "Stopped", {}, "button--danger"));
        adminActions.push(commandButton("Checkpoint", "Checkpoint", mutationAllowed && lifecycle !== "Unavailable", {}));
    }

    return `
        <section class="section-card" data-testid="command-section">
            <h3>Authorized commands</h3>
            <div class="command-actions">${actions.join("")}</div>
            ${canAdmin ? `<details class="admin-drawer" data-testid="admin-drawer"><summary data-testid="admin-open">Simulator administration</summary><div class="command-actions">${adminActions.join("")}</div><p class="connect-form__note">Stop is a simulator command, not a physical emergency stop.</p></details>` : ""}
            ${disabledText}
            <p class="connect-form__note">Commands use coordinator ExecuteAsync only. No optimistic plant-state mutation is applied in the browser.</p>
        </section>
    `;

    function commandButton(kind, label, enabled, values, extraClass = "") {
        const command = buildCommand(kind, state, values);
        const testId = kind === "DisableProcess"
            ? "process-disable"
            : kind === "Pause" && !values.process
                ? "admin-pause"
                : `command-${testIdPart(kind)}-${testIdPart(values.process ?? "plant")}`;
        return `<button class="button ${extraClass}" type="button" ${enabled ? "" : "disabled"} data-command="${escapeHtml(JSON.stringify(command))}" data-testid="${testId}">${escapeHtml(label)}</button>`;
    }
}

function faultReason(stage) {
    if (stage?.jamAlarm) {
        return "Jam alarm";
    }

    if (stage?.strandBreakAlarm) {
        return "Strand break alarm";
    }

    if (stage?.enabled === false) {
        return "Process disabled";
    }

    if (isConstraintState(stage?.state)) {
        return stage.state;
    }

    return "Constraint";
}

function renderHoldRow(hold, compact, canDispose = false) {
    const id = hold?.id ?? "0";
    const commandReleased = JSON.stringify({ kind: "DisposeHold", holdId: id, disposition: "Released" });
    const commandRejected = JSON.stringify({ kind: "DisposeHold", holdId: id, disposition: "Rejected" });
    return `
        <div class="exception-item exception-item--hold" data-testid="hold-row-${testIdPart(id)}" data-hold-id="${escapeHtml(id)}">
            <strong>Hold ${escapeHtml(id)} · ${escapeHtml(hold?.disposition ?? "Held")}</strong>
            <span>Tick ${escapeHtml(hold?.tick ?? "—")} · stage ${escapeHtml(hold?.stage ?? "—")} · ${escapeHtml(hold?.reason ?? "No reason supplied")}</span>
            ${compact ? "" : `<div class="command-actions"><button class="button" type="button" ${canDispose && hold?.disposition === "Held" ? "" : "disabled"} data-command="${escapeHtml(commandReleased)}" data-testid="command-DisposeHold-Released-${testIdPart(id)}">Release hold</button><button class="button button--warning" type="button" ${canDispose && hold?.disposition === "Held" ? "" : "disabled"} data-command="${escapeHtml(commandRejected)}" data-testid="command-DisposeHold-Rejected-${testIdPart(id)}">Reject hold</button></div>`}
        </div>
    `;
}

function renderCommandResult(command) {
    const canRetry = command?.state === "Unknown" || command?.state === "Pending";
    return `
        <div class="exception-item exception-item--${statusClass(command?.state)}" data-testid="command-result-${testIdPart(command?.requestId)}">
            <strong>${escapeHtml(command?.kind ?? "Command")} · ${escapeHtml(command?.state ?? "Unknown")}</strong>
            <span>${escapeHtml(command?.message ?? command?.status ?? "No detail")} · updated ${escapeHtml(displayTimestamp(command?.updatedUtc))}</span>
            ${canRetry ? `<button type="button" data-retry="${escapeHtml(command.requestId)}" data-testid="command-retry-${testIdPart(command.requestId)}">Retry exact envelope</button>` : ""}
        </div>
    `;
}

function renderTrendSvg(trends) {
    const points = trends.map((trend, index) => ({
        index,
        wip: toFiniteNumber(trend.wipMg),
        packaged: toFiniteNumber(trend.packagedMg),
        gap: trend.gap,
        tick: trend.tick
    })).filter(point => point.wip !== null || point.packaged !== null);
    if (points.length < 2) {
        return `<div class="empty-state" data-testid="trends-insufficient">At least two trend observations are needed for a line.</div>`;
    }

    const values = points.flatMap(point => [point.wip, point.packaged]).filter(value => value !== null);
    const max = Math.max(...values, 1);
    const min = Math.min(...values, 0);
    const width = 920;
    const height = 300;
    const left = 54;
    const right = 24;
    const top = 26;
    const bottom = 44;
    const xFor = index => left + (index / Math.max(points.length - 1, 1)) * (width - left - right);
    const yFor = value => height - bottom - ((value - min) / Math.max(max - min, 1)) * (height - top - bottom);
    const pathFor = field => points.map((point, index) => {
        const value = point[field];
        const x = xFor(index);
        const y = value === null ? height - bottom : yFor(value);
        return `${index === 0 ? "M" : "L"}${x.toFixed(1)} ${y.toFixed(1)}`;
    }).join(" ");

    const gapMarkers = points.map((point, index) => point.gap
        ? `<line class="trend-gap-marker" x1="${xFor(index).toFixed(1)}" y1="${top}" x2="${xFor(index).toFixed(1)}" y2="${height - bottom}"><title>Observation gap at tick ${escapeHtml(point.tick)}</title></line>`
        : "").join("");

    return `
        <svg class="trend-svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="Bounded WIP and packaged mass trends" data-testid="trend-chart">
            <line class="trend-axis" x1="${left}" y1="${height - bottom}" x2="${width - right}" y2="${height - bottom}"></line>
            <line class="trend-axis" x1="${left}" y1="${top}" x2="${left}" y2="${height - bottom}"></line>
            <line class="trend-gridline" x1="${left}" y1="${top}" x2="${width - right}" y2="${top}"></line>
            <line class="trend-gridline" x1="${left}" y1="${(height - bottom + top) / 2}" x2="${width - right}" y2="${(height - bottom + top) / 2}"></line>
            ${gapMarkers}
            <path class="trend-line" d="${pathFor("wip")}"><title>WIP mg</title></path>
            <path class="trend-line trend-line--packaged" d="${pathFor("packaged")}"><title>Packaged mg</title></path>
            <text x="${left}" y="${height - 14}" fill="#405060" font-size="13">logical ticks · gaps marked amber</text>
            <text x="${width - 210}" y="${height - 14}" fill="#1267b3" font-size="13">WIP</text>
            <text x="${width - 150}" y="${height - 14}" fill="#28724a" font-size="13">Packaged</text>
        </svg>
    `;
}

function renderMaterialRow(item) {
    const mass = item?.mass ?? {};
    return `
        <div class="material-card" data-testid="material-row-${testIdPart(item?.id)}" data-material-id="${escapeHtml(item?.id ?? "")}">
            <strong>${escapeHtml(item?.kind ?? "Material")} ${escapeHtml(item?.id ?? "")}</strong>
            <span>Tick ${escapeHtml(item?.tick ?? "—")}</span>
            <span>Stage ${escapeHtml(item?.stage ?? "—")}</span>
            <span>${escapeHtml(item?.form ?? "Unknown form")}</span>
            <span class="mass-chip">Total ${escapeHtml(formatMass(mass.total))}</span>
        </div>
    `;
}

function renderHistoryRow(item) {
    return `
        <div class="history-card" data-testid="history-row-${testIdPart(item?.id)}" data-execution-id="${escapeHtml(item?.id ?? "")}">
            <strong>Execution ${escapeHtml(item?.id ?? "")}</strong>
            <span>Tick ${escapeHtml(item?.tick ?? "—")}</span>
            <span>Stage ${escapeHtml(item?.stage ?? "—")}</span>
            <span>Inputs ${(item?.inputs ?? []).map(escapeHtml).join(", ") || "none"}</span>
            <span>Outputs ${(item?.outputs ?? []).map(escapeHtml).join(", ") || "none"}</span>
        </div>
    `;
}

function summaryValue(value, formatter, noRun) {
    if (noRun || !hasValue(value)) {
        return "—";
    }

    return formatter(value);
}

function canMutate(state) {
    return state?.available === true &&
        state?.connection === "Connected" &&
        hasValue(state?.runId) &&
        hasValue(state?.revision) &&
        !["Unavailable", "WaitingForParticipants"].includes(String(state?.lifecycle ?? ""));
}
