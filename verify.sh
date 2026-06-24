#!/usr/bin/env bash
# End-to-end verification of the DrawSync fixes via agent-browser.
# Uses isolated named browser sessions to avoid stale auth cookies.
set -uo pipefail
export PATH="/home/z/.dotnet:$PATH"

APIKEY="standard_38466fd4db1344d9113a949c4f36f7a5d9d7492c5a0f25584a492511c655bf89bfd279f971f11bdf54044532e7f0a81ba9e17a86c7f05a10f35538a9fd8f6e44c6fee438b7cda7debc4beaa825d45ef1ab820d3e166c65f878fce20e24172bef75a4b2fc3cce1aa7631bf81f4b2a2a082d7e12ec4a3ea627705d2a5c1eb37aa3"
EP="https://fra.cloud.appwrite.io/v1"
PROJ="68ff103a002d8b6b3e75"
TS=$(date +%s)
EMAIL="dsverify.${TS}@drawsync.local"
EMAIL2="dsverify2.${TS}@drawsync.local"
PASS="Verify1234!"
ORGNAME="Verify Org ${TS}"

# cookie jar for curl, read from a named agent-browser session
get_cookie() { agent-browser --session "$1" cookies --json 2>/dev/null | jq -r 'map(.name+"="+.value)|join("; ")' 2>/dev/null; }

# fill a form on a named session, retrying until each field holds the expected value
fill_form() {
  local sess="$1"; shift
  local sel val V
  while [ "$#" -gt 0 ]; do
    sel="$1"; val="$2"; shift 2
    for _ in 1 2 3 4; do
      agent-browser --session "$sess" fill "$sel" "$val" >/dev/null 2>&1
      V=$(agent-browser --session "$sess" get value "$sel" 2>/dev/null)
      [ "$V" = "$val" ] && break
      agent-browser --session "$sess" wait 400 >/dev/null 2>&1
    done
  done
}

echo "##### Starting .NET app on :3000 #####"
pkill -f "DrawSync.dll" 2>/dev/null || true
agent-browser --session reg close >/dev/null 2>&1 || true
agent-browser --session u1 close >/dev/null 2>&1 || true
agent-browser --session u2 close >/dev/null 2>&1 || true
agent-browser close >/dev/null 2>&1 || true
sleep 1
nohup setsid /home/z/.dotnet/dotnet /home/z/DrawSync/DrawSync/bin/Debug/net8.0/DrawSync.dll \
  --contentRoot /home/z/DrawSync/DrawSync --urls http://localhost:3000 --environment Development \
  > /home/z/my-project/dev.log 2>&1 < /dev/null &
DOTPID=$!
disown $DOTPID 2>/dev/null || true
for i in $(seq 1 30); do
  code=$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:3000/ --max-time 3)
  [ "$code" = "200" ] && { echo "  app ready after ${i}s"; break; }
  sleep 1
done
[ "$code" = "200" ] || { echo "  APP NOT READY (code=$code)"; tail -20 /home/z/my-project/dev.log; exit 1; }

echo ""
echo "##### 1. Register user1 (session: reg) #####"
agent-browser --session reg open "http://localhost:3000/Auth/Register" >/dev/null 2>&1
agent-browser --session reg wait 2500 >/dev/null 2>&1
fill_form "reg" "input#Name" "Tester ${TS}" "input#Email" "$EMAIL" "input#Password" "$PASS" "input#ConfirmPassword" "$PASS" "input#OrganizationName" "$ORGNAME"
agent-browser --session reg click "button#submitBtn" >/dev/null 2>&1
agent-browser --session reg wait 5000 >/dev/null 2>&1
echo "  after register: $(agent-browser --session reg get url 2>/dev/null)"

echo ""
echo "##### 2. Flip email verification for user1 #####"
AWUSERID=$(curl -s "$EP/users?limit=100" -H "X-Appwrite-Project: $PROJ" -H "X-Appwrite-Key: $APIKEY" --max-time 20 \
  | jq -r --arg e "$EMAIL" '.users[] | select(.email==$e) | .["$id"]' 2>/dev/null | head -1)
echo "  userId = ${AWUSERID:-<none>}"
if [ -n "$AWUSERID" ]; then
  curl -s -o /dev/null -X PATCH "$EP/users/$AWUSERID/verification" -H "X-Appwrite-Project: $PROJ" -H "X-Appwrite-Key: $APIKEY" -H "Content-Type: application/json" -d '{"emailVerification":true}' --max-time 20
  echo "  verified"
fi

echo ""
echo "##### 3. Log in as user1 (fresh session: u1) #####"
agent-browser --session u1 open "http://localhost:3000/Auth/Login" >/dev/null 2>&1
agent-browser --session u1 wait 2500 >/dev/null 2>&1
fill_form "u1" "input#Email" "$EMAIL" "input#passwordInput" "$PASS"
agent-browser --session u1 click "button#submitBtn" >/dev/null 2>&1
agent-browser --session u1 wait 6000 >/dev/null 2>&1
echo "  after login: $(agent-browser --session u1 get url 2>/dev/null)"

echo ""
echo "##### 4. FIX #1 — org list is user-scoped #####"
agent-browser --session u1 open "http://localhost:3000/Organization" >/dev/null 2>&1
agent-browser --session u1 wait 3000 >/dev/null 2>&1
ORG_COUNT=$(agent-browser --session u1 eval "document.querySelectorAll('.org-card').length" 2>/dev/null | tr -d '"')
FIRST_ORG_HREF=$(agent-browser --session u1 eval "document.querySelector('.org-card-link')?.getAttribute('href')" 2>/dev/null | tr -d '"')
echo "  /Organization org cards = $ORG_COUNT (expect 1 — only the user's own org)"
echo "  first org href = $FIRST_ORG_HREF"
ORGID=$(echo "$FIRST_ORG_HREF" | sed 's#.*/Details/##')
U1C=$(get_cookie u1)

echo ""
echo "##### 5. FIX #1 — drawings + members API (user-scoped, with admin flag) #####"
echo "  --- drawings ---"
curl -s "http://localhost:3000/api/organization/$ORGID/drawings" -b "$U1C" --max-time 20 | jq '{count: (.|length)}' 2>/dev/null
echo "  --- members ---"
curl -s "http://localhost:3000/api/organization/$ORGID/members" -b "$U1C" --max-time 20 | jq '{total, isCurrentMemberAdmin, currentUserId, members: (.memberships | map({userName, roles, isAdmin}))}' 2>/dev/null

echo ""
echo "##### 6. FIX #3 — create drawing, open board, check realtime debugger #####"
DRAW_RESP=$(curl -s -X POST "http://localhost:3000/api/organization/$ORGID/drawings" -b "$U1C" -H "Content-Type: application/json" -d '{"name":"Verify Board","type":"whiteboard"}' --max-time 20)
DRAW_ID=$(echo "$DRAW_RESP" | jq -r '."$id" // .id' 2>/dev/null)
echo "  created drawing id = $DRAW_ID"
if [ -n "$DRAW_ID" ] && [ "$DRAW_ID" != "null" ]; then
  agent-browser --session u1 open "http://localhost:3000/organization/$ORGID/whiteboard/board/$DRAW_ID" >/dev/null 2>&1
  agent-browser --session u1 wait 6000 >/dev/null 2>&1
  echo "  board url: $(agent-browser --session u1 get url 2>/dev/null)"
  echo "  rtDebugPanel display: $(agent-browser --session u1 eval "getComputedStyle(document.getElementById('rtDebugPanel')).display" 2>/dev/null | tr -d '"')"
  echo "  rtDebugLog lines: $(agent-browser --session u1 eval "document.getElementById('rtDebugLog')?.children.length || 0" 2>/dev/null | tr -d '"')"
  echo "  rtDebugStats: $(agent-browser --session u1 eval "document.getElementById('rtDebugStats')?.textContent" 2>/dev/null | tr -d '"')"
  echo "  rtDebugServerSummary: $(agent-browser --session u1 eval "document.getElementById('rtDebugServerSummary')?.textContent" 2>/dev/null | tr -d '"')"
  echo "  connection dot: $(agent-browser --session u1 eval "document.querySelector('#connectionStatus .status-dot')?.className" 2>/dev/null | tr -d '"')"
fi

echo ""
echo "##### 7. FIX #3 — server-side realtime debug log #####"
curl -s "http://localhost:3000/api/debug/realtime?count=10" -b "$U1C" --max-time 20 \
  | jq '{totalEvents, rooms: (.rooms|map({drawingId: (.drawingId[0:8]), connectionCount, org: (.organizationId[0:8])})), recent: (.events[0:8] | map({category, message, membershipOk, recipientCount}))}' 2>/dev/null

echo ""
echo "##### 8. FIX #1 enforcement — user2 (different org) must NOT see user1's drawings #####"
agent-browser --session u2 open "http://localhost:3000/Auth/Register" >/dev/null 2>&1
agent-browser --session u2 wait 2500 >/dev/null 2>&1
fill_form "u2" "input#Name" "Other User" "input#Email" "$EMAIL2" "input#Password" "$PASS" "input#ConfirmPassword" "$PASS" "input#OrganizationName" "Other Org ${TS}"
agent-browser --session u2 click "button#submitBtn" >/dev/null 2>&1
agent-browser --session u2 wait 5000 >/dev/null 2>&1
echo "  u2 after register: $(agent-browser --session u2 get url 2>/dev/null)"
AWUSERID2=$(curl -s "$EP/users?limit=100" -H "X-Appwrite-Project: $PROJ" -H "X-Appwrite-Key: $APIKEY" --max-time 20 \
  | jq -r --arg e "$EMAIL2" '.users[] | select(.email==$e) | .["$id"]' 2>/dev/null | head -1)
echo "  u2 userId = ${AWUSERID2:-<none>}"
[ -n "$AWUSERID2" ] && curl -s -o /dev/null -X PATCH "$EP/users/$AWUSERID2/verification" -H "X-Appwrite-Project: $PROJ" -H "X-Appwrite-Key: $APIKEY" -H "Content-Type: application/json" -d '{"emailVerification":true}' --max-time 20
# close u2 and reopen fresh to log in (avoid stale IsVerified=false cookie from registration)
agent-browser --session u2 close >/dev/null 2>&1
agent-browser --session u2 open "http://localhost:3000/Auth/Login" >/dev/null 2>&1
agent-browser --session u2 wait 2500 >/dev/null 2>&1
fill_form "u2" "input#Email" "$EMAIL2" "input#passwordInput" "$PASS"
agent-browser --session u2 click "button#submitBtn" >/dev/null 2>&1
agent-browser --session u2 wait 5000 >/dev/null 2>&1
echo "  u2 after login: $(agent-browser --session u2 get url 2>/dev/null)"
U2C=$(get_cookie u2)
echo "  u2 listing user1's org drawings ($ORGID):"
DRAW_CODE=$(curl -s -o /tmp/neg.json -w "%{http_code}" "http://localhost:3000/api/organization/$ORGID/drawings" -b "$U2C" --max-time 20)
echo "    HTTP $DRAW_CODE  body: $(head -c 200 /tmp/neg.json)"
echo "  u2 org list (should show ONLY u2's own org):"
agent-browser --session u2 open "http://localhost:3000/Organization" >/dev/null 2>&1
agent-browser --session u2 wait 3000 >/dev/null 2>&1
echo "    org cards = $(agent-browser --session u2 eval "document.querySelectorAll('.org-card').length" 2>/dev/null | tr -d '"')"
echo "    org names = $(agent-browser --session u2 eval "Array.from(document.querySelectorAll('.org-name')).map(e=>e.textContent).join(',')" 2>/dev/null | tr -d '"')"

echo ""
echo "##### 9. FIX #2 — Members tab UI: Remove button admin-only #####"
agent-browser --session u1 open "http://localhost:3000$FIRST_ORG_HREF" >/dev/null 2>&1
agent-browser --session u1 wait 3000 >/dev/null 2>&1
agent-browser --session u1 eval "document.querySelector('.tab-btn[data-tab=\"members\"]')?.click()" >/dev/null 2>&1
agent-browser --session u1 wait 3500 >/dev/null 2>&1
echo "  members rows = $(agent-browser --session u1 eval "document.querySelectorAll('#membersList tbody tr').length" 2>/dev/null | tr -d '"')"
echo "  remove buttons visible = $(agent-browser --session u1 eval "document.querySelectorAll('#membersList .btn-remove').length" 2>/dev/null | tr -d '"') (expect 0 — only self in org)"
echo "  invite button display = $(agent-browser --session u1 eval "document.getElementById('inviteMemberBtn').style.display" 2>/dev/null | tr -d '"') (expect flex for admin)"

echo ""
echo "##### Done. #####"
agent-browser --session reg close >/dev/null 2>&1 || true
agent-browser --session u1 close >/dev/null 2>&1 || true
agent-browser --session u2 close >/dev/null 2>&1 || true
kill $DOTPID 2>/dev/null || true
pkill -f "DrawSync.dll" 2>/dev/null || true
echo "verify.sh complete."
