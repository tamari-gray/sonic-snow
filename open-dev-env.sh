#!/bin/bash
# Launches the daily Sonic Snow dev setup: a cmd terminal in the project dir running
# `claude -r`, plus Chrome, the Unity Editor (opened straight into this project), and
# GitHub Desktop.
#
# Run from Git Bash: ./open-dev-env.sh
#
# Windows note: cmd.exe must be called as `cmd.exe //c` here, not `/c` — Git Bash's
# path mangling rewrites a bare `/c` into a filesystem path before cmd.exe ever sees
# it, which silently drops the flag and leaves cmd waiting on stdin instead of running
# the command.

set -e

PROJECT_DIR_WIN='C:\Users\tamar\code\sonic-snow'
UNITY_EXE='C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe'
CHROME_EXE='C:\Program Files\Google\Chrome\Application\chrome.exe'
GITHUB_DESKTOP_EXE="$LOCALAPPDATA\\GitHubDesktop\\GitHubDesktop.exe"

# New terminal window, cd'd into the project, running claude -r (resume last session).
# The empty "" is the window title arg `start` expects when the target itself takes
# quoted arguments — without it, `start` mistakes the first quoted string for the title.
cmd.exe //c start "" cmd.exe /k "cd /d ""$PROJECT_DIR_WIN"" && claude -r"

cmd.exe //c start "" "$CHROME_EXE"

cmd.exe //c start "" "$UNITY_EXE" -projectPath "$PROJECT_DIR_WIN"

cmd.exe //c start "" "$GITHUB_DESKTOP_EXE"
