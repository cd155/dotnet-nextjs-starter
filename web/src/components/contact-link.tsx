// Where people send questions and requests about their data. Set it to your own address.
const CONTACT_EMAIL = process.env.NEXT_PUBLIC_CONTACT_EMAIL ?? "support@example.com";

interface ContactLinkProps {
  /** The email's subject, filled in. */
  subject?: string;
  /** The email's opening text, filled in for the sender to finish. */
  body?: string;
}

/** Our address, as a link that starts an email to it. */
export function ContactLink({ subject, body }: ContactLinkProps) {
  // encodeURIComponent, not URLSearchParams: mail apps show the "+" it puts for a space as a "+".
  const fields = [
    subject && `subject=${encodeURIComponent(subject)}`,
    body && `body=${encodeURIComponent(body)}`,
  ].filter(Boolean);
  const href = `mailto:${CONTACT_EMAIL}${fields.length ? `?${fields.join("&")}` : ""}`;

  return (
    <a href={href} className="font-medium text-primary hover:underline">
      {CONTACT_EMAIL}
    </a>
  );
}
