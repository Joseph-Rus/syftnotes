#!/usr/bin/env bash
# What this ci run has to do. The version job runs it, from a checkout of the code being built, and every other job
# reads its answers (see the comment at the top of .github/workflows/ci.yml for the whole shape). It writes these
# lines to $GITHUB_OUTPUT:
#   version     StudyStashVersion in engine/Directory.Build.props
#   new         true when v<version> has no tag yet (only a push to main with a new version ever publishes it)
#   full        true when this run tests on every system (Mac, Windows and Linux apps, engine tests on all three): a
#               release (a push to main with a new version), a run by hand, and the daily run when main's latest
#               commit has no full run finished on it yet. false (Linux only) for a pull request and any other merge.
#   installers  true when this run builds and proves the installers: a push to main with a new version (the release),
#               and a run by hand on code with a new version, or with the "installers" box ticked (a rehearsal)
#   tested      true when this very code already passed, so the engine tests and the self-tests aren't run again
#   reuse_run   the id of an earlier run by hand whose tested installers a release publishes as they are, or empty
# Needs: git, gh (GH_TOKEN), and in the environment EVENT_NAME (push, pull_request, workflow_dispatch or schedule), REF,
# SHA, BEFORE (a push's previous tip), REPO and,
# for a run by hand, WANT_INSTALLERS and REUSE_RUN (its two inputs).
set -u

props=engine/Directory.Build.props
: "${EVENT_NAME:?}" "${REF:?}" "${SHA:?}" "${REPO:?}"
BEFORE=${BEFORE:-}
WANT_INSTALLERS=${WANT_INSTALLERS:-}
REUSE_RUN=${REUSE_RUN:-}
ZEROS=0000000000000000000000000000000000000000

out() { echo "$1=$2" >> "${GITHUB_OUTPUT:-/dev/stdout}"; }
say() { echo "$*"; if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then echo "$*" >> "$GITHUB_STEP_SUMMARY"; fi; }
fail() { echo "::error::$*"; exit 1; }

main_push=false
if [ "$EVENT_NAME" = push ] && [ "$REF" = refs/heads/main ]; then main_push=true; fi

V=$(sed -n 's:.*<StudyStashVersion>\(.*\)</StudyStashVersion>.*:\1:p' "$props")
[ -n "$V" ] || fail "no StudyStashVersion in $props"
tags=$(git ls-remote --tags origin "refs/tags/v$V") || fail "couldn't ask GitHub whether v$V is tagged"
new=true
if [ -n "$tags" ]; then new=false; fi

# Installers are built and proved by a release, and by a rehearsal of one; a merge that isn't a release doesn't
# build installers it would throw away.
installers=false
if [ "$main_push" = true ]; then
  installers=$new
elif [ "$EVENT_NAME" = workflow_dispatch ] && { [ "$new" = true ] || [ "$WANT_INSTALLERS" = true ] || [ -n "$REUSE_RUN" ]; }; then
  installers=true
fi

# Files whose change can't change what the code does, so a push that touches only these (and the version) on top of
# a commit that passed has nothing new to test. The workflow itself is never one of them: a change to ci is tested.
trivial() {
  case "$1" in
    "$props" | README.md | SECURITY.md | LICENSE | engine/README.md | docs/* | site/* | firebase.json | .firebaserc | .github/FUNDING.yml | .github/workflows/site.yml) return 0 ;;
  esac
  return 1
}

tree=$(git rev-parse 'HEAD^{tree}')
tested=false
reuse=""
why=""

# Only ci's own runs in this repository, started by a push or by hand (both need write access), are believed: a pull
# request's run, a fork's above all, runs whatever workflow that pull request brings, and could leave a tested-tree it
# never tested or installers it never proved.
trust="(.event == \"push\" or .event == \"workflow_dispatch\") and .head_repository.full_name == \"$REPO\" and .path == \".github/workflows/ci.yml\""
# The ids of the runs to believe that passed on a commit.
trusted_runs() {
  gh api "repos/$REPO/actions/workflows/ci.yml/runs?head_sha=$1&status=success&per_page=100" \
    --jq ".workflow_runs[] | select($trust) | .id" 2> /dev/null
}
# Is this a run to believe, and did it pass?
trusted_run() {
  [ "$(gh api "repos/$REPO/actions/runs/$1" --jq "($trust) and .conclusion == \"success\"" 2> /dev/null)" = true ]
}

# Was this run a full one (every system)? A full run keeps a full-run artifact (ci.yml's version job).
full_run() {
  gh api "repos/$REPO/actions/runs/$1/artifacts" --paginate \
    --jq '.artifacts[] | select(.name == "full-run" and .expired == false) | .name' 2> /dev/null | grep -qx full-run
}

# Does this run (by hand, on a branch) have every installer still to download (the Mac's, Windows', and Linux's two)?
has_installers() {
  local names
  trusted_run "$1" || return 1
  names=$(gh api "repos/$REPO/actions/runs/$1/artifacts" --paginate --jq '.artifacts[] | select(.expired == false) | .name' 2>/dev/null) || return 1
  # Installers signed by a rehearsal's throwaway identities (signing-plan.sh) are never published, so never reused.
  if printf '%s\n' "$names" | grep -qxE 'signing-(mac|windows)-rehearsal'; then return 1; fi
  printf '%s\n' "$names" | grep -qx mac-installers && printf '%s\n' "$names" | grep -qx windows-installers \
    && printf '%s\n' "$names" | grep -qx linux-installers-linux-x64 && printf '%s\n' "$names" | grep -qx linux-installers-linux-arm64
}

# Did this run (which passed) test exactly this tree? Runs by hand leave the tree they tested as an artifact.
tested_this_tree() {
  trusted_run "$1" || { echo "run $1 isn't one of ci's own runs here, by a push or by hand, that passed"; return 1; }
  rm -rf prior
  gh run download "$1" -R "$REPO" -n tested-tree -D prior > /dev/null 2>&1 || return 1
  echo "run $1 tested tree $(cat prior/tested-tree.txt); this is tree $tree"
  [ "$(cat prior/tested-tree.txt)" = "$tree" ]
}

# Every system, or Linux only. A merge of a version already released runs what a pull request runs; every system runs
# for a release, a run by hand, and once a day on main's latest commit, unless a full run (the daily one, a release's,
# or one by hand) already finished there, passed or failed: the same code isn't run in full twice.
full=false
case "$EVENT_NAME" in
  push) full=$new ;;
  workflow_dispatch) full=true ;;
  schedule)
    full=true
    for run in $(gh api "repos/$REPO/actions/workflows/ci.yml/runs?head_sha=$SHA&status=completed&per_page=100" \
        --jq ".workflow_runs[] | select(((.event == \"schedule\") or ($trust)) and .conclusion != \"cancelled\") | .id" 2> /dev/null); do
      if full_run "$run"; then
        full=false
        tested=true
        why="run $run already ran in full on this commit, and main hasn't moved since"
        break
      fi
    done
    ;;
esac

# 1. A push to main that changes only the version and files that can't matter, on top of a commit whose ci passed.
#    A release (a full run) believes only a full run's pass there: a merge's Linux check alone isn't enough.
if [ "$main_push" = true ] && [ -n "$BEFORE" ] && [ "$BEFORE" != "$ZEROS" ]; then
  if git fetch -q --depth=1 origin "$BEFORE"; then git fetch -q --deepen=1 origin "$SHA" || true; fi
  if changed=$(git diff --name-only "$BEFORE" "$SHA" 2> /dev/null); then
    others=0
    while IFS= read -r f; do
      if [ -n "$f" ] && ! trivial "$f"; then others=$((others + 1)); fi
    done <<< "$changed"
    # engine/Directory.Build.props holds more than the version: only a changed StudyStashVersion line is trivial.
    beyond=$(git diff -U0 "$BEFORE" "$SHA" -- "$props" | grep -E '^[+-]' | grep -vE '^(\+\+\+|---)' | grep -vc '<StudyStashVersion>')
    passed=0
    for run in $(trusted_runs "$BEFORE"); do
      if [ "$full" = false ] || full_run "$run"; then passed=$((passed + 1)); fi
    done
    echo "changed since $BEFORE: $(echo "$changed" | tr '\n' ' '); other files: $others; other lines of $props: $beyond; ci passed there: $passed"
    if [ "$others" = 0 ] && [ "${beyond:-1}" = 0 ] && [ "${passed:-0}" -gt 0 ]; then
      tested=true
      why="only the version and files that can't matter changed on top of $BEFORE, whose ci passed"
    fi
  else
    echo "couldn't tell what changed since $BEFORE"
  fi
fi

# 2. A push to main that merges a pull request whose run by hand passed on exactly this tree (main hadn't moved):
#    the code has passed; and when a release needs installers and that run built and proved them, those are the ones
#    to publish.
if [ "$main_push" = true ] && { [ "$tested" = false ] || [ "$installers" = true ]; }; then
  for head in $(gh api "repos/$REPO/commits/$SHA/pulls" --jq '.[].head.sha' 2> /dev/null); do
    for run in $(trusted_runs "$head"); do
      tested_this_tree "$run" || continue
      if [ "$tested" = false ]; then
        tested=true
        why="the pull request's run $run by hand passed on exactly this code"
      fi
      if [ "$installers" = true ] && [ -z "$reuse" ] && has_installers "$run"; then
        reuse=$run
        why="the pull request's run $run by hand passed on exactly this code, installers included"
        break 2
      fi
    done
  done
fi

# A rehearsal of that fast path, by hand: the run named must have passed on exactly this tree and still hold its
# installers; nothing is guessed, so a wrong id stops here.
if [ "$EVENT_NAME" = workflow_dispatch ] && [ -n "$REUSE_RUN" ]; then
  trusted_run "$REUSE_RUN" || fail "run $REUSE_RUN didn't pass, or isn't one of ci's own runs here (by a push or by hand)"
  tested_this_tree "$REUSE_RUN" || fail "run $REUSE_RUN tested other code than this"
  has_installers "$REUSE_RUN" || fail "run $REUSE_RUN has no installers left to reuse (or they were signed by a rehearsal of signing, which are never published)"
  tested=true
  reuse=$REUSE_RUN
  why="rehearsing a release from run $REUSE_RUN"
fi

say "### What this run does"
say "- version $V, $([ "$new" = true ] && echo "no tag yet" || echo "already tagged")"
if [ "$full" = true ]; then say "- systems: every system (Mac, Windows, Linux)"
elif [ "$EVENT_NAME" = schedule ]; then say "- systems: none, nothing new since a full run"
else say "- systems: Linux only"; fi
say "- code: $([ "$tested" = true ] && echo "already tested ($why)" || echo "tested here")"
if [ "$installers" = true ]; then
  say "- installers: $([ -n "$reuse" ] && echo "reused from run $reuse, not rebuilt" || echo "built and proved here")$([ "$main_push" = true ] && echo ", then published" || echo "; nothing is published from a run by hand")"
else
  say "- installers: none (not a release)"
fi

out version "$V"
out new "$new"
out full "$full"
out installers "$installers"
out tested "$tested"
out reuse_run "$reuse"
