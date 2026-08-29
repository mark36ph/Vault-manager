# Factburst Link Tracker Worker

The tracker Worker handles campaign redirects, campaign statistics and authenticated website quiz administration.

## Website quiz visibility

Website quiz content is created/refreshed with `POST /api/site/quizzes`. For an existing quiz, a full content resync preserves the current website `status` and `publish_at` so a manual visibility choice is not silently undone by scheduled sync or Autopilot.

Visibility/timing is changed independently with:

`PATCH /api/site/quizzes/{slug}`

using the same Bearer `TRACKER_API_KEY` authentication as the other admin routes. The request body accepts `status` (`draft` or `published`) and `publish_at` (ISO-8601 timestamp or `null`). This updates only the quiz state/timing; stored questions and images are not replaced.
