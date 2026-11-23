#!/bin/bash
# A display of our own with the GPU, for recording scenes while the user keeps working:
#   test/scenes/gpu-display.sh start [N]   headless GNOME Shell (private runtime dir) and a rootful Xwayland :N (default 7)
#   test/scenes/gpu-display.sh stop        stops both (only processes started from this runtime dir)
# Never the user's session: own XDG_RUNTIME_DIR under ~/.cache, own Wayland display name, a free X display number.
# Rootful Xwayland, not the one GNOME starts: that one sends XTEST through the remote desktop portal, ours does not.
set -e
RUN=$HOME/.cache/trt-gpu/run
LOG=$HOME/.cache/trt-gpu
N=${2:-7}

case "$1" in
start)
	if [ -e /tmp/.X11-unix/X$N ] || [ -e /tmp/.X$N-lock ]; then echo "display :$N is taken"; exit 1; fi
	mkdir -p "$RUN"; chmod 700 "$LOG" "$RUN"
	rm -rf "${RUN:?}"/*
	(
		export XDG_RUNTIME_DIR=$RUN XDG_SESSION_TYPE=wayland
		unset DISPLAY WAYLAND_DISPLAY XAUTHORITY DBUS_SESSION_BUS_ADDRESS XDG_SESSION_ID XDG_SEAT XDG_VTNR
		# The virtual monitor must be larger than the window, or Xwayland shrinks it to the work area.
		setsid nice -n 5 dbus-run-session -- gnome-shell --headless --wayland --virtual-monitor 1920x1080@60 --wayland-display trt-wl \
			> "$LOG/shell.log" 2>&1 < /dev/null &
	)
	for _ in $(seq 50); do [ -S "$RUN/trt-wl" ] && break; sleep 0.3; done
	[ -S "$RUN/trt-wl" ] || { echo "gnome-shell did not start, see $LOG/shell.log"; exit 1; }
	(
		export XDG_RUNTIME_DIR=$RUN WAYLAND_DISPLAY=trt-wl
		unset DISPLAY XAUTHORITY
		setsid Xwayland :$N -geometry 1280x720 -nolisten tcp -noreset > "$LOG/x$N.log" 2>&1 < /dev/null &
	)
	for _ in $(seq 30); do [ -S /tmp/.X11-unix/X$N ] && break; sleep 0.3; done
	[ -S /tmp/.X11-unix/X$N ] || { echo "Xwayland :$N did not start, see $LOG/x$N.log"; exit 1; }
	echo "display :$N ready (GPU)"
	;;
stop)
	for p in $(pgrep -u "$(id -u)" -f 'Xwayland :[0-9]+ -geometry 1280x720|gnome-shell --headless --wayland|dbus-run-session -- gnome-shell --headless'); do
		if grep -qz "XDG_RUNTIME_DIR=$RUN" /proc/$p/environ 2>/dev/null; then kill "$p" 2>/dev/null || true; fi
	done
	sleep 2
	# Services the shell started on its private bus, if any are left.
	for p in $(pgrep -u "$(id -u)"); do
		if grep -qz "XDG_RUNTIME_DIR=$RUN" /proc/$p/environ 2>/dev/null; then kill "$p" 2>/dev/null || true; fi
	done
	echo stopped
	;;
*)
	echo "usage: $0 start [N] | stop"; exit 2
	;;
esac
