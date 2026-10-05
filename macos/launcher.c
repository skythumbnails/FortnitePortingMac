// FortnitePorting for macOS - the bundle's executable.
//
// The .NET app host sits in Contents/Resources/runtime twice: FortnitePorting (ad-hoc signed) and
// FortnitePorting-unsigned. A normal Apple Silicon Mac kills unsigned arm64 code, so it needs the signed
// one; the macOS 27 beta refuses to start CoreCLR in any signed .NET host ("Failed to create CoreCLR,
// HRESULT: 0x8007000C") and needs the unsigned one. The launcher starts each host with --probe-runtime
// (Program.Main returns straight away), signed first, and execs the first one whose probe exits 0, so the
// app keeps this process and LaunchServices sees one app. A probe that runs past PROBE_TIMEOUT_MS is killed
// and counts as a failure. If neither probe starts, it execs the signed host anyway so its real error shows.
#include <errno.h>
#include <fcntl.h>
#include <libgen.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <signal.h>
#include <spawn.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

extern char **environ;

#define PROBE_TIMEOUT_MS 10000

static int runtime_starts(const char *host)
{
    char *args[] = { (char *) host, "--probe-runtime", NULL };
    posix_spawn_file_actions_t actions;
    posix_spawn_file_actions_init(&actions);
    posix_spawn_file_actions_addopen(&actions, STDOUT_FILENO, "/dev/null", O_WRONLY, 0);
    posix_spawn_file_actions_addopen(&actions, STDERR_FILENO, "/dev/null", O_WRONLY, 0);

    pid_t pid;
    int spawned = posix_spawn(&pid, host, &actions, NULL, args, environ);
    posix_spawn_file_actions_destroy(&actions);
    if (spawned != 0) return 0;

    int status;
    uint64_t deadline = clock_gettime_nsec_np(CLOCK_MONOTONIC) + PROBE_TIMEOUT_MS * 1000000ull;
    while (clock_gettime_nsec_np(CLOCK_MONOTONIC) < deadline) {
        pid_t done = waitpid(pid, &status, WNOHANG);
        if (done == pid) return WIFEXITED(status) && WEXITSTATUS(status) == 0;
        if (done < 0 && errno == EINTR) continue;
        if (done < 0) return 0;
        usleep(20 * 1000);
    }
    kill(pid, SIGKILL);
    waitpid(pid, &status, 0);
    return 0;
}

int main(int argc, char **argv)
{
    char executable[PATH_MAX], directory[PATH_MAX], signed_host[PATH_MAX], unsigned_host[PATH_MAX];
    uint32_t size = sizeof(executable);
    if (_NSGetExecutablePath(executable, &size) != 0) return 1;
    strlcpy(directory, dirname(executable), sizeof(directory));
    snprintf(signed_host, sizeof(signed_host), "%s/../Resources/runtime/FortnitePorting", directory);
    snprintf(unsigned_host, sizeof(unsigned_host), "%s/../Resources/runtime/FortnitePorting-unsigned", directory);

    const char *host = signed_host;
    if (!runtime_starts(signed_host) && runtime_starts(unsigned_host))
        host = unsigned_host;

    argv[0] = (char *) host;
    execv(host, argv);
    perror("FortnitePorting launch");
    return 1;
}
