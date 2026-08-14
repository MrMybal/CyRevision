#include <limits.h>
#include <mach-o/dyld.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char *argv[])
{
    (void)argc;

    char launcher_path[PATH_MAX];
    uint32_t launcher_path_size = sizeof(launcher_path);
    if (_NSGetExecutablePath(launcher_path, &launcher_path_size) != 0)
    {
        fprintf(stderr, "CyRevision launcher path is too long.\n");
        return 126;
    }

    char resolved_launcher_path[PATH_MAX];
    if (realpath(launcher_path, resolved_launcher_path) == NULL)
    {
        perror("CyRevision could not resolve its launcher path");
        return 126;
    }

    char *last_separator = strrchr(resolved_launcher_path, '/');
    if (last_separator == NULL)
    {
        fprintf(stderr, "CyRevision launcher has an invalid path.\n");
        return 126;
    }
    *last_separator = '\0';

    char application_path[PATH_MAX];
    int written = snprintf(
        application_path,
        sizeof(application_path),
        "%s/../Resources/app/CyRevision.Desktop",
        resolved_launcher_path);
    if (written < 0 || (size_t)written >= sizeof(application_path))
    {
        fprintf(stderr, "CyRevision application path is too long.\n");
        return 126;
    }

    argv[0] = application_path;
    execv(application_path, argv);
    perror("CyRevision could not start its packaged application");
    return 126;
}
