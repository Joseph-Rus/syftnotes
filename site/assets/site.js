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
  // In the hero, the button for this computer comes first and is the filled one.
  var hero = document.querySelector("[data-hero-downloads]");
  if (hero) {
    var mine = hero.querySelector("[data-os='" + os + "']");
    if (mine) {
      hero.querySelectorAll(".btn").forEach(function (b) { b.classList.remove("btn-primary"); b.classList.add("btn-quiet"); });
      mine.classList.remove("btn-quiet");
      mine.classList.add("btn-primary");
      hero.insertBefore(mine, hero.firstChild);
    }
  }
})();
