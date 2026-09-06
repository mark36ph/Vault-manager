(() => {
  const STORAGE_KEY = "factburst_pending_guest_score_v1";
  const RESULT_SELECTOR = "#quiz-results";
  const CLAIM_ENDPOINT = "/api/account/claim-score";
  const MAX_AGE_MS = 30 * 60 * 1000;

  const quizSlug = () => {
    const match = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i);
    if (match) return match[1].toLowerCase();
    const value = new URLSearchParams(location.search).get("slug") || "";
    return /^[a-z0-9][a-z0-9-]{0,79}$/i.test(value) ? value.toLowerCase() : "";
  };

  const readPending = () => {
    try {
      const value = JSON.parse(sessionStorage.getItem(STORAGE_KEY) || "null");
      if (!value || !value.slug || !Array.isArray(value.answers) || !value.created_at) return null;
      if (Date.now() - Number(value.created_at) > MAX_AGE_MS) {
        sessionStorage.removeItem(STORAGE_KEY);
        return null;
      }
      return value;
    } catch {
      return null;
    }
  };

  const writePending = (slug, answers) => {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
        slug,
        answers,
        created_at: Date.now(),
      }));
    } catch {
      // Session storage is optional; the normal guest result still works.
    }
  };

  const clearPending = () => {
    try { sessionStorage.removeItem(STORAGE_KEY); } catch {}
  };

  const originalFetch = window.fetch.bind(window);
  window.fetch = async (input, init) => {
    const response = await originalFetch(input, init);
    try {
      const url = typeof input === "string" ? input : input?.url || "";
      const method = String(init?.method || (input instanceof Request ? input.method : "GET")).toUpperCase();
      const match = new URL(url, location.origin).pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/score$/i);
      if (method === "POST" && match && response.ok && init?.body) {
        const body = typeof init.body === "string" ? JSON.parse(init.body) : null;
        if (Array.isArray(body?.answers) && body.answers.length > 0) {
          writePending(match[1].toLowerCase(), body.answers);
        }
      }
    } catch {
      // Never interfere with normal quiz scoring.
    }
    return response;
  };

  const api = async (path, options = {}) => {
    const response = await originalFetch(path, {
      credentials: "same-origin",
      headers: { accept: "application/json", ...(options.headers || {}) },
      ...options,
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload?.error || "Could not save that score.");
    return payload;
  };

  const setNote = (note, title, copy, actionLabel = "") => {
    note.replaceChildren();
    const strong = document.createElement("strong");
    strong.textContent = title;
    const text = document.createElement("span");
    text.textContent = copy;
    note.append(strong, text);
    if (actionLabel) {
      const action = document.createElement("button");
      action.type = "button";
      action.className = "account-inline-action";
      action.textContent = actionLabel;
      note.append(action);
      return action;
    }
    return null;
  };

  const openSignup = () => {
    const trigger = document.querySelector(".account-trigger");
    if (!trigger) return false;
    trigger.click();
    setTimeout(() => {
      const tabs = [...document.querySelectorAll(".account-tabs button")];
      const signup = tabs.find(button => /sign up/i.test(button.textContent || ""));
      signup?.click();
    }, 80);
    return true;
  };

  const waitForAccount = async () => {
    for (let attempt = 0; attempt < 60; attempt++) {
      try {
        const account = await api("/api/account");
        if (account?.authenticated && account?.user) return account.user;
      } catch {}
      await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error("Account creation timed out. Your quiz result is still on this page.");
  };

  const claimPending = async (note, action) => {
    const pending = readPending();
    if (!pending || pending.slug !== quizSlug()) {
      setNote(note, "Score ready to save.", "Your result is still available, but this save request has expired. Please finish another quiz or sign up before leaving this page.");
      return;
    }

    action.disabled = true;
    action.textContent = "Saving score…";
    try {
      const payload = await api(CLAIM_ENDPOINT, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ slug: pending.slug, answers: pending.answers }),
      });
      clearPending();
      const verified = payload?.user?.email_verified === true;
      setNote(
        note,
        "Score saved to your account.",
        verified
          ? "Your result is now part of your account history and can appear on the leaderboard."
          : "Verify your email to make the score eligible for the leaderboard.",
      );
      window.dispatchEvent(new CustomEvent("factburst:guest-score-claimed", { detail: payload }));
    } catch (error) {
      action.disabled = false;
      action.textContent = "Save this score to my account";
      setNote(note, "Account created, but the score was not saved yet.", error.message || "Please try again.", action.textContent);
      note.querySelector("button")?.addEventListener("click", () => claimPending(note, note.querySelector("button")), { once: true });
    }
  };

  const enhance = () => {
    const results = document.querySelector(RESULT_SELECTOR);
    if (!results || results.classList.contains("hidden")) return;
    if (results.dataset.guestClaimEnhanced === "true") return;

    const pending = readPending();
    if (!pending || pending.slug !== quizSlug()) return;

    results.dataset.guestClaimEnhanced = "true";
    let note = results.querySelector(".result-account-note");
    if (!note) {
      note = document.createElement("div");
      note.className = "result-account-note";
      const actions = results.querySelector(".result-actions");
      if (actions) results.insertBefore(note, actions);
      else results.append(note);
    }

    const action = setNote(
      note,
      "Keep this score.",
      "Create a free account now and we’ll attach this quiz result to it.",
      "Create account & save score",
    );
    action.addEventListener("click", async () => {
      action.disabled = true;
      action.textContent = "Opening account…";
      if (!openSignup()) {
        action.disabled = false;
        action.textContent = "Create account & save score";
        return;
      }

      try {
        const user = await waitForAccount();
        action.textContent = user?.email_verified ? "Saving score…" : "Saving score…";
        await claimPending(note, action);
      } catch (error) {
        action.disabled = false;
        action.textContent = "Create account & save score";
        setNote(note, "Your score is still safe on this page.", error.message || "Finish creating your account, then try again.", action.textContent);
        note.querySelector("button")?.addEventListener("click", () => openSignup(), { once: true });
      }
    }, { once: true });
  };

  const results = document.querySelector(RESULT_SELECTOR);
  if (!results) return;
  const observer = new MutationObserver(enhance);
  observer.observe(results, { attributes: true, attributeFilter: ["class"] });
  enhance();
})();
