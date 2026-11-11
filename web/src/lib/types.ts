import type { components } from "@/lib/api-types";

// The API's types, generated from its OpenAPI document (openapi.json) into api-types.ts; this file gives them the
// names the app uses. Their explanations are the server's XML doc comments, which the generated types carry. To
// regenerate after a server change, see "API types" in CLAUDE.md.

type Schemas = components["schemas"];

// ── Notes ──────────────────────────────────────────────────────────────────

export type Note = Schemas["Note"];
export type SaveNoteRequest = Schemas["SaveNoteRequest"];

// ── Account ────────────────────────────────────────────────────────────────

export type UserProfile = Schemas["UserProfile"];
export type ChangePasswordRequest = Schemas["ChangePasswordRequest"];
export type DeleteAccountRequest = Schemas["DeleteAccountRequest"];
export type ConfirmEmailRequest = Schemas["ConfirmEmailRequest"];
export type RegisterRequest = Schemas["RegisterRequest"];
export type ResendConfirmationRequest = Schemas["ResendConfirmationRequest"];
export type ForgotPasswordRequest = Schemas["ForgotPasswordRequest"];
export type ResetPasswordRequest = Schemas["ResetPasswordRequest"];
