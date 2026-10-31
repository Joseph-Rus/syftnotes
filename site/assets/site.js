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
