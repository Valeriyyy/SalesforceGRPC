// What the Target Connection form needs beyond its view model: how each engine is presented, the bodies
// api/targetconnection takes and returns, and the identity rule the server enforces (docs/adr/0004).
import type { EngineView, FieldView, TargetConnectionDetailsView, TargetDatabaseErrorView } from "../../lib/types/views";

/** new: nothing stored. edit: change the stored connection. repoint: a different database, Bindings destroyed. */
export type FormMode = "new" | "edit" | "repoint";

/** Every field's value as typed, keyed by field name. Options included; a Bool is "true" or "false". */
export type FormValues = Record<string, string>;

/** PUT api/targetconnection. A blank password on an edit with the same identity keeps the stored one. */
export interface SaveTargetConnection {
    engine: string;
    host: string | null;
    port: number | null;
    databaseName: string | null;
    username: string | null;
    password: string | null;
    filePath: string | null;
    options: Record<string, string>;
}

/** POST api/targetconnection/repoint: the view's counts, so a Binding created since aborts instead of vanishing. */
export interface RepointTargetConnection extends SaveTargetConnection {
    expectedBindings: number;
    expectedFieldMappings: number;
}

/** What PUT and the other actions return. Only the part the page reads. */
export interface TargetConnectionResult {
    connectionState: "Incomplete" | "Connected" | "Failed";
    lastError: TargetDatabaseErrorView | null;
}

/** Field names that map onto the connection's own details. Every other field is an option. */
const detailFields = new Set(["host", "port", "databaseName", "username", "password", "filePath"]);

/** The fields that decide which database is reached, with the engine. */
const identityFields = ["host", "databaseName", "filePath"] as const;

export const isOption = (field: FieldView) => !detailFields.has(field.name);
export const isIdentity = (field: FieldView) => (identityFields as readonly string[]).includes(field.name);

/** Monograms stand in for logos; an engine missing here still renders, under its API name. */
const presentation: Record<string, { label: string; blurb: string; mark: string; tone: string }> = {
    Postgres: { label: "PostgreSQL", blurb: "Tables addressed as schema.table", mark: "Pg", tone: "bg-[#336791] text-white" },
    SqlServer: { label: "SQL Server", blurb: "Tables addressed as schema.table", mark: "SQL", tone: "bg-[#a91d22] text-white" },
    MySql: { label: "MySQL", blurb: "Tables addressed as database.table", mark: "My", tone: "bg-[#00758f] text-white" },
    Sqlite: { label: "SQLite", blurb: "A single file on this server", mark: "Lite", tone: "bg-[#0f80cc] text-white" }
};

export function presentationOf(engine: string) {
    return presentation[engine] ?? { label: engine, blurb: "", mark: engine.slice(0, 2), tone: "bg-surface-500 text-white" };
}

/** A fresh form for an engine: its definition's defaults and nothing else. Nothing carries over between engines. */
export function defaultsOf(engine: EngineView): FormValues {
    return Object.fromEntries(engine.fields.map(f => [f.name, f.default ?? ""]));
}

/** The stored details, with an empty password: it is never sent to the page. */
export function valuesOf(engine: EngineView, connection: TargetConnectionDetailsView): FormValues {
    const values = defaultsOf(engine);
    for (const f of engine.fields) {
        const stored = f.name === "port" ? connection.port?.toString()
            : f.name === "password" ? ""
            : detailFields.has(f.name) ? connection[f.name as "host" | "databaseName" | "username" | "filePath"]
            : connection.options[f.name];
        if (stored != null) {
            values[f.name] = stored;
        }
    }
    return values;
}

/** Whether the form names a different database than the stored one. Host compares case-insensitively, as DNS does. */
export function identityChanged(connection: TargetConnectionDetailsView, engine: EngineView, values: FormValues): boolean {
    if (engine.engine !== connection.engine) {
        return true;
    }
    const typed = (name: string) => (values[name] ?? "").trim();
    return typed("host").toLowerCase() !== (connection.host ?? "").toLowerCase()
        || typed("databaseName") !== (connection.databaseName ?? "")
        || typed("filePath") !== (connection.filePath ?? "");
}

/** The request body for an engine's fields. Blank details are sent as null so the server can tell them apart. */
export function requestOf(engine: EngineView, values: FormValues): SaveTargetConnection {
    const value = (name: string) => {
        const v = values[name]?.trim() ?? "";
        return v === "" ? null : v;
    };
    const has = (name: string) => engine.fields.some(f => f.name === name);
    const options: Record<string, string> = {};
    for (const f of engine.fields.filter(isOption)) {
        if (value(f.name) !== null) {
            options[f.name] = value(f.name)!;
        }
    }

    return {
        engine: engine.engine,
        host: has("host") ? value("host") : null,
        port: has("port") && value("port") !== null ? Number(value("port")) : null,
        databaseName: has("databaseName") ? value("databaseName") : null,
        username: has("username") ? value("username") : null,
        // Untrimmed: a password's spaces are part of it.
        password: has("password") && values.password ? values.password : null,
        filePath: has("filePath") ? value("filePath") : null,
        options
    };
}

/** How the stored connection names its database, as typed into the repoint confirmation. */
export const confirmationWord = (connection: TargetConnectionDetailsView) =>
    connection.filePath ?? connection.databaseName ?? "";

/** host:port/database, or the file path. */
export function addressOf(d: { host: string | null; port: number | null; databaseName: string | null; filePath: string | null }) {
    return d.filePath && !d.host ? d.filePath : `${d.host ?? ""}${d.port ? `:${d.port}` : ""}/${d.databaseName ?? ""}`;
}
