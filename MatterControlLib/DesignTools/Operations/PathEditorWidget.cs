﻿/*
Copyright (c) 2018, Lars Brubaker, John Lewin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using MatterHackers.Agg;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.Transform;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.VertexSource;
using MatterHackers.ImageProcessing;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;
using System;
using System.Linq;

namespace MatterHackers.MatterControl.DesignTools
{
    public class PathEditorWidget : GuiWidget
    {
        public enum ETransformState
        {
            Edit,
            Move,
            Scale
        };

        public enum ControlPointConstraint
        {
            Sharp,
            Aligned,
            Free
        }

        private Vector2 lastMousePosition = new Vector2(0, 0);
        private ETransformState mouseDownTransformOverride;
        private Vector2 mouseDownPosition = new Vector2(0, 0);
        private Action<Vector2, double> scaleChanged;
        private ThemeConfig theme;

        private Vector2 unscaledRenderOffset = new Vector2(0, 0);
        private Action vertexChanged;

        private VertexStorage vertexStorage;

        private ControlPointConstraint controlPointConstraint = ControlPointConstraint.Free;

        public PathEditorWidget(VertexStorage vertexStorage,
            UndoBuffer undoBuffer,
            ThemeConfig theme,
            Action vertexChanged,
            Vector2 unscaledRenderOffset = default,
            double layerScale = 1,
            Action<Vector2, double> scaleChanged = null)
        {
            HAnchor = HAnchor.Stretch;
            BackgroundOutlineWidth = 1;
            BackgroundColor = theme.BackgroundColor;
            BorderColor = theme.TextColor;
            Margin = 1;

            this.unscaledRenderOffset = unscaledRenderOffset;
            this.layerScale = layerScale;

            var topToBottom = this;

            this.scaleChanged = scaleChanged;

            SizeChanged += (s, e) =>
            {
                Height = Width * 6 / 9;
            };

            this.vertexChanged = vertexChanged;
            this.theme = theme;
            this.vertexStorage = vertexStorage;

            var toolBar = new FlowLayoutWidget()
            {
                HAnchor = HAnchor.Stretch,
                Margin = 5,
                BackgroundColor = theme.TextColor.WithAlpha(20),
            };

            toolBar.VAnchor |= VAnchor.Bottom;

            this.AddChild(toolBar);

            AddControlsToToolBar(theme, toolBar);
        }

        private void AddControlsToToolBar(ThemeConfig theme, FlowLayoutWidget toolBar)
        {
            AddButtons(theme, toolBar);

            toolBar.AddChild(new HorizontalSpacer());

            AddPositionControls(theme, toolBar);
        }

        public static readonly int VectorXYEditWidth = (int)(60 * GuiWidget.DeviceScale + .5);

        private void AddPositionControls(ThemeConfig theme, FlowLayoutWidget toolBar)
        {
            var tabIndex = 0;
            xEditWidget = new ThemedNumberEdit(0, theme, singleCharLabel: 'X', allowNegatives: true, allowDecimals: true, pixelWidth: VectorXYEditWidth, tabIndex: tabIndex)
            {
                TabIndex = tabIndex++,
                SelectAllOnFocus = true,
                Margin = theme.ButtonSpacing,
                VAnchor = VAnchor.Center,
            };
            xEditWidget.ActuallNumberEdit.InternalNumberEdit.MaxDecimalsPlaces = 3;
            xEditWidget.ActuallNumberEdit.EditComplete += (sender, e) =>
            {
                if (controlPointBeingDragged > -1)
                {
                    var vertexData = vertexStorage[controlPointBeingDragged];
                    if (vertexData.Hint == CommandHint.C4Point)
                    {
                        // the prev point
                        if (controlPointBeingDragged > 0)
                        {
                            controlPointBeingDragged--;
                            vertexData = new VertexData(vertexData.Command, new Vector2(vertexData.Position.X, xEditWidget.ActuallNumberEdit.Value), vertexData.Hint);
                            controlPointBeingDragged++;
                        }
                    }
                    else
                    {
                    }
                }
            };

            xEditWidget.ActuallNumberEdit.KeyDown += NumberField.InternalTextEditWidget_KeyDown;
            toolBar.AddChild(xEditWidget);

            yEditWidget = new ThemedNumberEdit(0, theme, 'Y', allowNegatives: true, allowDecimals: true, pixelWidth: VectorXYEditWidth, tabIndex: tabIndex)
            {
                TabIndex = tabIndex++,
                SelectAllOnFocus = true,
                VAnchor = VAnchor.Center,
                Margin = theme.ButtonSpacing,
            };
            yEditWidget.ActuallNumberEdit.InternalNumberEdit.MaxDecimalsPlaces = 3;
            yEditWidget.ActuallNumberEdit.EditComplete += (sender, e) =>
            {
            };
            yEditWidget.ActuallNumberEdit.KeyDown += NumberField.InternalTextEditWidget_KeyDown;

            toolBar.AddChild(yEditWidget);
        }

        private void AddButtons(ThemeConfig theme, FlowLayoutWidget toolBar)
        {
            var homeButton = new ThemedIconButton(StaticData.Instance.LoadIcon("fa-home_16.png", 16, 16).GrayToColor(theme.TextColor), theme)
            {
                BackgroundColor = theme.SlightShade,
                HoverColor = theme.SlightShade.WithAlpha(75),
                Margin = new BorderDouble(3, 3, 6, 3),
                ToolTipText = "Reset Zoom".Localize()
            };
            toolBar.AddChild(homeButton);

            homeButton.Click += (s, e) =>
            {
                CenterPartInView();
            };

            // the sharp corner button
            var sharpButton = new ThemedRadioTextButton("S", theme)
            {
                BackgroundColor = theme.SlightShade,
                HoverColor = theme.SlightShade.WithAlpha(75),
                Margin = new BorderDouble(3, 3, 6, 3),
                ToolTipText = "Sharp Corner".Localize(),
                Checked = controlPointConstraint == ControlPointConstraint.Sharp,
            };
            toolBar.AddChild(sharpButton);

            sharpButton.Click += (s, e) => { controlPointConstraint = ControlPointConstraint.Sharp; };

            // the aligned corner button
            var alignedButton = new ThemedRadioTextButton("A", theme)
            {
                BackgroundColor = theme.SlightShade,
                HoverColor = theme.SlightShade.WithAlpha(75),
                Margin = new BorderDouble(3, 3, 6, 3),
                ToolTipText = "Aligned Corner".Localize(),
                Checked = controlPointConstraint == ControlPointConstraint.Aligned,
            };
            toolBar.AddChild(alignedButton);

            alignedButton.Click += (s, e) => { controlPointConstraint = ControlPointConstraint.Aligned; };

            // the free button
            var freeButton = new ThemedRadioTextButton("F", theme)
            {
                BackgroundColor = theme.SlightShade,
                HoverColor = theme.SlightShade.WithAlpha(75),
                Margin = new BorderDouble(3, 3, 6, 3),
                ToolTipText = "Free".Localize(),
                Checked = controlPointConstraint == ControlPointConstraint.Free,
            };
            toolBar.AddChild(freeButton);

            freeButton.Click += (s, e) => { controlPointConstraint = ControlPointConstraint.Free; };
        }

        public ETransformState TransformState { get; set; }
        private double layerScale { get; set; } = 1;

        private Affine ScalingTransform => Affine.NewScaling(layerScale, layerScale);
        private Affine TotalTransform => Affine.NewTranslation(unscaledRenderOffset) * ScalingTransform * Affine.NewTranslation(Width / 2, Height / 2);

        private int controlPointBeingDragged = -1;
        private int selectedPointIndex;
        private int controlPointBeingHovered = -1;
        private ThemedNumberEdit yEditWidget;
        private ThemedNumberEdit xEditWidget;

        public void CenterPartInView()
        {
            if (vertexStorage != null)
            {
                var partBounds = vertexStorage.GetBounds();
                var weightedCenter = partBounds.Center;

                unscaledRenderOffset = -weightedCenter;
                layerScale = Math.Min((Height - 30) / partBounds.Height, (Width - 30) / partBounds.Width);

                Invalidate();
            }
        }

        public override void OnDraw(Graphics2D graphics2D)
        {
            new VertexSourceApplyTransform(vertexStorage, TotalTransform).RenderCurve(graphics2D, theme.TextColor, 2, true, theme.PrimaryAccentColor.Blend(theme.TextColor, .5), theme.PrimaryAccentColor);

            //if (vertexStorage.GetType().GetCustomAttributes(typeof(PathEditorFactory.ShowAxisAttribute), true).FirstOrDefault() is PathEditorFactory.ShowAxisAttribute showAxisAttribute)
            {
                var leftOrigin = new Vector2(-10000, 0);
                var rightOrigin = new Vector2(10000, 0);
                graphics2D.Line(TotalTransform.Transform(leftOrigin), TotalTransform.Transform(rightOrigin), Color.Red);
                var bottomOrigin = new Vector2(0, -10000);
                var topOrigin = new Vector2(0, 10000);
                graphics2D.Line(TotalTransform.Transform(bottomOrigin), TotalTransform.Transform(topOrigin), Color.Green);
            }

            base.OnDraw(graphics2D);
        }

        public override void OnMouseDown(MouseEventArgs mouseEvent)
        {
            base.OnMouseDown(mouseEvent);
            if (MouseCaptured)
            {
                controlPointBeingDragged = -1;

                mouseDownPosition.X = mouseEvent.X;
                mouseDownPosition.Y = mouseEvent.Y;

                lastMousePosition = mouseDownPosition;

                mouseDownTransformOverride = TransformState;

                // check if not left button
                switch (mouseEvent.Button)
                {
                    case MouseButtons.Left:
                        if (Keyboard.IsKeyDown(Keys.ControlKey))
                        {
                            if (Keyboard.IsKeyDown(Keys.Alt))
                            {
                                mouseDownTransformOverride = ETransformState.Scale;
                            }
                            else
                            {
                                mouseDownTransformOverride = ETransformState.Move;
                            }
                        }
                        else
                        {
                            // we are in edit mode, check if we are over any control points
                            controlPointBeingDragged = GetControlPointIndex(mouseEvent.Position);
                            selectedPointIndex = controlPointBeingDragged;

                            if (selectedPointIndex == -1)
                            {
                                xEditWidget.Text = "---";
                                xEditWidget.Enabled= false;
                                yEditWidget.Text = "---";
                                yEditWidget.Enabled= false;
                            }
                            else
                            {
                                xEditWidget.Enabled = true;
                                yEditWidget.Enabled = true;
                                UpdatePositionControls();
                            }
                        }
                        break;

                    case MouseButtons.Middle:
                        mouseDownTransformOverride = ETransformState.Move;
                        break;

                    case MouseButtons.Right:
                        mouseDownTransformOverride = ETransformState.Scale;
                        break;
                }
            }
        }

        private void UpdatePositionControls()
        {
            xEditWidget.Value = vertexStorage[controlPointBeingDragged].Position.X;
            yEditWidget.Value = vertexStorage[controlPointBeingDragged].Position.Y;
        }

        private int GetControlPointIndex(Vector2 mousePosition)
        {
            double hitThreshold = 10; // Threshold for considering a hit, in screen pixels

            for (int i = 0; i < vertexStorage.Count; i++)
            {
                Vector2 controlPoint = vertexStorage[i].Position + unscaledRenderOffset;
                ScalingTransform.transform(ref controlPoint);
                // we center on the scren so we have to add that in after scaling
                controlPoint += new Vector2(Width / 2, Height / 2);

                if ((controlPoint - mousePosition).Length <= hitThreshold) // Check if the mouse position is within the threshold
                {
                    return i; // Control point index
                }
            }

            return -1; // No control point found at this position
        }

        public override void OnMouseMove(MouseEventArgs mouseEvent)
        {
            base.OnMouseMove(mouseEvent);

            if (MouseCaptured)
            {
                DoTranslateAndZoom(mouseEvent);

                if (controlPointBeingDragged > -1)
                {
                    // we are dragging a control point
                    var mouseDelta = mouseEvent.Position - lastMousePosition;
                    if (mouseDelta.LengthSquared > 0)
                    {
                        ScalingTransform.inverse_transform(ref mouseDelta);
                        OffsetSelectedPoint(mouseDelta);
                        vertexChanged?.Invoke();
                        UpdatePositionControls();
                    }
                }
            }
            else
            {
                // highlight any contorl points we are over
            }

            lastMousePosition = mouseEvent.Position;
        }

        private void OffsetSelectedPoint(Vector2 delta)
        {
            if (controlPointBeingDragged < 0
                || controlPointBeingDragged >= vertexStorage.Count)
            {
                return;
            }

            var vertexData = vertexStorage[controlPointBeingDragged];

            if (vertexData.Hint == CommandHint.C4Point)
            {
                for (int i = -1; i < 2; i++)
                {
                    var pointIndex = controlPointBeingDragged + i;
                    // the prev point
                    if (pointIndex > 0
                        && pointIndex < vertexStorage.Count)
                    {
                        var vertexData2 = vertexStorage[pointIndex];
                        vertexStorage[pointIndex] = new VertexData(vertexData2.Command, vertexData2.Position + delta, vertexData2.Hint);
                    }
                }
            }
            else
            {
                // only drag the point
                vertexStorage[controlPointBeingDragged] = new VertexData(vertexData.Command, vertexData.Position + delta, vertexData.Hint);
            }
        }

        private void DoTranslateAndZoom(MouseEventArgs mouseEvent)
        {
            var mousePos = new Vector2(mouseEvent.X, mouseEvent.Y);
            var mouseDelta = mousePos - lastMousePosition;

            switch (mouseDownTransformOverride)
            {
                case ETransformState.Scale:
                    double zoomDelta = 1;
                    if (mouseDelta.Y < 0)
                    {
                        zoomDelta = 1 - (-1 * mouseDelta.Y / 100);
                    }
                    else if (mouseDelta.Y > 0)
                    {
                        zoomDelta = 1 + (1 * mouseDelta.Y / 100);
                    }

                    var mousePreScale = mouseDownPosition;
                    TotalTransform.inverse_transform(ref mousePreScale);

                    layerScale *= zoomDelta;

                    var mousePostScale = mouseDownPosition;
                    TotalTransform.inverse_transform(ref mousePostScale);

                    unscaledRenderOffset += (mousePostScale - mousePreScale);
                    scaleChanged?.Invoke(unscaledRenderOffset, layerScale);
                    break;

                case ETransformState.Move:
                    ScalingTransform.inverse_transform(ref mouseDelta);

                    unscaledRenderOffset += mouseDelta;
                    scaleChanged?.Invoke(unscaledRenderOffset, layerScale);
                    break;

                case ETransformState.Edit:
                default: // also treat everything else like an edit
                    break;
            }

            Invalidate();
        }

        public override void OnMouseWheel(MouseEventArgs mouseEvent)
        {
            base.OnMouseWheel(mouseEvent);
            if (FirstWidgetUnderMouse) // TODO: find a good way to decide if you are what the wheel is trying to do
            {
                const double deltaFor1Click = 120;
                double scaleAmount = (mouseEvent.WheelDelta / deltaFor1Click) * .1;

                ScalePartAndFixPosition(mouseEvent, layerScale + layerScale * scaleAmount);

                mouseEvent.WheelDelta = 0;

                Invalidate();
            }
        }

        public void Zoom(double scaleAmount)
        {
            ScalePartAndFixPosition(new MouseEventArgs(MouseButtons.None, 0, Width / 2, Height / 2, 0), layerScale * scaleAmount);
            Invalidate();
        }

        private void ScalePartAndFixPosition(MouseEventArgs mouseEvent, double scaleAmount)
        {
            var mousePreScale = new Vector2(mouseEvent.X, mouseEvent.Y);
            TotalTransform.inverse_transform(ref mousePreScale);

            layerScale = scaleAmount;

            var mousePostScale = new Vector2(mouseEvent.X, mouseEvent.Y);
            TotalTransform.inverse_transform(ref mousePostScale);

            unscaledRenderOffset += (mousePostScale - mousePreScale);

            scaleChanged?.Invoke(unscaledRenderOffset, layerScale);
        }
    }
}