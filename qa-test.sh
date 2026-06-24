#!/usr/bin/env bash
# End-to-end QA for DrawSync access-control fixes.
# Starts the server, logs in, and exercises every fixed code path.
set -uo pipefail
export PATH="/home/z/.dotnet:$PATH"
cd /home/z/DrawSync/DrawSync

pkill -f "DrawSync.dll" 2>/dev/null; sleep 2

# Start server in background of THIS shell
ASPNETCORE_ENVIRONMENT=Development dotnet /home/z/DrawSync/DrawSync/bin/Debug/net8.0/DrawSync.dll --urls http://localhost:5006 > /tmp/ds_run.log 2>&1 &
SRV=$!
for i in $(seq 1 15); do ss -tln 2>/dev/null | rg -q ":5006" && break; sleep 1; done

PASS=0; FAIL=0
check() { if [ "$1" = "$2" ]; then echo "  ✅ PASS: $3 ($1)"; PASS=$((PASS+1)); else echo "  ❌ FAIL: $3 (got '$1', expected '$2')"; FAIL=$((FAIL+1)); fi; }
contains() { if echo "$1" | rg -q "$2"; then echo "  ✅ PASS: $3"; PASS=$((PASS+1)); else echo "  ❌ FAIL: $3 (missing '$2')"; FAIL=$((FAIL+1)); fi; }

echo "=== 1. Login (cookie capture + claim) ==="
rm -f /tmp/ds_cookies.txt
curl -s -c /tmp/ds_cookies.txt -o /tmp/lp.html "http://127.0.0.1:5006/Auth/Login"
TOKEN=$(rg -o 'name="__RequestVerificationToken" type="hidden" value="[^"]+"' /tmp/lp.html | head -1 | sed 's/.*value="//;s/"$//')
CODE=$(curl -s -c /tmp/ds_cookies.txt -b /tmp/ds_cookies.txt -o /tmp/lr.html -w "%{http_code}" \
  -X POST "http://127.0.0.1:5006/Auth/Login" -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Email=dstest@drawsync.local" --data-urlencode "Password=Test1234!" \
  --data-urlencode "RememberMe=false" -L --max-redirs 5)
check "$CODE" "200" "Login redirects to org dashboard (HTTP 200)"
contains "$(rg -o '<title>[^<]+</title>' /tmp/lr.html | head -1)" "DrawSync Test Org" "Org dashboard page loaded"
AUTH_COOKIE=$(rg -c "DrawSync.Auth" /tmp/ds_cookies.txt 2>/dev/null || echo 0)
check "$AUTH_COOKIE" "1" "Auth cookie set"

echo "=== 2. Drawings listing (user-scoped, only dstestteam1's) ==="
CODE=$(curl -s -b /tmp/ds_cookies.txt -o /tmp/drawings.json -w "%{http_code}" "http://127.0.0.1:5006/api/organization/dstestteam1/drawings")
check "$CODE" "200" "GET drawings for own org (HTTP 200)"

echo "=== 3. Members listing (isCurrentMemberAdmin=true for owner) ==="
CODE=$(curl -s -b /tmp/ds_cookies.txt -o /tmp/members.json -w "%{http_code}" "http://127.0.0.1:5006/api/organization/dstestteam1/members")
check "$CODE" "200" "GET members for own org (HTTP 200)"
contains "$(cat /tmp/members.json)" "isCurrentMemberAdmin\":true" "Members API reports current user is admin (owner)"

echo "=== 4. ACCESS CONTROL: user cannot access ANOTHER org's dashboard ==="
CODE=$(curl -s -b /tmp/ds_cookies.txt -o /tmp/denied.html -w "%{http_code}" "http://127.0.0.1:5006/Organization/Details/6a036692003e62581706" -L --max-redirs 3)
# Should redirect to AccessDenied (200 on the AccessDenied page) — NOT show the other org
TITLE=$(rg -o '<title>[^<]+</title>' /tmp/denied.html 2>/dev/null | head -1)
echo "  (other-org page title: $TITLE)"
contains "$TITLE" "Access Denied" "Other org access redirected to AccessDenied page"

echo "=== 5. ACCESS CONTROL: user cannot list ANOTHER org's drawings ==="
CODE=$(curl -s -b /tmp/ds_cookies.txt -o /tmp/other_drawings.json -w "%{http_code}" "http://127.0.0.1:5006/api/organization/6a036692003e62581706/drawings")
check "$CODE" "403" "GET drawings for another org → 403 Forbidden"

echo "=== 6. ACCESS CONTROL: user cannot list ANOTHER org's members ==="
CODE=$(curl -s -b /tmp/ds_cookies.txt -o /dev/null -w "%{http_code}" "http://127.0.0.1:5006/api/organization/6a036692003e62581706/members")
check "$CODE" "403" "GET members for another org → 403 Forbidden"

echo "=== 7. Realtime debugger endpoint ==="
CODE=$(curl -s -b /tmp/ds_cookies.txt -o /tmp/rt.json -w "%{http_code}" "http://127.0.0.1:5006/api/debug/realtime")
check "$CODE" "200" "Realtime debugger endpoint (HTTP 200)"
contains "$(cat /tmp/rt.json)" "totalEvents" "Debugger returns event data"

echo "=== 8. Server log: defense-in-depth filtering ==="
contains "$(grep -c 'dropped 32' /tmp/ds_run.log)" "1" "Server log shows 32 non-member orgs dropped from listing"

echo ""
echo "==================== RESULTS ===================="
echo "  PASSED: $PASS"
echo "  FAILED: $FAIL"
echo "================================================="

# Show key log lines
echo "=== Key server log lines ==="
rg -i "ListOrgsForSession|IsCurrentUserOrgAdmin|Membership DENIED|JoinDrawing" /tmp/ds_run.log | head -10

# Cleanup
kill $SRV 2>/dev/null
exit $FAIL
