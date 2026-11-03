#!/usr/bin/env python3
"""The engine tests' results on the run's page: the tests that failed (with the first lines of why) and the 20 slowest,
read from the .trx files `dotnet test --logger trx` wrote. Appended to $GITHUB_STEP_SUMMARY (printed when that isn't
set). Standard library only, so it runs on the macOS, Linux and Windows runners as they come.
  python3 .github/scripts/test-summary.py "<title>" <results folder or .trx file>...
It never fails the step: the test step itself says whether the tests passed."""
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
SLOWEST = 20


def seconds(duration):
    # hh:mm:ss.fffffff
    try:
        h, m, s = duration.split(":")
        return int(h) * 3600 + int(m) * 60 + float(s)
    except (AttributeError, ValueError):
        return 0.0


def cell(text, limit):
    text = " ".join((text or "").split())
    if len(text) > limit:
        text = text[: limit - 1] + "…"
    return text.replace("|", "\\|").replace("<", "&lt;").replace(">", "&gt;")


def trx_files(args):
    for arg in args:
        p = Path(arg)
        if p.is_dir():
            yield from sorted(p.rglob("*.trx"))
        elif p.suffix == ".trx" and p.is_file():
            yield p


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # a Windows console's code page has no "…"
    if len(sys.argv) < 3:
        print("usage: test-summary.py <title> <results folder or .trx file>...", file=sys.stderr)
        return 0
    title, paths = sys.argv[1], sys.argv[2:]
    results, failed, unreadable = [], [], []
    for trx in trx_files(paths):
        try:
            root = ET.parse(trx).getroot()
        except ET.ParseError as e:
            unreadable.append(f"{trx.name}: {e}")
            continue
        for r in root.iter(f"{NS}UnitTestResult"):
            name = r.get("testName", "?")
            outcome = r.get("outcome", "?")
            took = seconds(r.get("duration"))
            results.append((name, outcome, took))
            if outcome in ("Failed", "Error", "Timeout", "Aborted"):
                message = r.findtext(f"{NS}Output/{NS}ErrorInfo/{NS}Message") or ""
                failed.append((name, outcome, took, message))

    lines = [f"### {title}"]
    if not results:
        lines.append("No test results were written (the test run stopped before the end, or never started).")
    else:
        passed = sum(1 for _, o, _ in results if o == "Passed")
        skipped = sum(1 for _, o, _ in results if o == "NotExecuted")
        lines.append(f"{passed} passed, {len(failed)} failed, {skipped} skipped, "
                     f"{sum(t for _, _, t in results) / 60:.1f} min of test time")
        if failed:
            lines += ["", "**Failed**", "", "| Test | Outcome | Seconds | Why |", "|---|---|---:|---|"]
            for name, outcome, took, message in failed[:50]:
                lines.append(f"| {cell(name, 160)} | {outcome} | {took:.1f} | {cell(message, 300)} |")
            if len(failed) > 50:
                lines.append(f"| … and {len(failed) - 50} more | | | |")
        lines += ["", f"<details><summary>The {SLOWEST} slowest</summary>", "", "| Test | Seconds |", "|---|---:|"]
        for name, _, took in sorted(results, key=lambda r: r[2], reverse=True)[:SLOWEST]:
            lines.append(f"| {cell(name, 160)} | {took:.1f} |")
        lines += ["", "</details>"]
    for u in unreadable:
        lines.append(f"- couldn't read {u}")
    text = "\n".join(lines) + "\n\n"

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(text)
    sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
