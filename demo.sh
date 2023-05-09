#!/bin/bash

dotnetSettingsFile="./src/DeviceAuthService/local.settings.json"
# device_auth_proxy="https://0rcs6z1l-7071.euw.devtunnels.ms"
device_auth_proxy="$( cat "${dotnetSettingsFile}" | jq -r '.Values."Config:VerificationUri"' )"

# aadB2CTenant="chgeuerb2cfasthack.onmicrosoft.com"
aadB2CTenant="$( cat "${dotnetSettingsFile}" | jq -r '.Values."Config:Tenant"' ).onmicrosoft.com"

api="videoapi/watch"
apiScope="https://${aadB2CTenant}/${api}"
scope="${apiScope} openid offline_access"

echo "Want to get a token from ${aadB2CTenant} for scope \"${scope}\""

echo "Calling ${device_auth_proxy}/oauth/device_authorization"
deviceResponse="$( curl \
    --silent \
    --request POST \
    --url "${device_auth_proxy}/oauth/device_authorization" \
    --header "Content-Type: application/x-www-form-urlencoded" \
    --data "scope=${scope}" | jq . )"

echo "${deviceResponse}" | jq -r '.message'
echo "$( echo "${deviceResponse}" | jq -r '.user_code' )" | iconv -f utf-8 -t utf-16le | clip.exe
cmd.exe /C "start $( echo "${deviceResponse}" | jq -r '.verification_uri' )"

device_code="$( echo "${deviceResponse}" | jq -r '.device_code' )"
sleep_duration="$(echo "${deviceResponse}" | jq -r '.interval' )"

access_token=""
while [ "${access_token}" == "" ]
do
    token_response="$( curl \
      --silent \
      --request POST \
      --url "${device_auth_proxy}/oauth/token" \
      --data "grant_type=urn:ietf:params:oauth:grant-type:device_code" \
      --data "device_code=${device_code}" )"

    if [ "$(echo "${token_response}" | jq -r ".error")" == "authorization_pending" ]; then
      echo "$(echo "${deviceResponse}" | jq -r ".message")"
      sleep "${sleep_duration}"
    else
      access_token="$(echo "${token_response}" | jq -r ".access_token")"
      echo "User authenticated"
    fi
done

echo "${token_response}" | jq .

access_token="$( echo "${token_response}" | jq -r '.access_token' )"
refresh_token="$( echo "${token_response}" | jq -r '.refresh_token' )"

echo "############################
access_token
---------------
$( echo "${access_token}" | jq -R 'split(".") | .[1] | @base64d | fromjson')
############################
refresh_token
---------------
${refresh_token}
---------------"

################
# Token refresh
################

token_response="$( curl \
  --silent \
  --request POST \
  --url "${device_auth_proxy}/oauth/token" \
  --data "grant_type=refresh_token" \
  --data "refresh_token=${refresh_token}" \
  --data "scope=${scope}" )"

access_token="$( echo "${token_response}" | jq -r '.access_token' )"
refresh_token="$( echo "${token_response}" | jq -r '.refresh_token' )"

echo "############################
access_token refreshed
---------------
$( echo "${access_token}" | jq -R 'split(".") | .[1] | @base64d | fromjson')
############################
refresh_token
---------------
${refresh_token}
---------------"