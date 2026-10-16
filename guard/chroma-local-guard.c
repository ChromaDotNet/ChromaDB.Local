/*
 * chroma-local-guard: starts a command as its child, and stops it when the standard input closes.
 *
 * Usage: chroma-local-guard [--grace-ms N] -- command [args...]
 *
 * The parent gives the guard a pipe as standard input and never writes to it. The pipe closes when the parent closes it, and when the
 * parent dies in any way, kill -9 included: then the guard sends SIGTERM to the command, and SIGKILL after N milliseconds (5000 by
 * default). SIGTERM, SIGINT and SIGHUP sent to the guard do the same. The guard ends with the exit code of the command, or 128 plus
 * the signal that ended it.
 *
 * The guard puts itself and the command in a new process group, so that a Ctrl+C in the terminal reaches only the parent, which
 * decides. On Linux the command also gets PR_SET_PDEATHSIG, for the case where the guard itself is killed with SIGKILL.
 *
 * Before starting the command the guard writes "chroma-local-guard: pid <pid>" on its standard error, with the pid of the command.
 *
 * Copyright (c) ChromaDotNet contributors. MIT License.
 */
#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>
#ifdef __linux__
#include <sys/prctl.h>
#endif

static int signal_pipe[2] = { -1, -1 };

static void on_signal(int sig)
{
	int saved = errno;
	unsigned char c = (unsigned char)sig;
	ssize_t ignored = write(signal_pipe[1], &c, 1);
	(void)ignored;
	errno = saved;
}

static int set_flags(int fd)
{
	int flags = fcntl(fd, F_GETFL);
	if (flags < 0 || fcntl(fd, F_SETFL, flags | O_NONBLOCK) < 0)
		return -1;
	return fcntl(fd, F_SETFD, FD_CLOEXEC);
}

static long long now_ms(void)
{
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return (long long)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
}

static int usage(void)
{
	fputs("usage: chroma-local-guard [--grace-ms N] -- command [args...]\n", stderr);
	return 2;
}

int main(int argc, char **argv)
{
	long grace_ms = 5000;
	int first = 1;
	while (first < argc && strcmp(argv[first], "--") != 0) {
		if (strcmp(argv[first], "--grace-ms") == 0 && first + 1 < argc) {
			char *end;
			errno = 0;
			grace_ms = strtol(argv[first + 1], &end, 10);
			if (errno != 0 || *end != '\0' || grace_ms < 0)
				return usage();
			first += 2;
		} else {
			return usage();
		}
	}
	if (first + 1 >= argc)
		return usage();
	char **command = argv + first + 1;

	if (pipe(signal_pipe) != 0 || set_flags(signal_pipe[0]) != 0 || set_flags(signal_pipe[1]) != 0) {
		perror("chroma-local-guard: pipe");
		return 125;
	}
	struct sigaction sa;
	memset(&sa, 0, sizeof sa);
	sa.sa_handler = on_signal;
	sigemptyset(&sa.sa_mask);
	sa.sa_flags = SA_RESTART;
	sigaction(SIGCHLD, &sa, NULL);
	sigaction(SIGTERM, &sa, NULL);
	sigaction(SIGINT, &sa, NULL);
	sigaction(SIGHUP, &sa, NULL);
	signal(SIGPIPE, SIG_IGN);

	/* Fails only when the guard already leads its group: nothing to do then. */
	(void)setpgid(0, 0);

	pid_t guard = getpid();
	pid_t child = fork();
	if (child < 0) {
		perror("chroma-local-guard: fork");
		return 125;
	}
	if (child == 0) {
#ifdef __linux__
		prctl(PR_SET_PDEATHSIG, SIGKILL);
		if (getppid() != guard)
			_exit(125);
#else
		(void)guard;
#endif
		signal(SIGCHLD, SIG_DFL);
		signal(SIGTERM, SIG_DFL);
		signal(SIGINT, SIG_DFL);
		signal(SIGHUP, SIG_DFL);
		signal(SIGPIPE, SIG_DFL);
		close(signal_pipe[0]);
		close(signal_pipe[1]);
		/* The command does not keep the pipe of the parent. */
		int null = open("/dev/null", O_RDONLY);
		if (null >= 0) {
			dup2(null, STDIN_FILENO);
			if (null != STDIN_FILENO)
				close(null);
		}
		execvp(command[0], command);
		fprintf(stderr, "chroma-local-guard: cannot start %s: %s\n", command[0], strerror(errno));
		_exit(127);
	}
	fprintf(stderr, "chroma-local-guard: pid %ld\n", (long)child);
	fflush(stderr);

	int stdin_open = 1;
	long long deadline = -1; /* set once SIGTERM is sent */
	int killed = 0;
	for (;;) {
		struct pollfd fds[2];
		nfds_t n = 0;
		fds[n].fd = signal_pipe[0];
		fds[n].events = POLLIN;
		n++;
		if (stdin_open) {
			fds[n].fd = STDIN_FILENO;
			fds[n].events = POLLIN;
			n++;
		}
		int timeout = -1;
		if (deadline >= 0 && !killed) {
			long long left = deadline - now_ms();
			timeout = left > 0 ? (int)left : 0;
		}
		int ready = poll(fds, n, timeout);
		if (ready < 0 && errno != EINTR) {
			perror("chroma-local-guard: poll");
			kill(child, SIGKILL);
		}
		int stop = 0;
		if (ready > 0 && stdin_open && fds[1].revents != 0) {
			char buf[256];
			ssize_t r = read(STDIN_FILENO, buf, sizeof buf);
			if (r == 0 || (r < 0 && errno != EAGAIN && errno != EINTR)) {
				stdin_open = 0;
				stop = 1;
			}
		}
		if (ready > 0 && fds[0].revents != 0) {
			unsigned char sigs[64];
			ssize_t r;
			while ((r = read(signal_pipe[0], sigs, sizeof sigs)) > 0) {
				for (ssize_t i = 0; i < r; i++)
					if (sigs[i] != SIGCHLD)
						stop = 1;
			}
		}
		int status;
		pid_t done = waitpid(child, &status, WNOHANG);
		if (done == child) {
			if (WIFEXITED(status))
				return WEXITSTATUS(status);
			if (WIFSIGNALED(status))
				return 128 + WTERMSIG(status);
			return 125;
		}
		if (stop && deadline < 0) {
			kill(child, SIGTERM);
			deadline = now_ms() + grace_ms;
		}
		if (deadline >= 0 && !killed && now_ms() >= deadline) {
			kill(child, SIGKILL);
			killed = 1;
		}
	}
}
