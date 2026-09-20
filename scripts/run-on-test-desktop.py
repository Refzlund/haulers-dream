"""Run an existing Windows test command on a private, never-activated desktop.

No injection, input, foreground activation, or desktop switching. The command
retains its own test/exit semantics; this launcher only isolates its windows.
"""
import argparse
import ctypes as C
from ctypes import wintypes as W
import datetime
import json
import math
import msvcrt
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid


class StartupInfo(C.Structure):
    _fields_ = [("cb", W.DWORD), ("reserved", W.LPWSTR), ("desktop", W.LPWSTR),
                ("title", W.LPWSTR), ("x", W.DWORD), ("y", W.DWORD),
                ("width", W.DWORD), ("height", W.DWORD), ("cols", W.DWORD),
                ("rows", W.DWORD), ("fill", W.DWORD), ("flags", W.DWORD),
                ("show", W.WORD), ("reservedSize", W.WORD), ("reservedBytes", C.c_void_p),
                ("stdin", W.HANDLE), ("stdout", W.HANDLE), ("stderr", W.HANDLE)]


class ProcessInfo(C.Structure):
    _fields_ = [("process", W.HANDLE), ("thread", W.HANDLE),
                ("pid", W.DWORD), ("tid", W.DWORD)]


class BasicLimits(C.Structure):
    _fields_ = [("processTime", C.c_int64), ("jobTime", C.c_int64),
                ("flags", W.DWORD), ("minWorkingSet", C.c_size_t),
                ("maxWorkingSet", C.c_size_t), ("activeProcesses", W.DWORD),
                ("affinity", C.c_size_t), ("priority", W.DWORD), ("scheduling", W.DWORD)]


class IoCounters(C.Structure):
    _fields_ = [(name, C.c_uint64) for name in
                ("readOps", "writeOps", "otherOps", "readBytes", "writeBytes", "otherBytes")]


class ExtendedLimits(C.Structure):
    _fields_ = [("basic", BasicLimits), ("io", IoCounters),
                ("processMemory", C.c_size_t), ("jobMemory", C.c_size_t),
                ("peakProcessMemory", C.c_size_t), ("peakJobMemory", C.c_size_t)]


class JobAccounting(C.Structure):
    _fields_ = [(name, C.c_int64) for name in ("userTime", "kernelTime", "periodUserTime", "periodKernelTime")] + [
        (name, W.DWORD) for name in ("pageFaults", "totalProcesses", "activeProcesses", "terminatedProcesses")]


def bind(library, name, result, *arguments):
    fn = getattr(library, name)
    fn.restype, fn.argtypes = result, arguments
    return fn


def require(value):
    if not value:
        raise C.WinError(C.get_last_error())
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--cwd", required=True, type=Path)
    parser.add_argument("--timeout", type=float, default=900)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if os.name != "nt" or not command or not Path(command[0]).is_file() or not math.isfinite(args.timeout) or args.timeout <= 0:
        parser.error("Windows, an absolute existing executable, and a positive timeout are required")
    if not Path(command[0]).is_absolute():
        parser.error("Executable must be absolute")
    args.output.mkdir(parents=True, exist_ok=False)
    cwd = args.cwd.resolve(strict=True)
    user, kernel = C.WinDLL("user32", use_last_error=True), C.WinDLL("kernel32", use_last_error=True)
    create_desktop = bind(user, "CreateDesktopW", W.HANDLE, W.LPCWSTR, W.LPCWSTR, C.c_void_p, W.DWORD, W.DWORD, C.c_void_p)
    close_desktop = bind(user, "CloseDesktop", W.BOOL, W.HANDLE)
    open_input = bind(user, "OpenInputDesktop", W.HANDLE, W.DWORD, W.BOOL, W.DWORD)
    object_info = bind(user, "GetUserObjectInformationW", W.BOOL, W.HANDLE, C.c_int, C.c_void_p, W.DWORD, C.POINTER(W.DWORD))
    window_callback = C.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)
    enum_windows = bind(user, "EnumDesktopWindows", W.BOOL, W.HANDLE, window_callback, W.LPARAM)
    window_process = bind(user, "GetWindowThreadProcessId", W.DWORD, W.HWND, C.POINTER(W.DWORD))
    create_process = bind(kernel, "CreateProcessW", W.BOOL, W.LPCWSTR, W.LPWSTR, C.c_void_p, C.c_void_p, W.BOOL,
                          W.DWORD, C.c_void_p, W.LPCWSTR, C.POINTER(StartupInfo), C.POINTER(ProcessInfo))
    close = bind(kernel, "CloseHandle", W.BOOL, W.HANDLE)
    create_job = bind(kernel, "CreateJobObjectW", W.HANDLE, C.c_void_p, W.LPCWSTR)
    set_job = bind(kernel, "SetInformationJobObject", W.BOOL, W.HANDLE, C.c_int, C.c_void_p, W.DWORD)
    assign_job = bind(kernel, "AssignProcessToJobObject", W.BOOL, W.HANDLE, W.HANDLE)
    query_job = bind(kernel, "QueryInformationJobObject", W.BOOL, W.HANDLE, C.c_int, C.c_void_p, W.DWORD, C.POINTER(W.DWORD))
    stop_job = bind(kernel, "TerminateJobObject", W.BOOL, W.HANDLE, W.UINT)
    stop_process = bind(kernel, "TerminateProcess", W.BOOL, W.HANDLE, W.UINT)
    resume = bind(kernel, "ResumeThread", W.DWORD, W.HANDLE)
    wait = bind(kernel, "WaitForSingleObject", W.DWORD, W.HANDLE, W.DWORD)
    exit_code = bind(kernel, "GetExitCodeProcess", W.BOOL, W.HANDLE, C.POINTER(W.DWORD))

    def input_name():
        handle = require(open_input(0, False, 1))
        try:
            buf, needed = C.create_unicode_buffer(1024), W.DWORD()
            require(object_info(handle, 2, buf, C.sizeof(buf), C.byref(needed)))
            return buf.value
        finally:
            require(close_desktop(handle))

    def active_processes():
        accounting = JobAccounting()
        require(query_job(job, 1, C.byref(accounting), C.sizeof(accounting), None))
        return accounting.activeProcesses

    name = "HaulersDreamTests-" + uuid.uuid4().hex
    record = dict(desktop=name, command=command, cwd=str(cwd), pid=None, exitCode=None,
                  startedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  switchedDesktop=False, inputDesktopSamples=[], privateWindowPids=[], timedOut=False, error=None,
                  cleanupErrors=[], activeProcessesBeforeClose=None)
    desktop = job = None
    process, assigned, joined = ProcessInfo(), False, False
    try:
        record["inputDesktopSamples"].append(input_name())
        desktop = require(create_desktop(name, None, None, 0, 0x00FF, None))
        job = require(create_job(None, None))
        limits = ExtendedLimits()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; only our new tree.
        require(set_job(job, 9, C.byref(limits), C.sizeof(limits)))
        with open(os.devnull, "rb") as stdin, (args.output / "stdout.txt").open("xb") as stdout, (args.output / "stderr.txt").open("xb") as stderr:
            handles = [msvcrt.get_osfhandle(f.fileno()) for f in (stdin, stdout, stderr)]
            for h in handles:
                os.set_handle_inheritable(h, True)
            si = StartupInfo()
            si.cb, si.desktop = C.sizeof(si), name
            si.flags, si.show = 0x101 | 0x80, 0  # hidden, explicit std handles, no busy cursor.
            si.stdin, si.stdout, si.stderr = handles
            line = C.create_unicode_buffer(subprocess.list2cmdline(command))
            try:
                require(create_process(command[0], line, None, None, True, 0x08000004,
                                       None, str(cwd), C.byref(si), C.byref(process)))
            finally:
                for h in handles:
                    os.set_handle_inheritable(h, False)
            record["pid"] = process.pid
            require(assign_job(job, process.process))
            assigned = True
            if resume(process.thread) == 0xFFFFFFFF:
                raise C.WinError(C.get_last_error())
            print(json.dumps({"desktop": name, "pid": process.pid, "status": "running on inactive desktop"}), flush=True)
            deadline = time.monotonic() + args.timeout
            while True:
                state = wait(process.process, 500)
                if state == 0:
                    joined = True
                    break
                if state != 258:
                    raise C.WinError(C.get_last_error())
                current_input = input_name()
                if current_input not in record["inputDesktopSamples"]:
                    record["inputDesktopSamples"].append(current_input)
                if current_input == name:
                    raise RuntimeError("Private test desktop unexpectedly became the input desktop")
                @window_callback
                def observe_window(window, unused):
                    pid = W.DWORD()
                    window_process(window, C.byref(pid))
                    if pid.value and pid.value not in record["privateWindowPids"]:
                        record["privateWindowPids"].append(pid.value)
                    return True
                C.set_last_error(0)
                if not enum_windows(desktop, observe_window, 0) and C.get_last_error():
                    raise C.WinError(C.get_last_error())
                if time.monotonic() >= deadline:
                    record["timedOut"] = True
                    raise TimeoutError("Owned test command exceeded the desktop launch deadline")
            code = W.DWORD()
            require(exit_code(process.process, C.byref(code)))
            record["exitCode"] = code.value
    except BaseException as error:
        record["error"] = repr(error)
    finally:
        def cleanup(label, action):
            try:
                action()
            except BaseException as error:
                record["cleanupErrors"].append(label + ": " + repr(error))

        if process.process and not joined:
            if assigned:
                cleanup("stop owned job", lambda: require(stop_job(job, 1)))
            else:
                cleanup("stop owned suspended process", lambda: require(stop_process(process.process, 1)))
            joined = wait(process.process, 30000) == 0
        record["joined"] = joined
        if assigned:
            try:
                remaining = active_processes()
                # A signaled process handle can precede job-accounting removal.
                # Allow that asynchronous exit bookkeeping to settle, boundedly.
                settle_deadline = time.monotonic() + 2
                while remaining and time.monotonic() < settle_deadline:
                    time.sleep(0.05)
                    remaining = active_processes()
                record["activeProcessesBeforeClose"] = remaining
                if remaining:
                    record["cleanupErrors"].append("Owned command left " + str(remaining) + " active process(es); stopping the owned job")
                    require(stop_job(job, 1))
                    stop_deadline = time.monotonic() + 10
                    while active_processes() and time.monotonic() < stop_deadline:
                        time.sleep(0.1)
                    if active_processes():
                        raise RuntimeError("Owned job did not become empty after termination")
            except BaseException as error:
                record["cleanupErrors"].append("owned job accounting/termination: " + repr(error))
        # Job ownership covers only the command and its newly created descendants.
        for handle in (process.thread, process.process, job):
            if handle:
                cleanup("close process/thread/job handle", lambda h=handle: require(close(h)))
        if desktop:
            cleanup("close private desktop", lambda: require(close_desktop(desktop)))
        record["finishedUtc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        (args.output / "desktop-launch.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(record), flush=True)
    return 0 if joined and record["exitCode"] == 0 and not record["error"] and not record["cleanupErrors"] else 1


if __name__ == "__main__":
    sys.exit(main())
