# Changelog

## v1.1.0-beta.1

- Support portable mode with a `portable.flag` next to the EXE; keep configuration and logs in the application directory without automatic installation or startup registration.
- Select any nonempty subset of connected physical monitors, on desktops or laptops.
- Choose any selected display as primary, with independent scaling, rotation and saved layout.
- Add named presets, tray shortcuts, a 15-second preview confirmation, and automatic rollback.
- Handle missing selected monitors and preferred-primary reconnection without overwriting the desired profile.
- Pause on unknown new monitors, remote sessions and active virtual displays.
- Migrate v1 configuration and preserve the original selected monitor and scale.
- Verify with an isolated fake backend; no real display or UI tests were run for this beta.
- Leave the previously installed stable application untouched during development.

## v1.0.0

- Keep one selected physical external monitor active, or use an extended laptop/external desktop with the external monitor as primary.
- Restore external-only scaling to a configurable value, defaulting to 200%.
- Pause, disable, restore the laptop panel, and enable login startup from the tray/settings window.
- Add a custom monitor-and-shield icon to the executable, settings window, tray, and installed shortcuts.
- Embed seven icon sizes from 16 to 256 pixels, with DPI-aware tray size selection.
- Include a portable Windows x64 executable, installation/update/uninstallation scripts, and diagnostics.
