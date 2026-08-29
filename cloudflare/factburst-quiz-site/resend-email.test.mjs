import test from "node:test";
import assert from "node:assert/strict";
import {
  buildResendPayload,
  createResendEmailAdapter,
} from "./resend-email.js";

test("maps verification email fields to the Resend API payload", () => {
  assert.deepEqual(buildResendPayload({
    from: { email: "noreply@factburstquiz.com", name: "Factburst Quiz" },
    to: ["player@example.com"],
    subject: "Verify your Factburst Quiz email",
    text: "Verify your email",
    html: "<p>Verify your email</p>",
  }), {
    from: "Factburst Quiz <noreply@factburstquiz.com>",
    to: ["player@example.com"],
    subject: "Verify your Factburst Quiz email",
    text: "Verify your email",
    html: "<p>Verify your email</p>",
  });
});

test("does not create a mail adapter without a Resend API key", () => {
  assert.equal(createResendEmailAdapter({ RESEND_API_KEY: "" }, async () => new Response()), null);
});

test("sends through the Resend HTTPS API without exposing the key in the payload", async () => {
  let requestUrl = "";
  let requestOptions = null;
  const adapter = createResendEmailAdapter(
    { RESEND_API_KEY: "re_test_secret" },
    async (url, options) => {
      requestUrl = url;
      requestOptions = options;
      return new Response(JSON.stringify({ id: "email_123" }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    },
  );

  const result = await adapter.send({
    from: { email: "noreply@factburstquiz.com", name: "Factburst Quiz" },
    to: ["player@example.com"],
    subject: "Verify",
    text: "Text",
    html: "<p>Text</p>",
  });

  assert.equal(requestUrl, "https://api.resend.com/emails");
  assert.equal(requestOptions.method, "POST");
  assert.equal(requestOptions.headers.authorization, "Bearer re_test_secret");
  assert.equal(JSON.parse(requestOptions.body).from, "Factburst Quiz <noreply@factburstquiz.com>");
  assert.equal(requestOptions.body.includes("re_test_secret"), false);
  assert.equal(result.id, "email_123");
});

test("treats non-success Resend responses as delivery failures", async () => {
  const adapter = createResendEmailAdapter(
    { RESEND_API_KEY: "re_test_secret" },
    async () => new Response("rate limited", { status: 429 }),
  );

  await assert.rejects(
    () => adapter.send({
      from: { email: "noreply@factburstquiz.com", name: "Factburst Quiz" },
      to: ["player@example.com"],
      subject: "Verify",
      text: "Text",
      html: "<p>Text</p>",
    }),
    /Resend verification email failed \(429\)/,
  );
});
