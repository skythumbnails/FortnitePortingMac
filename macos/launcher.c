// FortnitePorting for macOS - the bundle's executable.
//
// The .NET app host sits in Contents/Resources/runtime twice: FortnitePorting (ad-hoc signed) and
// FortnitePorting-unsigned. A normal Apple Silicon Mac kills unsigned arm64 code, so it needs the signed
// one; the macOS 27 beta refuses to start CoreCLR in any signed .NET host ("Failed to create CoreCLR,
// HRESULT: 0x8007000C") and needs the unsigned one. The launcher starts the signed host once with
// --probe-runtime (Program.Main returns straight away) and execs whichever host can run here, so the
// app keeps this process and LaunchServices sees one app.
#include <fcntl.h>
#include <libgen.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <spawn.h>
#include <stdio.h>
#include <string.h>
#include <sys/wait.h>
#include <unistd.h>

extern char **environ;

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
    if (waitpid(pid, &status, 0) < 0) return 0;
    return WIFEXITED(status) && WEXITSTATUS(status) == 0;
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
    if (!runtime_starts(signed_host) && access(unsigned_host, X_OK) == 0)
        host = unsigned_host;

    argv[0] = (char *) host;
    execv(host, argv);
    perror("FortnitePorting launch");
    return 1;
}
