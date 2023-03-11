﻿/*
Copyright (c) 2018, Lars Brubaker
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
using System.Threading;
using System.Threading.Tasks;
using MatterControl.Printing;
using MatterHackers.Agg;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.ConfigurationPage.PrintLeveling;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.PrinterCommunication.Io
{
	public class ValidatePrintLevelingStream : GCodeStreamProxy
	{
		private readonly double[] babySteppingValue = new double[4];
		private readonly Queue<string> queuedCommands = new Queue<string>();
		private readonly List<double> samplesForSinglePosition = new List<double>();
		private int activeProbeIndex;
		private bool gcodeAlreadyLeveled;
		private LevelingPlan levelingPlan;
		private List<Vector2> positionsToSample;
		private Vector3 positionToSample;
		private Vector3 positionToSampleWithProbeOffset;
		private List<PrintLevelingWizard.ProbePosition> sampledPositions;

		private bool validationHasBeenRun;
		private bool validationRunning;
		private bool waitingToCompleteNextSample;
		private bool haveSeenM190;
		private bool haveSeenG28;
        private bool validationCanceled;

        public ValidatePrintLevelingStream(PrinterConfig printer, GCodeStream internalStream)
			: base(printer, internalStream)
		{
			if (!printer.Settings.GetValue<bool>(SettingsKey.has_heated_bed)
				|| printer.Settings.Helpers.ActiveBedTemperature == 0)
			{
				// If we don't have a bed or we are not going to set the temperature
				// do not wait for an M190
				haveSeenM190 = true;
			}
		}

		public override string DebugInfo => "";

		public override void Dispose()
		{
			CancelValidation();

			base.Dispose();
		}

		private void CancelValidation()
		{
			validationCanceled = true;

			if (validationRunning)
			{
				validationRunning = false;
				validationHasBeenRun = true;
				haveSeenG28 = false;
				haveSeenM190 = false;

				printer.Connection.LineReceived -= GetZProbeHeight;

				// If leveling was on when we started, make sure it is on when we are done.
				printer.Connection.AllowLeveling = true;

				// set the baby stepping back to the last known good value
				printer.Settings.ForTools<double>(SettingsKey.baby_step_z_offset, (key, value, i) =>
				{
					printer.Settings.SetValue(key, babySteppingValue[i].ToString());
				});

				queuedCommands.Clear();
				RetractProbe();
			}
		}

		private void RetractProbe()
		{
			// make sure we raise the probe on close
			if (printer.Settings.Helpers.ProbeBeingUsed
				&& printer.Settings.GetValue<bool>(SettingsKey.has_z_servo))
			{
				// make sure the servo is retracted
				var servoRetract = printer.Settings.GetValue<double>(SettingsKey.z_servo_retracted_angle);
				queuedCommands.Enqueue($"M280 P0 S{servoRetract}");
			}
		}

		public void Cancel()
		{
			CancelValidation();
		}

		public override string ReadLine()
		{
			if (queuedCommands.Count > 0)
			{
				return queuedCommands.Dequeue();
			}

			if (validationRunning
				&& printer.Connection.PrintWasCanceled)
            {
				CancelValidation();
			}

			if (validationRunning
				&& !validationHasBeenRun)
			{
				SampleProbePoints();
			}

			string lineToSend = base.ReadLine();

			if (lineToSend != null
				&& lineToSend.EndsWith("; NO_PROCESSING"))
			{
				return lineToSend;
			}

			if (lineToSend == PrintLevelingStream.SoftwareLevelingAppliedMessage)
			{
				gcodeAlreadyLeveled = true;
			}


			if (lineToSend != null)
			{
				if (lineToSend.Contains("M190"))
				{
					haveSeenM190 = true;
				}

				if (lineToSend.Contains("G28"))
				{
					haveSeenG28 = true;
				}

				if (haveSeenG28
					&& haveSeenM190
					&& !validationRunning
					&& !validationHasBeenRun
					&& printer.Connection.Printing
					&& !validationCanceled)
				{
					SetupForValidation();
				}

				if (!validationHasBeenRun
					&& !gcodeAlreadyLeveled
					&& printer.Connection.IsConnected
					&& printer.Connection.Printing
					&& printer.Connection.CurrentlyPrintingLayer <= 0
					&& printer.Connection.ActivePrintTask?.RecoveryCount < 1
					&& printer.Settings.GetValue<bool>(SettingsKey.validate_leveling))
				{
					// we are setting the bed temp
					if (haveSeenG28 && haveSeenM190)
					{
						haveSeenG28 = false;
						haveSeenM190 = false;
						// still set the bed temp and wait
						return lineToSend;
					}
				}
			}

			return lineToSend;
		}

		private void GetZProbeHeight(object sender, string line)
		{
			if (line != null)
			{
				double sampleRead = double.MinValue;
				if (line.StartsWith("Bed")) // marlin G30 return code (looks like: 'Bed Position X:20 Y:32 Z:.01')
				{
					sampledPositions[activeProbeIndex].Position.X = positionToSample.X;
					sampledPositions[activeProbeIndex].Position.Y = positionToSample.Y;
					GCodeFile.GetFirstNumberAfter("Z:", line, ref sampleRead);
				}
				else if (line.StartsWith("Z:")) // smoothie G30 return code (looks like: 'Z:10.01')
				{
					sampledPositions[activeProbeIndex].Position.X = positionToSample.X;
					sampledPositions[activeProbeIndex].Position.Y = positionToSample.Y;
					// smoothie returns the position relative to the start position
					double reportedProbeZ = 0;

					GCodeFile.GetFirstNumberAfter("Z:", line, ref reportedProbeZ);
					sampleRead = positionToSample.Z - reportedProbeZ;
				}

				if (sampleRead != double.MinValue)
				{
					samplesForSinglePosition.Add(sampleRead);

					int numberOfSamples = printer.Settings.GetValue<int>(SettingsKey.z_probe_samples);
					if (samplesForSinglePosition.Count >= numberOfSamples)
					{
						samplesForSinglePosition.Sort();
						if (samplesForSinglePosition.Count > 3)
						{
							// drop the high and low values
							samplesForSinglePosition.RemoveAt(0);
							samplesForSinglePosition.RemoveAt(samplesForSinglePosition.Count - 1);
						}

						sampledPositions[activeProbeIndex].Position.Z = Math.Round(samplesForSinglePosition.Average(), 2);

						// If we are sampling the first point, check if it is unchanged from the last time we ran leveling
						if (activeProbeIndex == 0)
						{
							var levelingData = printer.Settings.Helpers.PrintLevelingData;

							var currentSample = sampledPositions[activeProbeIndex].Position.Z;
							var oldSample = levelingData.SampledPositions.Count > 0 ? levelingData.SampledPositions[activeProbeIndex].Z : 0;
							var delta = currentSample - oldSample;

							printer.Connection.TerminalLog.WriteLine($"Validation Sample: Old {oldSample}, New {currentSample}, Delta {delta}");

							if (levelingData.SampledPositions.Count == sampledPositions.Count
								&& Math.Abs(delta) < printer.Settings.GetValue<double>(SettingsKey.validation_threshold))
							{
								// the last leveling is still good abort this new calibration and start printing
								CancelValidation();
								waitingToCompleteNextSample = false;
								validationRunning = false;
								validationHasBeenRun = true;
							}
						}

						// When probe data has been collected, resume our thread to continue collecting
						waitingToCompleteNextSample = false;
						// and go on to the next point
						activeProbeIndex++;
					}
					else
					{
						// add the next request for probe
						queuedCommands.Enqueue("G30");
						// raise the probe after each sample
						var feedRates = printer.Settings.Helpers.ManualMovementSpeeds();
						queuedCommands.Enqueue($"G1 X{positionToSampleWithProbeOffset.X:0.###}Y{positionToSampleWithProbeOffset.Y:0.###}Z{positionToSampleWithProbeOffset.Z:0.###} F{feedRates.X}");
					}
				}
			}
		}

		private void SampleProbePoints()
		{
			if (waitingToCompleteNextSample)
			{
				return;
			}

			double startProbeHeight = printer.Settings.GetValue<double>(SettingsKey.print_leveling_probe_start);

			if (activeProbeIndex < positionsToSample.Count)
			{
				var validProbePosition2D = PrintLevelingWizard.EnsureInPrintBounds(printer, positionsToSample[activeProbeIndex]);
				positionToSample = new Vector3(validProbePosition2D, startProbeHeight);

				this.SampleNextPoint();
			}
			else
			{
				SaveSamplePoints();
				CancelValidation();
			}
		}

		private void SaveSamplePoints()
		{
			PrintLevelingData levelingData = printer.Settings.Helpers.PrintLevelingData;
			levelingData.SampledPositions.Clear();

			for (int i = 0; i < sampledPositions.Count; i++)
			{
				levelingData.SampledPositions.Add(sampledPositions[i].Position);
			}

			levelingData.LevelingSystem = printer.Settings.GetValue<LevelingSystem>(SettingsKey.print_leveling_solution);
			levelingData.CreationDate = DateTime.Now;
			// record the temp the bed was when we measured it (or 0 if no heated bed)
			levelingData.BedTemperature = printer.Settings.GetValue<bool>(SettingsKey.has_heated_bed) ?
				printer.Settings.Helpers.ActiveBedTemperature
				: 0;
			levelingData.IssuedLevelingTempWarning = false;

			// Invoke setter forcing persistence of leveling data
			printer.Settings.Helpers.PrintLevelingData = levelingData;
			printer.Settings.ForTools<double>(SettingsKey.baby_step_z_offset, (key, value, i) =>
			{
				printer.Settings.SetValue(key, "0");
			});
			printer.Connection.AllowLeveling = true;
			printer.Settings.Helpers.DoPrintLeveling(true);
		}

		private void SetupForValidation()
		{
			validationRunning = true;
			activeProbeIndex = 0;

			printer.Connection.LineReceived += GetZProbeHeight;

			printer.Settings.ForTools<double>(SettingsKey.baby_step_z_offset, (key, value, i) =>
			{
				// remember the current baby stepping values
				babySteppingValue[i] = value;
				// clear them while we measure the offsets
				printer.Settings.SetValue(key, "0");
			});

			// turn off print leveling
			printer.Connection.AllowLeveling = false;

			var levelingData = new PrintLevelingData()
			{
				LevelingSystem = printer.Settings.GetValue<LevelingSystem>(SettingsKey.print_leveling_solution)
			};

			switch (levelingData.LevelingSystem)
			{
				case LevelingSystem.Probe3Points:
					levelingPlan = new LevelWizard3Point(printer);
					break;

				case LevelingSystem.Probe7PointRadial:
					levelingPlan = new LevelWizard7PointRadial(printer);
					break;

				case LevelingSystem.Probe13PointRadial:
					levelingPlan = new LevelWizard13PointRadial(printer);
					break;

				case LevelingSystem.Probe100PointRadial:
					levelingPlan = new LevelWizard100PointRadial(printer);
					break;

				case LevelingSystem.Probe3x3Mesh:
					levelingPlan = new LevelWizardMesh(printer, 3, 3);
					break;

				case LevelingSystem.Probe5x5Mesh:
					levelingPlan = new LevelWizardMesh(printer, 5, 5);
					break;

				case LevelingSystem.Probe10x10Mesh:
					levelingPlan = new LevelWizardMesh(printer, 10, 10);
					break;

				case LevelingSystem.ProbeCustom:
					levelingPlan = new LevelWizardCustom(printer);
					break;

				default:
					throw new NotImplementedException();
			}

			sampledPositions = new List<PrintLevelingWizard.ProbePosition>(levelingPlan.ProbeCount);
			for (int j = 0; j < levelingPlan.ProbeCount; j++)
			{
				sampledPositions.Add(new PrintLevelingWizard.ProbePosition());
			}

			positionsToSample = levelingPlan.GetPositionsToSample(printer.Connection.HomingPosition).ToList();

			StartReporting();
		}

		private void StartReporting()
		{
			ApplicationController.Instance.Tasks.Execute(
				"Leveling".Localize(),
				printer,
				(reporter, cancellationToken) =>
				{
					var status = "";
					while (validationRunning)
					{
						if (activeProbeIndex == 0)
						{
							status = "Validating";
						}
						else
						{
							status = $"Probing point {activeProbeIndex} of {sampledPositions.Count}";
						}

						var progress0To1 = (activeProbeIndex + 1) / (double)sampledPositions.Count;
						reporter?.Invoke(progress0To1, status);
						Thread.Sleep(100);
					}

					return Task.CompletedTask;
				},
				new RunningTaskOptions()
				{
					ReadOnlyReporting = true
				});
		}

		private void SampleNextPoint()
		{
			waitingToCompleteNextSample = true;

			samplesForSinglePosition.Clear();

			if (printer.Settings.GetValue<bool>(SettingsKey.has_z_servo))
			{
				// make sure the servo is deployed
				var servoDeployCommand = printer.Settings.GetValue<double>(SettingsKey.z_servo_depolyed_angle);
				queuedCommands.Enqueue($"M280 P0 S{servoDeployCommand}");
			}

			positionToSampleWithProbeOffset = positionToSample;

			// subtract out the probe offset
			var probeOffset = printer.Settings.GetValue<Vector3>(SettingsKey.probe_offset);
			// we are only interested in the xy position
			probeOffset.Z = 0;
			positionToSampleWithProbeOffset -= probeOffset;

			var feedRates = printer.Settings.Helpers.ManualMovementSpeeds();

			queuedCommands.Enqueue($"G1 Z{positionToSample.Z:0.###} F{feedRates.Z}");
			queuedCommands.Enqueue($"G1 X{positionToSampleWithProbeOffset.X:0.###}Y{positionToSampleWithProbeOffset.Y:0.###}Z{positionToSampleWithProbeOffset.Z:0.###} F{feedRates.X}");

			// probe the current position
			queuedCommands.Enqueue("G30");

			// raise the probe after each sample
			queuedCommands.Enqueue($"G1 X{positionToSampleWithProbeOffset.X:0.###}Y{positionToSampleWithProbeOffset.Y:0.###}Z{positionToSampleWithProbeOffset.Z:0.###} F{feedRates.X}");
		}
	}
}