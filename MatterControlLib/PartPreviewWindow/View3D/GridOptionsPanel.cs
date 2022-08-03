﻿/*
Copyright (c) 2017, Lars Brubaker, John Lewin
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
using System;
using System.Collections.Generic;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.CustomWidgets;

namespace MatterHackers.MatterControl.PartPreviewWindow
{
	public class GridOptionsPanel : DropButton
	{
		Object3DControlsLayer object3DControlLayer;
		private GuiWidget textButton;
		private PopupMenu popupMenu;

		public GridOptionsPanel(Object3DControlsLayer object3DControlLayer, ThemeConfig theme)
			: base(theme)
		{
			this.object3DControlLayer = object3DControlLayer;
			this.PopupContent = () => ShowGridOptions(theme);

			var gridDistance = object3DControlLayer.SnapGridDistance;

			textButton = this.AddChild(new ThemedTextButton(gridDistance.ToString(), theme)
			{
				Selectable = false,
				HAnchor = HAnchor.Center
			});
			this.VAnchor = VAnchor.Fit;
			// make sure the button is square
			this.Width = this.Height;

			UserSettings.Instance.SettingChanged += UserSettings_SettingChanged;
		
			SetToolTip();
		}

		public override void OnClosed(EventArgs e)
		{
			// Unregister listener
			UserSettings.Instance.SettingChanged -= UserSettings_SettingChanged;

			base.OnClosed(e);
		}

		private void UserSettings_SettingChanged(object sender, StringEventArgs e)
		{
			if (e.Data == UserSettingsKey.SnapGridDistance)
			{
				SetToolTip();
			}
		}

		private void SetToolTip()
		{
			var distance = object3DControlLayer.SnapGridDistance;
			if (distance == 0)
			{
				textButton.Text = "-";
				ToolTipText = "Snapping Turned Off".Localize();
			}
			else
			{
				textButton.Text = distance.ToString().TrimStart('0');
				ToolTipText = "Snap Grid".Localize() + " = " + textButton.Text;
			}

			popupMenu?.Close();
			popupMenu = null;
		}

		private GuiWidget ShowGridOptions(ThemeConfig theme)
		{
			popupMenu = new PopupMenu(ApplicationController.Instance.MenuTheme)
			{
				HAnchor = HAnchor.Absolute,
				Width = 80 * GuiWidget.DeviceScale
			};

			var siblingList = new List<GuiWidget>();

			popupMenu.CreateBoolMenuItem(
				"Off".Localize(),
				() => object3DControlLayer.SnapGridDistance ==  0,
				(isChecked) =>
				{
					object3DControlLayer.SnapGridDistance = 0;
				},
				useRadioStyle: true,
				siblingRadioButtonList: siblingList);

			var snapSettings = new List<double>()
			{
				.1, .25, .5, 1, 2, 5
			};

			foreach (var snap in snapSettings)
			{
				popupMenu.CreateBoolMenuItem(
					snap.ToString(),
					() => object3DControlLayer.SnapGridDistance == snap,
					(isChecked) =>
					{
						object3DControlLayer.SnapGridDistance =  snap;
					},
					useRadioStyle: true,
					siblingRadioButtonList: siblingList);
			}

			return popupMenu;
		}
	}
}