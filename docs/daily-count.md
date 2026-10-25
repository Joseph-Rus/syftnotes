# The daily count

How many copies of the computer app are in use, and nothing else. Joey's choice (2026-10-25): "a ping with a unique
code, no personal information."

## What's sent

Once a day, when the app is running, it sends Firestore (project `study-stash-app`, database `(default)`) one
document, with no sign-in:

```
POST https://firestore.googleapis.com/v1/projects/study-stash-app/databases/(default)/documents/pings?documentId=<code>_<day>
{"fields": {"d": {"stringValue": "<day>"}, "v": {"stringValue": "<app version>"}, "p": {"stringValue": "mac|windows|linux"}}}
```

- `<code>`: 32 lowercase hex characters made at random the first time (128 bits), kept in `<home>/count-code`. It isn't
  derived from anything (no hardware id, no account, no name), and it's never sent anywhere else.
- `<day>`: the day where the computer is, `yyyy-MM-dd`.
- Answers:
  - 200 means counted.
  - 409 means today was already counted (also fine).
  - Anything else, or no connection, is tried again at the next check.
- When: shortly after the app starts, then every few hours while it runs, sent at most once a day (the day of the last
  success is kept beside the code).

## The rules (`firestore.rules`)

Anyone may *create* `pings/<code>_<day>` holding exactly `d`, `v` and `p`, where `d` matches the day in its name, `v` is
at most 20 characters, and `p` is mac, windows or linux. Nobody outside the project can read, list, change or delete
them. A copy can't be counted twice on one day: the second create is a 409.

## Turning it off

Settings → General → **Count this copy** (on by default). Off: nothing is sent, and `count-code` is deleted, so turning
it on again counts as a new copy.

## Reading it

`python3 tools/daily-count.py [days]`, signed in to gcloud as an owner of the project. It prints copies per day for the
last week, different copies over 7 days and over the period, and the split by computer and by version.

## Not counted

The iPhone and iPad app. Its numbers come from App Store Connect, so its privacy answers stay "Data Not Collected".
