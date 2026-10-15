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
        onStatus?.("Unavailable", "This browser does not support server-sent events. Polling remains available.");
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
    events.onerror = () => onStatus?.("Reconnecting", "Live stream interrupted; fetching newest state.");
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
