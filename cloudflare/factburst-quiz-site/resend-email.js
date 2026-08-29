const RESEND_ENDPOINT = "https://api.resend.com/emails";

// RESEND_API_KEY is a Cloudflare Worker secret. EMAIL_FROM is a normal
// Worker variable containing a sender address on a domain verified in Resend.
export function createResendEmailAdapter(env, fetchImpl = fetch) {
  const apiKey = String(env?.RESEND_API_KEY || "").trim();
  if (!apiKey) return null;

  return {
    async send(message) {
      const payload = buildResendPayload(message);
      const response = await fetchImpl(RESEND_ENDPOINT, {
        method: "POST",
        headers: {
          "authorization": `Bearer ${apiKey}`,
          "content-type": "application/json",
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        throw new Error(`Resend verification email failed (${response.status}).`);
      }

      try {
        return await response.json();
      } catch {
        return {};
      }
    },
  };
}

export function buildResendPayload(message) {
  const fromEmail = String(message?.from?.email || "").trim();
  const fromName = String(message?.from?.name || "").trim();
  const to = Array.isArray(message?.to)
    ? message.to.map(value => String(value || "").trim()).filter(Boolean)
    : [String(message?.to || "").trim()].filter(Boolean);

  if (!fromEmail || to.length === 0) {
    throw new Error("Resend email sender and recipient are required.");
  }

  return {
    from: fromName ? `${fromName} <${fromEmail}>` : fromEmail,
    to,
    subject: String(message?.subject || ""),
    text: String(message?.text || ""),
    html: String(message?.html || ""),
  };
}
