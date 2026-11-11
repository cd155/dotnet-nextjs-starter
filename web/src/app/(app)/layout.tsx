import { PageFrame } from "@/components/page-frame";

export default function AppLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  // Wide enough for a two-column page; narrower pages centre themselves.
  return <PageFrame className="mx-auto max-w-6xl px-6 py-6">{children}</PageFrame>;
}
