// The only script on the site, and none of it is needed: every page works without it.
// It draws a line under the header once the page scrolls, and marks the download for the computer you're on.
(function () {
  var head = document.querySelector(".site-head");
  if (head) {
    var onScroll = function () { head.classList.toggle("scrolled", window.scrollY > 8); };
    window.addEventListener("scroll", onScroll, { passive: true });
    onScroll();
  }

  var ua = navigator.userAgent || "";
  var platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
  var mobile = /iPhone|iPad|iPod|Android/i.test(ua) || (/Mac/.test(platform) && navigator.maxTouchPoints > 1);
  var os = mobile ? null : /Mac/i.test(platform) ? "mac" : /Win/i.test(platform) ? "windows" : null;
  if (!os) return;
  document.documentElement.dataset.os = os;
  document.querySelectorAll("[data-for-os='" + os + "']").forEach(function (el) { el.classList.add("here"); });
  // In the hero there is one big button, for this computer (both files serve every chip: Apple silicon and Intel on
  // a Mac, Intel/AMD and ARM on Windows). The other computer's download and the iPhone app are one small line away,
  // and a click says what happens next.
  var hero = document.querySelector("[data-hero-downloads]");
  var next = document.querySelector("[data-next]");
  var then = {
    mac: "Downloading. When it's done, open Syft-Notes.dmg and drag Syft Notes onto Applications, then open it.",
    windows: "Downloading. When it's done, open Syft-Notes-Setup.exe: it installs and opens Syft Notes by itself. If Windows says it protected your PC, click More info, then Run anyway."
  };
  if (hero) {
    var mine = hero.querySelector("[data-os='" + os + "']");
    if (mine) {
      hero.querySelectorAll(".btn").forEach(function (b) { if (b !== mine) b.hidden = true; });
      mine.classList.remove("btn-quiet");
      mine.classList.add("btn-primary");
      var other = document.querySelector("[data-other-downloads]");
      if (other) {
        other.querySelectorAll("[data-other='" + os + "']").forEach(function (a) { a.hidden = true; });
        other.hidden = false;
      }
    }
  }
  document.querySelectorAll("a[href$='Syft-Notes.dmg'], a[href$='Syft-Notes-Setup.exe']").forEach(function (a) {
    a.addEventListener("click", function () {
      if (!next) return;
      next.textContent = then[/\.dmg$/.test(a.getAttribute("href")) ? "mac" : "windows"];
      next.hidden = false;
    });
  });
})();

// The hero's wave: the pixel s's cells flowing behind the page, a ribbon four cells deep sorted into the s's colour
// bands (lilac, sky, coral, mustard, top to bottom) rolling across the gap above the app's picture and behind it. Cells
// stay on the grid and only fade as the wave moves through them. Still for anyone who asks for less motion, and it
// only runs while it's on screen.
(function () {
  var hero = document.querySelector(".hero");
  var stage = hero && hero.querySelector(".stage");
  var canvas = document.createElement("canvas");
  if (!stage || !canvas.getContext) return;
  canvas.className = "hero-wave";
  canvas.setAttribute("aria-hidden", "true");
  hero.insertBefore(canvas, hero.firstChild);
  var ctx = canvas.getContext("2d");
  var PITCH = 24, CELL = 0, RADIUS = 0, ROWS = 13, MOST = 0.38;
  var BANDS = ["#CDBDFF", "#A3DAF2", "#FF6E4E", "#FFC943"];
  var still = window.matchMedia("(prefers-reduced-motion: reduce)");
  var w = 0, start = performance.now(), frame = 0, onScreen = true;

  function place() {
    // Smaller cells on a phone. The ribbon's middle is two rows down the app's picture, so mostly it flows behind and
    // round the picture and only its crests rise into the gap above, clear of the words.
    w = hero.clientWidth;
    PITCH = w < 700 ? 14 : 24;
    CELL = PITCH * 6.6 / 8;
    RADIUS = CELL * 1.8 / 6.6;
    var top = stage.getBoundingClientRect().top - hero.getBoundingClientRect().top - ROWS * PITCH / 2 + 2 * PITCH;
    var dpr = window.devicePixelRatio || 1;
    canvas.style.top = top + "px";
    canvas.style.height = ROWS * PITCH + "px";
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(ROWS * PITCH * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }

  function cell(x, y) {
    ctx.beginPath();
    if (ctx.roundRect) ctx.roundRect(x, y, CELL, CELL, RADIUS); else ctx.rect(x, y, CELL, CELL);
    ctx.fill();
  }

  function draw(t) {
    ctx.clearRect(0, 0, w, ROWS * PITCH);
    var cols = Math.ceil(w / PITCH) + 1, left = (w - cols * PITCH) / 2 + (PITCH - CELL) / 2;
    for (var c = 0; c < cols; c++) {
      // Where the ribbon's top is in this column, in rows: one long slow swell and a shorter one riding on it.
      var top = (ROWS - 4) / 2 + 1.8 * Math.sin(c * 0.3 - t * 0.8) + 0.7 * Math.sin(c * 0.11 + t * 0.45);
      for (var r = 0; r < ROWS; r++) {
        var d = r + 0.5 - top;
        if (d <= -0.5 || d >= 4.5) continue;
        ctx.globalAlpha = MOST * Math.min(1, d + 0.5, 4.5 - d);
        ctx.fillStyle = BANDS[Math.max(0, Math.min(3, Math.floor(d)))];
        cell(left + c * PITCH, r * PITCH + (PITCH - CELL) / 2);
      }
    }
    ctx.globalAlpha = 1;
  }

  function tick(now) {
    draw((now - start) / 1000);
    frame = requestAnimationFrame(tick);
  }

  function run() {
    cancelAnimationFrame(frame);
    frame = 0;
    if (still.matches) draw(0);
    else if (onScreen && !document.hidden) frame = requestAnimationFrame(tick);
  }

  place();
  draw(0);
  run();
  if (window.ResizeObserver) new ResizeObserver(function () { place(); draw((performance.now() - start) / 1000); }).observe(hero);
  if (window.IntersectionObserver) new IntersectionObserver(function (seen) { onScreen = seen[0].isIntersecting; run(); }).observe(canvas);
  document.addEventListener("visibilitychange", run);
  if (still.addEventListener) still.addEventListener("change", run);
})();
