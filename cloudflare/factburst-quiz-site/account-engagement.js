import { activeSessionUser } from "./account-access.js";

const ACHIEVEMENTS = {
  first_quiz: ["First Steps", "Complete your first Factburst quiz."],
  perfect_score: ["Perfect Score", "Score 100% on a quiz."],
  ten_quizzes: ["Quiz Regular", "Complete 10 different quizzes."],
  hundred_questions: ["Century", "Answer 100 quiz questions."],
  seven_day_streak: ["On Fire", "Complete the Daily Quiz on 7 consecutive days."],
  category_master: ["Category Master", "Reach at least 80% across 5 quizzes in one category."],
};

export async function ensureEngagementSchema(db) {
  await db.batch([
    db.prepare(`CREATE TABLE IF NOT EXISTS site_user_attempts (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      user_id INTEGER NOT NULL,
      quiz_id INTEGER NOT NULL,
      score INTEGER NOT NULL,
      total INTEGER NOT NULL,
      duration_ms INTEGER NOT NULL DEFAULT 0,
      completed_at TEXT NOT NULL,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_user_progress (
      user_id INTEGER PRIMARY KEY,
      xp INTEGER NOT NULL DEFAULT 0,
      updated_at TEXT NOT NULL,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_daily_completions (
      user_id INTEGER NOT NULL,
      day_key TEXT NOT NULL,
      quiz_id INTEGER NOT NULL,
      score INTEGER NOT NULL,
      total INTEGER NOT NULL,
      completed_at TEXT NOT NULL,
      PRIMARY KEY (user_id, day_key),
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_user_achievements (
      user_id INTEGER NOT NULL,
      achievement_key TEXT NOT NULL,
      unlocked_at TEXT NOT NULL,
      PRIMARY KEY (user_id, achievement_key),
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_saved_quizzes (
      user_id INTEGER NOT NULL,
      quiz_id INTEGER NOT NULL,
      created_at TEXT NOT NULL,
      PRIMARY KEY (user_id, quiz_id),
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_quiz_reactions (
      user_id INTEGER NOT NULL,
      quiz_id INTEGER NOT NULL,
      reaction TEXT NOT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      PRIMARY KEY (user_id, quiz_id),
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_notifications (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      user_id INTEGER NOT NULL,
      type TEXT NOT NULL,
      title TEXT NOT NULL,
      message TEXT NOT NULL DEFAULT '',
      url TEXT NOT NULL DEFAULT '',
      read_at TEXT,
      created_at TEXT NOT NULL,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_question_reports (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      user_id INTEGER NOT NULL,
      quiz_id INTEGER NOT NULL,
      question_position INTEGER NOT NULL,
      reason TEXT NOT NULL,
      detail TEXT NOT NULL DEFAULT '',
      status TEXT NOT NULL DEFAULT 'open',
      created_at TEXT NOT NULL,
      FOREIGN KEY (user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )`),
    db.prepare(`CREATE TABLE IF NOT EXISTS site_live_matches (
      token_hash TEXT PRIMARY KEY,
      host_user_id INTEGER NOT NULL,
      guest_user_id INTEGER NOT NULL,
      quiz_id INTEGER NOT NULL,
      status TEXT NOT NULL DEFAULT 'open',
      host_score INTEGER,
      host_total INTEGER,
      guest_score INTEGER,
      guest_total INTEGER,
      created_at TEXT NOT NULL,
      expires_at TEXT NOT NULL,
      FOREIGN KEY (host_user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (guest_user_id) REFERENCES site_users(id) ON DELETE CASCADE,
      FOREIGN KEY (quiz_id) REFERENCES site_quizzes(id) ON DELETE CASCADE
    )`),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_user_attempts_user ON site_user_attempts(user_id, completed_at DESC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_user_attempts_quiz ON site_user_attempts(quiz_id, completed_at DESC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_daily_user ON site_daily_completions(user_id, day_key DESC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_notifications_user ON site_notifications(user_id, read_at, created_at DESC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_question_reports_status ON site_question_reports(status, created_at DESC)"),
    db.prepare("CREATE INDEX IF NOT EXISTS idx_site_live_matches_users ON site_live_matches(host_user_id, guest_user_id, created_at DESC)"),
  ]);

  const challengeColumns = await db.prepare("PRAGMA table_info(site_challenges)").all();
  const challengeNames = new Set((challengeColumns.results || []).map(column => String(column?.name || "")));
  for (const [name, sql] of [
    ["challenged_score", "ALTER TABLE site_challenges ADD COLUMN challenged_score INTEGER"],
    ["challenged_total", "ALTER TABLE site_challenges ADD COLUMN challenged_total INTEGER"],
    ["challenged_completed_at", "ALTER TABLE site_challenges ADD COLUMN challenged_completed_at TEXT"],
  ]) {
    if (challengeNames.size > 0 && !challengeNames.has(name)) await db.prepare(sql).run();
  }
}

export async function handleEngagementApi(request, db, url) {
  const pathname = url.pathname;
  if (!pathname.startsWith("/api/engagement") &&
      !pathname.startsWith("/api/notifications") &&
      !pathname.startsWith("/api/live-matches") &&
      !/^\/api\/quizzes\/.+\/(reaction|questions\/\d+\/report)$/.test(pathname)) return null;

  await ensureEngagementSchema(db);

  if (pathname === "/api/engagement/dashboard" && request.method === "GET") return dashboard(request, db, url);
  if (pathname === "/api/engagement/daily" && request.method === "GET") return dailyEndpoint(request, db);
  if (pathname === "/api/engagement/leaderboard" && request.method === "GET") return engagementLeaderboard(request, db, url);
  if (pathname === "/api/engagement/saved" && request.method === "POST") return saveQuiz(request, db);
  if (pathname === "/api/engagement/saved" && request.method === "DELETE") return unsaveQuiz(request, db, url);
  if (pathname === "/api/engagement/challenge-result" && request.method === "GET") return challengeResult(request, db, url);
  if (pathname === "/api/engagement/challenge-result" && request.method === "POST") return submitChallengeResult(request, db);
  if (pathname === "/api/notifications" && request.method === "GET") return listNotifications(request, db);
  if (pathname === "/api/notifications/read-all" && request.method === "PATCH") return readAllNotifications(request, db);

  const notificationMatch = pathname.match(/^\/api\/notifications\/(\d+)$/);
  if (notificationMatch && request.method === "PATCH") return readNotification(request, db, Number(notificationMatch[1]));

  const reactionMatch = pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/reaction$/i);
  if (reactionMatch && request.method === "POST") return reactToQuiz(request, db, reactionMatch[1].toLowerCase());

  const reportMatch = pathname.match(/^\/api\/quizzes\/([a-z0-9][a-z0-9-]{0,79})\/questions\/(\d+)\/report$/i);
  if (reportMatch && request.method === "POST") return reportQuestion(request, db, reportMatch[1].toLowerCase(), Number(reportMatch[2]));

  if (pathname === "/api/live-matches" && request.method === "POST") return createLiveMatch(request, db, url);
  const liveMatch = pathname.match(/^\/api\/live-matches\/([A-Za-z0-9_-]{20,120})$/);
  if (liveMatch && request.method === "GET") return getLiveMatch(request, db, liveMatch[1]);
  const liveScore = pathname.match(/^\/api\/live-matches\/([A-Za-z0-9_-]{20,120})\/score$/);
  if (liveScore && request.method === "POST") return submitLiveMatchScore(request, db, liveScore[1]);

  return null;
}

export async function recordEngagementAttempt(request, db, quizId, score, total, completedAt) {
  await ensureEngagementSchema(db);
  const user = await activeSessionUser(request, db);
  if (!user?.email_verified_at) return null;

  await db.prepare(`
    INSERT INTO site_user_attempts (user_id, quiz_id, score, total, duration_ms, completed_at)
    VALUES (?, ?, ?, ?, 0, ?)
  `).bind(user.id, quizId, score, total, completedAt).run();

  const xpEarned = 10 + Math.max(0, Number(score || 0)) * 2 + (Number(score) === Number(total) ? 25 : 0);
  await db.prepare(`
    INSERT INTO site_user_progress (user_id, xp, updated_at) VALUES (?, ?, ?)
    ON CONFLICT(user_id) DO UPDATE SET xp = site_user_progress.xp + excluded.xp, updated_at = excluded.updated_at
  `).bind(user.id, xpEarned, completedAt).run();

  const dayKey = completedAt.slice(0, 10);
  const daily = await dailyQuizForDay(db, dayKey);
  if (daily && Number(daily.id) === Number(quizId)) {
    await db.prepare(`
      INSERT INTO site_daily_completions (user_id, day_key, quiz_id, score, total, completed_at)
      VALUES (?, ?, ?, ?, ?, ?)
      ON CONFLICT(user_id, day_key) DO UPDATE SET
        score = MAX(site_daily_completions.score, excluded.score), total = excluded.total, completed_at = excluded.completed_at
    `).bind(user.id, dayKey, quizId, score, total, completedAt).run();
  }

  const unlocked = await unlockAchievements(db, user.id, score, total, completedAt);
  const progress = await db.prepare("SELECT xp FROM site_user_progress WHERE user_id = ? LIMIT 1").bind(user.id).first();
  const xp = Number(progress?.xp || 0);
  return { xp, level: levelForXp(xp), xp_earned: xpEarned, achievements_unlocked: unlocked };
}

async function dashboard(request, db, url) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const now = new Date();
  const today = now.toISOString().slice(0, 10);
  const [progress, attempts, categories, achievements, saved, recent, daily, notifications, pendingFriends, pendingChallenges, tournament] = await Promise.all([
    db.prepare("SELECT xp FROM site_user_progress WHERE user_id = ? LIMIT 1").bind(user.id).first(),
    db.prepare(`SELECT COUNT(*) AS attempts, COALESCE(SUM(total),0) AS questions, COALESCE(SUM(score),0) AS correct FROM site_user_attempts WHERE user_id = ?`).bind(user.id).first(),
    categoryProgress(db, user.id),
    userAchievements(db, user.id),
    savedQuizzes(db, user.id),
    recentQuizzes(db, user.id),
    dailyQuizForDay(db, today),
    notificationSummary(db, user.id),
    friendRequestCount(db, user.id),
    challengeInvitationCount(db, user.id, now.toISOString()),
    weeklyTournament(db, user.id, now),
  ]);
  const streak = await dailyStreak(db, user.id, today);
  const recommendations = await recommendedQuizzes(db, user.id);
  const xp = Number(progress?.xp || 0);
  const dailyCompletion = daily
    ? await db.prepare("SELECT score,total,completed_at FROM site_daily_completions WHERE user_id = ? AND day_key = ? LIMIT 1").bind(user.id, today).first()
    : null;

  return json({
    player: {
      username: String(user.username || "Player"),
      xp,
      level: levelForXp(xp),
      attempts: Number(attempts?.attempts || 0),
      questions_answered: Number(attempts?.questions || 0),
      correct_answers: Number(attempts?.correct || 0),
      accuracy: percentage(attempts?.correct, attempts?.questions),
      streak: streak.current,
      longest_streak: streak.longest,
    },
    daily: daily ? quizSummary(daily, dailyCompletion) : null,
    categories,
    achievements,
    saved,
    recent,
    recommendations,
    notifications: {
      unread: Number(notifications?.unread || 0),
      recent: notifications?.recent || [],
      friend_requests: pendingFriends,
      challenge_invitations: pendingChallenges,
    },
    tournament,
  });
}

async function dailyEndpoint(request, db) {
  const today = new Date().toISOString().slice(0, 10);
  const quiz = await dailyQuizForDay(db, today);
  const user = await activeSessionUser(request, db);
  let completion = null;
  let streak = { current: 0, longest: 0 };
  if (user?.email_verified_at) {
    completion = await db.prepare("SELECT score,total,completed_at FROM site_daily_completions WHERE user_id = ? AND day_key = ? LIMIT 1").bind(user.id, today).first();
    streak = await dailyStreak(db, user.id, today);
  }
  return json({ daily: quiz ? quizSummary(quiz, completion) : null, streak });
}

async function engagementLeaderboard(request, db, url) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const scope = String(url.searchParams.get("scope") || "friends").toLowerCase();
  const period = String(url.searchParams.get("period") || "week").toLowerCase();
  const start = periodStart(period);
  const params = [start];
  let friendFilter = "";
  if (scope === "friends") {
    friendFilter = `AND u.id IN (
      SELECT CASE WHEN f.user_a_id = ? THEN f.user_b_id ELSE f.user_a_id END
      FROM site_friendships f
      WHERE (f.user_a_id = ? OR f.user_b_id = ?) AND f.status = 'accepted'
      UNION SELECT ?
    )`;
    params.push(user.id, user.id, user.id, user.id);
  }
  const result = await db.prepare(`
    WITH best AS (
      SELECT a.user_id, a.quiz_id, MAX(1.0 * a.score / NULLIF(a.total,0)) AS ratio,
             MAX(a.score) AS score, MAX(a.total) AS total
      FROM site_user_attempts a
      WHERE a.completed_at >= ?
      GROUP BY a.user_id, a.quiz_id
    ), totals AS (
      SELECT user_id, COUNT(*) AS quizzes, SUM(score) AS score, SUM(total) AS total
      FROM best GROUP BY user_id
    )
    SELECT u.id, u.username, t.quizzes, t.score, t.total
    FROM totals t JOIN site_users u ON u.id = t.user_id
    WHERE u.email_verified_at IS NOT NULL AND COALESCE(u.status,'active')='active' ${friendFilter}
    ORDER BY t.score DESC, t.quizzes DESC, (1.0*t.score/NULLIF(t.total,0)) DESC, u.username_key ASC
    LIMIT 50
  `).bind(...params).all();
  return json({
    scope: scope === "friends" ? "friends" : "all",
    period: ["week", "month", "all"].includes(period) ? period : "week",
    leaderboard: (result.results || []).map((row, index) => ({
      rank: index + 1,
      user_id: Number(row.id),
      username: String(row.username || "Player"),
      quizzes: Number(row.quizzes || 0),
      score: Number(row.score || 0),
      total: Number(row.total || 0),
      percentage: percentage(row.score, row.total),
      current_user: Number(row.id) === Number(user.id),
    })),
  });
}

async function saveQuiz(request, db) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const body = await readJson(request);
  const quiz = await publishedQuiz(db, body?.slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  const now = new Date().toISOString();
  await db.prepare("INSERT OR IGNORE INTO site_saved_quizzes (user_id,quiz_id,created_at) VALUES (?,?,?)").bind(user.id, quiz.id, now).run();
  return json({ saved: true, quiz: quizSummary(quiz) });
}

async function unsaveQuiz(request, db, url) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const quiz = await publishedQuiz(db, url.searchParams.get("slug"));
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  await db.prepare("DELETE FROM site_saved_quizzes WHERE user_id = ? AND quiz_id = ?").bind(user.id, quiz.id).run();
  return json({ saved: false, quiz: quizSummary(quiz) });
}

async function reactToQuiz(request, db, slug) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const quiz = await publishedQuiz(db, slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  const body = await readJson(request);
  const reaction = String(body?.reaction || "").trim().toLowerCase();
  if (reaction === "clear") {
    await db.prepare("DELETE FROM site_quiz_reactions WHERE user_id = ? AND quiz_id = ?").bind(user.id, quiz.id).run();
  } else if (reaction === "up" || reaction === "down") {
    const now = new Date().toISOString();
    await db.prepare(`INSERT INTO site_quiz_reactions (user_id,quiz_id,reaction,created_at,updated_at)
      VALUES (?,?,?,?,?) ON CONFLICT(user_id,quiz_id) DO UPDATE SET reaction=excluded.reaction,updated_at=excluded.updated_at`)
      .bind(user.id, quiz.id, reaction, now, now).run();
  } else return json({ error: "Reaction must be up, down or clear." }, 400);
  const counts = await db.prepare(`SELECT reaction,COUNT(*) AS total FROM site_quiz_reactions WHERE quiz_id = ? GROUP BY reaction`).bind(quiz.id).all();
  const mine = await db.prepare("SELECT reaction FROM site_quiz_reactions WHERE user_id = ? AND quiz_id = ? LIMIT 1").bind(user.id, quiz.id).first();
  const map = Object.fromEntries((counts.results || []).map(row => [String(row.reaction), Number(row.total || 0)]));
  return json({ reaction: String(mine?.reaction || ""), up: map.up || 0, down: map.down || 0 });
}

async function reportQuestion(request, db, slug, position) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const quiz = await publishedQuiz(db, slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  const question = await db.prepare("SELECT id FROM site_questions WHERE quiz_id = ? AND position = ? LIMIT 1").bind(quiz.id, position).first();
  if (!question) return json({ error: "Question not found." }, 404);
  const body = await readJson(request);
  const reason = String(body?.reason || "other").trim().toLowerCase();
  if (!["incorrect", "typo", "duplicate", "outdated", "other"].includes(reason)) return json({ error: "Choose a valid report reason." }, 400);
  const detail = String(body?.detail || "").trim().slice(0, 800);
  await db.prepare(`INSERT INTO site_question_reports (user_id,quiz_id,question_position,reason,detail,status,created_at) VALUES (?,?,?,?,?,'open',?)`)
    .bind(user.id, quiz.id, position, reason, detail, new Date().toISOString()).run();
  return json({ reported: true }, 201);
}

async function listNotifications(request, db) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const result = await db.prepare(`SELECT id,type,title,message,url,read_at,created_at FROM site_notifications WHERE user_id = ? ORDER BY created_at DESC LIMIT 100`).bind(user.id).all();
  return json({ notifications: (result.results || []).map(notificationRow) });
}

async function readNotification(request, db, id) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  await db.prepare("UPDATE site_notifications SET read_at = COALESCE(read_at, ?) WHERE id = ? AND user_id = ?").bind(new Date().toISOString(), id, user.id).run();
  return json({ read: true });
}

async function readAllNotifications(request, db) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  await db.prepare("UPDATE site_notifications SET read_at = COALESCE(read_at, ?) WHERE user_id = ?").bind(new Date().toISOString(), user.id).run();
  return json({ read: true });
}

async function createLiveMatch(request, db, url) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const body = await readJson(request);
  const quiz = await publishedQuiz(db, body?.slug);
  if (!quiz) return json({ error: "Quiz not found." }, 404);
  const guestId = Number.parseInt(String(body?.friend_user_id || ""), 10);
  if (!Number.isInteger(guestId) || guestId <= 0 || guestId === Number(user.id)) return json({ error: "Choose a valid friend." }, 400);
  if (!await acceptedFriends(db, user.id, guestId)) return json({ error: "Live matches can only be started with an accepted friend." }, 403);
  const guest = await db.prepare("SELECT id,username FROM site_users WHERE id = ? AND email_verified_at IS NOT NULL AND COALESCE(status,'active')='active' LIMIT 1").bind(guestId).first();
  if (!guest) return json({ error: "That friend is not available." }, 404);
  const token = randomToken(24);
  const hash = await sha256(token);
  const now = new Date();
  const expires = new Date(now.getTime() + 60 * 60 * 1000).toISOString();
  await db.prepare(`INSERT INTO site_live_matches (token_hash,host_user_id,guest_user_id,quiz_id,status,created_at,expires_at) VALUES (?,?,?,?,'open',?,?)`)
    .bind(hash, user.id, guestId, quiz.id, now.toISOString(), expires).run();
  const matchUrl = new URL("/quiz.html", url.origin);
  matchUrl.searchParams.set("slug", String(quiz.slug));
  matchUrl.searchParams.set("live", token);
  await createNotification(db, guestId, "live_match", "Live quiz challenge", `${user.username} invited you to a live head-to-head quiz.`, matchUrl.pathname + matchUrl.search);
  return json({ match: { token, url: matchUrl.toString(), quiz: quizSummary(quiz), host: String(user.username), guest: String(guest.username), expires_at: expires } }, 201);
}

async function getLiveMatch(request, db, token) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const match = await liveMatchRow(db, token);
  if (!match || (Number(match.host_user_id) !== Number(user.id) && Number(match.guest_user_id) !== Number(user.id))) return json({ error: "Live match not found." }, 404);
  return json({ match: mapLiveMatch(match, user.id) });
}

async function submitLiveMatchScore(request, db, token) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const match = await liveMatchRow(db, token);
  if (!match || (Number(match.host_user_id) !== Number(user.id) && Number(match.guest_user_id) !== Number(user.id))) return json({ error: "Live match not found." }, 404);
  if (Date.parse(String(match.expires_at)) <= Date.now()) return json({ error: "This live match has expired." }, 410);
  const body = await readJson(request);
  const score = Number(body?.score); const total = Number(body?.total);
  if (!Number.isInteger(score) || !Number.isInteger(total) || total <= 0 || score < 0 || score > total) return json({ error: "Invalid score." }, 400);
  const host = Number(match.host_user_id) === Number(user.id);
  await db.prepare(host
    ? "UPDATE site_live_matches SET host_score=?,host_total=? WHERE token_hash=?"
    : "UPDATE site_live_matches SET guest_score=?,guest_total=? WHERE token_hash=?")
    .bind(score, total, await sha256(token)).run();
  const updated = await liveMatchRow(db, token);
  if (updated?.host_score !== null && updated?.guest_score !== null) {
    await db.prepare("UPDATE site_live_matches SET status='complete' WHERE token_hash=?").bind(await sha256(token)).run();
    const otherId = host ? updated.guest_user_id : updated.host_user_id;
    await createNotification(db, Number(otherId), "live_result", "Live quiz result ready", `${user.username} has finished your head-to-head quiz.`, `/quiz.html?slug=${encodeURIComponent(updated.slug)}&live=${encodeURIComponent(token)}`);
  }
  return getLiveMatch(request, db, token);
}

async function submitChallengeResult(request, db) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const body = await readJson(request);
  const token = String(body?.token || "").trim();
  const score = Number(body?.score); const total = Number(body?.total);
  if (token.length < 20 || !Number.isInteger(score) || !Number.isInteger(total) || total <= 0 || score < 0 || score > total) return json({ error: "Invalid challenge result." }, 400);
  const hash = await sha256(token);
  const challenge = await db.prepare(`SELECT c.*,u.username AS challenger_username,q.slug,q.title FROM site_challenges c JOIN site_users u ON u.id=c.challenger_user_id JOIN site_quizzes q ON q.id=c.quiz_id WHERE c.token_hash=? AND c.expires_at>? LIMIT 1`).bind(hash, new Date().toISOString()).first();
  if (!challenge || Number(challenge.challenged_user_id) !== Number(user.id)) return json({ error: "This challenge is not available for this account." }, 403);
  const completed = new Date().toISOString();
  await db.prepare("UPDATE site_challenges SET challenged_score=?,challenged_total=?,challenged_completed_at=? WHERE token_hash=?").bind(score,total,completed,hash).run();
  await createNotification(db, Number(challenge.challenger_user_id), "challenge_result", "Challenge result ready", `${user.username} finished your challenge on ${challenge.title}.`, `/quiz.html?slug=${encodeURIComponent(challenge.slug)}&challenge=${encodeURIComponent(token)}`);
  return challengeResultPayload(db, hash, user.id);
}

async function challengeResult(request, db, url) {
  const user = await verifiedUser(request, db);
  if (user instanceof Response) return user;
  const token = String(url.searchParams.get("token") || "").trim();
  if (token.length < 20) return json({ error: "Invalid challenge." }, 400);
  return challengeResultPayload(db, await sha256(token), user.id);
}

async function challengeResultPayload(db, hash, viewerId) {
  const row = await db.prepare(`
    SELECT c.*,a.username AS challenger_username,b.username AS challenged_username,q.slug,q.title
    FROM site_challenges c JOIN site_users a ON a.id=c.challenger_user_id
    LEFT JOIN site_users b ON b.id=c.challenged_user_id JOIN site_quizzes q ON q.id=c.quiz_id
    WHERE c.token_hash=? LIMIT 1
  `).bind(hash).first();
  if (!row || (Number(row.challenger_user_id) !== Number(viewerId) && Number(row.challenged_user_id) !== Number(viewerId))) return json({ error: "Challenge result not found." }, 404);
  const challengerPct = percentage(row.challenger_score,row.total);
  const challengedPct = row.challenged_score === null ? null : percentage(row.challenged_score,row.challenged_total || row.total);
  let winner = "pending";
  if (challengedPct !== null) winner = challengerPct === challengedPct ? "draw" : (challengerPct > challengedPct ? "challenger" : "challenged");
  return json({ result: {
    quiz: { slug: String(row.slug), title: String(row.title) },
    challenger: { user_id:Number(row.challenger_user_id), username:String(row.challenger_username), score:Number(row.challenger_score), total:Number(row.total), percentage:challengerPct },
    challenged: { user_id:Number(row.challenged_user_id||0), username:String(row.challenged_username||""), score:row.challenged_score===null?null:Number(row.challenged_score), total:row.challenged_total===null?Number(row.total):Number(row.challenged_total), percentage:challengedPct, completed_at:String(row.challenged_completed_at||"") },
    winner,
  }});
}

async function unlockAchievements(db, userId, latestScore, latestTotal, now) {
  const attempt = await db.prepare("SELECT COUNT(DISTINCT quiz_id) AS quizzes,COALESCE(SUM(total),0) AS questions FROM site_user_attempts WHERE user_id=?").bind(userId).first();
  const streak = await dailyStreak(db, userId, now.slice(0,10));
  const category = await db.prepare(`SELECT q.category,COUNT(*) AS quizzes,SUM(s.best_score) AS score,SUM(s.total) AS total FROM site_user_scores s JOIN site_quizzes q ON q.id=s.quiz_id WHERE s.user_id=? GROUP BY q.category HAVING COUNT(*)>=5 ORDER BY (1.0*SUM(s.best_score)/NULLIF(SUM(s.total),0)) DESC LIMIT 1`).bind(userId).first();
  const eligible = new Set();
  if (Number(attempt?.quizzes||0)>=1) eligible.add("first_quiz");
  if (Number(latestScore)===Number(latestTotal) && Number(latestTotal)>0) eligible.add("perfect_score");
  if (Number(attempt?.quizzes||0)>=10) eligible.add("ten_quizzes");
  if (Number(attempt?.questions||0)>=100) eligible.add("hundred_questions");
  if (streak.current>=7) eligible.add("seven_day_streak");
  if (category && percentage(category.score,category.total)>=80) eligible.add("category_master");
  const unlocked = [];
  for (const key of eligible) {
    const result = await db.prepare("INSERT OR IGNORE INTO site_user_achievements (user_id,achievement_key,unlocked_at) VALUES (?,?,?)").bind(userId,key,now).run();
    if (Number(result?.meta?.changes||0)>0) {
      const [title,message] = ACHIEVEMENTS[key]; unlocked.push({key,title,message,unlocked_at:now});
      await createNotification(db,userId,"achievement",`Achievement unlocked: ${title}`,message,"/profile.html");
    }
  }
  return unlocked;
}

async function userAchievements(db,userId) {
  const result = await db.prepare("SELECT achievement_key,unlocked_at FROM site_user_achievements WHERE user_id=? ORDER BY unlocked_at DESC").bind(userId).all();
  return (result.results||[]).map(row=>{const key=String(row.achievement_key);const def=ACHIEVEMENTS[key]||[key,"Achievement unlocked."];return {key,title:def[0],description:def[1],unlocked_at:String(row.unlocked_at||"")};});
}

async function categoryProgress(db,userId) {
  const result = await db.prepare(`SELECT q.category,COUNT(*) AS quizzes,SUM(s.best_score) AS score,SUM(s.total) AS total FROM site_user_scores s JOIN site_quizzes q ON q.id=s.quiz_id WHERE s.user_id=? GROUP BY q.category ORDER BY (1.0*SUM(s.best_score)/NULLIF(SUM(s.total),0)) DESC,q.category COLLATE NOCASE`).bind(userId).all();
  return (result.results||[]).map(row=>({category:String(row.category||"General"),quizzes:Number(row.quizzes||0),score:Number(row.score||0),total:Number(row.total||0),percentage:percentage(row.score,row.total),rank:categoryRank(percentage(row.score,row.total))}));
}

async function savedQuizzes(db,userId) {
  const result=await db.prepare(`SELECT q.id,q.slug,q.title,q.category,s.created_at FROM site_saved_quizzes s JOIN site_quizzes q ON q.id=s.quiz_id WHERE s.user_id=? AND q.status='published' ORDER BY s.created_at DESC LIMIT 20`).bind(userId).all();
  return (result.results||[]).map(quizSummary);
}

async function recentQuizzes(db,userId) {
  const result=await db.prepare(`SELECT q.id,q.slug,q.title,q.category,s.best_score,s.total,s.last_completed_at FROM site_user_scores s JOIN site_quizzes q ON q.id=s.quiz_id WHERE s.user_id=? AND q.status='published' ORDER BY s.last_completed_at DESC LIMIT 8`).bind(userId).all();
  return (result.results||[]).map(row=>({...quizSummary(row),best_score:Number(row.best_score||0),total:Number(row.total||0),percentage:percentage(row.best_score,row.total),last_completed_at:String(row.last_completed_at||"")}));
}

async function recommendedQuizzes(db,userId) {
  const latest=await db.prepare(`SELECT q.category FROM site_user_scores s JOIN site_quizzes q ON q.id=s.quiz_id WHERE s.user_id=? ORDER BY s.last_completed_at DESC LIMIT 1`).bind(userId).first();
  const result=await db.prepare(`SELECT q.id,q.slug,q.title,q.category,q.description FROM site_quizzes q WHERE q.status='published' AND (q.publish_at IS NULL OR q.publish_at<=?) AND q.id NOT IN (SELECT quiz_id FROM site_user_scores WHERE user_id=?) ORDER BY CASE WHEN q.category=? THEN 0 ELSE 1 END,q.updated_at DESC LIMIT 3`).bind(new Date().toISOString(),userId,String(latest?.category||"")).all();
  return (result.results||[]).map(quizSummary);
}

async function dailyQuizForDay(db,dayKey) {
  const countRow=await db.prepare("SELECT COUNT(*) AS total FROM site_quizzes WHERE status='published' AND (publish_at IS NULL OR publish_at<=?)").bind(`${dayKey}T23:59:59.999Z`).first();
  const count=Number(countRow?.total||0); if(count<=0)return null;
  const dayNumber=Math.floor(Date.parse(`${dayKey}T00:00:00Z`)/86400000);
  const offset=((dayNumber%count)+count)%count;
  return db.prepare("SELECT id,slug,title,category,description FROM site_quizzes WHERE status='published' AND (publish_at IS NULL OR publish_at<=?) ORDER BY id LIMIT 1 OFFSET ?").bind(`${dayKey}T23:59:59.999Z`,offset).first();
}

async function dailyStreak(db,userId,today) {
  const result=await db.prepare("SELECT day_key FROM site_daily_completions WHERE user_id=? ORDER BY day_key DESC LIMIT 400").bind(userId).all();
  const days=[...new Set((result.results||[]).map(row=>String(row.day_key||"")).filter(Boolean))].sort().reverse();
  let longest=0,run=0,previous=null;
  for(const day of days){const time=Date.parse(`${day}T00:00:00Z`);if(previous===null||previous-time===86400000)run++;else run=1;longest=Math.max(longest,run);previous=time;}
  let current=0;let expected=Date.parse(`${today}T00:00:00Z`);
  const daySet=new Set(days);
  if(!daySet.has(today)){expected-=86400000;}
  while(daySet.has(new Date(expected).toISOString().slice(0,10))){current++;expected-=86400000;}
  return {current,longest};
}

async function weeklyTournament(db,userId,now) {
  const start=new Date(now);const weekday=(start.getUTCDay()+6)%7;start.setUTCDate(start.getUTCDate()-weekday);start.setUTCHours(0,0,0,0);const end=new Date(start.getTime()+7*86400000);const weekKey=start.toISOString().slice(0,10);
  const countRow=await db.prepare("SELECT COUNT(*) AS total FROM site_quizzes WHERE status='published' AND (publish_at IS NULL OR publish_at<=?)").bind(now.toISOString()).first();const count=Number(countRow?.total||0);if(!count)return null;
  const seed=Math.floor(start.getTime()/86400000);const quizzes=[];
  for(let i=0;i<Math.min(3,count);i++){const offset=((seed*7+i*13)%count+count)%count;const q=await db.prepare("SELECT id,slug,title,category FROM site_quizzes WHERE status='published' AND (publish_at IS NULL OR publish_at<=?) ORDER BY id LIMIT 1 OFFSET ?").bind(now.toISOString(),offset).first();if(q&&!quizzes.some(x=>Number(x.id)===Number(q.id)))quizzes.push(q);}
  if(!quizzes.length)return null;
  const ids=quizzes.map(q=>Number(q.id));while(ids.length<3)ids.push(-1);
  const scores=await db.prepare(`SELECT user_id,quiz_id,MAX(score) AS score,MAX(total) AS total FROM site_user_attempts WHERE completed_at>=? AND completed_at<? AND quiz_id IN (?,?,?) GROUP BY user_id,quiz_id`).bind(start.toISOString(),end.toISOString(),...ids).all();
  const grouped=new Map();for(const row of scores.results||[]){const id=Number(row.user_id);const item=grouped.get(id)||{score:0,total:0,quizzes:0};item.score+=Number(row.score||0);item.total+=Number(row.total||0);item.quizzes++;grouped.set(id,item);}
  const users=[];for(const [id,item] of grouped){const u=await db.prepare("SELECT username FROM site_users WHERE id=? AND email_verified_at IS NOT NULL AND COALESCE(status,'active')='active' LIMIT 1").bind(id).first();if(u)users.push({user_id:id,username:String(u.username),...item,percentage:percentage(item.score,item.total)});}
  users.sort((a,b)=>b.score-a.score||b.quizzes-a.quizzes||b.percentage-a.percentage||a.username.localeCompare(b.username));
  return {week_key:weekKey,starts_at:start.toISOString(),ends_at:end.toISOString(),quizzes:quizzes.map(quizSummary),leaderboard:users.slice(0,25).map((row,index)=>({...row,rank:index+1,current_user:row.user_id===Number(userId)}))};
}

async function notificationSummary(db,userId){const unread=await db.prepare("SELECT COUNT(*) AS total FROM site_notifications WHERE user_id=? AND read_at IS NULL").bind(userId).first();const recent=await db.prepare("SELECT id,type,title,message,url,read_at,created_at FROM site_notifications WHERE user_id=? ORDER BY created_at DESC LIMIT 5").bind(userId).all();return {unread:Number(unread?.total||0),recent:(recent.results||[]).map(notificationRow)};}
async function friendRequestCount(db,userId){const row=await db.prepare("SELECT COUNT(*) AS total FROM site_friendships WHERE status='pending' AND requested_by_user_id<>? AND (user_a_id=? OR user_b_id=?)").bind(userId,userId,userId).first();return Number(row?.total||0);}
async function challengeInvitationCount(db,userId,now){const row=await db.prepare("SELECT COUNT(*) AS total FROM site_challenges WHERE challenged_user_id=? AND expires_at>? AND challenged_completed_at IS NULL").bind(userId,now).first();return Number(row?.total||0);}
async function createNotification(db,userId,type,title,message,url){await db.prepare("INSERT INTO site_notifications (user_id,type,title,message,url,read_at,created_at) VALUES (?,?,?,?,?,NULL,?)").bind(userId,type,title,message,url,new Date().toISOString()).run();}
function notificationRow(row){return {id:Number(row.id||0),type:String(row.type||"info"),title:String(row.title||"Factburst"),message:String(row.message||""),url:String(row.url||""),read:Boolean(row.read_at),read_at:String(row.read_at||""),created_at:String(row.created_at||"")};}

async function liveMatchRow(db,token){const hash=await sha256(token);return db.prepare(`SELECT m.*,h.username AS host_username,g.username AS guest_username,q.slug,q.title,q.category FROM site_live_matches m JOIN site_users h ON h.id=m.host_user_id JOIN site_users g ON g.id=m.guest_user_id JOIN site_quizzes q ON q.id=m.quiz_id WHERE m.token_hash=? LIMIT 1`).bind(hash).first();}
function mapLiveMatch(row,viewerId){const complete=row.host_score!==null&&row.guest_score!==null;let winner="pending";if(complete){const hp=percentage(row.host_score,row.host_total),gp=percentage(row.guest_score,row.guest_total);winner=hp===gp?"draw":hp>gp?"host":"guest";}return {quiz:{slug:String(row.slug),title:String(row.title),category:String(row.category||"")},status:complete?"complete":String(row.status||"open"),host:{user_id:Number(row.host_user_id),username:String(row.host_username),score:row.host_score===null?null:Number(row.host_score),total:row.host_total===null?null:Number(row.host_total)},guest:{user_id:Number(row.guest_user_id),username:String(row.guest_username),score:row.guest_score===null?null:Number(row.guest_score),total:row.guest_total===null?null:Number(row.guest_total)},viewer:Number(row.host_user_id)===Number(viewerId)?"host":"guest",winner,expires_at:String(row.expires_at||"")};}
async function acceptedFriends(db,a,b){const x=Math.min(Number(a),Number(b)),y=Math.max(Number(a),Number(b));return Boolean(await db.prepare("SELECT id FROM site_friendships WHERE user_a_id=? AND user_b_id=? AND status='accepted' LIMIT 1").bind(x,y).first());}

async function publishedQuiz(db,slugValue){const slug=String(slugValue||"").trim().toLowerCase();if(!/^[a-z0-9][a-z0-9-]{0,79}$/.test(slug))return null;return db.prepare("SELECT id,slug,title,category,description FROM site_quizzes WHERE slug=? AND status='published' LIMIT 1").bind(slug).first();}
function quizSummary(row,completion=null){return {id:Number(row.id||0),slug:String(row.slug||""),title:String(row.title||row.slug||"Quiz"),category:String(row.category||"Quiz"),description:String(row.description||""),completed:Boolean(completion),score:completion?Number(completion.score||0):null,total:completion?Number(completion.total||0):null,completed_at:completion?String(completion.completed_at||""):""};}
async function verifiedUser(request,db){const user=await activeSessionUser(request,db);if(!user)return json({error:"Log in to use this feature.",code:"account_required"},401);if(!user.email_verified_at)return json({error:"Verify your email to use this feature.",code:"verified_account_required"},403);return user;}
function levelForXp(xp){return Math.max(1,Math.floor(Math.sqrt(Math.max(0,Number(xp||0))/100))+1);}
function categoryRank(value){const p=Number(value||0);return p>=90?"Master":p>=80?"Expert":p>=70?"Advanced":p>=55?"Intermediate":"Learner";}
function percentage(score,total){const t=Number(total||0);return t>0?Math.round((Number(score||0)/t)*100):0;}
function periodStart(period){const now=new Date();if(period==="all")return "1970-01-01T00:00:00.000Z";if(period==="month")return new Date(Date.UTC(now.getUTCFullYear(),now.getUTCMonth(),1)).toISOString();const start=new Date(now);const weekday=(start.getUTCDay()+6)%7;start.setUTCDate(start.getUTCDate()-weekday);start.setUTCHours(0,0,0,0);return start.toISOString();}
async function readJson(request){try{return await request.json();}catch{return {};}}
function randomToken(byteCount){const bytes=new Uint8Array(byteCount);crypto.getRandomValues(bytes);return base64UrlEncode(bytes);}
async function sha256(value){const bytes=new TextEncoder().encode(String(value||""));const digest=await crypto.subtle.digest("SHA-256",bytes);return base64UrlEncode(new Uint8Array(digest));}
function base64UrlEncode(bytes){let binary="";for(const byte of bytes)binary+=String.fromCharCode(byte);return btoa(binary).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/g,"");}
function json(value,status=200,extraHeaders={}){return new Response(JSON.stringify(value),{status,headers:{"content-type":"application/json; charset=utf-8","cache-control":"no-store","x-content-type-options":"nosniff",...extraHeaders}});}
