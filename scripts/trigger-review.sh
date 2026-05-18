#!/usr/bin/env bash
set -euo pipefail

PR_NUMBER="${PR_NUMBER:?}"
HEAD_BRANCH="${HEAD_BRANCH:?}"
GH_DISPATCH_TOKEN="${GH_DISPATCH_TOKEN:?}"

cat > /tmp/review-prompt.txt << ENDPROMPT
You are a code reviewer. Execute these steps in order. You MUST run the commands in step 4 or 5 before finishing — do not stop at analysis.

STEP 1 — Get the PR diff:
  gh pr diff ${PR_NUMBER}

STEP 2 — Read only the files that appear in the diff output. Do not read other files.

STEP 3 — Check ONLY the code introduced by this PR for:
  MAJOR: security vulnerabilities, auth bypass, data loss, breaking API changes
  MODERATE: significant bugs, missing error handling for common failures, wrong HTTP status codes
  (Ignore minor style issues — they do not block merge)

STEP 4 — If NO major or moderate issues found, run ALL of these commands in order:

  # Leave an audit comment (required — do this first)
  gh pr comment ${PR_NUMBER} --body "✅ **Code review passed.** No major or moderate issues found. Merging."

  # Attempt a formal approval (may fail if token cannot self-approve — that is OK, the comment above is the record)
  GH_TOKEN="\$GH_REVIEW_TOKEN" gh pr review ${PR_NUMBER} --approve --body "Code review passed." 2>/dev/null || true

  # Merge
  gh pr merge ${PR_NUMBER} --squash --delete-branch

  # Update orchestrator issue label
  ISSUE_N=\$(gh pr view ${PR_NUMBER} --json body --jq '.body' | grep -oP 'orchestrator-mystore#\K[0-9]+' | head -1)
  if [ -n "\$ISSUE_N" ]; then
    GH_TOKEN="\$GH_DISPATCH_TOKEN" gh issue edit "\$ISSUE_N" --repo sbranham314/orchestrator-mystore --remove-label in-progress --add-label done
  fi

STEP 5 — If major or moderate issues exist, run ALL of these commands in order:

  RETRIES=\$(gh pr view ${PR_NUMBER} --json labels --jq '[.labels[].name | select(startswith("review-retry-"))] | length')
  if [ "\$RETRIES" -ge 3 ]; then
    gh pr edit ${PR_NUMBER} --add-label "needs-human-review"
    gh pr comment ${PR_NUMBER} --body "⚠️ Max revision cycles reached. Human review required."
  else
    NEXT=\$((RETRIES + 1))
    gh pr edit ${PR_NUMBER} --add-label "review-retry-\$NEXT"
    # Leave a comment with your specific findings (file:line — issue — fix required)
    gh pr comment ${PR_NUMBER} --body "## Review findings (attempt \$NEXT/3)

[replace this line with your specific findings: file path, line number, issue, fix required]"
    ORIGINAL=\$(gh pr view ${PR_NUMBER} --json body --jq '.body')
    jq -n --arg p "REVISION REQUEST for ${HEAD_BRANCH} (attempt \$NEXT/3).

Original task: \$ORIGINAL

Review feedback: [replace with your specific findings]

Push fixes to the EXISTING branch ${HEAD_BRANCH}. Do NOT create a new branch. Commit and push when done." \
      --arg b "${HEAD_BRANCH}" \
      '{"ref":"main","inputs":{"prompt":\$p,"branch":\$b}}' | \
    GH_TOKEN="\$GH_DISPATCH_TOKEN" gh api \
      repos/sbranham314/fn-mystore/actions/workflows/claude-code.yml/dispatches \
      --method POST --input -
  fi
ENDPROMPT

echo "Dispatching review for PR #${PR_NUMBER} on branch ${HEAD_BRANCH}"

jq -n --arg prompt "$(cat /tmp/review-prompt.txt)" --arg branch "$HEAD_BRANCH" \
  '{"ref":"main","inputs":{"prompt":$prompt,"branch":$branch}}' | \
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  repos/sbranham314/fn-mystore/actions/workflows/claude-code.yml/dispatches \
  --method POST --input -

echo "Review dispatched."
