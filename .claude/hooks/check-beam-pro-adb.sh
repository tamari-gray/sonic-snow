#!/usr/bin/env bash
# SessionStart hook: report whether the XREAL Beam Pro is reachable over wireless adb.
# The Beam Pro has one USB-C port, shared between the One Pro glasses and the PC, so
# every glasses-attached build/deploy has to go over adb-over-wifi. That link drops on
# device reboot and after idle periods, and can only be re-armed from a USB connection.
set -u

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
IP_FILE="$PROJECT_DIR/.claude/beam-pro-ip.txt"

# JSON-safe: messages below use \n escapes only, never literal quotes/newlines.
emit() {
  printf '{"systemMessage":"%s","hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"%s"}}\n' "$1" "$2"
  exit 0
}

ADB=""
if command -v adb >/dev/null 2>&1; then
  ADB="$(command -v adb)"
else
  for c in "/c/Program Files/Unity/Hub/Editor"/*/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe \
           "$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"; do
    if [ -x "$c" ]; then ADB="$c"; break; fi
  done
fi
[ -n "$ADB" ] || emit "Beam Pro adb check skipped: no adb binary found." \
  "adb was not found on PATH or in the Unity Android SDK. Beam Pro wireless-adb status is unknown."

run_adb() {
  if command -v timeout >/dev/null 2>&1; then timeout 10 "$ADB" "$@" 2>/dev/null
  else "$ADB" "$@" 2>/dev/null; fi
}

scan() {
  local list; list="$(run_adb devices | tail -n +2)"
  WIRELESS="$(printf '%s\n' "$list" | grep -E '^[0-9.]+:[0-9]+[[:space:]]+device$' | awk '{print $1}' | head -1)"
  USBDEV="$(printf '%s\n' "$list"  | grep -vE '^[0-9.]+:'                          | grep -E '[[:space:]]device$' | awk '{print $1}' | head -1)"
}

scan
if [ -n "$WIRELESS" ]; then
  printf '%s\n' "$WIRELESS" > "$IP_FILE"
  emit "Beam Pro wireless adb OK ($WIRELESS)." \
    "Beam Pro wireless adb is CONNECTED at $WIRELESS. Target deploys with: adb -s $WIRELESS ..."
fi

# Not connected. Try the last-known address once before bothering the user.
if [ -s "$IP_FILE" ]; then
  LAST="$(tr -d '\r\n' < "$IP_FILE")"
  run_adb connect "$LAST" >/dev/null
  scan
  if [ -n "$WIRELESS" ]; then
    emit "Beam Pro wireless adb reconnected ($WIRELESS)." \
      "Beam Pro wireless adb was down but reconnecting to $WIRELESS succeeded. Target deploys with: adb -s $WIRELESS ..."
  fi
fi

if [ -n "$USBDEV" ]; then
  emit "Beam Pro on USB, wireless adb NOT enabled - can arm it now." \
    "Beam Pro wireless adb is NOT enabled, but the device ($USBDEV) is connected over USB right now, so it can be armed immediately.\nWork with the user on this before any glasses-attached build or test:\n1. adb -s $USBDEV shell ip addr show wlan0   (get the device IP)\n2. adb -s $USBDEV tcpip 5555\n3. adb connect <ip>:5555\n4. Ask the user to unplug the PC cable and plug the One Pro glasses back in, then confirm the wireless device still lists.\nWrite the working <ip>:5555 to .claude/beam-pro-ip.txt so later sessions can auto-reconnect."
fi

emit "Beam Pro not reachable (no USB, no wireless adb)." \
  "Beam Pro wireless adb is NOT connected and no USB device is attached either. Any glasses-attached build or deploy will fail.\nAsk the user to briefly connect the Beam Pro to the PC over USB, then arm wireless adb:\n1. adb shell ip addr show wlan0\n2. adb tcpip 5555\n3. adb connect <ip>:5555\n4. User unplugs the PC cable and reattaches the One Pro glasses.\nWrite the working <ip>:5555 to .claude/beam-pro-ip.txt. Note: adb tcpip does not survive a Beam Pro reboot, and the link also dies after ~10-30 min idle - only a fresh USB connection can revive it."
