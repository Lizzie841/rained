using System.Collections.Generic;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Input;

#if GLES
using Silk.NET.OpenGLES;
#elif GL
using System;
using Silk.NET.OpenGL;
#elif LEGACY
using Silk.NET.OpenGL.Legacy;
#endif
using Silk.NET.Windowing;

#if GL
namespace Silk.NET.OpenGL.Extensions.ImGui
#elif GLES
namespace Silk.NET.OpenGLES.Extensions.ImGui
#elif LEGACY
namespace Silk.NET.OpenGL.Legacy.Extensions.ImGui
#endif
{
    // fucking hell
    // oh wow they're all inline in the imgui.h code. great.
    [StructLayout(LayoutKind.Sequential)]
    struct ImVector<T> where T : unmanaged
    {
        public int Size;
        public int Capacity;
        public unsafe T* Data;

        public unsafe void Resize(int new_size)
        {
            if (new_size > Capacity)
            {
                Reserve(GrowCapacity(new_size));
            }
            Size = new_size;
        }

        public unsafe void Reserve(int new_capacity)
        {
            if (new_capacity <= Capacity) return;
            T* new_data = (T*) ImGuiNET.ImGui.MemAlloc((uint)((nint)new_capacity * sizeof(T)));
            if (Data != null)
            {
                NativeMemory.Copy(Data, new_data, (nuint)(Size * sizeof(T)));
                ImGuiNET.ImGui.MemFree((nint) Data);
            }

            Data = new_data;
            Capacity = new_capacity;
        }

        private readonly unsafe int GrowCapacity(int sz)
        {
            int new_capacity = Capacity != 0 ? (Capacity + Capacity / 2) : 8;
            return new_capacity > sz ? new_capacity : sz;
        }

        public unsafe void PushBack(T v)
        {
            if (Size == Capacity)
                Reserve(GrowCapacity(Size + 1));
                
            NativeMemory.Copy(&v, &Data[Size], (nuint) sizeof(T));
            Size++;
        }
    };

    class ImGuiViewports : IDisposable
    {
        private readonly IWindow mainWindow;

        private ImGuiController controller;

        private Delegate[] _delegates = new Delegate[20];

        public unsafe ImGuiViewports(ImGuiController controller, GL mainRenderer, IWindow mainWindow)
        {
            this.mainWindow = mainWindow;
            this.controller = controller;

            int i = 0;
            var platformIO = ImGuiNET.ImGui.GetPlatformIO();
            platformIO.Platform_CreateWindow = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplCreateWindow);
            platformIO.Platform_DestroyWindow = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplDestroyWindow);
            platformIO.Platform_ShowWindow = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplShowWindow);
            platformIO.Platform_SetWindowPos = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplSetWindowPos);
            //platformIO.Platform_GetWindowPos = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplGetWindowPos);
            platformIO.Platform_SetWindowSize = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplSetWindowSize);
            //platformIO.Platform_GetWindowSize = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplGetWindowSize);
            platformIO.Platform_SetWindowFocus = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplSetWindowFocus);
            platformIO.Platform_GetWindowFocus = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplGetWindowFocus);
            platformIO.Platform_GetWindowMinimized = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplGetWindowMinimized);
            platformIO.Platform_SetWindowTitle = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplSetWindowTitle);
            platformIO.Platform_RenderWindow = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplRenderWindow);
            platformIO.Platform_SwapBuffers = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplSwapBuffers);
            platformIO.Platform_UpdateWindow = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplUpdateWindow);
            platformIO.Platform_SetWindowAlpha = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplSetWindowAlpha);

            // Some function pointers in the ImGuiPlatformIO structure are not C-compatible because of their
            // use of a complex return type. CImgui provides a workaround for this.
            ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowPos(platformIO, Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplGetWindowPos));
            ImGuiNative.ImGuiPlatformIO_Set_Platform_GetWindowSize(platformIO, Marshal.GetFunctionPointerForDelegate(_delegates[i++] = ImplGetWindowSize));

            platformIO.Renderer_RenderWindow = Marshal.GetFunctionPointerForDelegate(_delegates[i++] = RenderImplRenderWindow);

            //platformIO.Platform_CreateVkSurface = 0;
            //platformIO.Platform_GetWindowDpiScale = 0;
            //platformIO.Platform_OnChangedViewport = 0;
            //platformIO.Platform_SetWindowAlpha = 0;

            var viewport = ImGuiNET.ImGui.GetMainViewport();
            RegisterWindow(ImGuiNET.ImGui.GetMainViewport(), mainWindow);
            renderers[mainWindow] = mainRenderer;

            var monitors = (ImVector<ImGuiPlatformMonitor>*) &platformIO.NativePtr->Monitors;
            //ImGuiNET.ImGui.GetPlatformIO().Monitors.
            foreach (var monitor in Monitor.GetMonitors(mainWindow))
            {
                var imMonitor = new ImGuiPlatformMonitor()
                {
                    MainPos = (Vector2) monitor.Bounds.Origin,
                    MainSize = (Vector2) (monitor.VideoMode.Resolution ?? monitor.Bounds.Size),
                    DpiScale = 1f,
                };

                imMonitor.WorkPos = imMonitor.MainPos;
                imMonitor.WorkSize = imMonitor.MainSize;

                monitors->PushBack(imMonitor);
            }
        }

        public void Dispose()
        {
            ImGuiNET.ImGui.DestroyPlatformWindows();
        }

        private int nextWindowId = 1;
        private readonly Dictionary<int, IWindow> activeWindows = [];
        private readonly Dictionary<IWindow, GL> renderers = [];

        public unsafe void RegisterWindow(ImGuiViewport* viewport, IWindow window)
        {
            var id = nextWindowId++;
            viewport->PlatformHandle = (void*) id;
            activeWindows[id] = window;
        }

        public unsafe void UnregisterWindow(ImGuiViewport* viewport)
        {
            var window = GetWindow(viewport);
            activeWindows.Remove((int) viewport->PlatformHandle);
        }

        public int GetWindowID(IWindow window)
        {
            foreach (var (k, v) in activeWindows)
            {
                if (v == window)
                    return k;
            }

            throw new Exception("Window is not recognized");
        }

        public unsafe IWindow GetWindow(ImGuiViewport* viewport)
        {
            var windowId = (int) viewport->PlatformHandle;
            if (windowId == 0)
            {
                return mainWindow;
            }
            else
            {
                return activeWindows[windowId];

            }
        }
        public unsafe ImGuiViewportPtr GetViewport(IWindow window) => ImGuiNET.ImGui.FindViewportByPlatformHandle(GetWindowID(window));

        private unsafe void ImplCreateWindow(ImGuiViewport* viewport)
        {
            var opts = Silk.NET.Windowing.WindowOptions.Default;
            opts.IsVisible = false;
            opts.WindowBorder = viewport->Flags.HasFlag(ImGuiViewportFlags.NoDecoration) ? WindowBorder.Hidden : WindowBorder.Fixed;
            opts.TopMost = viewport->Flags.HasFlag(ImGuiViewportFlags.TopMost);
            opts.Size = new Silk.NET.Maths.Vector2D<int>((int) viewport->Size.X, (int) viewport->Size.Y);
            opts.Title = "No Title Yet";
            opts.ShouldSwapAutomatically = false;
            opts.SharedContext = mainWindow.GLContext;
            opts.VSync = false;
            
            //var window = Silk.NET.Windowing.Window.Create(opts);
            var window = mainWindow.CreateWindow(opts);
            IInputContext input = null!;

            window.Load += () =>
            {
                input = window.CreateInput();

                input.Keyboards[0].KeyDown += (IKeyboard keyboard, Key k, int c) =>
                    controller.AddKeyEvent(ImGuiNET.ImGui.GetIO(), k, true);

                input.Keyboards[0].KeyUp += (IKeyboard keyboard, Key k, int c) =>
                    controller.AddKeyEvent(ImGuiNET.ImGui.GetIO(), k, false);

                input.Keyboards[0].KeyChar += (IKeyboard keyboard, char c) =>
                {
                    ImGuiNET.ImGui.GetIO().AddInputCharacter(c);
                };

                input.Mice[0].MouseMove += (IMouse mouse, Vector2 pos) =>
                {
                    var io = ImGuiNET.ImGui.GetIO();
                    var winPos = window.Position;
                    io.AddMousePosEvent(pos.X + winPos.X, pos.Y + winPos.Y);
                };

                input.Mice[0].Scroll += (IMouse mouse, ScrollWheel wheel) =>
                {
                    var io = ImGuiNET.ImGui.GetIO();
                    io.AddMouseWheelEvent(wheel.X, wheel.Y);
                };
                
                input.Mice[0].MouseDown += (IMouse mouse, MouseButton button) =>
                {
                    var io = ImGuiNET.ImGui.GetIO();

                    switch (button)
                    {
                        case MouseButton.Left:
                            io.AddMouseButtonEvent(0, true);
                            break;

                        case MouseButton.Right:
                            io.AddMouseButtonEvent(1, true);
                            break;

                        case MouseButton.Middle:
                            io.AddMouseButtonEvent(2, true);
                            break;
                    };
                };

                input.Mice[0].MouseUp += (IMouse mouse, MouseButton button) =>
                {
                    var io = ImGuiNET.ImGui.GetIO();

                    switch (button)
                    {
                        case MouseButton.Left:
                            io.AddMouseButtonEvent(0, false);
                            break;

                        case MouseButton.Right:
                            io.AddMouseButtonEvent(1, false);
                            break;

                        case MouseButton.Middle:
                            io.AddMouseButtonEvent(2, false);
                            break;
                    };
                };

                RegisterWindow(viewport, window);
                renderers[window] = window.CreateOpenGL();
            };

            window.FocusChanged += (bool focused) =>
            {
                ImGuiNET.ImGui.GetIO().AddFocusEvent(focused);
            };

            window.Closing += () =>
            {
                GetViewport(window).PlatformRequestClose = true;
                input.Dispose();
            };

            window.Move += (Maths.Vector2D<int> _) =>
            {
                GetViewport(window).PlatformRequestMove = true;
            };

            window.Resize += (Maths.Vector2D<int> _) =>
            {
                GetViewport(window).PlatformRequestResize = true;
            };

            window.Initialize();
        }

        private unsafe void ImplDestroyWindow(ImGuiViewport* viewport)
        {
            var windowId = (nint) viewport->PlatformHandle;
            if (windowId == GetWindowID(mainWindow)) return;

            var window = GetWindow(viewport);

            // Release any keys that were pressed in the window being destroyed and are still held down,
            // because we will not receive any release events after window is destroyed.
            window.Close();
            window.Dispose();

            UnregisterWindow(viewport);
            renderers.Remove(window);
        }

        private unsafe void ImplShowWindow(ImGuiViewport* viewport)
        {
            var window = GetWindow(viewport);
            // TODO: respect ImGuiViewportFlags_NoFocusOnAppearing
            controller.ignoreMouseUp = true;
            window.IsVisible = true;
        }

        private unsafe void ImplGetWindowPos(ImGuiViewport* viewport, Vector2* outPos)
        {
            var window = GetWindow(viewport);
            *outPos = (Vector2) window.Position;
        }

        private unsafe void ImplSetWindowPos(ImGuiViewport* viewport, Vector2 pos)
        {
            var window = GetWindow(viewport);
            window.Position = new Silk.NET.Maths.Vector2D<int>((int)pos.X, (int)pos.Y);
        }

        private unsafe void ImplGetWindowSize(ImGuiViewport* viewport, Vector2* out_size)
        {
            var window = GetWindow(viewport);
            *out_size = (Vector2) window.Size;
        }

        private unsafe void ImplSetWindowSize(ImGuiViewport* viewport, Vector2 size)
        {
            // TODO: mac windows are positioned from the bottom-left
            var window = GetWindow(viewport);
            window.Size = new Silk.NET.Maths.Vector2D<int>((int)size.X, (int)size.Y);
        }

        private unsafe void ImplSetWindowTitle(ImGuiViewport* viewport, byte* title)
        {
            var window = GetWindow(viewport);

            // get length of c str
            int length = 0;
            for (; length < 256; length++)
            {
                if (title[length] == '\0') break;
            }

            window.Title = System.Text.Encoding.UTF8.GetString(title, length);
        }

        private unsafe void ImplSetWindowFocus(ImGuiViewport* viewport)
        {
            var window = GetWindow(viewport);

            var glfw = Windowing.Glfw.GlfwWindowing.GetExistingApi(window);
            glfw?.FocusWindow((GLFW.WindowHandle*) window.Native.Glfw!);
        }

        private unsafe bool ImplGetWindowFocus(ImGuiViewport* viewport)
        {
            var window = GetWindow(viewport);

            var glfw = Windowing.Glfw.GlfwWindowing.GetExistingApi(window);
            if (glfw is not null)
            {
                var handle = (GLFW.WindowHandle*) window.Native.Glfw!;
                return glfw.GetWindowAttrib(handle, GLFW.WindowAttributeGetter.Focused);
            }
            
            return false;
        }

        private unsafe bool ImplGetWindowMinimized(ImGuiViewport* viewport)
        {
            var window = GetWindow(viewport);
            return window.WindowState == WindowState.Minimized;
        }

        private unsafe void ImplSetWindowAlpha(ImGuiViewport* viewport, float alpha)
        {
            var window = GetWindow(viewport);

            var glfw = Windowing.Glfw.GlfwWindowing.GetExistingApi(window);
            if (glfw is not null)
            {
                var handle = (GLFW.WindowHandle*) window.Native.Glfw!;
                glfw.SetWindowOpacity(handle, alpha);
            }
        }

        private unsafe void ImplRenderWindow(ImGuiViewport* viewport, void* _)
        {
            var window = GetWindow(viewport);
            window.MakeCurrent();
        }

        private unsafe void ImplSwapBuffers(ImGuiViewport* viewport, void* _)
        {
            var window = GetWindow(viewport);
            if (window == mainWindow) return;
            window.MakeCurrent();
            window.SwapBuffers();
        }

        private unsafe void ImplUpdateWindow(ImGuiViewport* viewport)
        {
            var window = GetWindow(viewport);
            if (window == mainWindow) return;
            window.DoEvents();
        }

        private unsafe void RenderImplRenderWindow(ImGuiViewport* viewport, void* _)
        {
            var window = GetWindow(viewport);
            if (window == mainWindow) return;
            var gl = renderers[window];
            
            if (!viewport->Flags.HasFlag(ImGuiViewportFlags.NoRendererClear))
            {
                gl.ClearColor(0f, 0f, 0f, 1f);
                gl.Clear(ClearBufferMask.ColorBufferBit);
            }

            var drawData = viewport->DrawData;

            int framebufferWidth = (int) (drawData->DisplaySize.X * drawData->FramebufferScale.X);
            int framebufferHeight = (int) (drawData->DisplaySize.Y * drawData->FramebufferScale.Y);
            if (framebufferHeight < 0 || framebufferHeight < 0) return;

            gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);
            controller.RenderImDrawData(viewport->DrawData);
        }
    }
}