import ctypes
import subprocess
from ctypes import wintypes

user32 = ctypes.windll.user32
EnumWindowsProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)


class RECT(ctypes.Structure):
    _fields_ = [
        ("L", ctypes.c_long),
        ("T", ctypes.c_long),
        ("R", ctypes.c_long),
        ("B", ctypes.c_long),
    ]


def main():
    pids = set()
    out = subprocess.check_output(
        'tasklist /FI "IMAGENAME eq candb.exe" /FO CSV /NH', shell=True
    ).decode(errors="ignore")
    for line in out.splitlines():
        parts = line.strip('"').split('","')
        if len(parts) >= 2:
            try:
                pids.add(int(parts[1]))
            except ValueError:
                pass
    print("pids", sorted(pids))
    results = []

    def cb(hwnd, _lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value not in pids:
            return True
        n = user32.GetWindowTextLengthW(hwnd)
        if n == 0:
            return True
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(hwnd, buf, n + 1)
        r = RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        results.append((r.R - r.L, r.B - r.T, buf.value))
        return True

    user32.EnumWindows(EnumWindowsProc(cb), 0)
    for w, h, t in sorted(results, key=lambda x: -(x[0] * x[1])):
        print("%dx%d\t%s" % (w, h, t))


if __name__ == "__main__":
    main()
