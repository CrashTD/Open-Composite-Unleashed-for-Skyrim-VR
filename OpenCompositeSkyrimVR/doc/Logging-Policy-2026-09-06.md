# Diagnostic logging

SteamVR / Help has a themed **Turn on logging** checkbox. It writes the existing
`logLevel=debug` (checked) or `logLevel=normal` (unchecked/default). Save and restart
Skyrim. Basic startup, warnings and errors remain; this is not an all-logs-off
switch. It does not control CSX, other mods, DAPA recording, or the separate NGX
verbose setting. An explicit app-specific opencomposite_ext.ini override still
takes precedence, as it does for other OCU settings.

The SKSE plugin queries OCU_DebugLoggingEnabled at load/data initialization and
uses that resolved runtime policy instead of guessing INI location/precedence.
Older runtimes lacking the export leave SKSE diagnostics off. The bridge memory
layout is unchanged. Both the updated runtime and SKSE plugin are needed for
the checkbox to control the newly gated SKSE reports.

Removed six redundant per-parameter config echoes. Moved recurring body-mask
counters, ownership reports, menu/shared-memory messages, DAPA status and stage
latency reports behind detailed logging. Player-mask publishing no longer queries
the clock per successful draw with diagnostics off. Renderer shadow-state probing
on save/new-game load also requires diagnostics. DAPA scheduling measurements,
render math and ownership updates are unchanged.

Runtime log writes are serialized and buffered, with at most one timed flush per
second on subsequent messages; fatal errors explicitly flush. DLL shutdown uses
a non-blocking lock attempt to avoid waiting on a stopped thread. SKSE no longer
flushes on each informational report; warnings and errors still flush immediately.
Abrupt termination can lose the most recent buffered informational messages.

Validation: runtime/SKSE builds; themed checkbox rendering, five INI layouts and
on/off round trips; config cleanup/foveation persistence; diagnostic macro
argument suppression; shipping recording disabled; DAPA motion and refresh
timing; HIGGS, Spell Wheel, engine draw and installed-CSX GPU mask regressions.
No new headset performance measurement is claimed.
