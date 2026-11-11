import { turnstileSiteKey } from "@/lib/turnstile";
import { CheckEmailPage } from "./check-email-page";

export default async function Page() {
  return <CheckEmailPage siteKey={await turnstileSiteKey()} />;
}
