// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Input.Extensions;
using Silk.NET.Maths;
#if GLES
using Silk.NET.OpenGLES;
#elif GL
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
    public class ImGuiController : IDisposable
    {
        private GL _gl;
        private IView _view;
        private IInputContext _input;
        private bool _frameBegun;
        private static readonly Dictionary<Key, ImGuiKey> keyMap = new Dictionary<Key, ImGuiKey>();
        private IKeyboard _keyboard;

        private int _attribLocationTex;
        private int _attribLocationProjMtx;
        private int _attribLocationVtxPos;
        private int _attribLocationVtxUV;
        private int _attribLocationVtxColor;
        private uint _vboHandle;
        private uint _elementsHandle;
        private uint _vertexArrayObject;

        private Texture _fontTexture;
        private Shader _shader;

        private int _windowWidth;
        private int _windowHeight;

        public IntPtr Context;

        /// <summary>
        /// Constructs a new ImGuiController.
        /// </summary>
        public ImGuiController(GL gl, IView view, IInputContext input) : this(gl, view, input, null, null)
        {
        }

        /// <summary>
        /// Constructs a new ImGuiController with font configuration.
        /// </summary>
        public ImGuiController(GL gl, IView view, IInputContext input, ImGuiFontConfig imGuiFontConfig) : this(gl, view, input, imGuiFontConfig, null)
        {
        }

        /// <summary>
        /// Constructs a new ImGuiController with an onConfigureIO Action.
        /// </summary>
        public ImGuiController(GL gl, IView view, IInputContext input, Action onConfigureIO) : this(gl, view, input, null, onConfigureIO)
        {
        }

        /// <summary>
        /// Constructs a new ImGuiController with font configuration and onConfigure Action.
        /// </summary>
        public ImGuiController(GL gl, IView view, IInputContext input, ImGuiFontConfig? imGuiFontConfig = null, Action onConfigureIO = null)
        {
            Init(gl, view, input);

            var io = ImGuiNET.ImGui.GetIO();
            if (imGuiFontConfig is not null)
            {
                var glyphRange = imGuiFontConfig.Value.GetGlyphRange?.Invoke(io) ?? default(IntPtr);

                io.Fonts.AddFontFromFileTTF(imGuiFontConfig.Value.FontPath, imGuiFontConfig.Value.FontSize, null, glyphRange);
            }

            onConfigureIO?.Invoke();

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            CreateDeviceResources();
            SetKeyMappings();

            SetPerFrameImGuiData(1f / 60f);

            BeginFrame();
        }

        public void MakeCurrent()
        {
            ImGuiNET.ImGui.SetCurrentContext(Context);
        }

        private void Init(GL gl, IView view, IInputContext input)
        {
            _gl = gl;
            _view = view;
            _input = input;
            _windowWidth = view.Size.X;
            _windowHeight = view.Size.Y;

            Context = ImGuiNET.ImGui.CreateContext();
            ImGuiNET.ImGui.SetCurrentContext(Context);
            ImGuiNET.ImGui.StyleColorsDark();
        }

        private void BeginFrame()
        {
            ImGuiNET.ImGui.NewFrame();
            _frameBegun = true;
            _keyboard = _input.Keyboards[0];
            _view.Resize += WindowResized;

            _keyboard.KeyChar += OnKeyChar;
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;

            var mouse = _input.Mice[0];

            mouse.MouseDown += (IMouse mouse, MouseButton button) =>
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

            mouse.MouseUp += (IMouse mouse, MouseButton button) =>
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
        }

        private void OnKeyChar(IKeyboard arg1, char arg2)
        {
            var io = ImGuiNET.ImGui.GetIO();
            io.AddInputCharacter(arg2);
        }

        private void OnKeyDown(IKeyboard kb, Key key, int code)
        {
            AddKeyEvent(ImGuiNET.ImGui.GetIO(), key, true);
        }

        private void OnKeyUp(IKeyboard kb, Key key, int code)
        {
            AddKeyEvent(ImGuiNET.ImGui.GetIO(), key, false);
        }

        private void WindowResized(Vector2D<int> size)
        {
            _windowWidth = size.X;
            _windowHeight = size.Y;
        }

        private ImGuiMouseCursor lastCursorMode = ImGuiMouseCursor.Arrow;

        /// <summary>
        /// Renders the ImGui draw list data.
        /// This method requires a <see cref="GraphicsDevice"/> because it may create new DeviceBuffers if the size of vertex
        /// or index data has increased beyond the capacity of the existing buffers.
        /// A <see cref="CommandList"/> is needed to submit drawing and resource update commands.
        /// </summary>
        public void Render()
        {
            if (_frameBegun)
            {   
                var oldCtx = ImGuiNET.ImGui.GetCurrentContext();

                if (oldCtx != Context)
                {
                    ImGuiNET.ImGui.SetCurrentContext(Context);
                }

                _frameBegun = false;
                ImGuiNET.ImGui.Render();

                // update mouse
                var io = ImGuiNET.ImGui.GetIO();
                if (!io.ConfigFlags.HasFlag(ImGuiConfigFlags.NoMouseCursorChange))
                {
                    var cursor = _input.Mice[0].Cursor;
                    var imguiCursor = ImGuiNET.ImGui.GetMouseCursor();

                    if (imguiCursor != lastCursorMode)
                    {
                        lastCursorMode = imguiCursor;
                        
                        if (io.MouseDrawCursor || imguiCursor == ImGuiMouseCursor.None)
                        {
                            cursor.CursorMode = CursorMode.Hidden;  
                        }
                        else
                        {
                            cursor.CursorMode = CursorMode.Normal;
                            cursor.Type = CursorType.Standard;
                            var newCursor = imguiCursor switch
                            {
                                ImGuiMouseCursor.Arrow => StandardCursor.Arrow,
                                ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
                                ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
                                ImGuiMouseCursor.ResizeNS => StandardCursor.VResize,
                                ImGuiMouseCursor.ResizeEW => StandardCursor.HResize,
                                ImGuiMouseCursor.ResizeNESW => StandardCursor.NeswResize,
                                ImGuiMouseCursor.ResizeNWSE => StandardCursor.NwseResize,
                                ImGuiMouseCursor.Hand => StandardCursor.Hand,
                                ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
                                _ => StandardCursor.Arrow
                            };

                            if (newCursor != cursor.StandardCursor)
                            {
                                //cursor.StandardCursor = newCursor;

                                // some sort of silk.NET bug...
                                var cursorType = cursor.GetType();
                                var stdCursorFld = cursorType.GetField("_standardCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                                var updateStdCursorMethod = cursorType.GetMethod("UpdateStandardCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                                stdCursorFld.SetValue(cursor, newCursor);
                                updateStdCursorMethod.Invoke(cursor, null);
                            }
                        }
                    }
                }

                RenderImDrawData(ImGuiNET.ImGui.GetDrawData());

                if (oldCtx != Context)
                {
                    ImGuiNET.ImGui.SetCurrentContext(oldCtx);
                }
            }
        }

        /// <summary>
        /// Updates ImGui input and IO configuration state.
        /// </summary>
        public void Update(float deltaSeconds)
        {
            var oldCtx = ImGuiNET.ImGui.GetCurrentContext();

            if (oldCtx != Context)
            {
                ImGuiNET.ImGui.SetCurrentContext(Context);
            }

            if (_frameBegun)
            {
                ImGuiNET.ImGui.Render();
            }

            SetPerFrameImGuiData(deltaSeconds);
            UpdateImGuiInput();

            _frameBegun = true;
            ImGuiNET.ImGui.NewFrame();

            if (oldCtx != Context)
            {
                ImGuiNET.ImGui.SetCurrentContext(oldCtx);
            }
        }

        /// <summary>
        /// Sets per-frame data based on the associated window.
        /// This is called by Update(float).
        /// </summary>
        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            var io = ImGuiNET.ImGui.GetIO();
            io.DisplaySize = new Vector2(_windowWidth, _windowHeight);

            if (_windowWidth > 0 && _windowHeight > 0)
            {
                io.DisplayFramebufferScale = new Vector2(_view.FramebufferSize.X / _windowWidth,
                    _view.FramebufferSize.Y / _windowHeight);
            }

            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
        }

        private void AddKeyEvent(ImGuiIOPtr io, Key k, bool down)
        {
            // Rained-specific modification:
            // ImGui ignores the tab key
            if (k == Key.Tab) return;

            if (keyMap.TryGetValue(k, out ImGuiKey imKey))
            {
                var keyboardState = _input.Keyboards[0];
                io.AddKeyEvent(ImGuiKey.ModCtrl, keyboardState.IsKeyPressed(Key.ControlLeft) || keyboardState.IsKeyPressed(Key.ControlRight));
                io.AddKeyEvent(ImGuiKey.ModAlt, keyboardState.IsKeyPressed(Key.AltLeft) || keyboardState.IsKeyPressed(Key.AltRight));
                io.AddKeyEvent(ImGuiKey.ModShift, keyboardState.IsKeyPressed(Key.ShiftLeft) || keyboardState.IsKeyPressed(Key.ShiftRight));
                io.AddKeyEvent(ImGuiKey.ModSuper, keyboardState.IsKeyPressed(Key.SuperLeft) || keyboardState.IsKeyPressed(Key.SuperRight));

                io.AddKeyEvent(imKey, down);
            }
        }

        private static Key[] keyEnumArr = (Key[]) Enum.GetValues(typeof(Key));
        private void UpdateImGuiInput()
        {
            var io = ImGuiNET.ImGui.GetIO();

            var mouseState = _input.Mice[0].CaptureState();
            var keyboardState = _input.Keyboards[0];

            //io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
            //io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
            //io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);

            var point = new Point((int) mouseState.Position.X, (int) mouseState.Position.Y);
            io.AddMousePosEvent(point.X, point.Y);

            var wheel = mouseState.GetScrollWheels()[0];
            io.AddMouseWheelEvent(wheel.X, wheel.Y);
        }

        private static void SetKeyMappings()
        {
            if (keyMap.Count > 0) return;

            keyMap[Key.Apostrophe] = ImGuiKey.Apostrophe;
            keyMap[Key.Comma] = ImGuiKey.Comma;
            keyMap[Key.Minus] = ImGuiKey.Minus;
            keyMap[Key.Period] = ImGuiKey.Period;
            keyMap[Key.Slash] = ImGuiKey.Slash;
            keyMap[Key.Number0] = ImGuiKey._0;
            keyMap[Key.Number1] = ImGuiKey._1;
            keyMap[Key.Number2] = ImGuiKey._2;
            keyMap[Key.Number3] = ImGuiKey._3;
            keyMap[Key.Number4] = ImGuiKey._4;
            keyMap[Key.Number5] = ImGuiKey._5;
            keyMap[Key.Number6] = ImGuiKey._6;
            keyMap[Key.Number7] = ImGuiKey._7;
            keyMap[Key.Number8] = ImGuiKey._8;
            keyMap[Key.Number9] = ImGuiKey._9;
            keyMap[Key.Semicolon] = ImGuiKey.Semicolon;
            keyMap[Key.Equal] = ImGuiKey.Equal;
            keyMap[Key.A] = ImGuiKey.A;
            keyMap[Key.B] = ImGuiKey.B;
            keyMap[Key.C] = ImGuiKey.C;
            keyMap[Key.D] = ImGuiKey.D;
            keyMap[Key.E] = ImGuiKey.E;
            keyMap[Key.F] = ImGuiKey.F;
            keyMap[Key.G] = ImGuiKey.G;
            keyMap[Key.H] = ImGuiKey.H;
            keyMap[Key.I] = ImGuiKey.I;
            keyMap[Key.J] = ImGuiKey.J;
            keyMap[Key.K] = ImGuiKey.K;
            keyMap[Key.L] = ImGuiKey.L;
            keyMap[Key.M] = ImGuiKey.M;
            keyMap[Key.N] = ImGuiKey.N;
            keyMap[Key.O] = ImGuiKey.O;
            keyMap[Key.P] = ImGuiKey.P;
            keyMap[Key.Q] = ImGuiKey.Q;
            keyMap[Key.R] = ImGuiKey.R;
            keyMap[Key.S] = ImGuiKey.S;
            keyMap[Key.T] = ImGuiKey.T;
            keyMap[Key.U] = ImGuiKey.U;
            keyMap[Key.V] = ImGuiKey.V;
            keyMap[Key.W] = ImGuiKey.W;
            keyMap[Key.X] = ImGuiKey.X;
            keyMap[Key.Y] = ImGuiKey.Y;
            keyMap[Key.Z] = ImGuiKey.Z;
            keyMap[Key.Space] = ImGuiKey.Space;
            keyMap[Key.Escape] = ImGuiKey.Escape;
            keyMap[Key.Enter] = ImGuiKey.Enter;
            keyMap[Key.Tab] = ImGuiKey.Tab;
            keyMap[Key.Backspace] = ImGuiKey.Backspace;
            keyMap[Key.Insert] = ImGuiKey.Insert;
            keyMap[Key.Delete] = ImGuiKey.Delete;
            keyMap[Key.Right] = ImGuiKey.RightArrow;
            keyMap[Key.Left] = ImGuiKey.LeftArrow;
            keyMap[Key.Down] = ImGuiKey.DownArrow;
            keyMap[Key.Up] = ImGuiKey.UpArrow;
            keyMap[Key.PageUp] = ImGuiKey.PageUp;
            keyMap[Key.PageDown] = ImGuiKey.PageDown;
            keyMap[Key.Home] = ImGuiKey.Home;
            keyMap[Key.End] = ImGuiKey.End;
            keyMap[Key.CapsLock] = ImGuiKey.CapsLock;
            keyMap[Key.ScrollLock] = ImGuiKey.ScrollLock;
            keyMap[Key.NumLock] = ImGuiKey.NumLock;
            keyMap[Key.PrintScreen] = ImGuiKey.PrintScreen;
            keyMap[Key.Pause] = ImGuiKey.Pause;
            keyMap[Key.F1] = ImGuiKey.F1;
            keyMap[Key.F2] = ImGuiKey.F2;
            keyMap[Key.F3] = ImGuiKey.F3;
            keyMap[Key.F4] = ImGuiKey.F4;
            keyMap[Key.F5] = ImGuiKey.F5;
            keyMap[Key.F6] = ImGuiKey.F6;
            keyMap[Key.F7] = ImGuiKey.F7;
            keyMap[Key.F8] = ImGuiKey.F8;
            keyMap[Key.F9] = ImGuiKey.F9;
            keyMap[Key.F10] = ImGuiKey.F10;
            keyMap[Key.F11] = ImGuiKey.F11;
            keyMap[Key.F12] = ImGuiKey.F12;
            keyMap[Key.ShiftLeft] = ImGuiKey.LeftShift;
            keyMap[Key.ControlLeft] = ImGuiKey.LeftCtrl;
            keyMap[Key.AltLeft] = ImGuiKey.LeftAlt;
            keyMap[Key.SuperLeft] = ImGuiKey.LeftSuper;
            keyMap[Key.ShiftRight] = ImGuiKey.RightShift;
            keyMap[Key.ControlRight] = ImGuiKey.RightCtrl;
            keyMap[Key.AltRight] = ImGuiKey.RightAlt;
            keyMap[Key.SuperRight] = ImGuiKey.RightSuper;
            keyMap[Key.Menu] = ImGuiKey.Menu;
            keyMap[Key.LeftBracket] = ImGuiKey.LeftBracket;
            keyMap[Key.BackSlash] = ImGuiKey.Backslash;
            keyMap[Key.RightBracket] = ImGuiKey.RightBracket;
            keyMap[Key.GraveAccent] = ImGuiKey.GraveAccent;
            keyMap[Key.Keypad0] = ImGuiKey.Keypad0;
            keyMap[Key.Keypad1] = ImGuiKey.Keypad1;
            keyMap[Key.Keypad2] = ImGuiKey.Keypad2;
            keyMap[Key.Keypad3] = ImGuiKey.Keypad3;
            keyMap[Key.Keypad4] = ImGuiKey.Keypad4;
            keyMap[Key.Keypad5] = ImGuiKey.Keypad5;
            keyMap[Key.Keypad6] = ImGuiKey.Keypad6;
            keyMap[Key.Keypad7] = ImGuiKey.Keypad7;
            keyMap[Key.Keypad8] = ImGuiKey.Keypad8;
            keyMap[Key.Keypad9] = ImGuiKey.Keypad9;
            keyMap[Key.KeypadDecimal] = ImGuiKey.KeypadDecimal;
            keyMap[Key.KeypadDivide] = ImGuiKey.KeypadDivide;
            keyMap[Key.KeypadMultiply] = ImGuiKey.KeypadMultiply;
            keyMap[Key.KeypadSubtract] = ImGuiKey.KeypadSubtract;
            keyMap[Key.KeypadAdd] = ImGuiKey.KeypadAdd;
            keyMap[Key.KeypadEnter] = ImGuiKey.KeypadEnter;
            keyMap[Key.KeypadEqual] = ImGuiKey.KeypadEqual;
        }

        private unsafe void SetupRenderState(ImDrawDataPtr drawDataPtr, int framebufferWidth, int framebufferHeight)
        {
            // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, polygon fill
            _gl.Enable(GLEnum.Blend);
            _gl.BlendEquation(GLEnum.FuncAdd);
            _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
            _gl.Disable(GLEnum.CullFace);
            _gl.Disable(GLEnum.DepthTest);
            _gl.Disable(GLEnum.StencilTest);
            _gl.Enable(GLEnum.ScissorTest);
#if !GLES && !LEGACY
            _gl.Disable(GLEnum.PrimitiveRestart);
            _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);
#endif

            float L = drawDataPtr.DisplayPos.X;
            float R = drawDataPtr.DisplayPos.X + drawDataPtr.DisplaySize.X;
            float T = drawDataPtr.DisplayPos.Y;
            float B = drawDataPtr.DisplayPos.Y + drawDataPtr.DisplaySize.Y;

            Span<float> orthoProjection = stackalloc float[] {
                2.0f / (R - L), 0.0f, 0.0f, 0.0f,
                0.0f, 2.0f / (T - B), 0.0f, 0.0f,
                0.0f, 0.0f, -1.0f, 0.0f,
                (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f,
            };

            _shader.UseShader();
            _gl.Uniform1(_attribLocationTex, 0);
            _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);
            _gl.CheckGlError("Projection");

            _gl.BindSampler(0, 0);

            // Setup desired GL state
            // Recreate the VAO every time (this is to easily allow multiple GL contexts to be rendered to. VAO are not shared among GL contexts)
            // The renderer would actually work without any VAO bound, but then our VertexAttrib calls would overwrite the default one currently bound.
            _vertexArrayObject = _gl.GenVertexArray();
            _gl.BindVertexArray(_vertexArrayObject);
            _gl.CheckGlError("VAO");

            // Bind vertex/index buffers and setup attributes for ImDrawVert
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementsHandle);
            _gl.EnableVertexAttribArray((uint) _attribLocationVtxPos);
            _gl.EnableVertexAttribArray((uint) _attribLocationVtxUV);
            _gl.EnableVertexAttribArray((uint) _attribLocationVtxColor);
            _gl.VertexAttribPointer((uint) _attribLocationVtxPos, 2, GLEnum.Float, false, (uint) sizeof(ImDrawVert), (void*) 0);
            _gl.VertexAttribPointer((uint) _attribLocationVtxUV, 2, GLEnum.Float, false, (uint) sizeof(ImDrawVert), (void*) 8);
            _gl.VertexAttribPointer((uint) _attribLocationVtxColor, 4, GLEnum.UnsignedByte, true, (uint) sizeof(ImDrawVert), (void*) 16);
        }

        private unsafe void RenderImDrawData(ImDrawDataPtr drawDataPtr)
        {
            int framebufferWidth = (int) (drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
            int framebufferHeight = (int) (drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
            if (framebufferWidth <= 0 || framebufferHeight <= 0)
                return;

            // Backup GL state
            _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);
            _gl.ActiveTexture(GLEnum.Texture0);

            _gl.GetInteger(GLEnum.CurrentProgram, out int lastProgram);
            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);

            _gl.GetInteger(GLEnum.SamplerBinding, out int lastSampler);

            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArrayObject);

#if !GLES
            Span<int> lastPolygonMode = stackalloc int[2];
            _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);
#endif

            Span<int> lastScissorBox = stackalloc int[4];
            _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

            _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
            _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);

            _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
            _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);

            _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
            _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);

            bool lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
            bool lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
            bool lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
            bool lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
            bool lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);

#if !GLES && !LEGACY
            bool lastEnablePrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);
#endif

            SetupRenderState(drawDataPtr, framebufferWidth, framebufferHeight);

            // Will project scissor/clipping rectangles into framebuffer space
            Vector2 clipOff = drawDataPtr.DisplayPos;         // (0,0) unless using multi-viewports
            Vector2 clipScale = drawDataPtr.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

            // Render command lists
            for (int n = 0; n < drawDataPtr.CmdListsCount; n++)
            {
                ImDrawListPtr cmdListPtr = drawDataPtr.CmdLists[n];

                // Upload vertex/index buffers

                _gl.BufferData(GLEnum.ArrayBuffer, (nuint) (cmdListPtr.VtxBuffer.Size * sizeof(ImDrawVert)), (void*) cmdListPtr.VtxBuffer.Data, GLEnum.StreamDraw);
                _gl.CheckGlError($"Data Vert {n}");
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint) (cmdListPtr.IdxBuffer.Size * sizeof(ushort)), (void*) cmdListPtr.IdxBuffer.Data, GLEnum.StreamDraw);
                _gl.CheckGlError($"Data Idx {n}");

                for (int cmd_i = 0; cmd_i < cmdListPtr.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr cmdPtr = cmdListPtr.CmdBuffer[cmd_i];

                    if (cmdPtr.UserCallback != IntPtr.Zero)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        Vector4 clipRect;
                        clipRect.X = (cmdPtr.ClipRect.X - clipOff.X) * clipScale.X;
                        clipRect.Y = (cmdPtr.ClipRect.Y - clipOff.Y) * clipScale.Y;
                        clipRect.Z = (cmdPtr.ClipRect.Z - clipOff.X) * clipScale.X;
                        clipRect.W = (cmdPtr.ClipRect.W - clipOff.Y) * clipScale.Y;

                        if (clipRect.X < framebufferWidth && clipRect.Y < framebufferHeight && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
                        {
                            // Apply scissor/clipping rectangle
                            _gl.Scissor((int) clipRect.X, (int) (framebufferHeight - clipRect.W), (uint) (clipRect.Z - clipRect.X), (uint) (clipRect.W - clipRect.Y));
                            _gl.CheckGlError("Scissor");

                            // Bind texture, Draw
                            _gl.BindTexture(GLEnum.Texture2D, (uint) cmdPtr.TextureId);
                            _gl.CheckGlError("Texture");

                            _gl.DrawElementsBaseVertex(GLEnum.Triangles, cmdPtr.ElemCount, GLEnum.UnsignedShort, (void*) (cmdPtr.IdxOffset * sizeof(ushort)), (int) cmdPtr.VtxOffset);
                            _gl.CheckGlError("Draw");
                        }
                    }
                }
            }

            // Destroy the temporary VAO
            _gl.DeleteVertexArray(_vertexArrayObject);
            _vertexArrayObject = 0;

            // Restore modified GL state
            _gl.UseProgram((uint) lastProgram);
            _gl.BindTexture(GLEnum.Texture2D, (uint) lastTexture);

            _gl.BindSampler(0, (uint) lastSampler);

            _gl.ActiveTexture((GLEnum) lastActiveTexture);

            _gl.BindVertexArray((uint) lastVertexArrayObject);

            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint) lastArrayBuffer);
            _gl.BlendEquationSeparate((GLEnum) lastBlendEquationRgb, (GLEnum) lastBlendEquationAlpha);
            _gl.BlendFuncSeparate((GLEnum) lastBlendSrcRgb, (GLEnum) lastBlendDstRgb, (GLEnum) lastBlendSrcAlpha, (GLEnum) lastBlendDstAlpha);

            if (lastEnableBlend)
            {
                _gl.Enable(GLEnum.Blend);
            }
            else
            {
                _gl.Disable(GLEnum.Blend);
            }

            if (lastEnableCullFace)
            {
                _gl.Enable(GLEnum.CullFace);
            }
            else
            {
                _gl.Disable(GLEnum.CullFace);
            }

            if (lastEnableDepthTest)
            {
                _gl.Enable(GLEnum.DepthTest);
            }
            else
            {
                _gl.Disable(GLEnum.DepthTest);
            }
            if (lastEnableStencilTest)
            {
                _gl.Enable(GLEnum.StencilTest);
            }
            else
            {
                _gl.Disable(GLEnum.StencilTest);
            }

            if (lastEnableScissorTest)
            {
                _gl.Enable(GLEnum.ScissorTest);
            }
            else
            {
                _gl.Disable(GLEnum.ScissorTest);
            }

#if !GLES && !LEGACY
            if (lastEnablePrimitiveRestart)
            {
                _gl.Enable(GLEnum.PrimitiveRestart);
            }
            else
            {
                _gl.Disable(GLEnum.PrimitiveRestart);
            }

            _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum) lastPolygonMode[0]);
#endif

            _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint) lastScissorBox[2], (uint) lastScissorBox[3]);
        }

        private void CreateDeviceResources()
        {
            // Backup GL state

            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArray);

            string vertexSource =
#if GLES
                @"#version 300 es
        precision highp float;
            
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0.0,1.0);
        }";
#elif GL
                @"#version 330
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0,1);
        }";
#elif LEGACY
                @"#version 110
        attribute vec2 Position;
        attribute vec2 UV;
        attribute vec4 Color;

        uniform mat4 ProjMtx;

        varying vec2 Frag_UV;
        varying vec4 Frag_Color;

        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0,1);
        }";
#endif


            string fragmentSource =
#if GLES
                @"#version 300 es
        precision highp float;
        
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }";
#elif GL
                @"#version 330
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }";
#elif LEGACY
                @"#version 110
        varying vec2 Frag_UV;
        varying vec4 Frag_Color;

        uniform sampler2D Texture;

        void main()
        {
            gl_FragColor = Frag_Color * texture2D(Texture, Frag_UV.st);
        }";
#endif

            _shader = new Shader(_gl, vertexSource, fragmentSource);

            _attribLocationTex = _shader.GetUniformLocation("Texture");
            _attribLocationProjMtx = _shader.GetUniformLocation("ProjMtx");
            _attribLocationVtxPos = _shader.GetAttribLocation("Position");
            _attribLocationVtxUV = _shader.GetAttribLocation("UV");
            _attribLocationVtxColor = _shader.GetAttribLocation("Color");

            _vboHandle = _gl.GenBuffer();
            _elementsHandle = _gl.GenBuffer();

            RecreateFontDeviceTexture();

            // Restore modified GL state
            _gl.BindTexture(GLEnum.Texture2D, (uint) lastTexture);
            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint) lastArrayBuffer);

            _gl.BindVertexArray((uint) lastVertexArray);

            _gl.CheckGlError("End of ImGui setup");
        }

        /// <summary>
        /// Creates the texture used to render text.
        /// </summary>
        private unsafe void RecreateFontDeviceTexture()
        {
            // Build texture atlas
            var io = ImGuiNET.ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);   // Load as RGBA 32-bit (75% of the memory is wasted, but default font is so small) because it is more likely to be compatible with user's existing shaders. If your ImTextureId represent a higher-level concept than just a GL texture id, consider calling GetTexDataAsAlpha8() instead to save on GPU memory.

            // Upload texture to graphics system
            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);

            _fontTexture = new Texture(_gl, width, height, pixels);
            _fontTexture.Bind();
            _fontTexture.SetMagFilter(TextureMagFilter.Linear);
            _fontTexture.SetMinFilter(TextureMinFilter.Linear);

            // Store our identifier
            io.Fonts.SetTexID((IntPtr) _fontTexture.GlTexture);

            // Restore state
            _gl.BindTexture(GLEnum.Texture2D, (uint) lastTexture);
        }

        /// <summary>
        /// Frees all graphics resources used by the renderer.
        /// </summary>
        public void Dispose()
        {
            _view.Resize -= WindowResized;
            _keyboard.KeyChar -= OnKeyChar;

            _gl.DeleteBuffer(_vboHandle);
            _gl.DeleteBuffer(_elementsHandle);
            _gl.DeleteVertexArray(_vertexArrayObject);

            _fontTexture.Dispose();
            _shader.Dispose();

            ImGuiNET.ImGui.DestroyContext(Context);
        }
    }
}