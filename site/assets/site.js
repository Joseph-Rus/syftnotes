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

// The hero's wave: the pixel s's cells swelling across the whole hero and round its words. A diagonal swell rolls
// through a grid that isn't there until it arrives: cells rise from nothing, first the quiet grey of the s's empty
// grid, then, at the crest, the colour of the band they're in (the hero's height sorted like the s: lilac, sky, coral,
// mustard, top to bottom), and sink back as it passes. The headline, the buttons and the logo keep a clear margin, and
// the app's picture sits in front of it. Still for anyone who asks for less motion, running only while it's on
// screen. #wave=4.5 on the address draws it held at 4.5 seconds, for pictures.
(function () {
  var hero = document.querySelector(".hero");
  var canvas = document.createElement("canvas");
  if (!hero || !canvas.getContext) return;
  canvas.className = "hero-wave";
  canvas.setAttribute("aria-hidden", "true");
  hero.insertBefore(canvas, hero.firstChild);
  var ctx = canvas.getContext("2d");
  var BANDS = ["#CDBDFF", "#A3DAF2", "#FF6E4E", "#FFC943"], MESH = "#E2DFD8";
  var still = window.matchMedia("(prefers-reduced-motion: reduce)");
  var held = /[#&]wave=([\d.]+)/.exec(location.hash);
  var pitch = 24, cell = 0, w = 0, h = 0, cols = 0, rows = 0, left = 0, keep = [];
  var start = performance.now(), frame = 0, onScreen = true;

  // What the wave goes round: the words and buttons, and the logo's tile, each with a margin, in canvas points.
  function clearOf() {
    var at = hero.getBoundingClientRect();
    return Array.prototype.map.call(hero.querySelectorAll(".hero-grid > div, .hero-mark .tile"), function (el) {
      var r = el.getBoundingClientRect(), m = pitch * 1.5;
      return [r.left - at.left - m, r.top - at.top - m, r.right - at.left + m, r.bottom - at.top + m];
    });
  }

  function place() {
    w = hero.clientWidth;
    h = hero.clientHeight;
    pitch = w < 700 ? 14 : 22;
    cell = pitch * 6.6 / 8;
    cols = Math.ceil(w / pitch) + 1;
    rows = Math.ceil(h / pitch) + 1;
    left = (w - cols * pitch) / 2;
    keep = clearOf();
    var dpr = window.devicePixelRatio || 1;
    canvas.style.top = "0px";
    canvas.style.height = h + "px";
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }

  // How far a point is inside the margin round the words (0 well clear of them, 1 on them), fading over two cells.
  function covered(x, y) {
    var most = 0;
    for (var i = 0; i < keep.length; i++) {
      var k = keep[i];
      var dx = Math.max(k[0] - x, 0, x - k[2]), dy = Math.max(k[1] - y, 0, y - k[3]);
      most = Math.max(most, 1 - Math.min(1, Math.sqrt(dx * dx + dy * dy) / (pitch * 2)));
    }
    return most;
  }

  // A steady unevenness for each cell, so the swell isn't ruled.
  function grain(c, r) {
    var n = Math.sin(c * 12.9898 + r * 78.233) * 43758.5453;
    return n - Math.floor(n);
  }

  function draw(t) {
    ctx.clearRect(0, 0, w, h);
    for (var r = 0; r < rows; r++) {
      var y = r * pitch;
      var band = BANDS[Math.min(3, Math.floor((y / h) * 4))];
      for (var c = 0; c < cols; c++) {
        var x = left + c * pitch;
        // Two swells crossing at a slant: a long slow one and a shorter one, each a narrow crest of a sine.
        var u = (x * 0.94 + y * 0.34) / pitch, v = (x * 0.5 - y * 0.87) / pitch;
        var a = Math.pow(Math.max(0, Math.sin(u * 0.16 - t * 0.55)), 6) * 0.85 + Math.pow(Math.max(0, Math.sin(v * 0.23 + t * 0.4)), 8) * 0.45;
        a *= 0.55 + 0.45 * grain(c, r);
        a *= 1 - covered(x + pitch / 2, y + pitch / 2);
        if (a < 0.06) continue;
        var size = cell * (0.35 + 0.65 * Math.min(1, a * 1.25)), off = (pitch - size) / 2;
        ctx.globalAlpha = Math.min(0.62, a * 0.75);
        ctx.fillStyle = a > 0.42 ? band : MESH;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(x + off, y + off, size, size, size * 1.8 / 6.6); else ctx.rect(x + off, y + off, size, size);
        ctx.fill();
      }
    }
    ctx.globalAlpha = 1;
  }

  function now() { return (performance.now() - start) / 1000; }
  function tick() { draw(now()); frame = requestAnimationFrame(tick); }
  function run() {
    cancelAnimationFrame(frame);
    frame = 0;
    if (held) draw(parseFloat(held[1]));
    else if (still.matches) draw(6);
    else if (onScreen && !document.hidden) frame = requestAnimationFrame(tick);
  }

  place();
  draw(held ? parseFloat(held[1]) : 6);
  run();
  if (window.ResizeObserver) new ResizeObserver(function () { place(); draw(held ? parseFloat(held[1]) : now()); }).observe(hero);
  if (window.IntersectionObserver) new IntersectionObserver(function (seen) { onScreen = seen[0].isIntersecting; run(); }).observe(canvas);
  // The words settle once the fonts arrive: measure round them again then.
  window.addEventListener("load", function () { place(); draw(held ? parseFloat(held[1]) : now()); });
  document.addEventListener("visibilitychange", run);
  if (still.addEventListener) still.addEventListener("change", run);
})();
