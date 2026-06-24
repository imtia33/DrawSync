#!/usr/bin/env bash
# Slim end-to-end verification of the three DrawSync fixes.
set -uo pipefail
export PATH="/home/z/.dotnet:$PATH"
APIKEY="standard_38466fd4db1344d9113a949c4f36f7a5d9d7492c5a0f25584a492511c655bf89bfd279f971f11bdf54044532e7f0a81ba9e17a86c7f05a10f35538a9fd8f6e44c6fee438b7cda7debc4beaa825d45ef1ab820d3e166c65f878fce20e24172bef75a4b2fc3cce1aa7631bf81f4b2a2a082d7e12ec4a3ea627705d2a5c1eb37aa3"
EP="https://fra.cloud.appwrite.io/v1"; PROJ="68ff103a002d8b6b3e75"
TS=$(date +%s); EMAIL="ds.${TS}@drawsync.local"; PASS="Verify1234!"; ORG="Org ${TS}"
ck() { agent-browser --session "$1" cookies --json 2>/dev/null | jq -r 'map(.name+"="+.value)|join("; ")' 2>/dev/null; }
# fill a field on a named session with retry until get value matches
ffill() { local s="$1" sel="$2" val="$3" V; for _ in 1 2 3 4 5; do agent-browser --session "$s" fill "$sel" "$val" >/dev/null 2>&1; V=$(agent-browser --session "$s" get value "$sel" 2>/dev/null); [ "$V" = "$val" ] && break; sleep 0.3; done; }

echo "##### start app #####"
pkill -f "DrawSync.dll" 2>/dev/null; agent-browser --session r close 2>/dev/null; agent-browser --session u close 2>/dev/null; agent-browser close 2>/dev/null; sleep 1
nohup setsid /home/z/.dotnet/dotnet /home/z/DrawSync/DrawSync/bin/Debug/net8.0/DrawSync.dll --contentRoot /home/z/DrawSync/DrawSync --urls http://localhost:3000 --environment Development > /home/z/my-project/dev.log 2>&1 < /dev/null &
disown 2>/dev/null || true
for i in $(seq 1 30); do c=$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:3000/ --max-time 3); [ "$c" = "200" ] && break; sleep 1; done
echo "  app: $c"

echo "##### register #####"
agent-browser --session r open "http://localhost:3000/Auth/Register" >/dev/null 2>&1; agent-browser --session r wait 3000 >/dev/null 2>&1
ffill r "input#Name" "Tester"
ffill r "input#Email" "$EMAIL"
ffill r "input#Password" "$PASS"
ffill r "input#ConfirmPassword" "$PASS"
ffill r "input#OrganizationName" "$ORG"
agent-browser --session r click "button#submitBtn" >/dev/null 2>&1; agent-browser --session r wait 4000 >/dev/null 2>&1
echo "  reg url: $(agent-browser --session r get url 2>/dev/null)"

echo "##### verify email #####"
UID1=$(curl -s "$EP/users?limit=100" -H "X-Appwrite-Project: $PROJ" -H "X-Appwrite-Key: $APIKEY" --max-time 20 | jq -r --arg e "$EMAIL" '.users[] | select(.email==$e) | .["$id"]' 2>/dev/null | head -1)
echo "  uid=$UID1"
curl -s -o /dev/null -X PATCH "$EP/users/$UID1/verification" -H "X-Appwrite-Project: $PROJ" -H "X-Appwrite-Key: $APIKEY" -H "Content-Type: application/json" -d '{"emailVerification":true}' --max-time 20

echo "##### login (fresh session u) #####"
agent-browser --session u open "http://localhost:3000/Auth/Login" >/dev/null 2>&1; agent-browser --session u wait 3000 >/dev/null 2>&1
ffill u "input#Email" "$EMAIL"
ffill u "input#passwordInput" "$PASS"
agent-browser --session u click "button#submitBtn" >/dev/null 2>&1; agent-browser --session u wait 5000 >/dev/null 2>&1
echo "  login url: $(agent-browser --session u get url 2>/dev/null)"

echo "##### FIX#1: org list (expect 1) #####"
agent-browser --session u open "http://localhost:3000/Organization" >/dev/null 2>&1; agent-browser --session u wait 2500 >/dev/null 2>&1
echo "  org cards: $(agent-browser --session u eval 'document.querySelectorAll(".org-card").length' 2>/dev/null)"
HREF=$(agent-browser --session u eval 'document.querySelector(".org-card-link")?.getAttribute("href")' 2>/dev/null)
echo "  first org href: $HREF"
ORGID=$(echo "$HREF" | sed 's#.*/Details/##')
UC=$(ck u)

echo "##### FIX#1: drawings + members API #####"
echo "  drawings: $(curl -s "http://localhost:3000/api/organization/$ORGID/drawings" -b "$UC" --max-time 20 | jq '{count:(.|length)}' 2>/dev/null)"
echo "  members: $(curl -s "http://localhost:3000/api/organization/$ORGID/members" -b "$UC" --max-time 20 | jq '{total,isCurrentMemberAdmin,members:(.memberships|map({userName,roles,isAdmin}))}' 2>/dev/null)"

echo "##### FIX#3: create drawing + open board + debugger #####"
DR=$(curl -s -X POST "http://localhost:3000/api/organization/$ORGID/drawings" -b "$UC" -H "Content-Type: application/json" -d '{"name":"VBoard","type":"whiteboard"}' --max-time 20)
DID=$(echo "$DR" | jq -r '."$id" // .id' 2>/dev/null)
echo "  drawing id: $DID"
agent-browser --session u open "http://localhost:3000/organization/$ORGID/whiteboard/board/$DID" >/dev/null 2>&1; agent-browser --session u wait 5000 >/dev/null 2>&1
echo "  board url: $(agent-browser --session u get url 2>/dev/null)"
echo "  panel display: $(agent-browser --session u eval 'getComputedStyle(document.getElementById("rtDebugPanel")).display' 2>/dev/null)"
echo "  log lines: $(agent-browser --session u eval 'document.getElementById("rtDebugLog")?.children.length' 2>/dev/null)"
echo "  stats: $(agent-browser --session u eval 'document.getElementById("rtDebugStats")?.textContent' 2>/dev/null)"
echo "  server summary: $(agent-browser --session u eval 'document.getElementById("rtDebugServerSummary")?.textContent' 2>/dev/null)"
echo "  conn dot: $(agent-browser --session u eval 'document.querySelector("#connectionStatus .status-dot")?.className' 2>/dev/null)"

echo "##### FIX#3: server debug log #####"
curl -s "http://localhost:3000/api/debug/realtime?count=8" -b "$UC" --max-time 20 | jq '{totalEvents,rooms:(.rooms|map({drawingId:(.drawingId[0:8]),connectionCount,org:(.organizationId[0:8])})),recent:(.events[0:6]|map({category,message,membershipOk,recipientCount}))}' 2>/dev/null

echo "##### FIX#2: members tab UI #####"
agent-browser --session u open "http://localhost:3000$HREF" >/dev/null 2>&1; agent-browser --session u wait 2500 >/dev/null 2>&1
agent-browser --session u eval 'document.querySelector(".tab-btn[data-tab=members]")?.click()' >/dev/null 2>&1
agent-browser --session u wait 3000 >/dev/null 2>&1
echo "  rows: $(agent-browser --session u eval 'document.querySelectorAll("#membersList tbody tr").length' 2>/dev/null)"
echo "  remove btns: $(agent-browser --session u eval 'document.querySelectorAll("#membersList .btn-remove").length' 2>/dev/null)"
echo "  invite display: $(agent-browser --session u eval 'document.getElementById("inviteMemberBtn").style.display' 2>/dev/null)"

echo "##### done #####"
agent-browser --session r close 2>/dev/null; agent-browser --session u close 2>/dev/null
kill $(pgrep -f "DrawSync.dll") 2>/dev/null
echo "slim-verify complete."
