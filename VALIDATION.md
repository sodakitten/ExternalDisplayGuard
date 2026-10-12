# Validation

Tested on Windows 11 x64, build 26300, using a laptop panel and one HDMI external monitor.

- Native Win32 structure layouts and topology policies pass automated checks.
- Embedded icons load at 16, 32, 48 and 256 pixels.
- External-only and independent extended desktops work, with the external monitor at the primary origin.
- External resolution and refresh rate remain 3840 × 2160 at approximately 160 Hz during the tested switches.
- Injecting an extended desktop and 175% external scaling is corrected to external-only and 200% in approximately three seconds.
- Scaling changes are respected while paused and in extended mode.
- Laptop panel scaling remains unchanged during the tested external scaling corrections.
- Updating the installed application preserves configuration and existing login startup entries.

The live verification scripts change real display settings. They are intended for an interactive local desktop with one connected external screen, and finish in external-only mode. `--self-test` only checks code and resource behavior and does not change display settings.

Physical unplug/replug, lid changes, suspend/resume, actual next login, different graphics drivers and long-term operation have not been comprehensively tested. DPI scaling uses undocumented Windows packet types and may need updates after an OS change.
