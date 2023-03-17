﻿/*
Copyright (c) 2022, John Lewin, Lars Brubaker
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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using MatterHackers.Agg;

namespace MatterHackers.MatterControl.Library
{
	public class ZipMemoryItem : FileSystemItem, ILibraryAssetStream
	{
		public ZipMemoryItem(ZipMemoryContainer containingZip, string filePath, string relativePath, long fileSize)
			: base(filePath)
		{
			this.ContainingZip = containingZip;
			this.RelativePath = relativePath;
			this.Name = Path.GetFileName(relativePath);
			this.FileSize = fileSize;
		}

        public override string Name { get; set; }

        public string AssetPath { get; } = null;

		public string ContentType => Path.GetExtension(this.RelativePath).ToLower().Trim('.');

		public string FileName => Path.GetFileName(this.RelativePath);

		/// <summary>
		/// Gets the size, in bytes, of the current file.
		/// </summary>
		public long FileSize { get; private set; }

		public override string ID => Util.GetLongHashCode($"{this.FilePath}/{this.RelativePath}").ToString();

		public ZipMemoryContainer ContainingZip { get; }
		public string RelativePath { get; set; }

		public async Task<StreamAndLength> GetStream(Action<double, string> reportProgress)
		{
			var memStream = await Task.Run(() =>
			{
				var memoryStream = new MemoryStream();

				using (var file = File.OpenRead(this.FilePath))
				using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
				{
					var zipStream = zip.Entries.Where(e => e.FullName == this.RelativePath).FirstOrDefault()?.Open();
					zipStream?.CopyTo(memoryStream);
					zipStream?.Dispose();
				}

				memoryStream.Position = 0;

				return memoryStream;
			});

			return new StreamAndLength()
			{
				Stream = memStream,
				Length = memStream.Length
			};
		}
	}
}