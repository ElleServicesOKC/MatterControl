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

using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using System;

namespace MatterHackers.MatterControl.PartPreviewWindow
{
	public class ClickablePopover : Popover, IOverrideAutoClose
	{
		private bool allowAutoClose = true;

		public ClickablePopover(ArrowDirection arrowDirection, BorderDouble padding, int notchSize, int arrowOffset, bool autoBorderColor = true)
			: base(arrowDirection, padding, notchSize, arrowOffset, autoBorderColor)
		{
		}

		public override void Initialize()
		{
			UiThread.RunOnIdle(() =>
			{
				this.Width += 1;
				this.Width -= 1;
			});
			base.Initialize();
		}

		public override void OnMouseEnterBounds(MouseEventArgs mouseEvent)
		{
			this.allowAutoClose = false;
			base.OnMouseEnterBounds(mouseEvent);
		}

		public override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
		}

		public override void OnMouseLeaveBounds(MouseEventArgs mouseEvent)
		{
			if (!PopupWidget.DebugKeepOpen)
			{
				this.Close();
			}
			base.OnMouseLeaveBounds(mouseEvent);
		}

		public bool AllowAutoClose => allowAutoClose;
	}
}
