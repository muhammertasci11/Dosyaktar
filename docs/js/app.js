/* ============================================================
   DOSYAKTAR — landing interactions
   Pointer-reactive 3D stage, tilt, reveals, theme picker, demo
   ============================================================ */
(function () {
  'use strict';

  var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var finePointer = window.matchMedia('(pointer: fine)').matches;
  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  /* ───────────────────────── year ───────────────────────── */
  var yearEl = $('#year');
  if (yearEl) yearEl.textContent = String(new Date().getFullYear());

  /* ───────────────────────── nav ───────────────────────── */
  var nav = $('#nav');
  function syncNav() {
    if (nav) nav.classList.toggle('scrolled', window.scrollY > 12);
  }
  syncNav();
  window.addEventListener('scroll', syncNav, { passive: true });

  var burger = $('#burger');
  var menu = $('#mobileMenu');
  function setMenu(open) {
    if (!burger || !menu) return;
    burger.setAttribute('aria-expanded', String(open));
    burger.setAttribute('aria-label', open ? 'Menüyü kapat' : 'Menüyü aç');
    menu.hidden = !open;
  }
  if (burger && menu) {
    setMenu(false);
    burger.addEventListener('click', function () { setMenu(menu.hidden); });
    menu.addEventListener('click', function (e) {
      if (e.target.closest('a')) setMenu(false);
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') setMenu(false);
    });
    window.addEventListener('resize', function () {
      if (window.innerWidth > 960) setMenu(false);
    });
  }

  /* ────────────────────── reveal on scroll ────────────────────── */
  var revealables = $$('.reveal');
  if (reduceMotion || !('IntersectionObserver' in window)) {
    revealables.forEach(function (el) { el.classList.add('in'); });
  } else {
    var revealIO = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('in');
        revealIO.unobserve(entry.target);
      });
    }, { threshold: 0.12, rootMargin: '0px 0px -8% 0px' });
    revealables.forEach(function (el) { revealIO.observe(el); });
  }

  /* ───────────────────────── counters ───────────────────────── */
  function animateCount(el) {
    var target = parseFloat(el.getAttribute('data-count') || '0');
    var suffix = el.getAttribute('data-suffix') || '';
    var duration = 1300;
    var started = 0;
    function frame(now) {
      if (!started) started = now;
      var p = Math.min(1, (now - started) / duration);
      var eased = 1 - Math.pow(1 - p, 3);
      el.textContent = Math.round(target * eased).toLocaleString('tr-TR') + suffix;
      if (p < 1) requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }

  var counters = $$('[data-count]');
  if (counters.length) {
    if (reduceMotion || !('IntersectionObserver' in window)) {
      counters.forEach(animateCount);
    } else {
      var countIO = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (!entry.isIntersecting) return;
          animateCount(entry.target);
          countIO.unobserve(entry.target);
        });
      }, { threshold: 0.6 });
      counters.forEach(function (el) { countIO.observe(el); });
    }
  }

  /* ══════════════════════ 3D HERO STAGE ══════════════════════ */
  var stage = $('#stage');
  if (stage) {
    var layers = $$('[data-depth]', stage).map(function (el, i) {
      return {
        el: el,
        depth: parseFloat(el.getAttribute('data-depth')) || 1,
        float: el.hasAttribute('data-float'),
        phase: (i * 1.7) % (Math.PI * 2)
      };
    });

    if (reduceMotion || !finePointer) {
      /* static, but keep the float chip bobbing for life */
      if (!reduceMotion) {
        var bobStart = performance.now();
        (function bob(now) {
          var t = (now - bobStart) / 1000;
          layers.forEach(function (l) {
            if (!l.float) return;
            l.el.style.transform = 'translate3d(0,' + (Math.sin(t * 1.1 + l.phase) * 7).toFixed(2) + 'px,0)';
          });
          requestAnimationFrame(bob);
        })(bobStart);
      }
    } else {
      layers.forEach(function (l) {
        l.el.style.transition = 'none';
        l.el.style.willChange = 'transform';
      });

      var px = 0, py = 0, cx = 0, cy = 0, running = true, rafId = 0;

      stage.addEventListener('pointermove', function (e) {
        var r = stage.getBoundingClientRect();
        px = ((e.clientX - r.left) / r.width - 0.5) * 2;
        py = ((e.clientY - r.top) / r.height - 0.5) * 2;
      }, { passive: true });

      stage.addEventListener('pointerleave', function () { px = 0; py = 0; });

      function stageLoop(now) {
        if (!running) return;
        cx += (px - cx) * 0.075;
        cy += (py - cy) * 0.075;
        var t = now / 1000;

        for (var i = 0; i < layers.length; i++) {
          var l = layers[i];
          var bobY = l.float ? Math.sin(t * 1.15 + l.phase) * 8 : Math.sin(t * 0.85 + l.phase) * 1.8;
          var tx = cx * l.depth * 16;
          var ty = cy * l.depth * 14 + bobY;
          var ry = cx * l.depth * 5.2;
          var rx = -cy * l.depth * 4.2;
          l.el.style.transform =
            'translate3d(' + tx.toFixed(2) + 'px,' + ty.toFixed(2) + 'px,0)' +
            ' rotateX(' + rx.toFixed(2) + 'deg) rotateY(' + ry.toFixed(2) + 'deg)';
        }
        rafId = requestAnimationFrame(stageLoop);
      }

      rafId = requestAnimationFrame(stageLoop);

      if ('IntersectionObserver' in window) {
        new IntersectionObserver(function (entries) {
          var visible = entries[0].isIntersecting;
          if (visible === running) return;
          running = visible;
          if (running) rafId = requestAnimationFrame(stageLoop);
          else cancelAnimationFrame(rafId);
        }, { threshold: 0 }).observe(stage);
      }
    }
  }

  /* ═════════════════════ card tilt (non-stage) ═════════════════════ */
  if (!reduceMotion && finePointer) {
    $$('[data-tilt]').forEach(function (el) {
      if (el.closest('#stage')) return;
      var amount = parseFloat(el.getAttribute('data-tilt-amount')) || 6;

      el.addEventListener('pointerenter', function () {
        el.style.transition = 'transform .18s cubic-bezier(.16,1,.3,1)';
      });

      el.addEventListener('pointermove', function (e) {
        var r = el.getBoundingClientRect();
        var nx = (e.clientX - r.left) / r.width - 0.5;
        var ny = (e.clientY - r.top) / r.height - 0.5;
        el.style.transform =
          'perspective(1000px) translateY(-4px)' +
          ' rotateX(' + (-ny * amount).toFixed(2) + 'deg)' +
          ' rotateY(' + (nx * amount).toFixed(2) + 'deg)';
        el.style.setProperty('--mx', ((nx + 0.5) * 100).toFixed(1) + '%');
        el.style.setProperty('--my', ((ny + 0.5) * 100).toFixed(1) + '%');
      }, { passive: true });

      el.addEventListener('pointerleave', function () {
        el.style.transition = 'transform .6s cubic-bezier(.16,1,.3,1)';
        el.style.transform = '';
      });
    });
  }

  /* ───────────────────── magnetic buttons ───────────────────── */
  if (!reduceMotion && finePointer) {
    $$('[data-magnetic]').forEach(function (el) {
      var lift = el.classList.contains('btn-primary') ? ' translateY(-2px) scale(1.015)' : '';
      el.addEventListener('pointermove', function (e) {
        var r = el.getBoundingClientRect();
        var dx = (e.clientX - (r.left + r.width / 2)) / r.width;
        var dy = (e.clientY - (r.top + r.height / 2)) / r.height;
        el.style.transform =
          'translate3d(' + (dx * 9).toFixed(2) + 'px,' + (dy * 7).toFixed(2) + 'px,0)' + lift;
      }, { passive: true });
      el.addEventListener('pointerleave', function () { el.style.transform = ''; });
    });
  }

  /* ─────────────────────── cursor glow ─────────────────────── */
  var glow = $('.cursor-glow');
  if (glow && finePointer && !reduceMotion) {
    document.body.classList.add('pointer-fine');
    var gx = -1000, gy = -1000, gcx = -1000, gcy = -1000;
    window.addEventListener('pointermove', function (e) {
      gx = e.clientX; gy = e.clientY;
    }, { passive: true });
    (function glowLoop() {
      gcx += (gx - gcx) * 0.15;
      gcy += (gy - gcy) * 0.15;
      glow.style.transform = 'translate3d(' + gcx.toFixed(1) + 'px,' + gcy.toFixed(1) + 'px,0)';
      requestAnimationFrame(glowLoop);
    })();
  }

  /* ─────────────────── flash-mode channel bars ─────────────────── */
  var channels = $('#channels');
  if (channels) {
    var frag = document.createDocumentFragment();
    for (var c = 0; c < 32; c++) {
      var chan = document.createElement('div');
      chan.className = 'chan';
      var bar = document.createElement('i');
      bar.style.setProperty('--d', (c * 0.07).toFixed(2) + 's');
      bar.style.setProperty('--n', String(c));
      chan.appendChild(bar);
      frag.appendChild(chan);
    }
    channels.appendChild(frag);
  }

  /* ───────────────────── live demo speed ───────────────────── */
  var speedEl = $('#demoSpeed');
  if (speedEl) {
    var speed = 1.18;
    setInterval(function () {
      if (document.hidden || reduceMotion) return;
      speed += (Math.random() - 0.5) * 0.11;
      speed = Math.min(1.44, Math.max(0.95, speed));
      speedEl.textContent = speed.toFixed(2).replace('.', ',');
    }, 1500);
  }

  /* ───────────────────────── QR pattern ───────────────────────── */
  var qrBox = $('#qrBox');
  if (qrBox) {
    var SIZE = 21;
    var seed = 987654321;
    function rnd() {
      seed = (seed * 1103515245 + 12345) & 0x7fffffff;
      return seed / 0x7fffffff;
    }
    function inFinder(r, c) {
      return (r < 7 && c < 7) ||
             (r < 7 && c >= SIZE - 7) ||
             (r >= SIZE - 7 && c < 7);
    }
    function finderCell(r, c) {
      var lr = r < 7 ? r : r - (SIZE - 7);
      var lc = c < 7 ? c : c - (SIZE - 7);
      var border = lr === 0 || lr === 6 || lc === 0 || lc === 6;
      var core = lr >= 2 && lr <= 4 && lc >= 2 && lc <= 4;
      return border || core;
    }
    var cells = [];
    for (var r = 0; r < SIZE; r++) {
      for (var col = 0; col < SIZE; col++) {
        var on;
        if (inFinder(r, col)) on = finderCell(r, col);
        else if ((r < 8 && col < 8) || (r < 8 && col >= SIZE - 8) || (r >= SIZE - 8 && col < 8)) on = false;
        else on = rnd() > 0.53;
        cells.push('<i' + (on ? ' class="on"' : '') + '></i>');
      }
    }
    qrBox.innerHTML = cells.join('');
    qrBox.style.setProperty('--qr-size', String(SIZE));
  }

  /* ─────────────────────── theme picker ─────────────────────── */
  var picker = $('#themePicker');
  var themeMock = $('#themeMock');
  var heroMock = $('#appMock');

  function applyTheme(theme, btn) {
    if (themeMock) themeMock.setAttribute('data-theme', theme);
    if (heroMock) heroMock.setAttribute('data-theme', theme);
    if (!picker) return;
    $$('[data-set-theme]', picker).forEach(function (b) {
      b.setAttribute('aria-selected', String(b === btn));
    });
  }

  if (picker) {
    picker.addEventListener('click', function (e) {
      var btn = e.target.closest('[data-set-theme]');
      if (!btn) return;
      applyTheme(btn.getAttribute('data-set-theme'), btn);
    });

    picker.addEventListener('keydown', function (e) {
      if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
      var items = $$('[data-set-theme]', picker);
      var at = items.indexOf(document.activeElement);
      if (at < 0) return;
      e.preventDefault();
      var next = items[(at + (e.key === 'ArrowRight' ? 1 : -1) + items.length) % items.length];
      next.focus();
      next.click();
    });
  }

  /* ────────────────────── anchor offset fix ────────────────────── */
  $$('a[href^="#"]').forEach(function (a) {
    var id = a.getAttribute('href');
    if (!id || id === '#') return;
    a.addEventListener('click', function (e) {
      var target = document.querySelector(id);
      if (!target) return;
      e.preventDefault();
      var top = target.getBoundingClientRect().top + window.scrollY - 88;
      window.scrollTo({ top: top, behavior: reduceMotion ? 'auto' : 'smooth' });
      if (history.replaceState) history.replaceState(null, '', id);
      setMenu(false);
    });
  });
})();
