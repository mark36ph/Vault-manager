(() => {
  const path = window.location.pathname.replace(/\/+$/, '') || '/';
  const isQuiz = path === '/quiz' || path === '/quiz.html';

  function pickRandomQuiz() {
    return fetch('/api/quizzes?limit=100', { credentials: 'same-origin' })
      .then(r => r.ok ? r.json() : Promise.reject(new Error('Quiz list unavailable')))
      .then(data => {
        const list = Array.isArray(data?.quizzes) ? data.quizzes : Array.isArray(data) ? data : [];
        const live = list.filter(q => q && (q.slug || q.id));
        if (!live.length) throw new Error('No quizzes available');
        const quiz = live[Math.floor(Math.random() * live.length)];
        const slug = String(quiz.slug || quiz.id);
        window.location.href = `/quiz?slug=${encodeURIComponent(slug)}&surprise=1`;
      });
  }

  function startSpeedMode() {
    if (!isQuiz) {
      const target = document.querySelector('a[href^="/quiz"]');
      if (target) {
        const url = new URL(target.href, location.origin);
        url.searchParams.set('speed', '1');
        window.location.href = url.href;
        return;
      }
      window.location.href = '/quizzes?speed=1';
      return;
    }
    document.documentElement.classList.add('speed-mode');
    document.body.dataset.speedMode = 'true';
    window.dispatchEvent(new CustomEvent('factburst:speed-mode'));
  }

  function addQuickActions() {
    const nav = document.querySelector('.top-nav');
    if (!nav || nav.querySelector('.quick-play-links')) return;
    const wrap = document.createElement('span');
    wrap.className = 'quick-play-links';
    wrap.innerHTML = '<button type="button" class="quick-play-link" data-surprise-me>🎲 Surprise Me</button><button type="button" class="quick-play-link" data-speed-mode>⚡ Speed</button>';
    nav.insertBefore(wrap, nav.querySelector('.notification-slot'));
    wrap.querySelector('[data-surprise-me]').addEventListener('click', () => pickRandomQuiz().catch(() => { window.location.href = '/quizzes'; }));
    wrap.querySelector('[data-speed-mode]').addEventListener('click', startSpeedMode);
  }

  function setupQuizSpeed() {
    const params = new URLSearchParams(location.search);
    if (params.get('speed') !== '1') return;
    document.documentElement.classList.add('speed-mode');
    document.body.dataset.speedMode = 'true';
    const progress = document.querySelector('#quiz-progress-text');
    const title = document.querySelector('#quiz-title');
    if (title && !document.querySelector('.speed-mode-label')) {
      const label = document.createElement('span');
      label.className = 'speed-mode-label';
      label.textContent = '⚡ Speed Mode';
      title.insertAdjacentElement('beforebegin', label);
    }
    if (progress) progress.title = 'Speed Mode';
  }

  document.addEventListener('DOMContentLoaded', () => { addQuickActions(); setupQuizSpeed(); });
})();
