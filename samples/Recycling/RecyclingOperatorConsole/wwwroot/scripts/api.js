// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

const jsonHeaders = {
    "Accept": "application/json"
};

export async function fetchSession() {
    return fetchJson("/api/session");
}

export async function fetchState() {
    return fetchJson("/api/state");
}

export async function fetchInspector(processName) {
    return fetchJson(`/api/processes/${encodeURIComponent(processName)}`);
}

export async function fetchMaterial(runId, after = "0", limit = 50) {
    const query = new URLSearchParams({ runId, after: String(after), limit: String(limit) });
    return fetchJson(`/api/material?${query}`);
}

export async function fetchHistory(runId, after = "0", limit = 50) {
    const query = new URLSearchParams({ runId, after: String(after), limit: String(limit) });
    return fetchJson(`/api/history?${query}`);
}

export async function fetchHolds(runId, after = "0", limit = 50, disposition = "All") {
    const query = new URLSearchParams({ runId, after: String(after), limit: String(limit), disposition });
    return fetchJson(`/api/holds?${query}`);
}

export async function postCommand(command, csrfToken) {
    return fetchJson("/api/commands", {
        method: "POST",
        headers: {
            ...jsonHeaders,
            "Content-Type": "application/json",
            "X-CSRF-Token": csrfToken
        },
        body: JSON.stringify(command)
    });
}

export async function fetchCommand(requestId) {
    return fetchJson(`/api/commands/${encodeURIComponent(requestId)}`);
}

export function openStateStream(onState, onStatus) {
    if (!("EventSource" in window)) {
        onStatus?.("Unavailable", "This browser does not support live state delivery; controls remain disabled.");
        return null;
    }

    const events = new EventSource("/api/events", { withCredentials: true });
    events.addEventListener("state", event => {
        try {
            onState(JSON.parse(event.data));
        } catch (error) {
            onStatus?.("Unavailable", `State event could not be parsed: ${error.message}`);
        }
    });
    events.onerror = () => {
        const terminal = events.readyState === EventSource.CLOSED;
        onStatus?.(terminal ? "Unavailable" : "Reconnecting",
            terminal ? "Live stream closed; retrying with bounded backoff." : "Live stream interrupted.",
            terminal);
    };
    events.onopen = () => onStatus?.("Connected", "Live state stream connected.");
    return events;
}

async function fetchJson(url, init = {}) {
    const response = await fetch(url, {
        credentials: "same-origin",
        ...init,
        headers: {
            ...jsonHeaders,
            ...(init.headers ?? {})
        }
    });

    const text = await response.text();
    let payload = null;
    if (text.length > 0) {
        try {
            payload = JSON.parse(text);
        } catch (error) {
            throw Object.assign(new Error(`Unexpected non-JSON response from ${url}: ${error.message}`), {
                status: response.status,
                payload: text
            });
        }
    }

    if (!response.ok) {
        const message = payload?.error ?? response.statusText ?? `HTTP ${response.status}`;
        throw Object.assign(new Error(message), {
            status: response.status,
            payload
        });
    }

    return payload;
}
