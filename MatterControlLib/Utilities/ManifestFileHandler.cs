﻿/*
Copyright (c) 2014, Lars Brubaker
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
using System.IO;
using MatterHackers.MatterControl.DataStorage;
using Newtonsoft.Json;

namespace MatterHackers.MatterControl
{
	public class QueueData
    {
		private static QueueData instance;
		public static QueueData Instance
		{
			get
			{
				if (instance == null)
                {
					instance = new QueueData();
                }

				return instance;
			}
		}

		public int ItemCount
        {
			get
            {
				throw new NotImplementedException();
            }
        }

        public void AddItem(string filePath)
        {
            throw new NotImplementedException();
        }

        public string GetFirstItem()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetItemNames()
        {
            throw new NotImplementedException();
        }
    }

	public class LegacyQueueFiles
	{
		public List<PrintItem> ProjectFiles { get; set; }

		public static void ImportFromLegacy(string destPath)
		{
			var filePath = Path.Combine(ApplicationDataStorage.ApplicationUserDataPath, "data", "default.mcp");

			if (!File.Exists(filePath))
			{
				// nothing to do
				return;
			}

			string json = File.ReadAllText(filePath);

			LegacyQueueFiles newProject = JsonConvert.DeserializeObject<LegacyQueueFiles>(json);
			if (newProject.ProjectFiles.Count == 0)
            {
				return;
            }

			Directory.CreateDirectory(destPath);
			foreach (var printItem in newProject.ProjectFiles)
            {
				var destFile = Path.Combine(destPath, Path.ChangeExtension(printItem.Name, Path.GetExtension(printItem.FileLocation)));
				if (!File.Exists(destFile)
					&& File.Exists(printItem.FileLocation))
				{
					// copy the print item to the destination directory
					File.Copy(printItem.FileLocation, destFile, true);
				}
            }
		}
	}
}