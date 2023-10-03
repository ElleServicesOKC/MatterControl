﻿/*
Copyright (c) 2018, John Lewin
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
using System.Linq;

using MatterHackers.Agg;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.UI;
using MatterHackers.ImageProcessing;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.CustomWidgets;
using MatterHackers.MatterControl.PrinterControls.PrinterConnections;
using MatterHackers.MatterControl.SettingsManagement;
using MatterHackers.MatterControl.SlicerConfiguration;

namespace MatterHackers.MatterControl.Library.Widgets
{
	public class HardwareTreeView : TreeView
	{
		private TreeNode printersNode;
		private FlowLayoutWidget rootColumn;
		private EventHandler unregisterEvents;

		public HardwareTreeView(ThemeConfig theme)
			: base(theme)
		{
			rootColumn = new FlowLayoutWidget(FlowDirection.TopToBottom)
			{
				HAnchor = HAnchor.Stretch,
				VAnchor = VAnchor.Fit
			};
			this.AddChild(rootColumn);

			// Printers
			printersNode = new TreeNode(theme)
			{
				Text = "Printers".Localize(),
				HAnchor = HAnchor.Stretch,
				AlwaysExpandable = true,
				Image = StaticData.Instance.LoadIcon("printer.png", 16, 16).GrayToColor(theme.TextColor)
			};
			printersNode.TreeView = this;

			var forcedHeight = 20 * GuiWidget.DeviceScale;
			var mainRow = printersNode.Children.FirstOrDefault();
			mainRow.HAnchor = HAnchor.Stretch;
			mainRow.AddChild(new HorizontalSpacer());

			// add in the create pulse button
			var createPulse = new ThemedIconButton(StaticData.Instance.LoadIcon("pulse_logo.png", 18, 18).GrayToColor(theme.TextColor), theme)
			{
				Name = "Setup Pulse",
				VAnchor = VAnchor.Center,
				Margin = theme.ButtonSpacing.Clone(left: theme.ButtonSpacing.Right),
				ToolTipText = "Create Pulse".Localize(),
				Height = forcedHeight,
				Width = forcedHeight
			};
			createPulse.Click += (s, e) => UiThread.RunOnIdle(() =>
			{
				DialogWindow.Show(PrinterSetup.GetBestStartPage(PrinterSetup.StartPageOptions.ShowPulseModels));
			});
			mainRow.AddChild(createPulse);

			// add in the create printer button
			var createPrinter = new ThemedIconButton(StaticData.Instance.LoadIcon("md-add-circle_18.png", 18, 18).GrayToColor(theme.TextColor), theme)
			{
				Name = "Create Printer",
				VAnchor = VAnchor.Center,
				Margin = theme.ButtonSpacing.Clone(left: theme.ButtonSpacing.Right),
				ToolTipText = "Create Printer".Localize(),
				Height = forcedHeight,
				Width = forcedHeight
			};
			createPrinter.Click += (s, e) => UiThread.RunOnIdle(() =>
			{
				DialogWindow.Show(PrinterSetup.GetBestStartPage(PrinterSetup.StartPageOptions.ShowMakeModel));
			});
			mainRow.AddChild(createPrinter);

			// add in the import printer button
			var importPrinter = new ThemedIconButton(StaticData.Instance.LoadIcon("md-import_18.png", 18, 18).GrayToColor(theme.TextColor), theme)
			{
				VAnchor = VAnchor.Center,
				Margin = theme.ButtonSpacing,
				ToolTipText = "Import Printer".Localize(),
				Height = forcedHeight,
				Width = forcedHeight,
				Name = "Import Printer Button"
			};
			importPrinter.Click += (s, e) => UiThread.RunOnIdle(() =>
			{
				DialogWindow.Show(new CloneSettingsPage());
			});
			mainRow.AddChild(importPrinter);

			rootColumn.AddChild(printersNode);

			HardwareTreeView.CreatePrinterProfilesTree(printersNode, theme);
			this.Invalidate();

			// Register listeners
			PrinterSettings.AnyPrinterSettingChanged += Printer_SettingChanged;

			// Rebuild the treeview anytime the Profiles list changes
			ProfileManager.ProfilesListChanged.RegisterEvent((s, e) =>
			{
				HardwareTreeView.CreatePrinterProfilesTree(printersNode, theme);
				this.Invalidate();
			}, ref unregisterEvents);
		}

		public static void CreatePrinterProfilesTree(TreeNode printersNode, ThemeConfig theme)
		{
			if (printersNode == null)
			{
				return;
			}

			printersNode.Nodes.Clear();

			// Add the menu items to the menu itself
			foreach (var printer in ProfileManager.Instance.ActiveProfiles.OrderBy(p => p.Name))
			{
				var printerNode = new TreeNode(theme)
				{
					Text = printer.Name,
					Name = $"{printer.Name} Node",
					Tag = printer
				};

				printerNode.Load += (s, e) =>
				{
					printerNode.Image = OemSettings.Instance.GetIcon(printer.Make, theme);
				};

				printersNode.Nodes.Add(printerNode);
			}

			printersNode.Expanded = true;
		}

		public static void CreateOpenPrintersTree(TreeNode printersNode, ThemeConfig theme)
		{
			if (printersNode == null)
			{
				return;
			}

			printersNode.Nodes.Clear();

			// Add the menu items to the menu itself
			foreach (var printer in ApplicationController.Instance.ActivePrinters)
			{
				string printerName = printer.PrinterName;

				var printerNode = new TreeNode(theme)
				{
					Text = printerName,
					Name = $"{printerName} Node",
					Tag = printer
				};

				printerNode.Load += (s, e) =>
				{
					printerNode.Image = OemSettings.Instance.GetIcon(printer.Settings.GetValue(SettingsKey.make), theme);
				};

				printersNode.Nodes.Add(printerNode);
			}

			printersNode.Expanded = true;
		}

		public override void OnClosed(EventArgs e)
		{
			// Unregister listeners
			PrinterSettings.AnyPrinterSettingChanged -= Printer_SettingChanged;

			base.OnClosed(e);
		}

		private void Printer_SettingChanged(object s, StringEventArgs e)
		{
			string settingsName = e?.Data;
			if (settingsName != null && settingsName == SettingsKey.printer_name)
			{
				// Allow enough time for ProfileManager to respond and refresh its data
				UiThread.RunOnIdle(() =>
				{
					HardwareTreeView.CreatePrinterProfilesTree(printersNode, theme);
				}, .2);

				this.Invalidate();
			}
		}
	}
}
