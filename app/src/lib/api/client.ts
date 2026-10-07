// Calls to this application's api/* endpoints, with their error bodies turned into one typed failure. Pages act
// through these and then reload on success (ADR 0006): the server rebuilds the page and the shell from one read.
import type { SalesforceErrorView } from "../types/views";

/** Why a call failed, in the three shapes the controllers answer with. */
export type ApiFailure =
    /** {error} — a refused request (400), something missing (404), or the service unable (503). */
    | { kind: "message"; message: string }
    /** 502: Salesforce refused, with the translation and its own words. */
    | { kind: "salesforce"; error: SalesforceErrorView }
    /** 409: the credentials belong to a different org than this installation is bound to. */
    | { kind: "orgMismatch"; message: string; storedOrgId: string; discoveredOrgId: string };

export type ApiResult<T> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

export const getJson = <T>(url: string) => send<T>("GET", url);
export const postJson = <T>(url: string, body?: unknown) => send<T>("POST", url, body);
export const putJson = <T>(url: string, body?: unknown) => send<T>("PUT", url, body);
export const patchJson = <T>(url: string, body?: unknown) => send<T>("PATCH", url, body);
export const deleteJson = <T>(url: string) => send<T>("DELETE", url);

async function send<T>(method: string, url: string, body?: unknown): Promise<ApiResult<T>> {
    let response: Response;
    try {
        response = await fetch(url, {
            method,
            headers: body === undefined ? undefined : { "Content-Type": "application/json" },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
    } catch {
        return fail({ kind: "message", message: "This application could not be reached. Check that it is still running." });
    }

    const payload = await response.json().catch(() => null);
    return response.ok ? { ok: true, value: payload as T } : fail(failureFrom(response.status, payload));
}

function fail(failure: ApiFailure): ApiResult<never> {
    return { ok: false, failure };
}

function failureFrom(status: number, body: any): ApiFailure {
    if (status === 409 && typeof body?.storedOrgId === "string") {
        return { kind: "orgMismatch", message: body.error, storedOrgId: body.storedOrgId, discoveredOrgId: body.discoveredOrgId };
    }

    if (status === 502 && typeof body?.rawResponse === "string") {
        return {
            kind: "salesforce",
            error: {
                error: body.error ?? "",
                errorDescription: body.errorDescription ?? "",
                guidance: body.guidance ?? null,
                rawResponse: body.rawResponse,
                occurredAt: null
            }
        };
    }

    if (typeof body?.error === "string") {
        return { kind: "message", message: body.detail ? `${body.error} (${body.detail})` : body.error };
    }

    // ASP.NET's own validation response, before the action runs.
    if (typeof body?.title === "string") {
        return { kind: "message", message: body.title };
    }

    return { kind: "message", message: `The request failed with HTTP ${status}.` };
}
