import { headers } from "next/headers";

/**
 * This site's origin as the visitor reached it, for the absolute URLs in the sitemap and robots.txt. The
 * reverse proxy passes the public host along, and says it was HTTPS; locally there's no proxy, and it's
 * plain HTTP.
 */
export async function siteOrigin(): Promise<string> {
  const headerStore = await headers();
  // Each header can be a list when there's more than one proxy; the first is what the visitor used.
  const host = (headerStore.get("x-forwarded-host") ?? headerStore.get("host") ?? "localhost:3000").split(",")[0].trim();
  const scheme =
    headerStore.get("x-forwarded-proto")?.split(",")[0].trim() ??
    (host.startsWith("localhost") || host.startsWith("127.0.0.1") ? "http" : "https");
  return `${scheme}://${host}`;
}
