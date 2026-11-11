import { turnstileSiteKey } from "@/lib/turnstile";
import { LoginPage } from "./login-page";

export default async function Page() {
  return <LoginPage siteKey={await turnstileSiteKey()} />;
}
