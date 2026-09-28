#!/usr/bin/env node
// Checks the website before it's published: `node site/check.mjs` (Node 18 or later, nothing to install).
//
// It serves site/ on a local port, as GitHub Pages would, and fails if:
//  - a page doesn't load, or lacks lang, a viewport, a <title>, a description or an <h1>;
//  - a link, image, script or stylesheet on the site doesn't resolve, or a #fragment names no id on its page;
//  - a link starts with "/" (the site must also work under <user>.github.io/study-stash/, so links are relative);
//  - a page loads anything from another site (the privacy policy promises this site loads nothing from others);
//  - the downloads aren't the two release assets, Study-Stash.dmg and Study-Stash-Setup.exe, or the home page
//    doesn't offer both.
import { createServer } from "node:http";
import { readFile, readdir, stat } from "node:fs/promises";
import { dirname, extname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const releases = "https://github.com/Joseph-Rus/study-stash/releases/latest/download/";
const downloads = [releases + "Study-Stash.dmg", releases + "Study-Stash-Setup.exe"];
const types = {
  ".html": "text/html; charset=utf-8", ".css": "text/css", ".js": "text/javascript", ".mjs": "text/javascript",
  ".png": "image/png", ".webp": "image/webp", ".svg": "image/svg+xml", ".ico": "image/x-icon", ".md": "text/plain",
};

const problems = [];
const fail = (where, what) => problems.push(`${where}: ${what}`);

async function walk(dir) {
  const out = [];
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) out.push(...await walk(p));
    else out.push(p);
  }
  return out;
}

// A static server that answers like GitHub Pages: a folder serves its index.html, a folder without the slash
// redirects to it, and anything missing gets 404.html with a 404.
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, "http://x").pathname);
  let file = resolve(root, "." + path);
  if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
  try {
    if ((await stat(file)).isDirectory()) {
      if (!path.endsWith("/")) { res.writeHead(301, { Location: path + "/" }).end(); return; }
      file = join(file, "index.html");
    }
    const body = await readFile(file);
    res.writeHead(200, { "Content-Type": types[extname(file)] ?? "application/octet-stream" }).end(body);
  } catch {
    res.writeHead(404, { "Content-Type": types[".html"] }).end(await readFile(join(root, "404.html")));
  }
});
await new Promise(r => server.listen(0, "127.0.0.1", r));
const base = `http://127.0.0.1:${server.address().port}/`;

const attr = (tag, name) => tag.match(new RegExp(`\\s${name}\\s*=\\s*"([^"]*)"`, "i"))?.[1];
const ids = new Map(); // page URL -> Set of ids
async function idsOf(url) {
  if (!ids.has(url)) {
    const r = await fetch(url);
    ids.set(url, new Set([...(await r.text()).matchAll(/\sid="([^"]+)"/g)].map(m => m[1])));
  }
  return ids.get(url);
}

const files = await walk(root);
const pages = files.filter(f => f.endsWith(".html"));
const checked = new Set();
let links = 0;

for (const file of pages) {
  const rel = relative(root, file).split(sep).join("/");
  const pageUrl = base + rel.replace(/(^|\/)index\.html$/, "$1");
  const r = await fetch(pageUrl);
  if (r.status !== 200) fail(rel, `loads with ${r.status}`);
  const html = await r.text();

  if (!/<html[^>]*\slang="[a-z-]+"/i.test(html)) fail(rel, "no lang on <html>");
  if (!/<meta name="viewport"/.test(html)) fail(rel, "no viewport meta");
  const title = html.match(/<title>([^<]*)<\/title>/)?.[1]?.trim();
  if (!title) fail(rel, "no <title>");
  const desc = html.match(/<meta name="description" content="([^"]*)"/)?.[1]?.trim();
  if (!desc || desc.length < 50) fail(rel, "no description, or one under 50 characters");
  if (!/<h1[\s>]/.test(html)) fail(rel, "no <h1>");
  for (const img of html.match(/<img\b[^>]*>/g) ?? []) if (attr(img, "alt") === undefined) fail(rel, `an image without alt: ${img}`);

  // 404.html is served at any missing address, so its links are made from the site's root by a <base>.
  const from = rel === "404.html" ? base : pageUrl;
  for (const tag of html.match(/<(a|link|script|img|source)\b[^>]*>/g) ?? []) {
    const name = tag.match(/^<(\w+)/)[1];
    for (const key of ["href", "src"]) {
      const value = attr(tag, key);
      if (value === undefined) continue;
      links++;
      if (/^(mailto|tel):/.test(value)) continue;
      if (/^https?:\/\//.test(value)) {
        const loads = name !== "a" && !(name === "link" && /rel="(canonical|alternate)"/.test(tag));
        if (loads) fail(rel, `loads ${value} from another site`);
        if (value.startsWith(releases) && !downloads.includes(value)) fail(rel, `a download that isn't a release asset we ship: ${value}`);
        continue;
      }
      if (value.startsWith("/")) { fail(rel, `a link from the root, which breaks under github.io/study-stash: ${value}`); continue; }
      const target = new URL(value, from);
      const key2 = target.href;
      if (checked.has(key2)) continue;
      checked.add(key2);
      const [path, hash] = [target.href.split("#")[0], target.hash.slice(1)];
      const t = await fetch(path, { redirect: "manual" });
      if (t.status !== 200) { fail(rel, `${value} answers ${t.status}`); continue; }
      if (hash && !(await idsOf(path)).has(hash)) fail(rel, `${value}: no id "${hash}" on that page`);
    }
  }
  // og:image and the like are fetched by other sites, so they only need to exist.
  for (const m of html.matchAll(/<meta property="og:image" content="([^"]+)"/g)) {
    if (!/^https?:/.test(m[1]) && (await fetch(new URL(m[1], from))).status !== 200) fail(rel, `og:image ${m[1]} is missing`);
  }
}

const home = await (await fetch(base)).text();
for (const d of downloads) if (!home.includes(`href="${d}"`)) fail("index.html", `no download link to ${d}`);
if ((await fetch(base + "no-such-page/")).status !== 404) fail("404.html", "a missing page doesn't answer 404");

server.close();
if (problems.length) {
  console.error(`The website has ${problems.length} problem(s):\n  ${problems.join("\n  ")}`);
  process.exit(1);
}
console.log(`The website is fine: ${pages.length} pages, ${files.length} files, ${links} links and assets checked, ` +
  `${checked.size} of them on the site.`);
