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

export function escapeHtml(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

export function testIdPart(value) {
    const normalized = String(value ?? "unknown").replace(/[^a-zA-Z0-9]+/g, "-").replace(/^-+|-+$/g, "");
    return normalized.length === 0 ? "unknown" : normalized;
}

export function displayValue(value, unit = "") {
    if (value === null || value === undefined || value === "") {
        return "Unavailable";
    }

    return `${String(value)}${unit ? ` ${unit}` : ""}`;
}

export function hasValue(value) {
    return value !== null && value !== undefined && value !== "";
}

export function displayTimestamp(value) {
    if (!value) {
        return "No timestamp";
    }

    const date = new Date(value);
    if (Number.isNaN(date.valueOf())) {
        return String(value);
    }

    return date.toLocaleString(undefined, {
        hour12: false,
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
    });
}

export function statusClass(value) {
    const normalized = String(value ?? "").toLowerCase();
    if (normalized.includes("bad") || normalized.includes("fault") || normalized.includes("unavailable") || normalized.includes("rejected")) {
        return "bad";
    }

    if (normalized.includes("uncertain") || normalized.includes("paused") || normalized.includes("pending") || normalized.includes("resource") || normalized.includes("reconnecting")) {
        return "uncertain";
    }

    if (normalized.includes("good") || normalized.includes("running") || normalized.includes("connected") || normalized.includes("accepted")) {
        return "good";
    }

    return "unknown";
}

export function lifecycleBadgeClass(lifecycle) {
    switch (lifecycle) {
        case "Running":
            return "badge--running";
        case "Paused":
        case "ResourceLimited":
        case "WaitingForParticipants":
            return "badge--paused";
        case "Stopped":
        case "Unavailable":
            return "badge--stopped";
        default:
            return "badge--constraint";
    }
}

export function formatMass(value) {
    return formatScaledDecimal(value, 1_000_000n, "kg", "mg");
}

export function formatEnergy(value) {
    return formatScaledDecimal(value, 3_600_000n, "kWh", "J");
}

export function toFiniteNumber(value) {
    if (value === null || value === undefined || value === "") {
        return null;
    }

    const number = Number(value);
    return Number.isFinite(number) ? number : null;
}

export function latestStage(process) {
    return process?.stages?.[0] ?? null;
}

export function sumStageString(process, field) {
    if (!Array.isArray(process?.stages)) {
        return "";
    }

    let total = 0n;
    let hasValue = false;
    for (const stage of process.stages) {
        const raw = stage?.[field];
        if (raw === null || raw === undefined || raw === "") {
            continue;
        }

        try {
            total += BigInt(raw);
            hasValue = true;
        } catch {
            return String(raw);
        }
    }

    return hasValue ? total.toString() : "";
}

export function processHasFault(process) {
    return Boolean(process?.stages?.some(stage => stage?.jamAlarm || stage?.strandBreakAlarm));
}

export function processHasWarning(process) {
    return !process?.initialized ||
        !process?.nativeConfigured ||
        Boolean(process?.stages?.some(stage => stage?.enabled === false || isConstraintState(stage?.state)));
}

export function isConstraintState(state) {
    return [
        "ElectricalConstraint",
        "ThermalConstraint",
        "BufferFull",
        "Unavailable",
        "ResourceLimited"
    ].includes(String(state ?? ""));
}

function formatScaledDecimal(value, divisor, scaledUnit, rawUnit) {
    if (!hasValue(value)) {
        return "No data";
    }

    try {
        const raw = BigInt(value);
        const sign = raw < 0n ? "-" : "";
        const absolute = raw < 0n ? -raw : raw;
        const whole = absolute / divisor;
        const fraction = absolute % divisor;
        const decimals = divisor === 1_000_000n ? 3 : 4;
        const scale = 10n ** BigInt(decimals);
        const rounded = (fraction * scale + divisor / 2n) / divisor;
        const adjustedWhole = whole + rounded / scale;
        const adjustedFraction = rounded % scale;
        const fractionText = adjustedFraction.toString().padStart(decimals, "0").replace(/0+$/, "");
        const scaled = fractionText.length > 0
            ? `${sign}${adjustedWhole}.${fractionText}`
            : `${sign}${adjustedWhole}`;
        return `${scaled} ${scaledUnit}`;
    } catch {
        return displayValue(value, rawUnit);
    }
}
