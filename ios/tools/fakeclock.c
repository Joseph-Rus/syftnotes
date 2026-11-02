// The App Store's pictures only (tools/app-store-shots.sh): loaded into the app's process in the simulator, it moves
// the clock the app sees by FAKE_CLOCK_OFFSET seconds, so the sample classes' times line up with the status bar's 9:41.
// Never built into the app.
#include <stdlib.h>
#include <stdint.h>
#include <time.h>
#include <sys/time.h>

static long long fake_offset(void) {
    static int ready = 0; static long long o = 0;
    if (!ready) { const char *s = getenv("FAKE_CLOCK_OFFSET"); o = s ? atoll(s) : 0; ready = 1; }
    return o;
}
static int my_clock_gettime(clockid_t id, struct timespec *tp) {
    int r = clock_gettime(id, tp);
    if (r == 0 && tp && id == CLOCK_REALTIME) tp->tv_sec += fake_offset();
    return r;
}
static uint64_t my_clock_gettime_nsec_np(clockid_t id) {
    uint64_t v = clock_gettime_nsec_np(id);
    if (id == CLOCK_REALTIME && v) v += (uint64_t)(fake_offset() * 1000000000LL);
    return v;
}
static int my_gettimeofday(struct timeval *tv, void *tz) {
    int r = gettimeofday(tv, tz);
    if (r == 0 && tv) tv->tv_sec += fake_offset();
    return r;
}
static time_t my_time(time_t *t) {
    time_t v = time(NULL) + fake_offset();
    if (t) *t = v;
    return v;
}
__attribute__((used)) static struct { const void *replacement; const void *replacee; } interposers[]
    __attribute__((section("__DATA,__interpose"))) = {
    { (const void *)my_clock_gettime, (const void *)clock_gettime },
    { (const void *)my_clock_gettime_nsec_np, (const void *)clock_gettime_nsec_np },
    { (const void *)my_gettimeofday, (const void *)gettimeofday },
    { (const void *)my_time, (const void *)time },
};
