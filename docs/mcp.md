# MCP

`/mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server over streamable HTTP, built with the
MCP C# SDK in `server/src/PlatformServer/Features/Mcp/`. It's stateless: every call is its own HTTP request.

## Tools and prompts

| Name | Kind | Who |
|---|---|---|
| `ping` | tool | anyone signed in |
| `list_capabilities` | tool | anyone signed in; lists exactly the tools and prompts the user can use |
| `list_my_notes`, `get_note`, `create_note`, `update_note` | tools | anyone signed in, on their own notes |
| `count_notes` | tool | admins only (`[Authorize(Roles = Admin)]`), hidden from everyone else |
| `summarize_my_notes` | prompt | anyone signed in |

A tool calls the same repository as the API (`NoteRepository`), so the rules are the same, and a repository's
error, such as "A note needs a title.", reaches the model as the tool's message.

## Sign-in

AI chat apps sign in with OAuth 2.1, authorization code with PKCE, from the app's own OpenIddict server. There's
no separate identity provider: a user signs in with their usual account, on the CMS's login page, and only
existing users can get a token.

1. The client calls `/mcp` without a token and gets a `401` whose `WWW-Authenticate` points at
   `/.well-known/oauth-protected-resource/mcp`.
2. That metadata names the authorization server (`MCP_AUTHORITY`), whose discovery document lists
   `/connect/authorize` and `/connect/token`.
3. The user signs in, and the client exchanges the code for an access token whose audience is `MCP_RESOURCE`.
   Consent is implicit: the client is one you registered.
4. The client calls `/mcp` with the token. `/mcp` checks its signature and audience in-process. A client that
   also asks for the `offline_access` scope gets a refresh token, which keeps it connected.

There's no dynamic client registration. `DatabaseInitializer`'s `OpenIddictSeeder` registers one public client
per app:

| Client ID | Callback | Seeded |
|---|---|---|
| `claude-connector` | `https://claude.ai/api/mcp/auth_callback` | always |
| `mcp-inspector` | `MCP_INSPECTOR_REDIRECT_URI` | when it's set, as the local stack does |

To add another app, add a `SeedClientAsync` call with its callback.

### Settings

| Setting | Read by | What |
|---|---|---|
| `MCP_AUTHORITY` | PlatformServer | This app's public base URL, the tokens' issuer, e.g. `https://api.example.com` |
| `MCP_RESOURCE` | PlatformServer and DatabaseInitializer | The public `/mcp` URL, the tokens' audience, e.g. `https://api.example.com/mcp` |
| `OpenIddict__SigningCertificate`, `OpenIddict__EncryptionCertificate` | PlatformServer, outside Development | Base64 PFX files, so tokens survive a restart |
| `OpenIddict__CertificatePassword` | PlatformServer | The PFX files' password, if any |

Without `MCP_AUTHORITY` and `MCP_RESOURCE`, PlatformServer still starts, but `/mcp` refuses every request and the
startup log says so. Development uses throwaway certificates; production refuses to start without its own. To make
them:

```bash
for name in signing encryption; do
  openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=starter-$name" \
    -keyout $name.key -out $name.crt
  openssl pkcs12 -export -inkey $name.key -in $name.crt -out $name.pfx -passout pass:
  base64 -i $name.pfx | tr -d '\n' > $name.pfx.b64   # the value for OPENIDDICT_<NAME>_CERTIFICATE
done
```

## Try it locally with MCP Inspector

With the local stack up:

```bash
npx @modelcontextprotocol/inspector
```

In the Inspector, choose **Streamable HTTP**, enter `http://localhost:8080/mcp`, and under **Authentication** set
the client ID to `mcp-inspector` and the scope to `mcp`. **Connect** opens the CMS's login page: sign in as
`demo@example.com` / `Demopassword1@`, and the Inspector lists the tools. Sign in as the admin to see `count_notes`.

## Connect Claude

Claude reaches `/mcp` from the internet, so this needs a deployed server ([deployment](deployment.md)). In Claude,
add a custom connector with the URL `https://api.example.com/mcp`, and under **Advanced settings** set the OAuth
client ID to `claude-connector`. Claude opens your login page, and then the notes tools are in the chat.
