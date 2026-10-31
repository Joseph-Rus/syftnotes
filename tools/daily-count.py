#!/usr/bin/env python3
"""How many copies of Syft Notes are in use, from the app's daily count (docs/daily-count.md).

  python3 tools/daily-count.py [days]     (default 30)

Reads Firestore's `pings` with your own Google Cloud sign-in (gcloud, an owner of the study-stash-app project);
nobody outside the project can read them. Prints each day's copies for the last week, the copies seen in the last
7 and `days` days, and the split by Mac/Windows and by version.
"""
import collections, datetime, json, subprocess, sys, urllib.request

PROJECT = "study-stash-app"
URL = f"https://firestore.googleapis.com/v1/projects/{PROJECT}/databases/(default)/documents:runQuery"


def token():
    return subprocess.run(["gcloud", "auth", "print-access-token"], capture_output=True, text=True, check=True).stdout.strip()


def pings(since):
    query = {"structuredQuery": {
        "from": [{"collectionId": "pings"}],
        "where": {"fieldFilter": {"field": {"fieldPath": "d"}, "op": "GREATER_THAN_OR_EQUAL", "value": {"stringValue": since}}},
    }}
    req = urllib.request.Request(URL, data=json.dumps(query).encode(), method="POST",
                                 headers={"Authorization": f"Bearer {token()}", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        rows = json.load(r)
    for row in rows:
        doc = row.get("document")
        if not doc:
            continue
        code = doc["name"].rsplit("/", 1)[1].split("_")[0]
        f = doc["fields"]
        yield code, f["d"]["stringValue"], f["v"]["stringValue"], f["p"]["stringValue"]


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 else 30
    today = datetime.date.today()
    since = (today - datetime.timedelta(days=days - 1)).isoformat()
    rows = list(pings(since))
    by_day = collections.defaultdict(set)
    for code, d, _, _ in rows:
        by_day[d].add(code)
    print("Copies in use each day:")
    for i in range(6, -1, -1):
        d = (today - datetime.timedelta(days=i)).isoformat()
        print(f"  {d}  {len(by_day.get(d, ())):>5}")
    week = (today - datetime.timedelta(days=6)).isoformat()
    print(f"Different copies, last 7 days:  {len({c for c, d, _, _ in rows if d >= week})}")
    print(f"Different copies, last {days} days: {len({c for c, _, _, _ in rows})}")
    latest = {}
    for code, d, v, p in sorted(rows, key=lambda r: r[1]):
        latest[code] = (v, p)
    print("By computer:", dict(collections.Counter(p for _, p in latest.values())))
    print("By version: ", dict(collections.Counter(v for v, _ in latest.values()).most_common()))


if __name__ == "__main__":
    main()
