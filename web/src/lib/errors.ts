export class ApiError extends Error {
  constructor(public readonly status: number, statusText: string, options?: ErrorOptions) {
    super(`API error: ${status} ${statusText}`, options);
    this.name = "ApiError";
  }
}

/** Sign-in refused because the email waits for confirmation; the message is the one to show. */
export class EmailNotConfirmedError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "EmailNotConfirmedError";
  }
}
