﻿/*
Copyright (c) 2018, John Lewin
Copyright (c) 2021 Lars Brubaker
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
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.Image;

namespace MatterHackers.MatterControl.Library
{
	public abstract class LibraryContainer : ILibraryContainer
	{
		public event EventHandler ContentChanged;

		public string ID { get; set; }

		public string Name { get; set; }

		public string CollectionKeyName { get; set; }

		public Type ViewOverride { get; protected set; }

		public SafeList<ILibraryContainerLink> ChildContainers { get; set; } = new SafeList<ILibraryContainerLink>();

		public bool IsProtected { get; protected set; } = true;

		public virtual Task<ImageBuffer> GetThumbnail(ILibraryItem item, int width, int height)
		{
			return Task.FromResult<ImageBuffer>(null);
		}

		public SafeList<ILibraryItem> Items { get; set; } = new SafeList<ILibraryItem>();

		public ILibraryContainer Parent { get; set; }

		public string HeaderMarkdown { get; set; } = "";

		public virtual ICustomSearch CustomSearch { get; } = null;

		public LibrarySortBehavior DefaultSort { get; set; }

		/// <summary>
		/// Reloads the container when contents have changes and fires ContentChanged to notify listeners
		/// </summary>
		public void ReloadContent()
		{
			// Call the container specific reload implementation
			this.Load();

			// Notify
			this.OnContentChanged();
		}

		protected void OnContentChanged()
		{
			this.ContentChanged?.Invoke(this, null);
		}

		public abstract void Load();

		public virtual void Dispose()
		{
		}

		public virtual void Activate()
		{
		}

		public virtual void Deactivate()
		{
		}
	}
}
