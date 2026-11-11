import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";
import { isProtectedPath } from "@/lib/protected-routes";
import { SESSION_COOKIE } from "@/lib/route-helpers";

function isAuthenticated(request: NextRequest): boolean {
  return request.cookies.has(SESSION_COOKIE);
}

export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;
  const authed = isAuthenticated(request);

  if (isProtectedPath(pathname) && !authed) {
    const loginUrl = new URL("/login", request.url);
    loginUrl.searchParams.set("redirect", pathname + request.nextUrl.search);
    return NextResponse.redirect(loginUrl);
  }

  if ((pathname === "/" || pathname === "/login") && authed) {
    return NextResponse.redirect(new URL("/notes", request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    "/",
    "/login",
    "/notes/:path*",
    "/account/:path*",
  ],
};
