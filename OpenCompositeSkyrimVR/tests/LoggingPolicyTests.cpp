#include "OpenOVR/logging.h"
#include <cstdio>
static bool detailed = false;
static int messages = 0, evaluations = 0;
bool oovr_debug_logging_enabled() { return detailed; }
void oovr_log_raw(const char*, long, const char*, const char*) { ++messages; }
void oovr_log_raw_format(const char*, long, const char*, const char*, ...) { ++messages; }
int main() {
    OOVR_DEBUG_LOG("hidden");
    OOVR_DEBUG_LOGF("hidden %d", ++evaluations);
    if (messages || evaluations) return 1;
    OOVR_LOG("basic startup/errors retained");
    if (messages != 1) return 2;
    detailed = true;
    OOVR_DEBUG_LOG("enabled");
    OOVR_DEBUG_LOGF("enabled %d", ++evaluations);
    if (messages != 3 || evaluations != 1) return 3;
    std::puts("PASS: detailed logging OFF skips calls and argument work; ON emits; basic logs retained.");
}
