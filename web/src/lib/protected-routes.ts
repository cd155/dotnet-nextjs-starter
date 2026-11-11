// The pages only a signed-in user can open. The proxy sends anyone else to /login, and signing out on
// one leaves it; signing out anywhere else stays on the page.
const PROTECTED_ROUTES = ["/notes", "/account"];

export function isProtectedPath(pathname: string): boolean {
  return PROTECTED_ROUTES.some((route) => pathname.startsWith(route));
}
