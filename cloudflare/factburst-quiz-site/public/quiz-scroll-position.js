document.addEventListener("click", (event) => {
  const target = event.target instanceof Element ? event.target : null;
  const nextButton = target?.closest("#next-question");
  if (!nextButton || nextButton.disabled || nextButton.textContent.trim() === "See my score") {
    return;
  }

  const scrollTop = window.scrollY;
  window.setTimeout(() => {
    if (Math.abs(window.scrollY - scrollTop) > 1) {
      window.scrollTo({ top: scrollTop, behavior: "auto" });
    }
  }, 0);
}, true);
