import type { MetadataRoute } from "next";
import { siteOrigin } from "@/lib/site";

// The public pages for search engines to find: the home page and the privacy policy.
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const origin = await siteOrigin();
  return [{ url: `${origin}/` }, { url: `${origin}/privacy` }];
}
