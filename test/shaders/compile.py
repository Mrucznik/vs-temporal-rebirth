#!/usr/bin/env python3
"""Compiles the mod's shaders with the real OpenGL driver of this computer, headless (EGL, no window, no display):
test/shaders/compile.py [name ...]. Includes come from the game, like in the game. Prints the driver's log; exit 1 on errors.
"""
import ctypes, os, re, sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
GAME = Path(os.environ.get("VINTAGE_STORY") or "/var/lib/flatpak/app/at.vintagestory.VintageStory/x86_64/stable/active/files/extra/vintagestory")
INCLUDES = GAME / "assets/game/shaderincludes"
EGL_PLATFORM_SURFACELESS_MESA, EGL_OPENGL_API = 0x31DD, 0x30A2
EGL_CONTEXT_MAJOR, EGL_CONTEXT_MINOR, EGL_CORE_BIT_KEY, EGL_CORE_BIT, EGL_NONE = 0x3098, 0x30FB, 0x30FD, 1, 0x3038
GL_FRAGMENT, GL_VERTEX, GL_COMPILE_STATUS, GL_LINK_STATUS, GL_RENDERER = 0x8B30, 0x8B31, 0x8B81, 0x8B82, 0x1F01


def source(path):
    text = path.read_text(encoding="utf8")
    return re.sub(r"^#include\s+(\S+)", lambda m: (INCLUDES / m[1]).read_text(encoding="utf8"), text, flags=re.M)


def main():
    egl = ctypes.CDLL("libEGL.so.1")
    egl.eglGetPlatformDisplay.restype = ctypes.c_void_p
    egl.eglGetPlatformDisplay.argtypes = [ctypes.c_uint, ctypes.c_void_p, ctypes.c_void_p]
    display = egl.eglGetPlatformDisplay(EGL_PLATFORM_SURFACELESS_MESA, None, None)
    egl.eglInitialize.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
    if not egl.eglInitialize(display, None, None):
        sys.exit("no EGL")
    egl.eglBindAPI(EGL_OPENGL_API)
    egl.eglCreateContext.restype = ctypes.c_void_p
    egl.eglCreateContext.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
    attrs = (ctypes.c_int * 7)(EGL_CONTEXT_MAJOR, 3, EGL_CONTEXT_MINOR, 3, EGL_CORE_BIT_KEY, EGL_CORE_BIT, EGL_NONE)
    context = egl.eglCreateContext(display, None, None, attrs)
    egl.eglMakeCurrent.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
    if not context or not egl.eglMakeCurrent(display, None, None, context):
        sys.exit("no OpenGL 3.3 context")
    egl.eglGetProcAddress.restype = ctypes.c_void_p

    def fn(name, res, *args):
        return ctypes.CFUNCTYPE(res, *args)(egl.eglGetProcAddress(name.encode()))

    getString = fn("glGetString", ctypes.c_char_p, ctypes.c_uint)
    create = fn("glCreateShader", ctypes.c_uint, ctypes.c_uint)
    setSource = fn("glShaderSource", None, ctypes.c_uint, ctypes.c_int, ctypes.POINTER(ctypes.c_char_p), ctypes.c_void_p)
    compile_ = fn("glCompileShader", None, ctypes.c_uint)
    getShaderiv = fn("glGetShaderiv", None, ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_int))
    getLog = fn("glGetShaderInfoLog", None, ctypes.c_uint, ctypes.c_int, ctypes.c_void_p, ctypes.c_char_p)
    program = fn("glCreateProgram", ctypes.c_uint)
    attach = fn("glAttachShader", None, ctypes.c_uint, ctypes.c_uint)
    link = fn("glLinkProgram", None, ctypes.c_uint)
    getProgramiv = fn("glGetProgramiv", None, ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_int))
    getProgramLog = fn("glGetProgramInfoLog", None, ctypes.c_uint, ctypes.c_int, ctypes.c_void_p, ctypes.c_char_p)
    print("driver:", getString(GL_RENDERER).decode())

    folder = REPO / "assets/temporalrebirth/shaders"
    names = sys.argv[1:] or sorted({p.stem for p in folder.glob("*.fsh")})
    failed = False
    for name in names:
        prog = program()
        for ext, kind in (("vsh", GL_VERTEX), ("fsh", GL_FRAGMENT)):
            shader = create(kind)
            text = ctypes.c_char_p(source(folder / f"{name}.{ext}").encode())
            setSource(shader, 1, ctypes.byref(text), None)
            compile_(shader)
            ok, log = ctypes.c_int(), ctypes.create_string_buffer(8192)
            getShaderiv(shader, GL_COMPILE_STATUS, ctypes.byref(ok))
            getLog(shader, 8192, None, log)
            print(f"{name}.{ext}: {'ok' if ok.value else 'FAILED'} {log.value.decode().strip()}")
            failed |= not ok.value
            attach(prog, shader)
        link(prog)
        ok, log = ctypes.c_int(), ctypes.create_string_buffer(8192)
        getProgramiv(prog, GL_LINK_STATUS, ctypes.byref(ok))
        getProgramLog(prog, 8192, None, log)
        print(f"{name}: link {'ok' if ok.value else 'FAILED'} {log.value.decode().strip()}")
        failed |= not ok.value
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
