﻿/*
Copyright (c) 2019, Lars Brubaker, John Lewin
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
using System.Linq;
using MatterHackers.Agg;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.PrinterCommunication;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.ConfigurationPage.PrintLeveling
{
	public class PrintLevelingWizard : PrinterSetupWizard
	{
		// this class is so that it is not passed by value
		public class ProbePosition
		{
			public Vector3 Position;
		}

		private readonly double[] babySteppingValue = new double[4];
		private bool wizardExited;
		private bool hasHardwareLeveling;

		public PrintLevelingWizard(PrinterConfig printer)
			: base(printer)
		{
			this.Title = "Print Leveling".Localize();
			hasHardwareLeveling = printer.Settings.GetValue<bool>(SettingsKey.has_hardware_leveling);
		}

		public LevelingPlan LevelingPlan { get; set; }

		public override bool Visible => !hasHardwareLeveling;

		public override string HelpText => hasHardwareLeveling ? "Unable due to hardware leveling".Localize() : null;

		public override bool Enabled => !hasHardwareLeveling && Visible && printer.Connection.IsConnected && !printer.Connection.Printing && !printer.Connection.Paused;

		public override bool Completed => !hasHardwareLeveling && !LevelingPlan.NeedsToBeRun(printer);

		public override bool SetupRequired => LevelingPlan.NeedsToBeRun(printer);

		private void Initialize()
		{
			printer.Settings.ForTools<double>(SettingsKey.baby_step_z_offset, (key, value, i) =>
			{
				// remember the current baby stepping values
				babySteppingValue[i] = value;
				// clear them while we measure the offsets
				printer.Settings.SetValue(key, "0");
			});

			// turn off print leveling
			printer.Connection.AllowLeveling = false;

			// clear any data that we are going to be acquiring (sampled positions, after z home offset)
			var levelingData = new PrintLevelingData()
			{
				LevelingSystem = printer.Settings.GetValue<LevelingSystem>(SettingsKey.print_leveling_solution)
			};

			printer.Connection.QueueLine("T0");

			switch (levelingData.LevelingSystem)
			{
				case LevelingSystem.Probe3Points:
					LevelingPlan = new LevelWizard3Point(printer);
					break;

				case LevelingSystem.Probe7PointRadial:
					LevelingPlan = new LevelWizard7PointRadial(printer);
					break;

				case LevelingSystem.Probe13PointRadial:
					LevelingPlan = new LevelWizard13PointRadial(printer);
					break;

				case LevelingSystem.Probe100PointRadial:
					LevelingPlan = new LevelWizard100PointRadial(printer);
					break;

				case LevelingSystem.Probe3x3Mesh:
					LevelingPlan = new LevelWizardMesh(printer, 3, 3);
					break;

				case LevelingSystem.Probe5x5Mesh:
					LevelingPlan = new LevelWizardMesh(printer, 5, 5);
					break;

				case LevelingSystem.Probe10x10Mesh:
					LevelingPlan = new LevelWizardMesh(printer, 10, 10);
					break;

				case LevelingSystem.ProbeCustom:
					LevelingPlan = new LevelWizardCustom(printer);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		public override void Dispose()
		{
			// If leveling was on when we started, make sure it is on when we are done.
			printer.Connection.AllowLeveling = true;

			// set the baby stepping back to the last known good value
			printer.Settings.ForTools<double>(SettingsKey.baby_step_z_offset, (key, value, i) =>
			{
				printer.Settings.SetValue(key, babySteppingValue[i].ToString());
			});

			wizardExited = true;

			// make sure we raise the probe on close
			if (printer.Settings.Helpers.ProbeBeingUsed
				&& printer.Settings.GetValue<bool>(SettingsKey.has_z_servo))
			{
				// make sure the servo is retracted
				var servoRetract = printer.Settings.GetValue<double>(SettingsKey.z_servo_retracted_angle);
				printer.Connection.QueueLine($"M280 P0 S{servoRetract}");
			}
		}

		public override bool ClosePage()
		{
			printer.Connection.TurnOffBedAndExtruders(TurnOff.AfterDelay);
			return base.ClosePage();
		}

		protected override IEnumerator<WizardPage> GetPages()
		{
			var levelingStrings = new LevelingStrings();

			yield return new WizardPage(
				this,
				"{0} Overview".Localize().FormatWith(this.Title),
				@"Print Leveling measures the plane of the bed.

This data compensates for machine misalignment and bed distortion, and ensures good first layer adhesion.

Click 'Next' to continue.


".Replace("\r", "").FormatWith())
				{
					WindowTitle = Title,
				};

			// Switch to raw mode and construct leveling structures
			this.Initialize();

			// var probePositions = new List<ProbePosition>(Enumerable.Range(0, levelingPlan.ProbeCount).Select(p => new ProbePosition()));
			var probePositions = new List<ProbePosition>(LevelingPlan.ProbeCount);
			for (int j = 0; j < LevelingPlan.ProbeCount; j++)
			{
				probePositions.Add(new ProbePosition());
			}

			// Require user confirmation after this point
			this.RequireCancelConfirmation = true;

			// start heating up now so it has more time to heat
			var bedTemperature = printer.Settings.GetValue<bool>(SettingsKey.has_heated_bed) ?
				printer.Settings.Helpers.ActiveBedTemperature
				: 0;
			if (bedTemperature > 0)
			{
				printer.Connection.TargetBedTemperature = bedTemperature;
			}

			yield return new HomePrinterPage(
				this,
				levelingStrings.HomingPageInstructions(
					printer.Settings.Helpers.ProbeBeingUsed,
					printer.Settings.GetValue<bool>(SettingsKey.has_heated_bed)));

			// figure out the heating requirements
			double targetBedTemp = 0;
			double targetHotendTemp = 0;
			if (printer.Settings.GetValue<bool>(SettingsKey.has_heated_bed))
			{
				targetBedTemp = printer.Settings.Helpers.ActiveBedTemperature;
			}

			if (!printer.Settings.Helpers.ProbeBeingUsed)
			{
				targetHotendTemp = printer.Settings.Helpers.ExtruderTargetTemperature(0);
			}

			if (targetBedTemp > 0 || targetHotendTemp > 0)
			{
				string heatingInstructions = "";
				if (targetBedTemp > 0 && targetHotendTemp > 0)
				{
					// heating both the bed and the hotend
					heatingInstructions = @"Waiting for the bed to heat to {0} °C
and the hotend to heat to {1} °C.

This will improve the accuracy of print leveling
and ensure that no filament is stuck to your nozzle.

Warning! The tip of the nozzle will be HOT!
Avoid contact with your skin.".Replace("\r", "").Localize().FormatWith(targetBedTemp, targetHotendTemp);
				}
				else if (targetBedTemp > 0)
				{
					// only heating the bed
					heatingInstructions = @"Waiting for the bed to heat to {0} °C.
This will improve the accuracy of print leveling.".Replace("\r", "").Localize().FormatWith(targetBedTemp);
				}
				else // targetHotendTemp > 0
				{
					// only heating the hotend
					heatingInstructions += @"Waiting for the hotend to heat to {0} °C.
This will ensure that no filament is stuck to your nozzle.

Warning! The tip of the nozzle will be HOT!
Avoid contact with your skin.".Replace("\r", "").Localize().FormatWith(targetHotendTemp);
				}

				yield return new WaitForTempPage(
					this,
					"Heating the printer".Localize(),
					heatingInstructions,
					targetBedTemp,
					new double[] { targetHotendTemp });
			}

			double startProbeHeight = printer.Settings.GetValue<double>(SettingsKey.print_leveling_probe_start);

			int i = 0;

			var probePoints = LevelingPlan.GetPositionsToSample(printer.Connection.HomingPosition).ToList();
			if (printer.Settings.Helpers.ProbeBeingUsed)
			{
				var autoProbePage = new AutoProbePage(this, printer, "Bed Detection", probePoints, probePositions);
				yield return autoProbePage;
			}
			else
			{
				foreach (var goalProbePoint in probePoints)
				{
					if (wizardExited)
					{
						// Make sure when the wizard is done we turn off the bed heating
						printer.Connection.TurnOffBedAndExtruders(TurnOff.AfterDelay);

						if (printer.Settings.GetValue<bool>(SettingsKey.z_homes_to_max))
						{
							printer.Connection.HomeAxis(PrinterConnection.Axis.XYZ);
						}

						yield break;
					}

					var validProbePosition = EnsureInPrintBounds(printer, goalProbePoint);
					{
						yield return new GetCoarseBedHeight(
							this,
							new Vector3(validProbePosition, startProbeHeight),
							string.Format(
								"{0} {1} {2} - {3}",
								levelingStrings.GetStepString(LevelingPlan.TotalSteps),
								"Position".Localize(),
								i + 1,
								"Low Precision".Localize()),
							probePositions,
							i,
							levelingStrings);

						yield return new GetFineBedHeight(
							this,
							string.Format(
								"{0} {1} {2} - {3}",
								levelingStrings.GetStepString(LevelingPlan.TotalSteps),
								"Position".Localize(),
								i + 1,
								"Medium Precision".Localize()),
							probePositions,
							i,
							levelingStrings);

						yield return new GetUltraFineBedHeight(
							this,
							string.Format(
								"{0} {1} {2} - {3}",
								levelingStrings.GetStepString(LevelingPlan.TotalSteps),
								"Position".Localize(),
								i + 1,
								"High Precision".Localize()),
							probePositions,
							i);
					}

					i++;
				}
			}

			// if we are not using a z-probe, reset the baby stepping at the successful conclusion of leveling
			if (!printer.Settings.GetValue<bool>(SettingsKey.use_z_probe))
			{
				// clear the baby stepping so we don't save the old values
				var extruderCount = printer.Settings.GetValue<int>(SettingsKey.extruder_count);
				for (i = 0; i < extruderCount; i++)
				{
					babySteppingValue[i] = 0;
				}
			}

			yield return new LastPageInstructions(
				this,
				"Print Leveling Wizard".Localize(),
				printer.Settings.Helpers.ProbeBeingUsed,
				probePositions);
		}

		public static Vector2 EnsureInPrintBounds(PrinterConfig printer, Vector2 probePosition)
		{
			// check that the position is within the printing area and if not move it back in
			if (printer.Settings.Helpers.ProbeBeingUsed)
			{
				var probeOffset2D = new Vector2(printer.Settings.GetValue<Vector3>(SettingsKey.probe_offset));
				var actualNozzlePosition = probePosition - probeOffset2D;

				// clamp this to the bed bounds
				var bedBounds = printer.Settings.BedBounds;
				bedBounds.Inflate(-1);
				Vector2 adjustedPosition = bedBounds.Clamp(actualNozzlePosition);

				// and push it back into the probePosition
				probePosition = adjustedPosition + probeOffset2D;
			}

			return probePosition;
		}
	}
}