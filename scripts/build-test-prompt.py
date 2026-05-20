#!/usr/bin/env python3
"""Reads test-prompt-template.txt, substitutes placeholders, writes to /tmp/test-prompt.txt.
Supports comma-separated PR_NUMBERS and ISSUE_NUMBERS for batched testing."""
import os, sys

pr_numbers_raw = os.environ.get("PR_NUMBERS", "")
repo = os.environ.get("REPO", "")
issue_numbers_raw = os.environ.get("ISSUE_NUMBERS", "")

prs = [p.strip() for p in pr_numbers_raw.split(",") if p.strip()]
issues = [i.strip() for i in issue_numbers_raw.split(",") if i.strip()]

if not prs:
    print("ERROR: PR_NUMBERS is required", file=sys.stderr)
    sys.exit(1)

script_dir = os.path.dirname(os.path.abspath(__file__))
template_path = os.path.join(script_dir, "test-prompt-template.txt")

with open(template_path) as f:
    template = f.read()

def build_issue_parts(issue):
    if issue:
        issue_ref = f"retrostoremanager/orchestrator-mystore#{issue}"
        issue_step = f'  GH_TOKEN="$GH_DISPATCH_TOKEN" gh issue view {issue} --repo retrostoremanager/orchestrator-mystore'
        issue_label_step = (
            f'  GH_TOKEN="$GH_DISPATCH_TOKEN" gh issue edit {issue} \\\n'
            f'    --repo retrostoremanager/orchestrator-mystore \\\n'
            f'    --remove-label in-test \\\n'
            f'    --add-label done\n'
            f'  GH_TOKEN="$GH_DISPATCH_TOKEN" gh issue close {issue} \\\n'
            f'    --repo retrostoremanager/orchestrator-mystore'
        )
    else:
        issue_ref = "(no linked orchestrator issue)"
        issue_step = "  # No orchestrator issue linked — test based on PR diff only"
        issue_label_step = "  # No orchestrator issue linked — skipping label update"
    return issue_ref, issue_step, issue_label_step

if len(prs) == 1:
    # Single PR: use template as-is (original behavior)
    pr = prs[0]
    issue = issues[0] if issues else ""
    issue_ref, issue_step, issue_label_step = build_issue_parts(issue)

    prompt = template
    prompt = prompt.replace("{{PR}}", pr)
    prompt = prompt.replace("{{REPO}}", repo)
    prompt = prompt.replace("{{ISSUE_REF}}", issue_ref)
    prompt = prompt.replace("{{ISSUE_STEP}}", issue_step)
    prompt = prompt.replace("{{ISSUE_LABEL_STEP}}", issue_label_step)
else:
    # Multiple PRs: generate a sequential batch prompt
    sections = []
    all_issue_refs = []
    mark_done_steps = []

    for idx, pr in enumerate(prs):
        issue = issues[idx] if idx < len(issues) else ""
        issue_ref, issue_step, issue_label_step = build_issue_parts(issue)
        all_issue_refs.append(issue_ref)

        section = f"""
===== PR #{pr} ({issue_ref}) =====

  STEP 1 — Get context for PR #{pr}:
    gh pr view {pr} --repo retrostoremanager/{repo} --json title,body
    gh pr diff {pr} --repo retrostoremanager/{repo}
{issue_step}

  STEP 2 — API tests for PR #{pr}
    Based on the PR diff and acceptance criteria, test each new/modified endpoint:
    - Happy path: correct inputs return expected HTTP status and response shape
    - Error cases: missing/invalid fields → 400, no auth → 401, bad IDs → 404
    - Business logic: verify calculated values, state transitions, data persistence

  STEP 3 — Integration tests for PR #{pr}
    Test multi-step flows end-to-end through the API.

  STEP 4 — Report results for PR #{pr}
    If ALL tests pass: print "All tests passed for PR #{pr}." and proceed to the next PR.

    If a test FAILS: create one GitHub issue per distinct bug:
      cat > /tmp/bug-body-{pr}-1.md << 'EOF'
      ## Bug description
      [What is wrong — be specific]

      ## Failing test
      [Exact curl command that failed]

      Actual output:
      [paste the actual response or error]

      Expected:
      [what should have happened]

      ## Context
      Related to: {issue_ref}
      Deployed PR: https://github.com/retrostoremanager/{repo}/pull/{pr}

      ## Technical notes
      [Which file/function to fix; full error message if available]
      EOF

      GH_TOKEN="$GH_DISPATCH_TOKEN" gh issue create \\
        --repo retrostoremanager/orchestrator-mystore \\
        --title "Bug: [concise description]" \\
        --body-file /tmp/bug-body-{pr}-1.md \\
        --label "bug" --label "ready" --label "priority:high" --label "repo:{repo}"

    After creating all bugs for PR #{pr}, print "Created N bug issue(s) for PR #{pr}." and continue to next PR.
"""
        sections.append(section)

        if issue:
            mark_done_steps.append(f"""
  # Mark PR #{pr} ({issue_ref}) done if all its tests passed and no open bugs remain
  OPEN_BUGS_{pr}=$(GH_TOKEN="$GH_DISPATCH_TOKEN" gh issue list \\
    --repo retrostoremanager/orchestrator-mystore \\
    --label bug --state open --limit 50 \\
    --json number,body,labels \\
    --jq '[.[] | select(.body | contains("{issue_ref}")) | select(.labels | map(.name) | any(. == "done") | not)] | length')
  if [ "$OPEN_BUGS_{pr}" -eq 0 ]; then
{issue_label_step}
  else
    echo "PR #{pr}: $OPEN_BUGS_{pr} linked bug(s) still open — keeping {issue_ref} in-test"
  fi""")

    all_refs_str = ", ".join(all_issue_refs)
    prompt = f"""You are a QA testing agent for RetroStoreManager. Multiple PRs were recently deployed to the dev environment. Test each one in sequence and create bug issues for any failures.

Dev API: https://mystore-func-dev.azurewebsites.net/api
Dev frontend: https://dev.retrostoremanager.com
Deployed repo: {repo}
Testing PRs: {", ".join(f"#{p}" for p in prs)}
Orchestrator issues: {all_refs_str}

AUTHENTICATION — Do this once before running any tests below:
  Find the login route by reading MyStore.Functions/Functions/AccountFunctions.cs.
  Then authenticate (the login endpoint requires a company slug):
    cat > /tmp/login.json << LOGINEOF
    {{"email":"$TEST_EMAIL","password":"$TEST_PASSWORD","slug":"$TEST_COMPANY_SLUG"}}
    LOGINEOF
    RESPONSE=$(curl -s -X POST "https://mystore-func-dev.azurewebsites.net/api/[login-route]" \\
      -H "Content-Type: application/json" \\
      -d @/tmp/login.json)
    TOKEN=$(echo "$RESPONSE" | grep -oP '"token"\\s*:\\s*"\\K[^"]+')
    COMPANY_ID=$(echo "$RESPONSE" | grep -oP '"companyId"\\s*:\\s*"\\K[^"]+')

  Every authenticated request needs both headers:
    -H "Authorization: Bearer $TOKEN" -H "x-company-id: $COMPANY_ID"

{"".join(sections)}
FINAL STEP — Mark done any orchestrator issues where all tests passed:
{"".join(mark_done_steps) if mark_done_steps else "  # No orchestrator issues linked — skipping label updates"}

RULES:
- Only test what changed in each PR — do not run a full regression suite
- Each bug must include exact reproduction steps (copy-pasteable curl)
- Always include x-company-id header on multi-tenant endpoints
- If the dev environment is unreachable, create one bug titled: "Dev environment unreachable after deploy"
- Never comment on PRs — only create issues in orchestrator-mystore
"""

with open("/tmp/test-prompt.txt", "w") as f:
    f.write(prompt)

print(f"Test prompt built ({len(prompt)} chars, PRs {pr_numbers_raw}, repo {repo})")
