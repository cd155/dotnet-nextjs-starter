import type { MetadataRoute } from "next";
import { siteOrigin } from "@/lib/site";

// Search engines may crawl the public pages, and find them through the sitemap. The signed-in pages and the
// API's relays aren't for them.
export default async function robots(): Promise<MetadataRoute.Robots> {
  const origin = await siteOrigin();
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/api/", "/notes", "/account"] },
    sitemap: `${origin}/sitemap.xml`,
  };
}
