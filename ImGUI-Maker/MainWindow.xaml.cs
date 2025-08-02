using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using WinPoint = System.Windows.Point;

namespace ImGUI_Maker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private ObservableCollection<ImGuiElement> elements;
        private ImGuiButton selectedButton;
        private bool isDragging = false;
        private WinPoint dragStart;
        private Vector2 dragOffset;
        
        private DispatcherTimer _previewTimer;
        private GraphicsDevice _graphicsDevice;
        private CommandList _commandList;
        private ImGuiRenderer _imguiRenderer;
        private Sdl2Window _previewWindow;
        private bool _previewInitialized = false;
        private WindowsFormsHost _previewHost;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApplication();

            this.StateChanged += MainWindow_StateChanged;
        }      
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_previewWindow != null && _previewWindow.Exists)
                {
                    try
                    {
                        var newWidth = (int)PreviewContainer.ActualWidth;
                        var newHeight = (int)PreviewContainer.ActualHeight;
                        if (newWidth > 0 && newHeight > 0)
                        {
                            SetWindowPos(_previewWindow.Handle, IntPtr.Zero, 0, 0, newWidth, newHeight, 0x0010 | 0x0004);
                            
                            if (_imguiRenderer != null)
                            {
                                _imguiRenderer.WindowResized(newWidth, newHeight);
                            }
                            
                            if (_graphicsDevice != null && _graphicsDevice.MainSwapchain != null)
                            {
                                _graphicsDevice.ResizeMainWindow((uint)newWidth, (uint)newHeight);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to resize preview window on state change: {ex.Message}");
                    }
                }
            }));
        }
        private void InitializeApplication()
        {
            elements = new ObservableCollection<ImGuiElement>();
            
            DesignCanvas.MouseLeftButtonDown += DesignCanvas_MouseLeftButtonDown;
            DesignCanvas.MouseMove += DesignCanvas_MouseMove;
            DesignCanvas.MouseLeftButtonUp += DesignCanvas_MouseLeftButtonUp;
            
            ButtonTextTextBox.TextChanged += PropertyChanged;
            PosXTextBox.TextChanged += PropertyChanged;
            PosYTextBox.TextChanged += PropertyChanged;
            WidthTextBox.TextChanged += PropertyChanged;
            HeightTextBox.TextChanged += PropertyChanged;
            ButtonStyleComboBox.SelectionChanged += PropertyChanged;
            
            BackgroundColorButton.Click += BackgroundColorButton_Click;
            TextColorButton.Click += TextColorButton_Click;
            
            UpdateStatus("Ready to design ImGUI buttons");
        }
        private void DesignCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            WinPoint clickPoint = e.GetPosition(DesignCanvas);
            
            foreach (var element in elements)
            {
                if (element is ImGuiButton button && IsPointInButton(clickPoint, button))
                {
                    SelectButton(button);
                    StartDragging(clickPoint, button);
                    return;
                }
            }
            
            DeselectButton();
        }
        private void DesignCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging && selectedButton != null)
            {
                WinPoint currentPoint = e.GetPosition(DesignCanvas);
                Vector2 newPosition = new Vector2(
                    (float)(currentPoint.X - dragOffset.X),
                    (float)(currentPoint.Y - dragOffset.Y)
                );
                
                newPosition.X = Math.Max(0, Math.Min(newPosition.X, (float)DesignCanvas.ActualWidth - selectedButton.Size.X));
                newPosition.Y = Math.Max(0, Math.Min(newPosition.Y, (float)DesignCanvas.ActualHeight - selectedButton.Size.Y));
                
                selectedButton.Position = newPosition;
                UpdateButtonVisual(selectedButton);
                UpdatePropertyFields();
                UpdateStatus($"Dragging button to ({newPosition.X:F0}, {newPosition.Y:F0})");
            }
        }
        private void DesignCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                UpdateStatus("Button positioned");
                GenerateCode();
            }
        }
        private bool IsPointInButton(WinPoint point, ImGuiButton button)
        {
            return point.X >= button.Position.X && 
                   point.X <= button.Position.X + button.Size.X &&
                   point.Y >= button.Position.Y && 
                   point.Y <= button.Position.Y + button.Size.Y;
        }
        private void StartDragging(WinPoint clickPoint, ImGuiButton button)
        {
            isDragging = true;
            dragStart = clickPoint;
            dragOffset = new Vector2(
                (float)(clickPoint.X - button.Position.X),
                (float)(clickPoint.Y - button.Position.Y)
            );
        }
        private void SelectButton(ImGuiButton button)
        {
            selectedButton = button;
            UpdatePropertyFields();
            UpdateButtonSelectionVisual();
            UpdateStatus($"Selected button: {button.Text}");
        }
        private void DeselectButton()
        {
            selectedButton = null;
            ClearPropertyFields();
            UpdateButtonSelectionVisual();
            UpdateStatus("No button selected");
        }
        private void UpdatePropertyFields()
        {
            if (selectedButton != null)
            {
                ButtonTextTextBox.Text = selectedButton.Text;
                PosXTextBox.Text = selectedButton.Position.X.ToString("F0");
                PosYTextBox.Text = selectedButton.Position.Y.ToString("F0");
                WidthTextBox.Text = selectedButton.Size.X.ToString("F0");
                HeightTextBox.Text = selectedButton.Size.Y.ToString("F0");
                ButtonStyleComboBox.SelectedIndex = (int)selectedButton.Style;
                BackgroundColorButton.Background = new SolidColorBrush(selectedButton.BackgroundColor);
                TextColorButton.Background = new SolidColorBrush(selectedButton.TextColor);
            }
        }
        private void ClearPropertyFields()
        {
            ButtonTextTextBox.Text = "Button";
            PosXTextBox.Text = "100";
            PosYTextBox.Text = "100";
            WidthTextBox.Text = "120";
            HeightTextBox.Text = "30";
            ButtonStyleComboBox.SelectedIndex = 0;
            BackgroundColorButton.Background = new SolidColorBrush(Colors.Green);
            TextColorButton.Background = new SolidColorBrush(Colors.White);
        }
        private void UpdateButtonSelectionVisual()
        {
            foreach (var element in elements)
            {
                if (element is ImGuiButton button)
                {
                    var visual = DesignCanvas.Children.OfType<Border>().FirstOrDefault(b => b.Tag == button);
                    
                    if (visual != null)
                    {
                        if (button == selectedButton)
                        {
                            visual.BorderBrush = new SolidColorBrush(Colors.Blue);
                            visual.BorderThickness = new Thickness(2);
                        }
                        else
                        {
                            visual.BorderBrush = new SolidColorBrush(Colors.Gray);
                            visual.BorderThickness = new Thickness(1);
                        }
                    }
                }
            }
        }
        private void AddButtonButton_Click(object sender, RoutedEventArgs e)
        {
            var newButton = new ImGuiButton
            {
                Text = ButtonTextTextBox.Text,
                Position = new Vector2(
                    float.Parse(PosXTextBox.Text),
                    float.Parse(PosYTextBox.Text)
                ),
                Size = new Vector2(
                    float.Parse(WidthTextBox.Text),
                    float.Parse(HeightTextBox.Text)
                ),
                Style = (ButtonStyle)ButtonStyleComboBox.SelectedIndex,
                BackgroundColor = ((SolidColorBrush)BackgroundColorButton.Background).Color,
                TextColor = ((SolidColorBrush)TextColorButton.Background).Color
            };

            elements.Add(newButton);
            CreateButtonVisual(newButton);
            SelectButton(newButton);
            GenerateCode();
            UpdateStatus($"Added button: {newButton.Text}");
        }
        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            elements.Clear();
            DesignCanvas.Children.Clear();
            DeselectButton();
            GenerateCode();
            UpdateStatus("Cleared all elements");
        }
        private void GenerateCodeButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateCode();
            UpdateStatus("Code generated");
        }
        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_previewInitialized)
                {
                    UpdateStatus("Initializing ImGUI preview...");
                    InitializeEmbeddedPreview();
                }
                else
                {
                    UpdateEmbeddedPreview();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error updating preview: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Preview error: {ex}");
            }
        }
        private void InitializeEmbeddedPreview()
        {
            try
            {
                var control = new System.Windows.Forms.Control();
                control.Dock = DockStyle.Fill;
                control.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
                
                _previewHost = new WindowsFormsHost();
                _previewHost.Child = control;
                
                PreviewContainer.Child = _previewHost;
                
                _previewHost.Loaded += (s, e) =>
                {
                    PreviewContainer.SizeChanged += (sender, args) =>
                    {
                        if (_previewWindow != null && _previewWindow.Exists)
                        {
                            try
                            {
                                var newWidth = (int)PreviewContainer.ActualWidth;
                                var newHeight = (int)PreviewContainer.ActualHeight;
                                if (newWidth > 0 && newHeight > 0)
                                {
                                    SetWindowPos(_previewWindow.Handle, IntPtr.Zero, 0, 0, newWidth, newHeight, 0x0010 | 0x0004);
                                    
                                    if (_imguiRenderer != null)
                                    {
                                        _imguiRenderer.WindowResized(newWidth, newHeight);
                                    }
                                    
                                    if (_graphicsDevice != null && _graphicsDevice.MainSwapchain != null)
                                    {
                                        _graphicsDevice.ResizeMainWindow((uint)newWidth, (uint)newHeight);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to resize preview window: {ex.Message}");
                            }
                        }
                    };
                    
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        try
                        {
                            var width = (int)PreviewContainer.ActualWidth;
                            var height = (int)PreviewContainer.ActualHeight;
                            
                            if (width <= 0 || height <= 0)
                            {
                                width = 400;
                                height = 300;
                            }
                            
                            var windowCI = new WindowCreateInfo(0, 0, width, height, Veldrid.WindowState.Normal, "Embedded Preview");
                            windowCI.WindowInitialState = Veldrid.WindowState.Normal;
                            
                            _previewWindow = VeldridStartup.CreateWindow(ref windowCI);
                            
                            try
                            {
                                var hwnd = _previewWindow.Handle;
                                var controlHandle = control.Handle;
                                
                                SetParent(hwnd, controlHandle);
                                
                                var style = GetWindowLong(hwnd, -16); // GWL_STYLE
                                style &= ~(0x00C00000 | 0x00080000 | 0x00040000); // Remove WS_CAPTION | WS_SYSMENU | WS_THICKFRAME
                                style |= 0x40000000; // Add WS_CHILD
                                SetWindowLong(hwnd, -16, style);
                                
                                var exStyle = GetWindowLong(hwnd, -20); // GWL_EXSTYLE
                                exStyle &= ~(0x00010000 | 0x00020000); // Remove WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE
                                SetWindowLong(hwnd, -20, exStyle);
                                
                                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, 0x0010 | 0x0004 | 0x0001); // SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
                                
                                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004); // SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to embed window: {ex.Message}");
                            }
                            
                            GraphicsDevice graphicsDevice = null;
                            Exception lastException = null;
                            
                            var backends = new[] { GraphicsBackend.Direct3D11, GraphicsBackend.OpenGL, GraphicsBackend.Vulkan };
                            
                            foreach (var backend in backends)
                            {
                                try
                                {
                                    graphicsDevice = VeldridStartup.CreateGraphicsDevice(_previewWindow, backend);
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    lastException = ex;
                                    System.Diagnostics.Debug.WriteLine($"Failed to create graphics device with {backend}: {ex.Message}");
                                }
                            }
                            
                            if (graphicsDevice == null)
                            {
                                throw new Exception($"Failed to create graphics device with any backend. Last error: {lastException?.Message}");
                            }
                            
                            _graphicsDevice = graphicsDevice;
                            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
                            _imguiRenderer = new ImGuiRenderer(_graphicsDevice, _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription, width, height);
                            
                            _previewTimer = new DispatcherTimer();
                            _previewTimer.Interval = TimeSpan.FromMilliseconds(16);
                            _previewTimer.Tick += PreviewTimer_Tick;
                            _previewTimer.Start();
                            
                            _previewInitialized = true;
                            
                            if (elements.Count == 0)
                            {
                                var testButton = new ImGuiButton
                                {
                                    Text = "Test Button",
                                    Position = new Vector2(50, 50),
                                    Size = new Vector2(120, 30),
                                    Style = ButtonStyle.Default,
                                    BackgroundColor = Colors.Green,
                                    TextColor = Colors.White
                                };
                                elements.Add(testButton);
                            }
                            
                            UpdateStatus("Embedded ImGUI preview initialized");
                        }
                        catch (Exception ex)
                        {
                            UpdateStatus($"Error initializing embedded preview: {ex.Message}");
                        }
                    }));
                };
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error setting up embedded preview: {ex.Message}");
            }
        }
        private void UpdateEmbeddedPreview()
        {
            UpdateStatus("Preview updated with current elements");
        }
        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            if (!_previewInitialized || _previewWindow?.Exists != true || _graphicsDevice == null) return;

            try
            {
                var snapshot = _previewWindow.PumpEvents();
                if (!_previewWindow.Exists) return;

                _imguiRenderer.Update(1f / 60f, snapshot);

                ImGui.NewFrame();
                RenderImGuiElements();
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
            }
        }
        private float GetPreviewScale()
        {
            var width = (int)PreviewContainer.ActualWidth;
            var height = (int)PreviewContainer.ActualHeight;
            var designCanvasWidth = 800f;
            var designCanvasHeight = 400f;
            var scaleX = width / designCanvasWidth;
            var scaleY = height / designCanvasHeight;
            return Math.Min(scaleX, scaleY);
        }    
        private void RenderImGuiElements()
        {
            var width = (int)PreviewContainer.ActualWidth;
            var height = (int)PreviewContainer.ActualHeight;
            
            if (width <= 0) width = 400;
            if (height <= 0) height = 300;
            
            ImGui.Begin("Element Preview", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);
            ImGui.SetWindowPos(new Vector2(0, 0));
            ImGui.SetWindowSize(new Vector2(width, height));

            ImGui.Text($"Elements count: {elements?.Count ?? 0}");
            
            if (elements != null && elements.Count > 0)
            {
                var scale = GetPreviewScale();
                
                foreach (var element in elements)
                {
                    var scaledX = element.Position.X * scale + 10;
                    var scaledY = element.Position.Y * scale + 30;
                    ImGui.SetCursorPos(new Vector2(scaledX, scaledY));

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
            }
            else
            {
                ImGui.Text("No elements to display. Add some elements first!");
            }

            ImGui.End();
        }
        private void RenderButton(ImGuiButton button)
        {
            var scale = GetPreviewScale();
            var scaledSize = button.Size * scale;
            
            if (button.Style == ButtonStyle.Small)
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
            else if (button.Style == ButtonStyle.Large)
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 8));

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

            ImGui.Button(button.Text, scaledSize);

            if (button.Style == ButtonStyle.Invisible)
                ImGui.PopStyleColor(3);
            else
                ImGui.PopStyleColor(2);

            if (button.Style != ButtonStyle.Default)
                ImGui.PopStyleVar();
        }
        private void RenderText(ImGuiText text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(text.TextColor.R/255f, text.TextColor.G/255f, text.TextColor.B/255f, 1.0f));
            ImGui.Text(text.Text);
            ImGui.PopStyleColor();
        }
        private void RenderInputText(ImGuiInputText input)
        {
            var buffer = input.Value ?? "";
            var bufferArray = new byte[input.MaxLength];
            var bytes = System.Text.Encoding.UTF8.GetBytes(buffer);
            Array.Copy(bytes, bufferArray, Math.Min(bytes.Length, bufferArray.Length - 1));
            
            if (input.IsPassword)
                ImGui.InputText(input.Label, bufferArray, (uint)bufferArray.Length, ImGuiInputTextFlags.Password);
            else
                ImGui.InputText(input.Label, bufferArray, (uint)bufferArray.Length);
        }
        private void RenderCheckbox(ImGuiCheckbox checkbox)
        {
            var isChecked = checkbox.IsChecked;
            ImGui.Checkbox(checkbox.Label, ref isChecked);
        }
        private void RenderSlider(ImGuiSlider slider)
        {
            var value = slider.Value;
            ImGui.SliderFloat(slider.Label, ref value, slider.MinValue, slider.MaxValue);
        }
        private void RenderComboBox(ImGuiComboBox comboBox)
        {
            var selectedIndex = comboBox.SelectedIndex;
            if (comboBox.Items != null && comboBox.Items.Length > 0)
            {
                ImGui.Combo(comboBox.Label, ref selectedIndex, comboBox.Items, comboBox.Items.Length);
            }
        }
        private void PropertyChanged(object sender, EventArgs e)
        {
            if (selectedButton != null)
            {
                try
                {
                    selectedButton.Text = ButtonTextTextBox.Text;
                    selectedButton.Position = new Vector2(
                        float.Parse(PosXTextBox.Text),
                        float.Parse(PosYTextBox.Text)
                    );
                    selectedButton.Size = new Vector2(
                        float.Parse(WidthTextBox.Text),
                        float.Parse(HeightTextBox.Text)
                    );
                    selectedButton.Style = (ButtonStyle)ButtonStyleComboBox.SelectedIndex;
                    
                    UpdateButtonVisual(selectedButton);
                    GenerateCode();
                }
                catch (FormatException)
                {

                }
            }
        }
        private void BackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = Color.FromRgb(colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                BackgroundColorButton.Background = new SolidColorBrush(color);
                
                if (selectedButton != null)
                {
                    selectedButton.BackgroundColor = color;
                    UpdateButtonVisual(selectedButton);
                    GenerateCode();
                }
            }
        }
        private void TextColorButton_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = Color.FromRgb(colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                TextColorButton.Background = new SolidColorBrush(color);
                
                if (selectedButton != null)
                {
                    selectedButton.TextColor = color;
                    UpdateButtonVisual(selectedButton);
                    GenerateCode();
                }
            }
        }
        private void CreateButtonVisual(ImGuiButton button)
        {
            var border = new Border
            {
                Width = button.Size.X,
                Height = button.Size.Y,
                Background = new SolidColorBrush(button.BackgroundColor),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Tag = button
            };

            var textBlock = new TextBlock
            {
                Text = button.Text,
                Foreground = new SolidColorBrush(button.TextColor),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };

            border.Child = textBlock;
            
            Canvas.SetLeft(border, button.Position.X);
            Canvas.SetTop(border, button.Position.Y);
            
            DesignCanvas.Children.Add(border);
        }
        private void UpdateButtonVisual(ImGuiButton button)
        {
            var visual = DesignCanvas.Children.OfType<Border>().FirstOrDefault(b => b.Tag == button);
            
            if (visual != null)
            {
                visual.Width = button.Size.X;
                visual.Height = button.Size.Y;
                visual.Background = new SolidColorBrush(button.BackgroundColor);
                
                if (visual.Child is TextBlock textBlock)
                {
                    textBlock.Text = button.Text;
                    textBlock.Foreground = new SolidColorBrush(button.TextColor);
                }
                
                Canvas.SetLeft(visual, button.Position.X);
                Canvas.SetTop(visual, button.Position.Y);
            }
        }
        private void GenerateCode()
        {
            var codeBuilder = new StringBuilder();
            
            codeBuilder.AppendLine("// ImGUI Code Generated by ImGUI Maker");
            codeBuilder.AppendLine("// Add this code to your ImGUI render loop");
            codeBuilder.AppendLine();
            
            if (elements.Count == 0)
            {
                codeBuilder.AppendLine("// No elements defined");
                codeBuilder.AppendLine("// Use the designer to add elements");
            }
            else
            {
                foreach (var element in elements)
                {
                    codeBuilder.AppendLine($"// {element.ElementType}: {element.Id ?? "Unnamed"}");
                    codeBuilder.AppendLine($"ImGui.SetCursorPos(new Vector2({element.Position.X}f, {element.Position.Y}f));");
                    
                    switch (element.ElementType)
                    {
                        case ImGuiElementType.Button:
                            GenerateButtonCode(codeBuilder, (ImGuiButton)element);
                            break;
                        case ImGuiElementType.Text:
                            GenerateTextCode(codeBuilder, (ImGuiText)element);
                            break;
                        case ImGuiElementType.InputText:
                            GenerateInputTextCode(codeBuilder, (ImGuiInputText)element);
                            break;
                        case ImGuiElementType.Checkbox:
                            GenerateCheckboxCode(codeBuilder, (ImGuiCheckbox)element);
                            break;
                        case ImGuiElementType.Slider:
                            GenerateSliderCode(codeBuilder, (ImGuiSlider)element);
                            break;
                        case ImGuiElementType.ComboBox:
                            GenerateComboBoxCode(codeBuilder, (ImGuiComboBox)element);
                            break;
                    }
                    
                    codeBuilder.AppendLine();
                }
            }
            
            CodePreviewTextBox.Text = codeBuilder.ToString();
        }
        private void GenerateButtonCode(StringBuilder codeBuilder, ImGuiButton button)
        {
            if (button.Style == ButtonStyle.Small)
            {
                codeBuilder.AppendLine("ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));");
            }
            else if (button.Style == ButtonStyle.Large)
            {
                codeBuilder.AppendLine("ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 8));");
            }
            
            if (button.Style == ButtonStyle.Invisible)
            {
                codeBuilder.AppendLine("ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));");
                codeBuilder.AppendLine("ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));");
                codeBuilder.AppendLine("ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));");
            }
            else
            {
                var bgColor = button.BackgroundColor;
                var textColor = button.TextColor;
                
                codeBuilder.AppendLine($"ImGui.PushStyleColor(ImGuiCol.Button, new Vector4({bgColor.R/255f}f, {bgColor.G/255f}f, {bgColor.B/255f}f, 1.0f));");
                codeBuilder.AppendLine($"ImGui.PushStyleColor(ImGuiCol.Text, new Vector4({textColor.R/255f}f, {textColor.G/255f}f, {textColor.B/255f}f, 1.0f));");
            }
            
            codeBuilder.AppendLine($"if (ImGui.Button(\"{button.Text}\", new Vector2({button.Size.X}f, {button.Size.Y}f)))");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine($"    // Handle button click for: {button.Text}");
            codeBuilder.AppendLine("}");
            
            if (button.Style != ButtonStyle.Default)
            {
                codeBuilder.AppendLine("ImGui.PopStyleVar();");
            }
            
            if (button.Style == ButtonStyle.Invisible)
            {
                codeBuilder.AppendLine("ImGui.PopStyleColor(3);");
            }
            else
            {
                codeBuilder.AppendLine("ImGui.PopStyleColor(2);");
            }
        }
        private void GenerateTextCode(StringBuilder codeBuilder, ImGuiText text)
        {
            var textColor = text.TextColor;
            codeBuilder.AppendLine($"ImGui.PushStyleColor(ImGuiCol.Text, new Vector4({textColor.R/255f}f, {textColor.G/255f}f, {textColor.B/255f}f, 1.0f));");
            codeBuilder.AppendLine($"ImGui.Text(\"{text.Text}\");");
            codeBuilder.AppendLine("ImGui.PopStyleColor();");
        }
        private void GenerateInputTextCode(StringBuilder codeBuilder, ImGuiInputText inputText)
        {
            codeBuilder.AppendLine($"string {inputText.Label?.Replace(" ", "").ToLower() ?? "input"}Value = \"{inputText.Value ?? ""}\";");
            
            if (inputText.IsPassword)
            {
                codeBuilder.AppendLine($"ImGui.InputText(\"{inputText.Label}\", ref {inputText.Label?.Replace(" ", "").ToLower() ?? "input"}Value, {inputText.MaxLength}, ImGuiInputTextFlags.Password);");
            }
            else
            {
                codeBuilder.AppendLine($"ImGui.InputText(\"{inputText.Label}\", ref {inputText.Label?.Replace(" ", "").ToLower() ?? "input"}Value, {inputText.MaxLength});");
            }
        }
        private void GenerateCheckboxCode(StringBuilder codeBuilder, ImGuiCheckbox checkbox)
        {
            codeBuilder.AppendLine($"bool {checkbox.Label?.Replace(" ", "").ToLower() ?? "checkbox"}Value = {checkbox.IsChecked.ToString().ToLower()};");
            codeBuilder.AppendLine($"ImGui.Checkbox(\"{checkbox.Label}\", ref {checkbox.Label?.Replace(" ", "").ToLower() ?? "checkbox"}Value);");
        }
        private void GenerateSliderCode(StringBuilder codeBuilder, ImGuiSlider slider)
        {
            codeBuilder.AppendLine($"float {slider.Label?.Replace(" ", "").ToLower() ?? "slider"}Value = {slider.Value}f;");
            codeBuilder.AppendLine($"ImGui.SliderFloat(\"{slider.Label}\", ref {slider.Label?.Replace(" ", "").ToLower() ?? "slider"}Value, {slider.MinValue}f, {slider.MaxValue}f);");
        }
        private void GenerateComboBoxCode(StringBuilder codeBuilder, ImGuiComboBox comboBox)
        {
            if (comboBox.Items != null && comboBox.Items.Length > 0)
            {
                codeBuilder.AppendLine($"string[] {comboBox.Label?.Replace(" ", "").ToLower() ?? "combo"}Items = {{ {string.Join(", ", comboBox.Items.Select(item => $"\"{item}\""))} }};");
                codeBuilder.AppendLine($"int {comboBox.Label?.Replace(" ", "").ToLower() ?? "combo"}Selected = {comboBox.SelectedIndex};");
                codeBuilder.AppendLine($"ImGui.Combo(\"{comboBox.Label}\", ref {comboBox.Label?.Replace(" ", "").ToLower() ?? "combo"}Selected, {comboBox.Label?.Replace(" ", "").ToLower() ?? "combo"}Items, {comboBox.Items.Length});");
            }
        }
        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsNumeric(e.Text);
        }
        private bool IsNumeric(string text)
        {
            return text.All(char.IsDigit) || text == "-" || text == ".";
        }
        private void AddElementButton_Click(object sender, RoutedEventArgs e)
        {
            AddButtonButton_Click(sender, e);
        }
        private void ElementTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ElementTypeComboBox == null) return;
            
            if (ButtonPropertiesPanel != null)
                ButtonPropertiesPanel.Visibility = Visibility.Collapsed;
            if (TextPropertiesPanel != null)
                TextPropertiesPanel.Visibility = Visibility.Collapsed;
            if (InputTextPropertiesPanel != null)
                InputTextPropertiesPanel.Visibility = Visibility.Collapsed;
            if (CheckboxPropertiesPanel != null)
                CheckboxPropertiesPanel.Visibility = Visibility.Collapsed;
            if (SliderPropertiesPanel != null)
                SliderPropertiesPanel.Visibility = Visibility.Collapsed;
            if (ComboBoxPropertiesPanel != null)
                ComboBoxPropertiesPanel.Visibility = Visibility.Collapsed;
            
            switch (ElementTypeComboBox.SelectedIndex)
            {
                case 0: // Button
                    if (ButtonPropertiesPanel != null)
                        ButtonPropertiesPanel.Visibility = Visibility.Visible;
                    break;
                case 1: // Text
                    if (TextPropertiesPanel != null)
                        TextPropertiesPanel.Visibility = Visibility.Visible;
                    break;
                case 2: // Input Text
                    if (InputTextPropertiesPanel != null)
                        InputTextPropertiesPanel.Visibility = Visibility.Visible;
                    break;
                case 3: // Checkbox
                    if (CheckboxPropertiesPanel != null)
                        CheckboxPropertiesPanel.Visibility = Visibility.Visible;
                    break;
                case 4: // Slider
                    if (SliderPropertiesPanel != null)
                        SliderPropertiesPanel.Visibility = Visibility.Visible;
                    break;
                case 5: // ComboBox
                    if (ComboBoxPropertiesPanel != null)
                        ComboBoxPropertiesPanel.Visibility = Visibility.Visible;
                    break;
            }
        }
    }
    public abstract class ImGuiElement : INotifyPropertyChanged
    {
        private Vector2 position;
        private string id;

        public Vector2 Position
        {
            get => position;
            set
            {
                position = value;
                OnPropertyChanged(nameof(Position));
            }
        }

        public string Id
        {
            get => id;
            set
            {
                id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        public abstract ImGuiElementType ElementType { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ImGuiButton : ImGuiElement
    {
        private string text;
        private Vector2 size;
        private ButtonStyle style;
        private Color backgroundColor;
        private Color textColor;

        public override ImGuiElementType ElementType => ImGuiElementType.Button;

        public string Text
        {
            get => text;
            set
            {
                text = value;
                OnPropertyChanged(nameof(Text));
            }
        }

        public Vector2 Size
        {
            get => size;
            set
            {
                size = value;
                OnPropertyChanged(nameof(Size));
            }
        }

        public ButtonStyle Style
        {
            get => style;
            set
            {
                style = value;
                OnPropertyChanged(nameof(Style));
            }
        }

        public Color BackgroundColor
        {
            get => backgroundColor;
            set
            {
                backgroundColor = value;
                OnPropertyChanged(nameof(BackgroundColor));
            }
        }

        public Color TextColor
        {
            get => textColor;
            set
            {
                textColor = value;
                OnPropertyChanged(nameof(TextColor));
            }
        }
    }

    public class ImGuiText : ImGuiElement
    {
        private string text;
        private Color textColor;
        private float fontSize;
        private bool isBold;

        public override ImGuiElementType ElementType => ImGuiElementType.Text;

        public string Text
        {
            get => text;
            set
            {
                text = value;
                OnPropertyChanged(nameof(Text));
            }
        }

        public Color TextColor
        {
            get => textColor;
            set
            {
                textColor = value;
                OnPropertyChanged(nameof(TextColor));
            }
        }

        public float FontSize
        {
            get => fontSize;
            set
            {
                fontSize = value;
                OnPropertyChanged(nameof(FontSize));
            }
        }

        public bool IsBold
        {
            get => isBold;
            set
            {
                isBold = value;
                OnPropertyChanged(nameof(IsBold));
            }
        }
    }

    public class ImGuiInputText : ImGuiElement
    {
        private string label;
        private string value;
        private Vector2 size;
        private int maxLength;
        private bool isPassword;

        public override ImGuiElementType ElementType => ImGuiElementType.InputText;

        public string Label
        {
            get => label;
            set
            {
                label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public string Value
        {
            get => value;
            set
            {
                this.value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public Vector2 Size
        {
            get => size;
            set
            {
                size = value;
                OnPropertyChanged(nameof(Size));
            }
        }

        public int MaxLength
        {
            get => maxLength;
            set
            {
                maxLength = value;
                OnPropertyChanged(nameof(MaxLength));
            }
        }

        public bool IsPassword
        {
            get => isPassword;
            set
            {
                isPassword = value;
                OnPropertyChanged(nameof(IsPassword));
            }
        }
    }

    public class ImGuiCheckbox : ImGuiElement
    {
        private string label;
        private bool isChecked;

        public override ImGuiElementType ElementType => ImGuiElementType.Checkbox;

        public string Label
        {
            get => label;
            set
            {
                label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public bool IsChecked
        {
            get => isChecked;
            set
            {
                isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }
    }

    public class ImGuiSlider : ImGuiElement
    {
        private string label;
        private float value;
        private float minValue;
        private float maxValue;
        private Vector2 size;

        public override ImGuiElementType ElementType => ImGuiElementType.Slider;

        public string Label
        {
            get => label;
            set
            {
                label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public float Value
        {
            get => value;
            set
            {
                this.value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public float MinValue
        {
            get => minValue;
            set
            {
                minValue = value;
                OnPropertyChanged(nameof(MinValue));
            }
        }

        public float MaxValue
        {
            get => maxValue;
            set
            {
                maxValue = value;
                OnPropertyChanged(nameof(MaxValue));
            }
        }

        public Vector2 Size
        {
            get => size;
            set
            {
                size = value;
                OnPropertyChanged(nameof(Size));
            }
        }
    }

    public class ImGuiComboBox : ImGuiElement
    {
        private string label;
        private string[] items;
        private int selectedIndex;
        private Vector2 size;

        public override ImGuiElementType ElementType => ImGuiElementType.ComboBox;

        public string Label
        {
            get => label;
            set
            {
                label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public string[] Items
        {
            get => items;
            set
            {
                items = value;
                OnPropertyChanged(nameof(Items));
            }
        }

        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                selectedIndex = value;
                OnPropertyChanged(nameof(SelectedIndex));
            }
        }

        public Vector2 Size
        {
            get => size;
            set
            {
                size = value;
                OnPropertyChanged(nameof(Size));
            }
        }
    }

    public enum ImGuiElementType
    {
        Button,
        Text,
        InputText,
        Checkbox,
        Slider,
        ComboBox
    }

    public enum ButtonStyle
    {
        Default,
        Small,
        Large,
        Invisible
    }
}
