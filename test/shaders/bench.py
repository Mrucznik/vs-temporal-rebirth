#!/usr/bin/env python3
"""Measures what the barrier (leash.fsh) costs the GPU, headless: test/shaders/bench.py [width height].

Draws the barrier full screen into an offscreen picture many times with the real driver of this computer, in a few
situations (seen from afar, touching it, pushing into it), and prints milliseconds per frame for each. SHADER_DIR picks
another folder of shaders, so two versions can be compared; OUT=<folder> also saves the last frame of each as a PNG, to see
that a faster version still looks the same. Only the barrier's own cost, not the game's.
"""
import ctypes, math, os, sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from compile import source, REPO

EGL_PLATFORM_SURFACELESS_MESA, EGL_OPENGL_API = 0x31DD, 0x30A2
EGL_CONTEXT_MAJOR, EGL_CONTEXT_MINOR, EGL_CORE_BIT_KEY, EGL_CORE_BIT, EGL_NONE = 0x3098, 0x30FB, 0x30FD, 1, 0x3038
GL = dict(FRAGMENT=0x8B30, VERTEX=0x8B31, COMPILE_STATUS=0x8B81, LINK_STATUS=0x8B82, RENDERER=0x1F01, TEXTURE_2D=0x0DE1,
          RGBA8=0x8058, RGBA=0x1908, UNSIGNED_BYTE=0x1401, FRAMEBUFFER=0x8D40, COLOR_ATTACHMENT0=0x8CE0, ARRAY_BUFFER=0x8892,
          STATIC_DRAW=0x88E4, FLOAT=0x1406, TRIANGLE_STRIP=0x0005, TEXTURE0=0x84C0, R32F=0x822E, RED=0x1903,
          MIN_FILTER=0x2801, MAG_FILTER=0x2800, LINEAR=0x2601, NEAREST=0x2600, BLEND=0x0BE2, SRC_ALPHA=0x0302,
          ONE_MINUS_SRC_ALPHA=0x0303)

RADIUS = 24.0
# Name, distance of the eyes from the wall, seconds since the ghost last pushed (negative: not pushing).
CASES = [("from afar (15 blocks)", 15.0, -1.0), ("close (3 blocks)", 3.0, -1.0), ("touching", 0.05, -1.0),
         ("pushing into it", 0.05, 0.1)]


def gl_context():
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
    egl.eglMakeCurrent.argtypes = [ctypes.c_void_p] * 4
    if not context or not egl.eglMakeCurrent(display, None, None, context):
        sys.exit("no OpenGL 3.3 context")
    egl.eglGetProcAddress.restype = ctypes.c_void_p

    class G:
        def __getattr__(self, name):
            raise AttributeError(name)

    g = G()
    u, i, f, p = ctypes.c_uint, ctypes.c_int, ctypes.c_float, ctypes.c_void_p
    for name, res, *args in [
        ("glGetString", ctypes.c_char_p, u), ("glCreateShader", u, u),
        ("glShaderSource", None, u, i, ctypes.POINTER(ctypes.c_char_p), p), ("glCompileShader", None, u),
        ("glGetShaderiv", None, u, u, ctypes.POINTER(i)), ("glGetShaderInfoLog", None, u, i, p, ctypes.c_char_p),
        ("glCreateProgram", u), ("glAttachShader", None, u, u), ("glLinkProgram", None, u),
        ("glGetProgramiv", None, u, u, ctypes.POINTER(i)), ("glGetProgramInfoLog", None, u, i, p, ctypes.c_char_p),
        ("glUseProgram", None, u), ("glGetUniformLocation", i, u, ctypes.c_char_p),
        ("glUniform1f", None, i, f), ("glUniform3f", None, i, f, f, f), ("glUniform4f", None, i, f, f, f, f),
        ("glUniform1i", None, i, i), ("glUniformMatrix4fv", None, i, i, u, ctypes.POINTER(f)),
        ("glGenTextures", None, i, ctypes.POINTER(u)), ("glBindTexture", None, u, u), ("glActiveTexture", None, u),
        ("glTexImage2D", None, u, i, i, i, i, i, u, u, p), ("glTexParameteri", None, u, u, i),
        ("glGenFramebuffers", None, i, ctypes.POINTER(u)), ("glBindFramebuffer", None, u, u),
        ("glFramebufferTexture2D", None, u, u, u, u, i), ("glViewport", None, i, i, i, i),
        ("glGenVertexArrays", None, i, ctypes.POINTER(u)), ("glBindVertexArray", None, u),
        ("glGenBuffers", None, i, ctypes.POINTER(u)), ("glBindBuffer", None, u, u),
        ("glBufferData", None, u, ctypes.c_ssize_t, p, u), ("glVertexAttribPointer", None, u, i, u, ctypes.c_ubyte, i, p),
        ("glEnableVertexAttribArray", None, u), ("glDrawArrays", None, u, i, i), ("glFinish", None),
        ("glEnable", None, u), ("glBlendFunc", None, u, u), ("glReadPixels", None, i, i, i, i, u, u, p),
    ]:
        setattr(g, name[2:], ctypes.CFUNCTYPE(res, *args)(egl.eglGetProcAddress(name.encode())))
    return g


def build(g, folder):
    prog = g.CreateProgram()
    for ext, kind in (("vsh", GL["VERTEX"]), ("fsh", GL["FRAGMENT"])):
        shader = g.CreateShader(kind)
        text = ctypes.c_char_p(source(folder / f"leash.{ext}").encode())
        g.ShaderSource(shader, 1, ctypes.byref(text), None)
        g.CompileShader(shader)
        ok, log = ctypes.c_int(), ctypes.create_string_buffer(8192)
        g.GetShaderiv(shader, GL["COMPILE_STATUS"], ctypes.byref(ok))
        g.GetShaderInfoLog(shader, 8192, None, log)
        if not ok.value:
            sys.exit(f"leash.{ext}: {log.value.decode()}")
        g.AttachShader(prog, shader)
    g.LinkProgram(prog)
    ok = ctypes.c_int()
    g.GetProgramiv(prog, GL["LINK_STATUS"], ctypes.byref(ok))
    if not ok.value:
        sys.exit("link failed")
    return prog


def texture(g, width, height, internal, fmt, kind, data):
    t = ctypes.c_uint()
    g.GenTextures(1, ctypes.byref(t))
    g.BindTexture(GL["TEXTURE_2D"], t)
    g.TexImage2D(GL["TEXTURE_2D"], 0, internal, width, height, 0, fmt, kind, data)
    g.TexParameteri(GL["TEXTURE_2D"], GL["MIN_FILTER"], GL["LINEAR"])
    g.TexParameteri(GL["TEXTURE_2D"], GL["MAG_FILTER"], GL["LINEAR"])
    return t


def inverse_view_projection(width, height, fov=70.0, near=0.1, far=1000.0):
    """Looking along +x, y up; column-major, as OpenGL takes it. Camera at the origin."""
    f = 1 / math.tan(math.radians(fov) / 2)
    a = width / height
    # View: world x -> -z of the eye, world y -> y, world z -> x.  Projection: standard perspective.
    # Inverse of P*V written out: from clip (sx, sy, sz, 1) back to world.
    A, B = (far + near) / (near - far), 2 * far * near / (near - far)
    # Inverse projection (column-major rows below are rows of the matrix).
    ip = [[a / f, 0, 0, 0], [0, 1 / f, 0, 0], [0, 0, 0, -1], [0, 0, 1 / B, A / B]]
    # Inverse view: eye (x, y, z) -> world (-z, y, x).
    iv = [[0, 0, -1, 0], [0, 1, 0, 0], [1, 0, 0, 0], [0, 0, 0, 1]]
    m = [[sum(iv[r][k] * ip[k][c] for k in range(4)) for c in range(4)] for r in range(4)]
    return (ctypes.c_float * 16)(*[m[r][c] for c in range(4) for r in range(4)])


def save(g, width, height, path):
    import subprocess
    data = ctypes.create_string_buffer(width * height * 4)
    g.ReadPixels(0, 0, width, height, GL["RGBA"], GL["UNSIGNED_BYTE"], data)
    path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{width}x{height}", "-i", "-",
                    "-vf", "vflip", "-frames:v", "1", str(path)], input=data.raw, check=True)


def main():
    width, height = (int(sys.argv[1]), int(sys.argv[2])) if len(sys.argv) > 2 else (1920, 1080)
    folder = Path(os.environ["SHADER_DIR"]) if os.environ.get("SHADER_DIR") else REPO / "assets/temporalrebirth/shaders"
    g = gl_context()
    print("driver:", g.GetString(GL["RENDERER"]).decode(), f"  {width}x{height}  {folder}")
    prog = build(g, folder)
    g.UseProgram(prog)

    # A world behind: a coloured gradient, and depth far away (open sky beyond the wall).
    pixels = bytes((x * 255 // width) if c == 0 else (y * 255 // height) if c == 1 else 120 if c == 2 else 255
                   for y in range(0, height, 8) for x in range(0, width, 8) for c in range(4))
    g.ActiveTexture(GL["TEXTURE0"])
    texture(g, width // 8, height // 8, GL["RGBA8"], GL["RGBA"], GL["UNSIGNED_BYTE"], pixels)
    g.ActiveTexture(GL["TEXTURE0"] + 1)
    depth = (ctypes.c_float * (16 * 16))(*([0.9999] * 256))
    texture(g, 16, 16, GL["R32F"], GL["RED"], GL["FLOAT"], depth)
    g.Uniform1i(g.GetUniformLocation(prog, b"sceneColor"), 0)
    g.Uniform1i(g.GetUniformLocation(prog, b"sceneDepth"), 1)

    g.ActiveTexture(GL["TEXTURE0"] + 2)
    target = texture(g, width, height, GL["RGBA8"], GL["RGBA"], GL["UNSIGNED_BYTE"], None)
    fb = ctypes.c_uint()
    g.GenFramebuffers(1, ctypes.byref(fb))
    g.BindFramebuffer(GL["FRAMEBUFFER"], fb)
    g.FramebufferTexture2D(GL["FRAMEBUFFER"], GL["COLOR_ATTACHMENT0"], GL["TEXTURE_2D"], target, 0)
    g.Viewport(0, 0, width, height)
    g.Enable(GL["BLEND"])
    g.BlendFunc(GL["SRC_ALPHA"], GL["ONE_MINUS_SRC_ALPHA"])

    vao, vbo = ctypes.c_uint(), ctypes.c_uint()
    g.GenVertexArrays(1, ctypes.byref(vao))
    g.BindVertexArray(vao)
    g.GenBuffers(1, ctypes.byref(vbo))
    g.BindBuffer(GL["ARRAY_BUFFER"], vbo)
    quad = (ctypes.c_float * 20)(-1, -1, 0, 0, 0, 1, -1, 0, 1, 0, -1, 1, 0, 0, 1, 1, 1, 0, 1, 1)
    g.BufferData(GL["ARRAY_BUFFER"], ctypes.sizeof(quad), quad, GL["STATIC_DRAW"])
    g.VertexAttribPointer(0, 3, GL["FLOAT"], 0, 20, None)
    g.VertexAttribPointer(1, 2, GL["FLOAT"], 0, 20, ctypes.c_void_p(12))
    g.EnableVertexAttribArray(0)
    g.EnableVertexAttribArray(1)

    loc = lambda name: g.GetUniformLocation(prog, name.encode())
    g.UniformMatrix4fv(loc("inverseViewProjection"), 1, 0, inverse_view_projection(width, height))
    g.Uniform3f(loc("camera"), 0, 0, 0)
    frames = int(os.environ.get("FRAMES", "60"))
    for name, gap, pushed in CASES:
        # Eyes `gap` blocks inside the wall, looking straight out at it; the ghost's feet 1.6 below the eyes.
        g.Uniform3f(loc("fromCenter"), RADIUS - gap, 0, 0)
        g.Uniform1f(loc("outside"), (RADIUS - gap) ** 2 - RADIUS ** 2)
        g.Uniform3f(loc("player"), 0, -1.6, 0)
        g.Uniform4f(loc("hit0"), gap + 0.3, 0, 0, pushed)
        g.Uniform4f(loc("hit1"), 0, 0, 0, -1)
        g.Uniform4f(loc("hit2"), 0, 0, 0, -1)
        for k in range(5):
            g.Uniform1f(loc("time"), 100 + k / 30)
            g.DrawArrays(GL["TRIANGLE_STRIP"], 0, 4)
        g.Finish()
        start = time.perf_counter()
        for k in range(frames):
            g.Uniform1f(loc("time"), 100 + k / 30)
            g.DrawArrays(GL["TRIANGLE_STRIP"], 0, 4)
        g.Finish()
        ms = (time.perf_counter() - start) * 1000 / frames
        print(f"  {name:24s} {ms:6.2f} ms/frame")
        if os.environ.get("OUT"):
            save(g, width, height, Path(os.environ["OUT"]) / f"{CASES.index((name, gap, pushed))}.png")


if __name__ == "__main__":
    main()
