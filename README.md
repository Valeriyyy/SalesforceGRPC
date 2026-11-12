<img src="StarPush/wwwroot/favicon.svg" alt="" width="96" height="96">

# StarPush

StarPush keeps a SQL database in step with Salesforce. It listens to Salesforce **Change Data Capture** events
over the **Pub/Sub API** (gRPC) and applies every create, update, delete and undelete to tables you choose in
Postgres, SQL Server, MySQL or SQLite. It can also go the other way: rows your own systems change are written back
to Salesforce. Two-way sync works on Postgres today, and is [coming soon](#coming-soon) for SQL Server, MySQL and
SQLite.

Everything is configured from a web UI: connect an org, choose which objects to follow, point each one at a table
and map fields to columns. No code, and no Salesforce password stored anywhere.

## Why

Most Salesforce-to-database integrations poll the REST API on a schedule. That is slow (changes arrive minutes
late), expensive (every poll spends API calls whether anything changed or not) and lossy (deletes and fast
successive edits slip between polls).

Change Data Capture pushes each change the moment it is committed, and the event carries **only the fields that
changed**, so payloads are tiny. The Pub/Sub API delivers those events over a single long-lived gRPC stream and can
replay the last 72 hours, so nothing is lost across a restart. StarPush turns that stream into rows in your
database, so reporting, analytics and the rest of your systems can work from a copy that is seconds behind
Salesforce instead of hours.

Information on the Salesforce Pub/Sub API: <https://developer.salesforce.com/docs/platform/pub-sub-api/overview>

## How it works

```
 Salesforce org                          StarPush                                 Your database
┌────────────────────┐   gRPC stream   ┌──────────────────────────────┐   SQL   ┌──────────────────┐
│ Change Data Capture│ ──────────────▶ │ Worker                       │ ──────▶ │ Target Tables    │
│ (Primary Channel)  │  Avro payloads  │  decode → map → write        │         │ e.g. accounts    │
└────────────────────┘                 │                              │         └──────────────────┘
          ▲                            │ Writeback worker (Postgres)  │                  │
          └─────────── REST API ────── │  read changes → Salesforce   │ ◀── logical ─────┘
                                       └──────────────────────────────┘    replication
                                         App Database (Postgres): configuration, Checkpoints, keys
```

**Inbound: Salesforce → database**

1. StarPush subscribes to one Salesforce **Channel** (the **Primary Channel**), which carries change events for
   the objects you picked, such as `AccountChangeEvent`.
2. Each event arrives as an Apache Avro payload. StarPush fetches the Avro schema it was written with (and caches
   it), decodes it, and reads its header: which records changed, and whether it was a create, update, delete or
   undelete.
3. A **Binding** says which table the object lands in, and its **Field Mappings** say which Salesforce field feeds
   which column. Compound fields are flattened (`BillingAddress.City` becomes `BillingAddressCity`). Updates carry
   only the changed fields, so only those columns are written.
4. The **Key Mapping** column, which holds the Salesforce record ID, finds the row to update or delete. Creates
   are upserts on it, so an event delivered twice still leaves one row.

After each batch, StarPush saves a **Checkpoint**: the position in the stream it has fully applied. A dropped
connection, a database outage or a restart resumes from there. Delivery is therefore *at least once*, and every
write is safe to repeat. Events touching the same record are applied in order; unrelated records are applied in
parallel.

**Writeback: database → Salesforce (Postgres today; other engines coming soon)**

A Binding can be switched from **Inbound** to **Two-way**. StarPush then reads the table's changes and writes them
to Salesforce. On Postgres it reads them through logical replication:

- A changed row with a Salesforce ID updates that record with only the fields that changed.
- A new row is upserted on a **Writeback Key**: a column that identifies the row, stored in a Salesforce External
  ID field. Sending it twice still creates one record, and the new record's ID is written back into the row.
- StarPush tags its own writes, so changes it made itself are never sent back to Salesforce.
- Each field can be Two-way, Inbound only or Writeback only. Fields Salesforce won't let you write are locked to
  Inbound only.
- Rows Salesforce refuses (a validation rule, a required field) are recorded as **Writeback Failures**, with
  Salesforce's reason, and can be retried or dismissed from the UI.
- Deletes are not written back.

SQL Server, MySQL and SQLite are next; see [Coming soon](#coming-soon).

**Connecting to Salesforce**

StarPush signs in with the OAuth 2.0 JWT Bearer flow against an **External Client App**, using a keypair it
generates for itself. It never asks for a Salesforce password or security token. The private key and the database
passwords you enter are encrypted with ASP.NET Data Protection, protected by a certificate you supply from outside
the database.

## Concepts

| Term | Meaning |
| --- | --- |
| **Org Connection** | How StarPush signs in to your Salesforce org. There is exactly one. |
| **Channel** / **Primary Channel** | A Salesforce stream carrying change events for chosen objects. StarPush follows one, the Primary Channel. |
| **Channel Member** | One object (an Entity such as `AccountChangeEvent`) carried on a Channel. |
| **Target Connection** | How StarPush reaches the database it writes to. Entered in the UI, never in a config file. |
| **App Database** | StarPush's own Postgres database: configuration, Checkpoints and keys. Separate from the Target Database, though it can be the same server. |
| **Binding** | The decision that one object's changes land in one Target Table. |
| **Field Mapping** / **Key Mapping** | Salesforce field → column. The Key Mapping is the column holding the Salesforce record ID. |
| **Checkpoint** | How far through the Channel's stream StarPush has applied everything. |
| **Sync Direction** | Inbound, or Two-way with Writeback. |

## Project layout

| Project | What it holds |
| --- | --- |
| `StarPush/` | The ASP.NET Core host: the inbound `Worker`, the `Writeback/` worker, event strategies, REST API and page controllers. |
| `app/` | The Svelte 5 + Vite frontend (Skeleton on Tailwind 4), mounted into server-rendered pages. |
| `Application/` | Services: Bindings and validation, Org and Target Connections, Writeback processing, pipeline status. |
| `Database/` | The App Database repositories, one Target Database repository per engine, Postgres Writeback, and the SQL schema and migrations under `Definitions/`. |
| `Salesforce/` | Salesforce REST, Tooling and OAuth clients. |
| `DTO/`, `Common/` | Shared request/response types and retry policies. |
| `StarPush.Tests/` | xUnit v3 tests, with NSubstitute. |

## Setting it up

### Requirements

- **.NET 10 SDK**
- **Node 20+** for the frontend
- **Postgres** for the App Database
- **A Target Database**: Postgres, SQL Server, MySQL or SQLite. It can be the same Postgres server as the App
  Database. Its tables must already exist; StarPush never creates them.
- **`openssl`**, to make the protecting certificate (on Windows, PowerShell can make it instead; see step 2)
- **A Salesforce org** and an administrator login

The commands below are given for macOS/Linux (bash), Windows PowerShell and Windows cmd. On Windows, `psql` comes
with the Postgres installer but isn't on the `PATH` by default; add its `bin` folder, e.g.
`C:\Program Files\PostgreSQL\16\bin`, or call it by its full path.

### 1. Create the App Database

Create a Postgres database (the examples call it `salesforce_grpc`), then build the schema with the setup script
followed by every migration, in order:

```bash
export APP_DB="postgresql://postgres:password@localhost/salesforce_grpc"

psql "$APP_DB" -f Database/DatabaseSetup.sql
for f in Database/Definitions/migrations/*.sql; do psql "$APP_DB" -v ON_ERROR_STOP=1 -f "$f"; done
```

Windows PowerShell:

```powershell
$env:APP_DB = "postgresql://postgres:password@localhost/salesforce_grpc"

psql $env:APP_DB -f Database\DatabaseSetup.sql
Get-ChildItem Database\Definitions\migrations\*.sql | Sort-Object Name |
    ForEach-Object { psql $env:APP_DB -v ON_ERROR_STOP=1 -f $_.FullName }
```

Windows cmd:

```bat
set APP_DB=postgresql://postgres:password@localhost/salesforce_grpc

psql "%APP_DB%" -f Database\DatabaseSetup.sql
for %f in (Database\Definitions\migrations\*.sql) do psql "%APP_DB%" -v ON_ERROR_STOP=1 -f "%f"
```

(In a `.bat` file, write `%%f` instead of `%f`.)

The setup script also creates two tables StarPush doesn't use, which need PostGIS. Without PostGIS installed you'll
see three `extension "postgis" is not available` errors at the top; they are harmless, and everything StarPush
needs is still created. The migrations are safe to re-run.

**Upgrading an existing install:** run only the migrations. They are applied by hand, never at startup, and the
application fails with a "column does not exist" error until the newest is applied.

### 2. Make the protecting certificate

The keys StarPush encrypts secrets with are themselves protected by a certificate you create and keep outside the
App Database:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
  -keyout dp.key -out dp.crt -subj "/CN=StarPush Data Protection"
openssl pkcs12 -export -out dp.pfx -inkey dp.key -in dp.crt -passout pass:

export STARPUSH_PROTECTING_CERT="$(base64 < dp.pfx | tr -d '\n')"
```

Set `STARPUSH_PROTECTING_CERT_PASSWORD` too if you gave the PFX a password. Alternatively, point
`DataProtection:ProtectingCertificate:FilePath` at the PFX file.

Windows PowerShell, no OpenSSL needed. Windows requires a password on an exported PFX:

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=StarPush Data Protection" -KeyAlgorithm RSA -KeyLength 2048 `
    -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(10) -CertStoreLocation Cert:\CurrentUser\My
$password = Read-Host -AsSecureString "Password for dp.pfx"
Export-PfxCertificate -Cert $cert -FilePath dp.pfx -Password $password
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"   # the PFX is what StarPush reads

# For this terminal only:
$env:STARPUSH_PROTECTING_CERT = [Convert]::ToBase64String([IO.File]::ReadAllBytes("$PWD\dp.pfx"))
$env:STARPUSH_PROTECTING_CERT_PASSWORD = "<the password you chose>"

# Or for every new terminal (your Windows user's environment):
[Environment]::SetEnvironmentVariable("STARPUSH_PROTECTING_CERT", $env:STARPUSH_PROTECTING_CERT, "User")
[Environment]::SetEnvironmentVariable("STARPUSH_PROTECTING_CERT_PASSWORD", $env:STARPUSH_PROTECTING_CERT_PASSWORD, "User")
```

Windows cmd: a base64 PFX is longer than `setx` accepts (1024 characters), so point StarPush at the file instead.
Make `dp.pfx` with OpenSSL or the PowerShell above, then:

```bat
setx DataProtection__ProtectingCertificate__FilePath "C:\path\to\dp.pfx"
setx STARPUSH_PROTECTING_CERT_PASSWORD "<the password you chose>"
```

`setx` takes effect in terminals opened afterwards. Use `set` instead of `setx` for the current terminal only.

> **Back up `dp.pfx`.** Without it, the stored Salesforce key and database passwords can't be decrypted, and the
> only way out is to disconnect and set everything up again.

### 3. Configure

Copy `StarPush/appsettingstemplate.json` to `StarPush/appsettings.json` (or use `dotnet user-secrets`). Only
deployment settings live here; nothing org-specific and no secrets beyond the App Database connection:

- `ConnectionStrings:appDatabase`: the App Database from step 1.
- `SalesforceConfig:ApiVersion`: `61.0` or later.
- `SalesforceConfig:PubSubEndpoint`: leave the default (it serves sandboxes and production alike) unless your org
  uses EU data residency: `https://api.deu.pubsub.salesforce.com:7443`.
- `SalesforceConfig:CallbackUrl`: `https://localhost:5001/api/orgconnection/bootstrap/callback` for local use.
  It must be well-formed even if you never use the browser sign-in.

### 4. Run

For local development, run the backend and the Vite dev server side by side. The pages load their scripts from
Vite in Debug builds.

```bash
dotnet dev-certs https --trust                    # once: trust the local HTTPS certificate
npm --prefix app install
npm --prefix app run dev                          # terminal 1: Vite on :5173
dotnet watch run --project StarPush/StarPush.csproj   # terminal 2: https://localhost:5001
```

Windows PowerShell or cmd (the same commands, with Windows paths):

```powershell
dotnet dev-certs https --trust
npm --prefix app install
npm --prefix app run dev                                  # terminal 1
dotnet watch run --project StarPush\StarPush.csproj        # terminal 2
```

For a self-contained build, `dotnet run -c Release --project StarPush/StarPush.csproj` (or `dotnet publish`) builds
the frontend into `StarPush/wwwroot/dist` itself.

Open <https://localhost:5001>. The **Overview** shows each setup stage and what it is waiting on. A fresh install
idles rather than failing; `GET /health` reports `Degraded` until it's connected.

### 5. Connect Salesforce

On the **Org Connection** page:

1. In Salesforce Setup, enable **External Client Apps** (*External Client Apps → Settings → Allow creation of
   external client apps*), then create one with OAuth enabled, the scopes `api` and `refresh_token`, the **JWT
   Bearer flow** enabled, and your `CallbackUrl` as its callback. Copy its **Consumer Key**.
2. In StarPush, enter the Consumer Key, the **Run-as User** (the event stream runs as this user, so their
   permissions bound what StarPush can see) and the **Administering User** (used to manage Channels; may be the
   same user), and whether the org is a sandbox. Saving generates StarPush's signing keypair.
3. Download the **Signing Certificate** from the page and upload it to the External Client App's *Digital
   Signatures*. Set *Permitted Users* to **Admin approved users are pre-authorized**, and assign both users a
   permission set granting **API Enabled** and access to the app.
4. Click **Connect to Salesforce**. If Salesforce refuses, the page shows Salesforce's own error alongside the setup step that most
   likely caused it. Permission changes take a minute to propagate, so retry before changing anything.

### 6. Set up the Target Connection

On the **Target Connection** page, pick the engine and enter its details. They are proved on save by reading the
database's tables. Changing the engine, host or database later (a **Repoint**) destroys every Binding, because they
were built against the old database's tables.

### 7. Choose what to follow

On the **Channels** page, create a Change Data Capture Channel (or use an existing one), add a member for each
object you want, such as `AccountChangeEvent`, and make it the **Primary Channel**. For a Channel never read before,
choose where to start: **Latest** (changes from now on) or **Earliest** (everything Salesforce still keeps, up to
72 hours back).

### 8. Bind objects to tables

On the **Bindings** page, choose a Target Table for each object, pick its Key Mapping column (a text column with a
unique constraint, holding the Salesforce ID), map fields to columns, and **Activate**. The editor checks every
mapping for type compatibility as you go, and changes reach the running worker without a restart.

### 9. Optional: Two-way (Postgres)

Two-way is available on Postgres 16 or later; SQL Server, MySQL and SQLite are [coming soon](#coming-soon). On the
Postgres server:

```sql
ALTER SYSTEM SET wal_level = logical;   -- then restart Postgres (see below)
ALTER SYSTEM SET max_slot_wal_keep_size = '10GB';   -- caps what the server keeps while Writeback is down
CREATE ROLE starpush_replication LOGIN PASSWORD '…' REPLICATION;
GRANT SELECT ON salesforce.accounts TO starpush_replication;          -- each Two-way table
GRANT EXECUTE ON FUNCTION pg_replication_origin_session_setup(text) TO starpush_writer;   -- the Target Connection's login
```

Restart Postgres so `wal_level` takes effect. On Windows, from PowerShell or cmd run as Administrator (the service
name of the standard installer; `Get-Service postgresql*` lists yours):

```powershell
Restart-Service postgresql-x64-16      # PowerShell
net stop postgresql-x64-16 && net start postgresql-x64-16      # cmd
```

On macOS with the same installer, `sudo launchctl kickstart -k system/postgresql-16`; on Linux, usually
`sudo systemctl restart postgresql`.

In Salesforce, add a **Text, External ID, Unique** field to the object to hold the Writeback Key. Then enter the
replication login as the **Replication Credential** on the Target Connection page, and switch the Binding's
**Sync Direction** to Two-way. Anything still missing is listed with the exact SQL to fix it.

## Running the tests

```bash
dotnet test StarPush.Tests/StarPush.Tests.csproj
```

Windows: `dotnet test StarPush.Tests\StarPush.Tests.csproj`. To run the database tests, set the variables below
first: `$env:STARPUSH_TEST_TARGET_DATABASE = "Host=localhost;Database=…"` in PowerShell, or
`set STARPUSH_TEST_TARGET_DATABASE=Host=localhost;Database=…` in cmd.

Tests against real databases skip unless you point them at one: `STARPUSH_TEST_TARGET_DATABASE` (Postgres; the
Writeback tests also need Postgres 16+ with `wal_level = logical`), `STARPUSH_TEST_SQLSERVER_TARGET_DATABASE`,
`STARPUSH_TEST_MYSQL_TARGET_DATABASE` and `STARPUSH_TEST_APP_DATABASE`. Use throwaway databases; some of these tests
create and drop tables, and the App Database ones wipe the Org Connection.

## Observability

Logs go to the console and to `StarPush/logs/log-<date>.txt` as JSON. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to export
logs, traces and metrics over OpenTelemetry. `GET /health` reports the Org and Target Connections.

## Coming soon

**Two-way sync for SQL Server, MySQL and SQLite.** Today, Bindings on these engines sync Salesforce into the
database only. Two-way sync is coming to all three, so changes made in their tables will be written back to
Salesforce just as they are on Postgres, with the same per-field directions, Writeback Key, Writeback Defaults and
Writeback Failures.

Most of the work is already shared: everything after reading the database's changes works the same for every
engine. What each engine still needs is a way to read its own changes and a way to tag StarPush's own writes, so
they are never sent back to Salesforce:

| Engine | Reading changes |
| --- | --- |
| SQL Server | Change Tracking, with `CHANGE_TRACKING_CONTEXT` to tag StarPush's writes |
| MySQL | The row-based binary log |
| SQLite | Triggers writing to a change table, since SQLite has no log another process can read |

Until an engine is supported, its Bindings show the Two-way option but don't let you choose it, and Inbound sync
keeps working as it does now.

## Status

Under active development. Two-way sync is implemented for Postgres; SQL Server, MySQL and SQLite are coming soon.
Docker packaging doesn't yet include the frontend. Exactly one StarPush instance per App Database is assumed: two
would overwrite each other's Checkpoints.

Formerly SalesforceGRPC. The example App Database name `salesforce_grpc` is kept as-is: an existing database keeps
its name, and renaming a Postgres database is an operational step, not a code change.
