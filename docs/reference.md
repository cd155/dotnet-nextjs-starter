# Reference

Run every command from the repo root.

## Tests

```bash
dotnet test server/tests/PlatformServer.Tests
npm --prefix web run lint
npm --prefix web run build
```

The server tests run the whole app in memory on SQLite (`AppApiFactory`), with a fake email sender, so they need
no Docker. `McpTokens` runs the real OAuth flow to get an MCP access token.

## EF Core migrations

Install the tool once with `dotnet tool install --global dotnet-ef`. After changing an entity or `AppDbContext`:

```bash
ConnectionStrings__DefaultConnection="Host=unused" \
  dotnet ef migrations add <Name> -o Migrations --project server/src/DataLibrary
```

The design-time factory (`AppDbContextFactory`) needs a connection string but doesn't connect for `migrations add`.
`DatabaseInitializer` applies the migrations when the stack starts. Remove the last one, before it's applied
anywhere, with `dotnet ef migrations remove --project server/src/DataLibrary`.

## OpenAPI document

`web/openapi.json` is PlatformServer's OpenAPI document, checked in, and `web/src/lib/api-types.ts` is generated
from it. After changing anything an endpoint sends or accepts, regenerate both and commit them with the change:

```bash
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__DefaultConnection=Host=unused \
  dotnet build server/src/PlatformServer -p:OpenApiGenerateDocuments=true
npm --prefix web run api:types
```

CI fails when either is out of date. In Development the document is also served at `/openapi/v1.json`, with
Scalar at `/scalar/v1`.

## Docker

```bash
docker compose -f server/docker-compose.local.yml up -d --build    # start, rebuilding the images
docker compose -f server/docker-compose.local.yml logs -f platformserver
docker compose -f server/docker-compose.local.yml down             # stop, keeping the database
docker compose -f server/docker-compose.local.yml down -v          # stop and wipe the database
```

Set `SEED_MOCK_DATA=false` in `server/.env` to start without the demo user.

## Formatting

`dotnet format server/Starter.sln` before a PR. CI checks the whitespace (`dotnet format whitespace --folder`).
