#!/bin/sh
# ---------------------------------------------------------------------------
# DIGtv NAS cleanup  —  Synology DSM Task Scheduler friendly (POSIX sh)
#
# WHY: Plugin builds <= 1.1.2.0 wrote data to plugins/DIGtv. Jellyfin scans every
# top-level folder in plugins/ and mistook that "DIGtv" data folder for a phantom
# (newest) plugin, deleting the REAL DIGtv_x.y.z folder on every restart. Build
# 1.1.3.0 fixes the code (data now lives in plugins/configurations/DIGtv, which is
# not scanned), but the pre-existing phantom plugins/DIGtv folder must be removed
# ONCE — the plugin can't delete it itself. This script does that (and clears any
# stale DIGtv_* version folders), KEEPING your channel config xml.
# Run this ONCE, then install DIGtv 1.1.3.0 (or newer) and restart — it will stick.
#
# HOW TO RUN (Synology, NO SSH NEEDED):
#   DSM > Control Panel > Task Scheduler > Create > Scheduled Task > User-defined script
#   General tab : set User = root  (required for synopkg + deleting under @appdata)
#   Task Settings tab : paste this ENTIRE script into the "Run command" box.
#   Save, select the task, click "Run" once. Then install DIGtv 1.0.2.0 from
#   Dashboard > Plugins > Catalog and restart Jellyfin one more time.
#   (The "Run command" box accepts a full multi-line script — no file required.)
#
# Safe to re-run. Set DRY_RUN=1 to preview without deleting.
# ---------------------------------------------------------------------------
set -u
DRY_RUN=0          # 1 = show what would happen, delete nothing
KEEP_CONFIG=1      # 1 = preserve channel config (configurations/*.xml)

log() { echo "[DIGtv-cleanup] $*"; }

# 1) Locate the Jellyfin plugins directory (confirmed default first).
PLUGINS_DIR=""
for c in \
  /volume1/@appdata/jellyfin/data/plugins \
  /volume1/@appdata/Jellyfin/data/plugins \
  /volume2/@appdata/jellyfin/data/plugins \
  /volume1/@appstore/jellyfin/var/plugins ; do
  [ -d "$c" ] && PLUGINS_DIR="$c" && break
done
if [ -z "$PLUGINS_DIR" ]; then
  log "ERROR: could not find the Jellyfin plugins dir. Edit PLUGINS_DIR in this script."
  exit 1
fi
log "Plugins dir : $PLUGINS_DIR"

# 2) Find the Jellyfin package name for synopkg stop/start.
PKG="$(synopkg list 2>/dev/null | awk '{print $1}' | grep -i '^jellyfin' | head -n1)"
[ -z "$PKG" ] && PKG="jellyfin"
log "Package     : $PKG"
log "Dry run     : $DRY_RUN"

# 3) Stop Jellyfin so plugin files aren't locked.
if [ "$DRY_RUN" -eq 0 ]; then
  log "Stopping $PKG ..."
  synopkg stop "$PKG" >/dev/null 2>&1
  sleep 8
fi

# 4) Remove stale DIGtv plugin folders (DIGtv, DIGtv_1.0.0.0, DIGtv_1.0.1.0, ...).
FOUND=0
for d in "$PLUGINS_DIR"/DIGtv "$PLUGINS_DIR"/DIGtv_*; do
  [ -d "$d" ] || continue
  FOUND=1
  if [ "$DRY_RUN" -eq 1 ]; then
    log "WOULD remove: $d"
  else
    log "Removing: $d"
    rm -rf "$d"
  fi
done
[ "$FOUND" -eq 0 ] && log "No DIGtv plugin folders found (already clean)."

# 5) Channel config (kept by default so your channels survive the reinstall).
CFG="$PLUGINS_DIR/configurations/Jellyfin.Plugin.DigTv.xml"
if [ -f "$CFG" ]; then
  if [ "$KEEP_CONFIG" -eq 1 ]; then
    log "Preserved channel config: $CFG"
  elif [ "$DRY_RUN" -eq 0 ]; then
    log "Removing channel config: $CFG"
    rm -f "$CFG"
  fi
fi

# 6) Start Jellyfin again.
if [ "$DRY_RUN" -eq 0 ]; then
  log "Starting $PKG ..."
  synopkg start "$PKG" >/dev/null 2>&1
fi

log "Done. Next: Dashboard > Plugins > Catalog > install 'DIGtv' 1.0.2.0, then restart Jellyfin once."
