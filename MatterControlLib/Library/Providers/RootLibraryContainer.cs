﻿/*
Copyright (c) 2017, John Lewin
Copyright (c) 2021, Lars Brubaker
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
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Localizations;

namespace MatterHackers.MatterControl.Library
{
	public class RootLibraryContainer : ILibraryContainer
	{
		public event EventHandler ContentChanged;

		public RootLibraryContainer(SafeList<ILibraryContainerLink> items)
		{
			this.ChildContainers = items;
			this.Items = new SafeList<ILibraryItem>();
		}

		public ILibraryContainer Parent { get; set; } = null;

		public Type ViewOverride { get; } = null;

		public string ID { get; } = "rootLibraryProvider";

		public string CollectionKeyName { get; set; }

		public string Name => "Home".Localize();

		public bool IsProtected => true;

		public SafeList<ILibraryContainerLink> ChildContainers { get; }

		public SafeList<ILibraryItem> Items { get; }

		public string HeaderMarkdown { get; set; }

		public ICustomSearch CustomSearch { get; } = null;

        public LibrarySortBehavior DefaultSort => new LibrarySortBehavior()
        {
            SortKey = SortKey.ModifiedDate,
            Ascending = true
        };

        public Task<ImageBuffer> GetThumbnail(ILibraryItem item, int width, int height)
		{
			return Task.FromResult<ImageBuffer>(null);
		}

		public void Load() { }

		public void Dispose() { }

		public void Activate() { }

		public void Deactivate() { }
	}
}
