using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Threading;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using MessageBox = System.Windows.MessageBox;

namespace ImGUI_Maker
{
    public partial class ImGuiPreviewWindow : Window
    {
        private List<ImGuiElement> _elements;
        private DispatcherTimer _renderTimer;
        private GraphicsDevice _graphicsDevice;
        private CommandList _commandList;
        private ImGuiRenderer _imguiRenderer;
        private Sdl2Window _window;
        private bool _isInitialized = false;

        public ImGuiPreviewWindow()
        {
            InitializeComponent();
            InitializePreview();
        }

        private void InitializePreview()
        {
            _elements = new List<ImGuiElement>();
            
            try
            {
                var windowCI = new WindowCreateInfo(50, 50, 800, 600, Veldrid.WindowState.Normal, "ImGui Preview");
                _window = VeldridStartup.CreateWindow(ref windowCI);
                
                _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window, GraphicsBackend.Direct3D11);
                
                _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
                
                _imguiRenderer = new ImGuiRenderer(_graphicsDevice, _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription, 800, 600);
                
                _renderTimer = new DispatcherTimer();
                _renderTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
                _renderTimer.Tick += RenderTimer_Tick;
                _renderTimer.Start();
                
                _isInitialized = true;
                UpdateStatus("ImGui preview initialized with Veldrid");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error initializing preview: {ex.Message}");
            }
        }
        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            if (!_isInitialized || _window.Exists == false) return;

            try
            {
                var snapshot = _window.PumpEvents();
                if (!_window.Exists) return;

                _imguiRenderer.Update(1f / 60f, snapshot);

                ImGui.NewFrame();

                RenderElements();

                ImGui.Render();

                _commandList.Begin();
                _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
                _commandList.ClearColorTarget(0, RgbaFloat.Black);
                _imguiRenderer.Render(_graphicsDevice, _commandList);
                _commandList.End();
                _graphicsDevice.SubmitCommands(_commandList);
                _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Render error: {ex.Message}");
            }
        }
        private void RenderElements()
        {
            ImGui.Begin("ImGui Preview", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
            ImGui.SetWindowPos(new Vector2(10, 10));
            ImGui.SetWindowSize(new Vector2(780, 580));
            
            foreach (var element in _elements)
            {
                ImGui.SetCursorPos(new Vector2(element.Position.X + 10, element.Position.Y + 30));
                
                switch (element.ElementType)
                {
                    case ImGuiElementType.Button:
                        RenderButton((ImGuiButton)element);
                        break;
                    case ImGuiElementType.Text:
                        RenderText((ImGuiText)element);
                        break;
                    case ImGuiElementType.InputText:
                        RenderInputText((ImGuiInputText)element);
                        break;
                    case ImGuiElementType.Checkbox:
                        RenderCheckbox((ImGuiCheckbox)element);
                        break;
                    case ImGuiElementType.Slider:
                        RenderSlider((ImGuiSlider)element);
                        break;
                    case ImGuiElementType.ComboBox:
                        RenderComboBox((ImGuiComboBox)element);
                        break;
                }
            }
            
            ImGui.End();
        }
        private void RenderButton(ImGuiButton button)
        {
            if (button.Style == ButtonStyle.Small)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
            }
            else if (button.Style == ButtonStyle.Large)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 8));
            }
            
            if (button.Style == ButtonStyle.Invisible)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));
            }
            else
            {
                var bgColor = button.BackgroundColor;
                var textColor = button.TextColor;
                
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(bgColor.R/255f, bgColor.G/255f, bgColor.B/255f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(textColor.R/255f, textColor.G/255f, textColor.B/255f, 1.0f));
            }
            
            if (ImGui.Button(button.Text, button.Size))
            {
                MessageBox.Show($"Button '{button.Text}' clicked!", "Button Click", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            
            if (button.Style == ButtonStyle.Invisible)
            {
                ImGui.PopStyleColor(3);
            }
            else
            {
                ImGui.PopStyleColor(2);
            }
            
            if (button.Style != ButtonStyle.Default)
            {
                ImGui.PopStyleVar();
            }
        }
        private void RenderText(ImGuiText text)
        {
            if (text.IsBold)
            {
                ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]);
            }
            
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(text.TextColor.R/255f, text.TextColor.G/255f, text.TextColor.B/255f, 1.0f));
            ImGui.Text(text.Text);
            ImGui.PopStyleColor();
            
            if (text.IsBold)
            {
                ImGui.PopFont();
            }
        }
        private void RenderInputText(ImGuiInputText inputText)
        {
            string value = inputText.Value ?? "";
            
            if (inputText.IsPassword)
            {
                if (ImGui.InputText(inputText.Label, ref value, (uint)inputText.MaxLength, ImGuiInputTextFlags.Password))
                {
                    inputText.Value = value;
                }
            }
            else
            {
                if (ImGui.InputText(inputText.Label, ref value, (uint)inputText.MaxLength))
                {
                    inputText.Value = value;
                }
            }
        }
        private void RenderCheckbox(ImGuiCheckbox checkbox)
        {
            bool isChecked = checkbox.IsChecked;
            if (ImGui.Checkbox(checkbox.Label, ref isChecked))
            {
                checkbox.IsChecked = isChecked;
            }
        }
        private void RenderSlider(ImGuiSlider slider)
        {
            float value = slider.Value;
            if (ImGui.SliderFloat(slider.Label, ref value, slider.MinValue, slider.MaxValue))
            {
                slider.Value = value;
            }
        }
        private void RenderComboBox(ImGuiComboBox comboBox)
        {
            if (comboBox.Items != null && comboBox.Items.Length > 0)
            {
                int selectedIndex = comboBox.SelectedIndex;
                if (ImGui.Combo(comboBox.Label, ref selectedIndex, comboBox.Items, comboBox.Items.Length))
                {
                    comboBox.SelectedIndex = selectedIndex;
                }
            }
        }
        public void UpdateElements(List<ImGuiElement> elements)
        {
            _elements = new List<ImGuiElement>(elements);
            UpdateStatus($"Updated preview with {elements.Count} elements");
        }
        public void UpdateButtons(List<ImGuiButton> buttons)
        {
            _elements = new List<ImGuiElement>(buttons);
            UpdateStatus($"Updated preview with {buttons.Count} buttons");
        }
        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }
        protected override void OnClosed(EventArgs e)
        {
            _renderTimer?.Stop();
            
            if (_isInitialized)
            {
                _imguiRenderer?.Dispose();
                _commandList?.Dispose();
                _graphicsDevice?.Dispose();
                _window?.Close();
            }
            
            base.OnClosed(e);
        }
    }
} 