export const marketing = {
  landing: {
    metaTitle: "Starter: .NET 10 and Next.js, with sign-in, a CMS and MCP",
    metaDescription:
      "A full-stack starter: an ASP.NET Core API with a Blazor CMS and an MCP server, and a Next.js web app, deployed with Docker.",
    heroTitle: "Your notes, in the browser and in your AI chat",
    heroBody:
      "This is the starter's example app. Sign up, write a few notes, then connect Claude over MCP and ask it to read or add to them.",
    createAccount: "Create an account",
    openNotes: "Open your notes",
    featuresTitle: "What's inside",
    apiTitle: "API and CMS",
    apiBody: "ASP.NET Core on .NET 10 with EF Core and PostgreSQL, and a Blazor Server CMS for admins.",
    authTitle: "Accounts",
    authBody: "Sign-up with email confirmation, password reset, account deletion and rate limits, on ASP.NET Core Identity.",
    mcpTitle: "MCP server",
    mcpBody: "AI chat apps sign in with OAuth 2.1 (OpenIddict) and call the same features as the web app.",
    webTitle: "Web app",
    webBody: "Next.js, with API types generated from the server's OpenAPI document.",
  },
} as const;
