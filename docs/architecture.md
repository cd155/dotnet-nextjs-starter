# Architecture

## One server app, three kinds of client

`PlatformServer` is one ASP.NET Core app that serves three clients, each signed in its own way:

| Client | Path | Signed in by |
|---|---|---|
| The web app's server (Next.js) | `/api/v1/...` | The Identity cookie, which the web app keeps under its own name (`app_session`) and sends back to the API |
| The CMS (Blazor Server) | `/`, `/notes`, `/email`, `/login` | The same Identity cookie, set by the CMS's login form (`Auth/login`) |
| AI chat apps | `/mcp` | A bearer token from this app's OpenIddict server (`/connect/authorize`, `/connect/token`) |

A policy scheme (`McpFeature.AddMcp`) sends `/mcp` to the bearer token and everything else to the cookie, so a
page open in an admin's browser can never call an MCP tool with the admin's cookie.

**Secure by default:** the authorization fallback policy requires a signed-in user, so a new endpoint or CMS page
is protected until it says `AllowAnonymous`.

## The web app talks to the API from its server

The browser never calls the API. Pages read on the Next.js server (`web/src/lib/api.ts`) with the session cookie,
and writes go through the app's own route handlers (`web/src/app/api/...`), which relay them with
`forwardToApi` in `web/src/lib/route-helpers.ts`. So the API needs no CORS for the web app, `API_BASE_URL` never
reaches the browser, and the session cookie is `HttpOnly` on the web app's own origin. The handlers also pass on
`X-Forwarded-For`, so the API's per-IP rate limits count each visitor rather than the web server.

## Layers

- **SharedLibrary:** the exceptions and the limits the other projects share (`NoteLimits`).
- **DataLibrary:** `AppDbContext` (Identity's tables, OpenIddict's and the features'), the entities, the audit
  interceptor that stamps `CreatedTime` and `UpdatedTime`, and the migrations.
- **PlatformServer/Features/&lt;Feature&gt;:** a feature's endpoints, its repository and its request and response
  records. `Program.cs` registers each feature with `services.Add<Feature>()` and maps it with
  `api.Map<Feature>Endpoints()`.
- **Errors:** a repository throws a `SharedLibrary.Exceptions` type, such as `EntityNotFoundException` or
  `AppValidationException`, and `AppExceptionHandler` answers it with its status and a problem body. The MCP
  tools hand the same message to the model.
- **DatabaseInitializer:** a one-shot container that applies the migrations and seeds the roles, the admin, the
  MCP scope and clients, and, locally, the demo user.

## Adding a feature

Follow Notes through the layers. Say the feature is Bookmarks:

1. **Entity:** `DataLibrary/Entities/Bookmarks/BookmarkEntity.cs`, deriving from `BaseEntity`, with a
   `UserId` if each user has their own. Add a `DbSet` and its configuration to `AppDbContext`, then a migration
   ([reference](reference.md#ef-core-migrations)).
2. **Feature:** `PlatformServer/Features/Bookmarks/` with a repository that takes the user's id in every method,
   the records the API sends and reads (read-only: `init` and `IReadOnlyList<T>`), and `BookmarksFeature.cs` with
   `AddBookmarks()` and `MapBookmarksEndpoints()`. Register both in `Program.cs`.
3. **OpenAPI and types:** regenerate `web/openapi.json` and `web/src/lib/api-types.ts`
   ([reference](reference.md#openapi-document)), and name the new types in `web/src/lib/types.ts`.
4. **Web:** a fetcher in `web/src/lib/api.ts`, route handlers under `web/src/app/api/bookmarks/` for the writes,
   a page under `web/src/app/(app)/bookmarks/`, its strings in `web/src/lib/i18n/messages/en/`, its path in
   `web/src/lib/protected-routes.ts` and `proxy.ts`'s matcher, and a link in `components/app-nav.tsx`.
5. **MCP (optional):** a tool class in `Features/Mcp/`, registered with `WithTools<...>()` in `McpFeature`. Write
   each `[Description]` for the model: when to call the tool, and what it returns.
6. **CMS (optional):** a page in `Components/Pages/`, with `[Authorize(Roles = ...)]`, and a link in
   `Components/Layout/NavMenu.razor`.
7. **Tests:** an API test through `AppApiFactory` (see `NotesApiTests`) and, for MCP, through `McpTokens`
   (see `McpNoteToolsTests`).
