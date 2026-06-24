#!/usr/bin/env bash
# Bootstraps a verified DrawSync test user + organization so we can exercise the
# full login → org → drawings → realtime flow via agent-browser.
# Uses the Appwrite REST API with the server API key (user-secrets).
set -euo pipefail

APIKEY="standard_38466fd4db1344d9113a949c4f36f7a5d9d7492c5a0f25584a492511c655bf89bfd279f971f11bdf54044532e7f0a81ba9e17a86c7f05a10f35538a9fd8f6e44c6fee438b7cda7debc4beaa825d45ef1ab820d3e166c65f878fce20e24172bef75a4b2fc3cce1aa7631bf81f4b2a2a082d7e12ec4a3ea627705d2a5c1eb37aa3"
EP="https://fra.cloud.appwrite.io/v1"
PROJ="68ff103a002d8b6b3e75"
DB="68ff10d0000ba6bf6575"

USERID="dstestuser1"
EMAIL="dstest@drawsync.local"
PASSWORD="Test1234!"
NAME="DrawSync Tester"
TEAMID="dstestteam1"
ORGNAME="DrawSync Test Org"

h=(-H "X-Appwrite-Project: $PROJ" -H "X-Appwrite-Key: $APIKEY" -H "Content-Type: application/json")

echo ">>> 1. Create/ensure Appwrite user $USERID"
code=$(curl -s -o /tmp/u.json -w "%{http_code}" -X POST "$EP/users" "${h[@]}" \
  -d "{\"userId\":\"$USERID\",\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"name\":\"$NAME\"}" --max-time 20)
echo "  create user -> HTTP $code"
if [ "$code" = "409" ]; then
  echo "  user exists; updating password + name"
  curl -s -o /dev/null -X PUT "$EP/users/$USERID" "${h[@]}" \
    -d "{\"password\":\"$PASSWORD\",\"name\":\"$NAME\"}" --max-time 20
fi

echo ">>> 2. Mark email verified"
curl -s -o /dev/null -X PATCH "$EP/users/$USERID/verification" "${h[@]}" \
  -d '{"emailVerification":true}' --max-time 20
echo "  done"

echo ">>> 3. Create/ensure team $TEAMID"
code=$(curl -s -o /tmp/t.json -w "%{http_code}" -X POST "$EP/teams" "${h[@]}" \
  -d "{\"teamId\":\"$TEAMID\",\"name\":\"$ORGNAME\"}" --max-time 20)
echo "  create team -> HTTP $code"

echo ">>> 4. Add user as owner of the team (membership with userId)"
# If membership already exists this may 409; ignore.
code=$(curl -s -o /tmp/m.json -w "%{http_code}" -X POST "$EP/teams/$TEAMID/memberships" "${h[@]}" \
  -d "{\"userId\":\"$USERID\",\"roles\":[\"owner\"],\"email\":\"$EMAIL\",\"name\":\"$NAME\"}" --max-time 20)
echo "  create membership -> HTTP $code"
if [ "$code" != "201" ] && [ "$code" != "200" ]; then
  echo "  (membership may already exist; listing to confirm)"
  curl -s "$EP/teams/$TEAMID/memberships" "${h[@]}" --max-time 20 | jq '.memberships[] | {userId, roles}' 2>/dev/null || true
fi

echo ">>> 5. Create/ensure users DB row"
code=$(curl -s -o /tmp/r.json -w "%{http_code}" -X POST "$EP/tablesdb/$DB/tables/users/rows" "${h[@]}" \
  -d "{\"rowId\":\"$USERID\",\"data\":{\"name\":\"$NAME\",\"email\":\"$EMAIL\"},\"permissions\":[\"read(\\\"user(\\\"$USERID\\\")\\\")\"]}" --max-time 20)
echo "  create users row -> HTTP $code"
if [ "$code" = "409" ]; then echo "  (users row already exists)"; fi

echo ">>> 6. Create/ensure organization DB row (team-scoped read)"
code=$(curl -s -o /tmp/o.json -w "%{http_code}" -X POST "$EP/tablesdb/$DB/tables/organization/rows" "${h[@]}" \
  -d "{\"rowId\":\"$TEAMID\",\"data\":{\"name\":\"$ORGNAME\",\"plan\":\"free\"},\"permissions\":[\"read(\\\"team(\\\"$TEAMID\\\")\\\")\",\"update(\\\"team(\\\"$TEAMID\\\")\\\")\",\"delete(\\\"team(\\\"$TEAMID\\\")\\\")\"]}" --max-time 20)
echo "  create organization row -> HTTP $code"
if [ "$code" = "409" ]; then echo "  (organization row already exists)"; fi

echo ">>> 7. Create/ensure usage DB row"
USAGEID="usage_$TEAMID"
code=$(curl -s -o /tmp/usg.json -w "%{http_code}" -X POST "$EP/tablesdb/$DB/tables/usage/rows" "${h[@]}" \
  -d "{\"rowId\":\"$USAGEID\",\"data\":{\"organizationId\":\"$TEAMID\",\"drawingsCount\":0,\"collaborators\":1,\"renewDate\":\"$(date -u -d '+1 month' +%Y-%m-%d)\"},\"permissions\":[\"read(\\\"team(\\\"$TEAMID\\\")\\\")\",\"update(\\\"team(\\\"$TEAMID\\\")\\\")\"]}" --max-time 20)
echo "  create usage row -> HTTP $code"
if [ "$code" = "409" ]; then echo "  (usage row already exists)"; fi

echo ""
echo "=============================================="
echo "Bootstrap complete."
echo "  Login email:    $EMAIL"
echo "  Login password: $PASSWORD"
echo "  UserId:         $USERID"
echo "  TeamId/OrgId:   $TEAMID"
echo "=============================================="
