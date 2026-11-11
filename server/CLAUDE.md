# Server

The .NET half of the repo. Paths below are relative to `server/`; the root `CLAUDE.md` has the commands, run from
the repo root. Formatting follows `.editorconfig`: run `dotnet format` before a PR. CI checks only the whitespace,
so it misses `using` directives out of order.

## Conventions

- **A feature is a folder** under `src/PlatformServer/Features/`, with `Add<Feature>()` and
  `Map<Feature>Endpoints()` called from `Program.cs`. `docs/architecture.md` ("Adding a feature") has the steps.
- **Minimal APIs, no controllers.** JSON settings such as enums as strings go in `ConfigureHttpJsonOptions`.
- **Request records are checked by the built-in validation** (`AddValidation`): attributes such as `[Required]`
  are answered with a `400` and the errors by field before the handler runs. A repository checks the same rules
  again when the MCP tools also call it.
- **Errors:** code throws a `SharedLibrary.Exceptions` type, and `AppExceptionHandler` answers it with its status
  and a problem body; the MCP filter in `McpFeature` hands its message to the model.
- **Prefer the framework's built-in support** (Identity, `IExceptionHandler`, `AddProblemDetails`, the rate
  limiter, `AddValidation`) over custom code that does the same job.
- **No interface for a class with one implementation.** Tests use the real classes, on SQLite; `FakeEmailSender`
  stands in for SMTP (through `IAccountEmailSender`), and `InlineEmailQueue` for the email queue.
- **Records are read-only:** `init` properties and `IReadOnlyList<T>`. `FeatureRecordTests` checks every record
  under `Features`.
- **Endpoints describe the data, not a web page**, so another client, such as a mobile app, can use them.
- **User-scoped routes find the user, not an id the client keeps:** they sit under `/me`, and a handler takes
  `ClaimsPrincipal` and calls `GetRequiredUserId()`, which reads the cookie's user or an MCP token's `sub`.
- **Every endpoint needs a signed-in user** unless it says `AllowAnonymous`: the fallback policy is
  secure by default.
- **`/mcp` is signed in only by a bearer token**, never by the CMS cookie (`McpFeature`). A tool that needs a role
  carries `[Authorize(Roles = ...)]`, which hides it from everyone else.
- **The CMS works on untracked copies:** a Blazor page keeps one `DbContext` for its life, so a tracked entity
  changed by an edit that's never saved would be saved by the next save.
- **A prompt the server builds is an embedded Markdown file** beside the code that fills it
  (`Features/Mcp/Prompts/`), so a missing template fails the build.

## OpenAPI document

`../web/openapi.json` is PlatformServer's OpenAPI document, checked in; the web generates its API types from it.
After changing anything an endpoint sends or accepts, regenerate it as `docs/reference.md` says and commit it with
the change. Server CI fails when it's out of date. The XML doc comments on the records become the document's
descriptions, which the web's generated types carry: explain a field there.

## Migrations

`docs/reference.md`, "EF Core migrations". When the .NET SDK isn't available, hand-write a migration to match the
shape of an existing one, and keep `AppDbContextModelSnapshot.cs` in step.
