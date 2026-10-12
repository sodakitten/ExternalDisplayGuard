# Validation

## v1.1.0-beta.1

Development followed the requirement not to test on the user's desktop. The installed v1.0.0 EXE, configuration, startup entries and real display modes were not changed. The new GUI, installer, updater, uninstaller and real monitor backend were not run.

Validation consists of compilation and an isolated console runner using FakeDisplayBackend. It does not load application storage, register windows, enumerate WMI, call native display APIs or change the desktop. All monitor names, paths, identities and modes in the tests are synthetic.

Coverage includes:

- Every nonempty subset for three and four monitors, with every eligible primary choice: 12 three-monitor / primary variants and 32 four-monitor / primary variants, each on desktop and laptop fixtures.
- Exact enabled sets, distinct adapter/source assignments, primary origin, per-monitor scale and no repeated mutation for a matching state.
- Missing primary, fallback monitor, reconnection, retained desired profile, unknown monitor pause, remote and active virtual-display pause.
- Gaps, overlaps, valid vertical layouts, portrait rotation, limited source capacity, multiple adapters and unused mode removal.
- Partial driver failure, incomplete driver results, failed rollback cooldown, unsupported scale and persistent warning state.
- Preview confirmation, timeout, cancellation, late confirmation, unexpected deactivation, offline-only selections and save failure recovery.
- Safe first install, legacy migration, disabled-state preservation, configuration round trips and isolated profile copies.

Run `test-offline.ps1` for a JSON report in the ignored `evidence` folder. Real three/four-monitor behavior, native path validation, drivers, docking stations, UI layout, per-monitor DPI, hotplug, next login, suspend/resume and long-term operation remain unverified.

## Historical v1.0.0

### Later portable packaging

The portable storage switch was compiled with warnings treated as errors; the new application was not launched. At the user's later request, the old installation was backed up, stopped and removed along with its login startup value and known shortcuts, using the current uninstaller's display-preserving behavior. Eight other login startup values were checked unchanged. The old configuration, including its 200% scale preference, was copied to the portable directory. No native monitor tests or display-mode switches were performed as part of this migration.

The previous single-external / laptop-two-display version had actual local desktop checks on Windows 11 build 26300. Those historical results do not validate the new multi-monitor backend or new UI.
