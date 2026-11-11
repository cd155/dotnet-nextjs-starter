# Security policy

## Reporting a vulnerability

Please don't open a public issue for a security problem. Report it privately instead, through
[GitHub's private vulnerability reporting](https://github.com/cd155/dotnet-nextjs-starter/security/advisories/new)
(the repo's **Security** tab, then **Report a vulnerability**).

Include what you found, how to reproduce it, and what an attacker could do with it. You'll get a reply
within a week, and a fix or a plan once the report is confirmed.

## Scope

Reports about the code in this repository are in scope: sign-in and account flows, the OAuth server
(OpenIddict), the MCP endpoint, the API, the CMS and the web app.

The seeded accounts (`admin@example.com`, `demo@example.com`) and their passwords are for local development
only and are documented as such; they aren't a vulnerability. If you deploy the starter, change them and set
your own secrets as `docs/deployment.md` describes.

## Supported versions

Only the latest commit on `main` gets fixes. Projects created from this template don't receive updates
automatically; watch the repo's releases to hear about fixes you may want to copy.
