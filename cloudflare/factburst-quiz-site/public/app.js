const page = document.body.dataset.page;

if (page === "home") {
  initHome();
} else if (page === "quiz") {
  initQuiz();
}

async function initHome() {
  const latestCard = document.querySelector("#latest-card");
  const latestCta = document.querySelector("#latest-cta");
  const grid = document.querySelector("#quiz-grid");
  const empty = document.querySelector("#empty-state");
  const filter = document.querySelector("#category-filter");
  const upcomingSection = document.querySelector("#upcoming");
  const upcomingGrid = document.querySelector("#upcoming-grid");

  try {
    const [latestResponse, listResponse] = await Promise.all([
      fetchJson("/api/quizzes/latest"),
      fetchJson("/api/quizzes?limit=60"),
    ]);

    const quizzes = Array.isArray(listResponse.quizzes) ? listResponse.quizzes : [];
    const now = Date.now();
    const upcoming = quizzes
      .filter((quiz) => isUpcomingQuiz(quiz, now))
      .sort((a, b) => Date.parse(a.publish_at) - Date.parse(b.publish_at));
    const published = quizzes.filter((quiz) => !isUpcomingQuiz(quiz, now));
    const latest = latestResponse.quiz || published[0] || null;
    const nextUpcoming = upcoming[0] || null;

    renderLatest(latestCard, latest, nextUpcoming);
    if (latest && latestCta) {
      latestCta.href = quizUrl(latest.slug);
      latestCta.textContent = "Play the latest quiz";
    } else if (nextUpcoming && latestCta) {
      latestCta.href = "#upcoming";
      latestCta.textContent = "See what's coming";
    }

    if (upcomingSection && upcomingGrid && upcoming.length > 0) {
      upcomingGrid.replaceChildren(...upcoming.map(createUpcomingCard));
      upcomingSection.classList.remove("hidden");
    } else if (upcomingSection && upcomingGrid) {
      upcomingGrid.replaceChildren();
      upcomingSection.classList.add("hidden");
    }

    const categories = [...new Set(published.map((quiz) => quiz.category).filter(Boolean))]
      .sort((a, b) => a.localeCompare(b));
    for (const category of categories) {
      const option = document.createElement("option");
      option.value = category;
      option.textContent = category;
      filter.append(option);
    }

    const renderGrid = () => {
      const category = filter.value;
      const visible = category
        ? published.filter((quiz) => quiz.category.toLowerCase() === category.toLowerCase())
        : published;
      grid.replaceChildren(...visible.map(createQuizCard));
      empty.classList.toggle("hidden", visible.length !== 0);
    };

    filter.addEventListener("change", renderGrid);
    renderGrid();
  } catch (error) {
    latestCard.replaceChildren(messageBlock("Website ready", "The quiz feed is not available yet."));
    grid.replaceChildren();
    empty.classList.remove("hidden");
    if (upcomingSection) upcomingSection.classList.add("hidden");
    console.error(error);
  }
}

function renderLatest(container, quiz, upcoming) {
  container.classList.remove("loading-card");
  if (!quiz) {
    if (upcoming) {
      const copy = document.createElement("div");
      const category = document.createElement("span");
      category.className = "category-pill";
      category.textContent = upcoming.category || "Quiz";
      const heading = document.createElement("h3");
      heading.textContent = upcoming.title;
      const description = document.createElement("p");
      description.textContent = `First quiz goes live ${formatRelease(upcoming.publish_at)}. The questions will unlock automatically at release time.`;
      copy.append(category, heading, description);

      const action = document.createElement("a");
      action.className = "button button-secondary";
      action.href = "#upcoming";
      action.textContent = "View schedule";
      container.replaceChildren(copy, action);
      return;
    }

    container.replaceChildren(messageBlock(
      "The website is ready",
      "The first quiz will appear here when FactVaultManager publishes it.",
    ));
    return;
  }

  const copy = document.createElement("div");
  const category = document.createElement("span");
  category.className = "category-pill";
  category.textContent = quiz.category || "Quiz";
  const heading = document.createElement("h3");
  heading.textContent = quiz.title;
  const description = document.createElement("p");
  description.textContent = quiz.description || `${quiz.question_count || 10} questions. See how many you can get right.`;
  copy.append(category, heading, description);

  const action = document.createElement("a");
  action.className = "button button-primary";
  action.href = quizUrl(quiz.slug);
  action.textContent = "Play quiz";

  container.replaceChildren(copy, action);
}

function createQuizCard(quiz) {
  const card = document.createElement("a");
  card.className = "quiz-card";
  card.href = quizUrl(quiz.slug);

  const category = document.createElement("span");
  category.className = "category-pill";
  category.textContent = quiz.category || "Quiz";

  const heading = document.createElement("h3");
  heading.textContent = quiz.title;

  const copy = document.createElement("p");
  copy.textContent = quiz.description || "Test yourself and see what score you can get.";

  const footer = document.createElement("div");
  footer.className = "quiz-card-footer";
  const questions = document.createElement("span");
  questions.textContent = `${quiz.question_count || 0} questions`;
  const play = document.createElement("span");
  play.textContent = "Play →";
  footer.append(questions, play);

  card.append(category, heading, copy, footer);
  return card;
}

function createUpcomingCard(quiz) {
  const card = document.createElement("article");
  card.className = "quiz-card";
  card.setAttribute("aria-label", `${quiz.title}, scheduled ${formatRelease(quiz.publish_at)}`);

  const category = document.createElement("span");
  category.className = "category-pill";
  category.textContent = quiz.category || "Quiz";

  const heading = document.createElement("h3");
  heading.textContent = quiz.title;

  const copy = document.createElement("p");
  copy.textContent = quiz.description || "A new Factburst quiz is on the way.";

  const footer = document.createElement("div");
  footer.className = "quiz-card-footer";
  const release = document.createElement("span");
  release.textContent = formatRelease(quiz.publish_at);
  const locked = document.createElement("span");
  locked.textContent = "Coming up";
  footer.append(release, locked);

  card.append(category, heading, copy, footer);
  return card;
}

function isUpcomingQuiz(quiz, now = Date.now()) {
  if (!quiz?.publish_at) return false;
  const release = Date.parse(quiz.publish_at);
  return Number.isFinite(release) && release > now;
}

function formatRelease(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Scheduled soon";
  return new Intl.DateTimeFormat(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

async function initQuiz() {
  const slug = currentQuizSlug();
  const loading = document.querySelector("#quiz-loading");
  const player = document.querySelector("#quiz-player");
  const resultsPanel = document.querySelector("#quiz-results");
  const errorPanel = document.querySelector("#quiz-error");
  const errorCopy = document.querySelector("#quiz-error-copy");

  if (!slug) {
    showQuizError("That quiz link is not valid.");
    return;
  }

  try {
    const response = await fetchJson(`/api/quizzes/${encodeURIComponent(slug)}`);
    const quiz = response.quiz;
    if (!quiz || !Array.isArray(quiz.questions) || quiz.questions.length === 0) {
      showQuizError("This quiz does not have any playable questions yet.");
      return;
    }

    document.title = `${quiz.title} | Factburst Quiz`;
    loading.classList.add("hidden");
    player.classList.remove("hidden");

    const title = document.querySelector("#quiz-title");
    const category = document.querySelector("#quiz-category");
    const progressText = document.querySelector("#quiz-progress-text");
    const progressBar = document.querySelector("#quiz-progress-bar");
    const number = document.querySelector("#question-number");
    const questionVisual = document.querySelector("#question-visual");
    const questionImage = document.querySelector("#question-image");
    const questionText = document.querySelector("#question-text");
    const answerList = document.querySelector("#answer-list");
    const next = document.querySelector("#next-question");

    title.textContent = quiz.title;
    category.textContent = quiz.category || "Quiz";
    const quizLabel = `${quiz.category || ""} ${quiz.title || ""}`;
    player.classList.toggle("logo-quiz", /\blogos?\b/i.test(quizLabel));

    let index = 0;
    const answers = new Array(quiz.questions.length).fill("");

    const renderQuestion = () => {
      const question = quiz.questions[index];
      progressText.textContent = `Question ${index + 1} of ${quiz.questions.length}`;
      progressBar.style.width = `${((index + 1) / quiz.questions.length) * 100}%`;
      number.textContent = `Question ${index + 1}`;
      questionText.textContent = question.question;
      next.textContent = index === quiz.questions.length - 1 ? "See my score" : "Next question";
      next.disabled = !answers[index];

      const imageUrl = typeof question.image_url === "string" ? question.image_url.trim() : "";
      const imageDataUrl = typeof question.image_data_url === "string" ? question.image_data_url.trim() : "";
      const imageSource = imageUrl.startsWith("/quiz-images/")
        ? imageUrl
        : (imageDataUrl.startsWith("data:image/png;base64,") ? imageDataUrl : "");
      if (questionVisual && questionImage && imageSource) {
        questionImage.src = imageSource;
        questionImage.alt = `Quiz image for question ${index + 1}`;
        questionVisual.classList.remove("hidden");
      } else if (questionVisual && questionImage) {
        questionImage.removeAttribute("src");
        questionImage.alt = "";
        questionVisual.classList.add("hidden");
      }

      const buttons = question.answers.map((answer, answerIndex) => {
        const letter = String.fromCharCode(65 + answerIndex);
        const button = document.createElement("button");
        button.type = "button";
        button.className = "answer-button" + (answers[index] === letter ? " selected" : "");
        button.dataset.answer = letter;

        const badge = document.createElement("span");
        badge.className = "answer-letter";
        badge.textContent = letter;
        const label = document.createElement("span");
        label.textContent = answer;
        button.append(badge, label);

        button.addEventListener("click", () => {
          answers[index] = letter;
          for (const other of answerList.querySelectorAll(".answer-button")) {
            other.classList.toggle("selected", other === button);
          }
          next.disabled = false;
        });
        return button;
      });

      answerList.replaceChildren(...buttons);
    };

    next.addEventListener("click", async () => {
      if (!answers[index]) return;
      if (index < quiz.questions.length - 1) {
        index++;
        renderQuestion();
        return;
      }

      next.disabled = true;
      next.textContent = "Checking score…";
      try {
        const score = await fetchJson(`/api/quizzes/${encodeURIComponent(slug)}/score`, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ answers }),
        });
        renderResults(quiz, score, answers, player, resultsPanel);
      } catch (error) {
        next.disabled = false;
        next.textContent = "See my score";
        alert(error.message || "Could not score the quiz.");
      }
    });

    renderQuestion();
  } catch (error) {
    showQuizError(error.message || "This quiz could not be loaded.");
  }

  function showQuizError(message) {
    loading.classList.add("hidden");
    player.classList.add("hidden");
    resultsPanel.classList.add("hidden");
    errorCopy.textContent = message;
    errorPanel.classList.remove("hidden");
  }
}

function renderResults(quiz, score, answers, player, panel) {
  player.classList.add("hidden");
  panel.classList.remove("hidden");

  document.querySelector("#result-score").textContent = `${score.score}/${score.total}`;
  const heading = document.querySelector("#result-heading");
  const copy = document.querySelector("#result-copy");
  const percentage = Number(score.percentage || 0);

  if (percentage === 100) {
    heading.textContent = "Perfect score!";
    copy.textContent = "You got every question right. That is a 10/10 performance.";
  } else if (percentage >= 80) {
    heading.textContent = "Excellent score";
    copy.textContent = "You were very close to a perfect run. Try another category and keep the streak going.";
  } else if (percentage >= 60) {
    heading.textContent = "Good effort";
    copy.textContent = "Solid result. Check the answers below, then see if you can beat it on the next quiz.";
  } else {
    heading.textContent = "Challenge accepted";
    copy.textContent = "That one was tough. Review the answers below and try another quiz.";
  }

  const breakdown = document.querySelector("#result-breakdown");
  const rows = (score.results || []).map((result, index) => {
    const row = document.createElement("div");
    row.className = `result-row ${result.correct ? "correct" : "incorrect"}`;

    const badge = document.createElement("span");
    badge.className = "answer-letter";
    badge.textContent = result.correct ? "✓" : "×";

    const text = document.createElement("div");
    const answer = document.createElement("strong");
    const question = quiz.questions[index];
    const selectedIndex = Math.max(0, answers[index].charCodeAt(0) - 65);
    const correctIndex = Math.max(0, String(result.correct_answer || "A").charCodeAt(0) - 65);
    answer.textContent = result.correct
      ? `Correct: ${question.answers[correctIndex]}`
      : `Your answer: ${question.answers[selectedIndex]} · Correct: ${question.answers[correctIndex]}`;
    text.append(answer);

    if (result.explanation) {
      const explanation = document.createElement("p");
      explanation.textContent = result.explanation;
      text.append(explanation);
    }

    row.append(badge, text);
    return row;
  });
  breakdown.replaceChildren(...rows);

  const watch = document.querySelector("#watch-video");
  if (score.youtube_url) {
    watch.href = score.youtube_url;
    watch.classList.remove("hidden");
  } else {
    watch.classList.add("hidden");
  }

  const share = document.querySelector("#share-score");
  share.onclick = async () => {
    const text = `I scored ${score.score}/${score.total} on “${quiz.title}” at Factburst Quiz. Can you beat me?`;
    const url = quizUrl(quiz.slug || currentQuizSlug());
    try {
      if (navigator.share) {
        await navigator.share({ title: quiz.title, text, url });
      } else {
        await navigator.clipboard.writeText(`${text} ${url}`);
        share.textContent = "Copied!";
        setTimeout(() => { share.textContent = "Share score"; }, 1800);
      }
    } catch (error) {
      if (error?.name !== "AbortError") console.error(error);
    }
  };

  window.scrollTo({ top: 0, behavior: "smooth" });
}

function currentQuizSlug() {
  const querySlug = new URLSearchParams(location.search).get("slug") || "";
  if (/^[a-z0-9][a-z0-9-]{0,79}$/i.test(querySlug)) return querySlug.toLowerCase();
  const match = location.pathname.match(/^\/quiz\/([a-z0-9][a-z0-9-]{0,79})\/?$/i);
  return match ? match[1].toLowerCase() : "";
}

function quizUrl(slug) {
  const value = String(slug || "").toLowerCase();
  return /^[a-z0-9][a-z0-9-]{0,79}$/.test(value) ? `/quiz/${encodeURIComponent(value)}` : "/quizzes";
}

function messageBlock(title, copy) {
  const wrapper = document.createElement("div");
  const heading = document.createElement("h3");
  heading.textContent = title;
  const paragraph = document.createElement("p");
  paragraph.textContent = copy;
  wrapper.append(heading, paragraph);
  return wrapper;
}

async function fetchJson(url, options) {
  const response = await fetch(url, options);
  let payload = null;
  try {
    payload = await response.json();
  } catch {
    payload = null;
  }
  if (!response.ok) {
    throw new Error(payload?.error || `Request failed (${response.status}).`);
  }
  return payload || {};
}
