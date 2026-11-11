@AGENTS.md

# Web

The Next.js half of the repo (App Router, React 19, Tailwind 4). Paths below are relative to `web/`; run commands
from the repo root as `npm --prefix web <command>`.

## Conventions

- **The browser never calls the API.** Server Components read through `src/lib/api.ts`, which sends the session
  cookie; writes go from the browser to this app's route handlers under `src/app/api/`, which relay them with
  `forwardToApi` (`src/lib/route-helpers.ts`). After a write, a page calls `router.refresh()`.
- **The session cookie** is the API's Identity cookie, kept on this origin as `app_session` and sent back to the
  API under the name it issued. `proxy.ts` sends a signed-out visitor from a protected page to `/login`; list a new
  signed-in page in `src/lib/protected-routes.ts` and in `proxy.ts`'s matcher.
- **API types are generated:** `openapi.json` → `src/lib/api-types.ts` (`npm run api:types`). Name the types the app
  uses in `src/lib/types.ts`; never edit `api-types.ts` by hand.
- **Text goes in the message catalog**, `src/lib/i18n/messages/en/`, one file per feature namespace, never
  inline. Server Components use `getTranslations`, Client Components `useTranslations`.
- **Environment:** `API_BASE_URL` (server-side, default `http://localhost:8080`), and optionally
  `TURNSTILE_SITE_KEY`, `SENTRY_WEBAPP_DSN` and `NEXT_PUBLIC_CONTACT_EMAIL`. All but the last are read at runtime,
  so one image serves any deployment.
