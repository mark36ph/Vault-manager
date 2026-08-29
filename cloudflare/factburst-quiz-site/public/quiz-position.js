if (document.body.dataset.page === "quiz") {
  const nextButton = document.querySelector("#next-question");

  if (nextButton) {
    nextButton.addEventListener("click", () => {
      const label = String(nextButton.textContent || "").trim().toLowerCase();
      if (label === "see my score" || label === "checking score…" || label === "checking score...") return;

      const top = window.scrollY;
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          window.scrollTo({ top, left: 0, behavior: "auto" });
        });
      });
    }, true);
  }
}
