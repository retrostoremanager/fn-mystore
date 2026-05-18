#!/usr/bin/env bash
set -euo pipefail

PR_NUMBER="${PR_NUMBER:?}"
TARGET_REPO="${TARGET_REPO:?}"
GH_DISPATCH_TOKEN="${GH_DISPATCH_TOKEN:?}"
ORCHESTRATOR_ISSUE="${ORCHESTRATOR_ISSUE:-}"

ISSUE_REF=""
ISSUE_STEP=""
if [ -n "$ORCHESTRATOR_ISSUE" ]; then
  ISSUE_REF="sbranham314/orchestrator-mystore#${ORCHESTRATOR_ISSUE}"
  ISSUE_STEP="  GH_TOKEN=\"\$GH_DISPATCH_TOKEN\" gh issue view ${ORCHESTRATOR_ISSUE} --repo sbranham314/orchestrator-mystore"
else
  ISSUE_REF="(no linked orchestrator issue)"
  ISSUE_STEP="  # No orchestrator issue linked — test based on PR diff only"
fi

cat > /tmp/test-prompt.txt << ENDPROMPT
You are a QA testing agent for RetroStoreManager. A PR was just merged and deployed to the dev environment. Test the deployed feature and create bug issues for any failures.

Dev API: https://mystore-func-dev.azurewebsites.net/api
Dev frontend: https://dev.retrostoremanager.com
Deployed repo: ${TARGET_REPO}
PR: sbranham314/${TARGET_REPO}/pull/${PR_NUMBER}
Orchestrator issue: ${ISSUE_REF}

STEP 1 — Get context
  gh pr view ${PR_NUMBER} --repo sbranham314/${TARGET_REPO} --json title,body
  gh pr diff ${PR_NUMBER} --repo sbranham314/${TARGET_REPO}
${ISSUE_STEP}

STEP 2 — Authenticate against the dev API
  Find the login route: read MyStore.Functions/Functions/AccountFunctions.cs and look for the Route attribute on the login function.
  Then authenticate:
    RESPONSE=\$(curl -s -X POST "https://mystore-func-dev.azurewebsites.net/api/[login-route]" \
      -H "Content-Type: application/json" \
      -d "{\"email\":\"\$TEST_EMAIL\",\"password\":\"\$TEST_PASSWORD\"}")
    TOKEN=\$(echo "\$RESPONSE" | grep -oP '"token"\s*:\s*"\K[^"]+')
    COMPANY_ID=\$(echo "\$RESPONSE" | grep -oP '"companyId"\s*:\s*"\K[^"]+')

  All authenticated requests need:
    -H "Authorization: Bearer \$TOKEN" -H "x-company-id: \$COMPANY_ID"

STEP 3 — API tests
  Based on the PR diff and acceptance criteria, test each new/modified endpoint:
  - Happy path: correct inputs return expected status code and response shape
  - Error cases: missing/invalid fields return 400, no auth returns 401, bad IDs return 404
  - Business logic: verify calculated values, state transitions, data is actually persisted

STEP 4 — Integration tests
  Test multi-step flows end-to-end through the API.
  Example: if a sale was added, create a sale then verify it appears in GET /sales.
  Example: if settings were added, PUT the settings then verify GET returns the updated values.

STEP 5 — E2E tests with Playwright (for UI changes or full user flows)
  Node.js is available. Install Playwright if needed:
    npm install -D @playwright/test 2>/dev/null || true
    npx playwright install chromium --with-deps 2>/dev/null || true

  Write the test to /tmp/e2e-test.spec.js, then run:
    npx playwright test /tmp/e2e-test.spec.js --reporter=line

  Tests must use the TEST_EMAIL/TEST_PASSWORD env vars for login.
  The frontend is at https://dev.retrostoremanager.com.

STEP 6 — Report results
  If ALL tests pass: print "All tests passed for PR #${PR_NUMBER}." and stop. Do NOT create issues.

  If a test FAILS: create one GitHub issue per distinct bug.

  Write the body to a temp file first (avoids quoting issues):
    cat > /tmp/bug-body.md << 'EOF'
    ## Bug description
    [What is wrong — be specific]

    ## Failing test
    [Exact curl command or Playwright snippet that failed]
    [Actual response/output vs what was expected]

    ## Context
    Introduced by: ${ISSUE_REF}
    Deployed PR: https://github.com/sbranham314/${TARGET_REPO}/pull/${PR_NUMBER}

    ## Technical notes
    [Which file/function/component to fix; full error message or stack trace]
    EOF

  Then create the issue:
    GH_TOKEN="\$GH_DISPATCH_TOKEN" gh issue create \
      --repo sbranham314/orchestrator-mystore \
      --title "Bug: [concise description]" \
      --body-file /tmp/bug-body.md \
      --label "bug" \
      --label "ready" \
      --label "priority:high" \
      --label "repo:${TARGET_REPO}"

  Use a different file name (/tmp/bug-body-2.md etc.) for each distinct bug.

RULES:
- Only test what changed in this PR — do not run a full regression suite
- Each bug issue must include exact reproduction steps (copy-pasteable curl or test code)
- Always include x-company-id on multi-tenant API endpoints
- If the dev environment is unreachable, create one bug: title "Dev environment unreachable after deploy of PR #${PR_NUMBER}"
ENDPROMPT

echo "Dispatching test agent for PR #${PR_NUMBER} on ${TARGET_REPO}"

jq -n --arg prompt "$(cat /tmp/test-prompt.txt)" --arg branch "development" \
  '{"ref":"main","inputs":{"prompt":$prompt,"branch":$branch}}' | \
GH_TOKEN="$GH_DISPATCH_TOKEN" gh api \
  repos/sbranham314/fn-mystore/actions/workflows/claude-code.yml/dispatches \
  --method POST --input -

echo "Test agent dispatched."
