# Deployment

The production setup is one VPS running Docker, with [Caddy](https://caddyserver.com) in front for TLS. The images
are built from this repo and pushed to a registry such as GitHub Container Registry, and the VPS pulls them.

```
Caddy ─┬─ app.example.com ─> webapp (Next.js, port 3000)
       └─ api.example.com ─> platformserver (API, CMS, OAuth, /mcp, port 8080)
postgres, and databaseinitializer (runs once per deploy)
```

## First time

1. On the VPS, install Docker, and create the network Caddy shares with the app: `docker network create caddy-net`.
2. Run Caddy on `caddy-net`, with routes like [server/Caddyfile.example](../server/Caddyfile.example).
3. Copy `server/docker-compose.yml` to the VPS, and write a `.env` beside it (below).
4. Build and push the images, then start the stack (below).
5. Point the DNS for both hosts at the VPS.

## The `.env`

| Name | What |
|---|---|
| `REGISTRY_NAMESPACE` | Where the images are, e.g. `ghcr.io/you/dotnet-nextjs-starter` |
| `DB_NAME`, `DB_USER`, `DB_PASSWORD` | Postgres |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | The CMS admin, seeded on the first run. The password needs 8+ characters with upper and lower case and a digit |
| `WEB_APP_URL` | The web app's public URL, e.g. `https://app.example.com`: CORS, and the links in emails |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD` | Your email provider's SMTP server ([Email](#email)) |
| `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | The sender. The name is also the app's name in the emails |
| `MCP_AUTHORITY`, `MCP_RESOURCE` | e.g. `https://api.example.com` and `https://api.example.com/mcp` ([MCP](mcp.md#settings)) |
| `OPENIDDICT_SIGNING_CERTIFICATE`, `OPENIDDICT_ENCRYPTION_CERTIFICATE`, `OPENIDDICT_CERTIFICATE_PASSWORD` | The OAuth server's keys ([MCP](mcp.md#settings)) |
| `TURNSTILE_SITE_KEY`, `TURNSTILE_SECRET_KEY` | Optional bot check on sign-up ([Turnstile](#turnstile)) |
| `SENTRY_SERVER_DSN`, `SENTRY_WEBAPP_DSN` | Optional error reporting |

Keep the `.env` out of git: it holds the secrets.

## Build, push and deploy

From the repo root, for the VPS's platform:

```bash
NS=ghcr.io/you/dotnet-nextjs-starter
docker build --platform linux/amd64 -t $NS/platformserver:latest -f server/src/PlatformServer/Dockerfile server
docker build --platform linux/amd64 -t $NS/databaseinitializer:latest -f server/src/DatabaseInitializer/Dockerfile server
docker build --platform linux/amd64 -t $NS/webapp:latest -f web/Dockerfile web
docker push $NS/platformserver:latest && docker push $NS/databaseinitializer:latest && docker push $NS/webapp:latest
```

Then on the VPS, beside `docker-compose.yml` and `.env`:

```bash
docker compose pull && docker compose up -d
```

`databaseinitializer` applies any new migrations before `platformserver` starts. The data-protection keys are kept
in a volume, so a deploy doesn't sign anyone out. To automate this, a GitHub Actions workflow can run the same
build and push, then the two commands over SSH.

## Email

PlatformServer sends the account emails (confirmation, password reset, password changed, the deletion code and its
confirmation) over SMTP with STARTTLS, through any provider: Resend, Postmark, SES, Mailgun. Locally they go to
Mailpit. The CMS's **Email** page sends a test message.

Missing settings don't stop PlatformServer: each email fails instead, and the startup log names what's missing.

- Registering, resending the confirmation and forgot-password share the `email` rate limit: 10 an hour per IP.
- Confirmation emails go to addresses nobody has proved they own, so `ConfirmationEmailLimits` also sends at most
  3 a day to one address and 50 a day in all. Accounts left unconfirmed for 3 days are deleted.
- A reset link lasts an hour and works once; a confirmation link lasts 48 hours.

## Turnstile

With `TURNSTILE_SECRET_KEY` set, sign-up and resending the confirmation need a token from Cloudflare Turnstile,
which the web app's forms get with `TURNSTILE_SITE_KEY`. The API refuses a token made for any host but
`WEB_APP_URL`'s. In Development without a secret key the check is skipped; in production without one, sign-up
refuses everyone and the startup log says so, so set both, or neither and remove the check.
