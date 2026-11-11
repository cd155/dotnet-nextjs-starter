# dotnet-nextjs-starter

One app in two halves, side by side:

- **`server/`**: the .NET 10 solution. PlatformServer hosts the REST API, the Blazor Server CMS, the OpenIddict
  OAuth server and the MCP endpoint. Its conventions are in `server/CLAUDE.md`.
- **`web/`**: the Next.js app. Its conventions are in `web/CLAUDE.md`.

The web app calls the API from its own server (`API_BASE_URL`, default `http://localhost:8080`), never from the
browser. `docs/architecture.md` explains the layers and how to add a feature.

## Running commands

Run commands from the repo root and name the folder, instead of `cd`-ing into it:

- Server: `dotnet build server/Starter.sln`, `dotnet test server/tests/PlatformServer.Tests`,
  `dotnet format server/Starter.sln`, and migrations as in `docs/reference.md`.
- Web: `npm --prefix web ci`, `npm --prefix web run dev`, `npm --prefix web run lint`, `npm --prefix web run build`.
- Local stack: `docker compose -f server/docker-compose.local.yml up -d --build`. It reads overrides from
  `server/.env`.

`global.json` pins the .NET SDK for every `dotnet` command in the repo.

## Where the halves meet

Change both sides in the same PR:

- A change to what the API sends or accepts regenerates `web/openapi.json` and `web/src/lib/api-types.ts`
  (`docs/reference.md`, "OpenAPI document"); the type check then shows what the web must change.
- The web's reads are in `web/src/lib/api.ts`, and its writes go through the route handlers under
  `web/src/app/api/`.

## Before a PR

Run the checks of each half you changed: `dotnet test server/tests/PlatformServer.Tests` for the server;
`npm --prefix web run lint` and `npm --prefix web run build` for the web. CI runs each half's checks only when that
half changed.

## Testing end to end

1. `docker compose -f server/docker-compose.local.yml up -d --build`
2. `npm --prefix web run dev`, then open http://localhost:3000. Sign in as `demo@example.com` / `Demopassword1@`.
   The CMS is http://localhost:8080, as `admin@example.com` / `Adminpassword1@`. Emails land in Mailpit,
   http://localhost:8025.
3. MCP: `npx @modelcontextprotocol/inspector`, as in `docs/mcp.md`.
