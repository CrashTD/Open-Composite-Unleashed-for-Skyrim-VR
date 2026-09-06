#include "DrvOpenXR/DapaCapture.h"
#include "DrvOpenXR/DapaCaptureControl.h"
#include <cstdio>

static_assert(!DapaCaptureControl::Enabled, "Shipping test must use production capture policy");
void oovr_log_raw(const char*, long, const char*, const char*) {}
void oovr_log_raw_format(const char*, long, const char*, const char*, ...) {}

int main() {
    DapaCapture capture;
    capture.Request(0);
    capture.ToggleSession();
    DapaCaptureControl::toggleRequested.store(true);
    for (int i = 0; i < 10; ++i) capture.Poll();
    if (capture.Recording() || capture.Busy() || capture.ShaderReady()
        || !capture.OutputRoot().empty() || capture.NeedsPump() || capture.NeedsNext()
        || DapaCaptureControl::recording.load() || DapaCaptureControl::telemetryWanted.load()
        || DapaCaptureControl::feedback.load() != 0) {
        std::puts("FAIL: production capture entry point activated diagnostics");
        return 1;
    }
    std::puts("PASS: production requests, session toggle and polling remain inert; no output path, shader or feedback.");
    return 0;
}
