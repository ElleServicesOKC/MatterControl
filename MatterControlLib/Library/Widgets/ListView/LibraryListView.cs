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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Markdig.Agg;
using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.Library;
using MatterHackers.MatterControl.Library.Widgets;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.VectorMath;
using static MatterHackers.MatterControl.CustomWidgets.LibraryListView;
using static MatterHackers.MatterControl.Library.Widgets.PopupLibraryWidget;

namespace MatterHackers.MatterControl.CustomWidgets
{
	public class LibraryListView : ScrollableWidget
	{
		public enum DoubleClickBehaviors
		{
			PreviewItem,
			AddToBed,
		}

		public event EventHandler ContentReloaded;

		private readonly ThemeConfig theme;
		private readonly ILibraryContext libraryContext;

		private GuiWidget stashedContentView;

		private ILibraryContainerLink loadingContainerLink;

		// Default to IconListView
		private GuiWidget contentView;
		private Color loadingBackgroundColor;
		private ImageSequenceWidget loadingIndicator;

		public List<LibraryAction> MenuActions { get; set; }

		// Default constructor uses IconListView
		public LibraryListView(ILibraryContext context, ThemeConfig theme)
			: this(context, new IconListView(theme), theme)
		{
		}

		public LibraryListView(ILibraryContext context, GuiWidget libraryView, ThemeConfig theme)
		{
			contentView = new IconListView(theme);

			libraryView.Click += ContentView_Click;

			loadingBackgroundColor = new Color(theme.PrimaryAccentColor, 10);

			this.theme = theme;
			this.libraryContext = context;

			// Set Display Attributes
			this.AnchorAll();
			this.AutoScroll = true;
			this.ScrollArea.Padding = new BorderDouble(3);
			this.ScrollArea.HAnchor = HAnchor.Stretch;
			this.ListContentView = libraryView;

			context.ContainerChanged += ActiveContainer_Changed;
			context.ContentChanged += ActiveContainer_ContentChanged;

            if (ActiveContainer != null)
            {
                ActiveContainer_Changed(this, new ContainerChangedEventArgs(ActiveContainer, null));
            }
        }

		private void ContentView_Click(object sender, MouseEventArgs e)
		{
			if (sender is GuiWidget guiWidget)
			{
				var screenPosition = guiWidget.TransformToScreenSpace(e.Position);
				var thisPosition = this.TransformFromScreenSpace(screenPosition);
				var thisMouseClick = new MouseEventArgs(e, thisPosition.X, thisPosition.Y);
				ShowRightClickMenu(thisMouseClick);
			}
		}

		public override Color BackgroundColor
		{
			get => loadingIndicator.Visible ? loadingBackgroundColor : base.BackgroundColor;
			set => base.BackgroundColor = value;
		}

		public bool AllowContextMenu { get; set; } = true;

		public Predicate<ILibraryContainerLink> ContainerFilter { get; set; } = (o) => true;

		public Predicate<ILibraryItem> ItemFilter { get; set; } = (o) => true;

		public ILibraryContainer ActiveContainer => this.libraryContext.ActiveContainer;

		private async void ActiveContainer_Changed(object sender, ContainerChangedEventArgs e)
		{
			var activeContainer = e.ActiveContainer;

			if (activeContainer.DefaultSort != null)
			{
				this.ActiveSort = activeContainer.DefaultSort.SortKey;
				this.Ascending = activeContainer.DefaultSort.Ascending;
			}

			// If a container level view override is active, but the container is not, restore the original view
			if (stashedContentView != null
				&& activeContainer.ViewOverride == null)
			{
				// Switch back to the original view
				stashedContentView.ClearRemovedFlag();
				this.ListContentView = stashedContentView;
				stashedContentView = null;
			}

			if (activeContainer?.ViewOverride is Type targetType
				&& targetType != this.ListContentView.GetType())
			{
				// Stash the active view while the container level override is in place
				if (stashedContentView == null)
				{
					stashedContentView = this.ListContentView;
				}

				// If the current view doesn't match the view requested by the container, construct and switch to the requested view
				if (Activator.CreateInstance(targetType) is GuiWidget targetView)
				{
					this.ListContentView = targetView;
				}
			}
			else if (activeContainer.GetUserView() is LibraryViewState userView)
			{
				if (userView.ViewMode != ApplicationController.Instance.ViewState.LibraryViewMode)
				{
					this.SetContentView(userView.ViewMode, userDriven: false);
				}

				this.ActiveSort = userView.SortBehavior.SortKey;
				this.Ascending = userView.SortBehavior.Ascending;
			}

			await DisplayContainerContent(activeContainer);
		}

		public async Task Reload()
		{
			await DisplayContainerContent(ActiveContainer);
		}

		private async void ActiveContainer_ContentChanged(object sender, EventArgs e)
		{
			await DisplayContainerContent(ActiveContainer);
		}

		private readonly List<ListViewItem> items = new List<ListViewItem>();

		public IEnumerable<ListViewItem> Items => items;

		private SortKey _activeSort = SortKey.Name;

		public SortKey ActiveSort
		{
			get => _activeSort;
			set
			{
				if (_activeSort != value)
				{
					_activeSort = value;
					this.ApplySort();
				}
			}
		}

		private string filterText;

		private bool _ascending = true;
		private ILibraryContainer sourceContainer;

		public bool Ascending
		{
			get => _ascending;
			set
			{
				if (_ascending != value)
				{
					_ascending = value;
					this.ApplySort();
				}
			}
		}

		public void SetUserSort(SortKey sortKey)
		{
			this.ActiveSort = sortKey;
			this.PersistUserView();
		}

		public void SetUserSort(bool ascending)
		{
			this.Ascending = ascending;
			this.PersistUserView();
		}

		public void SetContentView(PopupLibraryWidget.ListViewModes viewMode, bool userDriven = true)
		{
			ApplicationController.Instance.ViewState.LibraryViewMode = viewMode;

			switch (viewMode)
			{
				case PopupLibraryWidget.ListViewModes.RowListView:
					this.ListContentView = new RowListView(theme);
					break;

				case PopupLibraryWidget.ListViewModes.IconListView18:
					this.ListContentView = new IconListView(theme, 18);
					break;

				case PopupLibraryWidget.ListViewModes.IconListView70:
					this.ListContentView = new IconListView(theme, 70);
					break;

				case PopupLibraryWidget.ListViewModes.IconListView256:
					this.ListContentView = new IconListView(theme, 256);
					break;

				case PopupLibraryWidget.ListViewModes.IconListView:
				default:
					if (viewMode != PopupLibraryWidget.ListViewModes.IconListView)
					{
						Debugger.Break(); // Unknown/unexpected value
					}

					this.ListContentView = new IconListView(theme);
					break;
			}

			if (userDriven)
			{
				this.PersistUserView();
				this.Reload().ConfigureAwait(false);
			}
		}

		public void PersistUserView()
		{
			this.ActiveContainer.PersistUserView(new LibraryViewState()
			{
				ViewMode = ApplicationController.Instance.ViewState.LibraryViewMode,
				SortBehavior = new LibrarySortBehavior()
				{
					SortKey = _activeSort,
					Ascending = _ascending,
				}
			});
		}

		private void ApplySort()
		{
			this.Reload().ConfigureAwait(false);
		}

		private bool ContainsActiveFilter(ILibraryItem item)
		{
			return string.IsNullOrWhiteSpace(filterText)
				|| item.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// Empties the list children and repopulates the list with the source container content
		/// </summary>
		/// <param name="sourceContainer">The container to load</param>
		private Task DisplayContainerContent(ILibraryContainer sourceContainer)
		{
			this.sourceContainer = sourceContainer;
			if (this.ActiveContainer is ILibraryWritableContainer activeWritable)
			{
				activeWritable.ItemContentChanged -= WritableContainer_ItemContentChanged;
			}

			if (sourceContainer == null)
			{
				return Task.CompletedTask;
			}

			var itemsNeedingLoad = new List<ListViewItem>();

			this.items.Clear();

			this.SelectedItems.Clear();
			contentView.CloseChildren();

			var itemsContentView = contentView as IListContentView;
			itemsContentView.ClearItems();

			int width = itemsContentView.ThumbWidth;
			int height = itemsContentView.ThumbHeight;

			itemsContentView.BeginReload();

			if (contentView is IconListView listView)
			{
				AddHeaderMarkdown(listView);
			}

			using (contentView.LayoutLock())
			{
				var containerItems = sourceContainer.ChildContainers
					.Where(item => item.IsVisible
						&& this.ContainerFilter(item)
						&& this.ContainsActiveFilter(item))
					.Select(item => item);

				// Folder items
				foreach (var childContainer in this.SortItems(containerItems))
				{
					var listViewItem = new ListViewItem(childContainer, this.ActiveContainer, this);
					listViewItem.DoubleClick += ListViewItem_DoubleClick;

					items.Add(listViewItem);

					listViewItem.ViewWidget = itemsContentView.AddItem(listViewItem);
					listViewItem.ViewWidget.HasMenu = this.AllowContextMenu;
					listViewItem.ViewWidget.Name = childContainer.Name + " Row Item Collection";
				}

				// List items
				var filteredResults = from item in sourceContainer.Items
									  where item.IsVisible
											&& (item.IsContentFileType() || item is MissingFileItem)
											&& this.ItemFilter(item)
											&& this.ContainsActiveFilter(item)
									  select item;

				foreach (var item in this.SortItems(filteredResults))
				{
					var listViewItem = new ListViewItem(item, this.ActiveContainer, this);

					if (DoubleClickItemEvent != null)
					{
						listViewItem.DoubleClick += DoubleClickItemEvent;
					}
					else
					{
						listViewItem.DoubleClick += ListViewItem_DoubleClick;
					}

					if (ClickItemEvent != null)
					{
						listViewItem.Click += ClickItemEvent;
					}

					items.Add(listViewItem);

					listViewItem.ViewWidget = itemsContentView.AddItem(listViewItem);
					listViewItem.ViewWidget.HasMenu = this.AllowContextMenu;
					listViewItem.ViewWidget.Name = "Row Item " + item.Name;
				}

				itemsContentView.EndReload();

				if (sourceContainer is ILibraryWritableContainer writableContainer)
				{
					writableContainer.ItemContentChanged += WritableContainer_ItemContentChanged;
				}

				this.ContentReloaded?.Invoke(this, null);

				if (itemsContentView is GuiWidget guiWidget)
				{
					guiWidget.Invalidate();
				}
			}

			contentView.PerformLayout();
			this.ScrollPositionFromTop = Vector2.Zero;

			return Task.CompletedTask;
		}

		public EventHandler<MouseEventArgs> ClickItemEvent;
		public EventHandler<MouseEventArgs> DoubleClickItemEvent;

        private void AddHeaderMarkdown(IconListView listView)
		{
			if (sourceContainer != null
				&& !string.IsNullOrEmpty(sourceContainer.HeaderMarkdown))
			{
				var markdownWidget = new MarkdownWidget(theme)
				{
					HAnchor = HAnchor.Stretch,
					VAnchor = VAnchor.Fit,
					MaximumSize = new Vector2(double.MaxValue, 200 * GuiWidget.DeviceScale),
					Padding = 5,
					Margin = 5,
					BackgroundRadius = 3 * GuiWidget.DeviceScale,
					BackgroundOutlineWidth = 1 * GuiWidget.DeviceScale,
					BorderColor = theme.PrimaryAccentColor
				};
				markdownWidget.ScrollArea.VAnchor = VAnchor.Fit | VAnchor.Center;
				markdownWidget.Markdown = sourceContainer.HeaderMarkdown;
				listView.AddHeaderItem(markdownWidget);
			}
		}

		private IEnumerable<ILibraryItem> SortItems(IEnumerable<ILibraryItem> items)
		{
			switch (ActiveSort)
			{
				case SortKey.CreatedDate when this.Ascending:
					return items.OrderBy(item => item.DateCreated);

				case SortKey.CreatedDate when !this.Ascending:
					return items.OrderByDescending(item => item.DateCreated);

				case SortKey.ModifiedDate when this.Ascending:
					return items.OrderBy(item => item.DateModified);

				case SortKey.ModifiedDate when !this.Ascending:
					return items.OrderByDescending(item => item.DateModified);

				case SortKey.Name when !this.Ascending:
					return items.OrderByDescending(item => item.Name);

				default:
					return items.OrderBy(item => item.Name);
			}
		}

		private void WritableContainer_ItemContentChanged(object sender, LibraryItemChangedEventArgs e)
		{
			if (items.Where(i => i.Model.ID == e.LibraryItem.ID).FirstOrDefault() is ListViewItem listViewItem)
			{
				listViewItem.ViewWidget.LoadItemThumbnail().ConfigureAwait(false);
			}
		}

		public enum ViewMode
		{
			Icons,
			List
		}

		/// <summary>
		/// Gets or sets the GuiWidget responsible for rendering ListViewItems
		/// </summary>
		public GuiWidget ListContentView
		{
			get => contentView;
			set
			{
				if (value is IListContentView)
				{
					if (contentView != null
						&& contentView != value)
					{
						this.ScrollArea.CloseChildren();

						contentView = value;
						contentView.HAnchor = HAnchor.Stretch;
						contentView.VAnchor = VAnchor.Fit | VAnchor.Top;
						contentView.Name = "Library ListContentView";
						this.AddChild(contentView);

						this.ScrollArea.AddChild(
							loadingIndicator = new ImageSequenceWidget(ApplicationController.Instance.GetProcessingSequence(theme.PrimaryAccentColor))
							{
								VAnchor = VAnchor.Top,
								HAnchor = HAnchor.Center,
								Visible = false
							});
					}
				}
				else
				{
					throw new FormatException("ListContentView must be assignable from IListContentView");
				}
			}
		}

		public static ImageBuffer ResizeCanvas(ImageBuffer originalImage, int width, int height)
		{
			var destImage = new ImageBuffer(width, height, 32, originalImage.GetRecieveBlender());

			var renderGraphics = destImage.NewGraphics2D();
			renderGraphics.Clear(Color.Transparent);

			var x = width / 2 - originalImage.Width / 2;
			var y = height / 2 - originalImage.Height / 2;

			var center = new RectangleInt(x, y + originalImage.Height, x + originalImage.Width, y);

			renderGraphics.ImageRenderQuality = Graphics2D.TransformQuality.Best;

			renderGraphics.Render(originalImage, width / 2 - originalImage.Width / 2, height / 2 - originalImage.Height / 2);

			renderGraphics.FillRectangle(center, Color.Transparent);

			return destImage;
		}

		private void ListViewItem_DoubleClick(object sender, MouseEventArgs e)
		{
			UiThread.RunOnIdle(async () =>
			{
				var listViewItem = sender as ListViewItem;
				var itemModel = listViewItem.Model;

				if (itemModel is ILibraryContainerLink containerLink)
				{
					// Prevent invalid assignment of container.Parent due to overlapping load attempts that
					// would otherwise result in containers with self referencing parent properties
					if (loadingContainerLink != containerLink)
					{
						loadingContainerLink = containerLink;

						try
						{
							// Container items
							var container = await containerLink.GetContainer(null);
							if (container != null)
							{
								(contentView as IListContentView)?.ClearItems();

								contentView.Visible = false;
								loadingIndicator.Visible = true;

								await Task.Run(() =>
								{
									container.Load();
								});

								loadingIndicator.Visible = false;
								contentView.Visible = true;

								container.Parent = ActiveContainer;
								SetActiveContainer(container);
							}
						}
						catch { }
						finally
						{
							// Clear the loading guard and any completed load attempt
							loadingContainerLink = null;
						}
					}
				}
				else
				{
					// List items
					if (itemModel != null)
					{
						switch (this.DoubleClickBehavior)
						{
							case DoubleClickBehaviors.PreviewItem:
								if (itemModel is ILibraryAsset asset && asset.ContentType == "mcx"
									&& itemModel is ILibraryItem firstItem
									&& this.ActiveContainer is ILibraryWritableContainer writableContainer)
								{
									var mainViewWidget = ApplicationController.Instance.MainView;

									// check if it is already open
									if (ApplicationController.Instance.SwitchToWorkspaceIfAlreadyOpen(asset.AssetPath))
									{
										return;
									}

									var workspace = new PartWorkspace(new BedConfig(ApplicationController.Instance.Library.PlatingHistory));

									ApplicationController.Instance.Workspaces.Add(workspace);

									var partTab = mainViewWidget.CreateDesignTab(workspace, true);
									mainViewWidget.TabControl.ActiveTab = partTab;

									// Load content after UI widgets to support progress notification during acquire/load
									await ApplicationController.Instance.Tasks.Execute(
										"Loading".Localize() + "...",
										null,
										async (reporter, cancellationTokenSource) =>
										{
											var editContext = new EditContext()
											{
												ContentStore = writableContainer,
												SourceItem = firstItem
											};
											await workspace.SceneContext.LoadContent(editContext, (progress, message) =>
											{
												reporter?.Invoke(progress, message);
											});
										});
								}
								else
								{
									void OpenNewTab()
									{
										_ = ApplicationController.Instance.OpenIntoNewTab(new[] { itemModel });
									}

									OpenNewTab();
								}
								break;

							case DoubleClickBehaviors.AddToBed:
								var activeContext = ApplicationController.Instance.DragDropData;
								activeContext.SceneContext?.AddToPlate(new[] { itemModel });
								break;
						}
					}
				}
			});
		}

		public DoubleClickBehaviors DoubleClickBehavior { get; set; } = DoubleClickBehaviors.AddToBed;

		public void SetActiveContainer(ILibraryContainer container)
		{
			this.libraryContext.ActiveContainer = container;
		}

		public ObservableCollection<ListViewItem> SelectedItems { get; } = new ObservableCollection<ListViewItem>();

		public ListViewItem DragSourceRowItem { get; set; }

		public override void OnLoad(EventArgs args)
		{
			if (this.ListContentView.Children.Count <= 0)
			{
				this.Reload().ConfigureAwait(false);
			}

			base.OnLoad(args);
		}

		public bool HasMenu { get; set; } = true;

		protected override void OnClick(MouseEventArgs mouseEvent)
		{
			ShowRightClickMenu(mouseEvent);

			base.OnClick(mouseEvent);
		}

		public override void OnKeyDown(KeyEventArgs keyEvent)
		{
			// this must be called first to ensure we get the correct Handled state
			base.OnKeyDown(keyEvent);

			if (!keyEvent.Handled)
			{
				switch (keyEvent.KeyCode)
				{
					case Keys.A:
						if (keyEvent.Control)
						{
							SelectAllItems();
							keyEvent.Handled = true;
							keyEvent.SuppressKeyPress = true;
						}

						break;
				}
			}
		}

		public void SelectAllItems()
		{
			this.SelectedItems.Clear();
			foreach (var item in this.Items)
			{
				if (item.Model is ILibraryAssetStream
					|| item.Model is ILibraryObject3D)
				{
					this.SelectedItems.Add(item);
				}
			}
		}

		private void ShowRightClickMenu(MouseEventArgs mouseEvent)
		{
			var bounds = this.LocalBounds;
			var hitRegion = new RectangleDouble(
				new Vector2(bounds.Right - 32, bounds.Top),
				new Vector2(bounds.Right, bounds.Top - 32));

			if (this.HasMenu
				&& this.MenuActions?.Any() == true
				&& (hitRegion.Contains(mouseEvent.Position)
					|| mouseEvent.Button == MouseButtons.Right))
			{
				var popupMenu = new PopupMenu(ApplicationController.Instance.MenuTheme);

				foreach (var menuAction in this.MenuActions.Where(m => m.Scope == ActionScope.ListView))
				{
					if (menuAction is MenuSeparator)
					{
						popupMenu.CreateSeparator();
					}
					else
					{
						var item = popupMenu.CreateMenuItem(menuAction.Title, menuAction.Icon);
						item.Enabled = menuAction.IsEnabled(this.SelectedItems, this);

						if (item.Enabled)
						{
							item.Click += (s, e) =>
							{
								popupMenu.Close();
								menuAction.Action.Invoke(this.SelectedItems.Select(o => o.Model), this);
							};
						}
					}
				}

				RectangleDouble popupBounds;
				if (mouseEvent.Button == MouseButtons.Right)
				{
					popupBounds = new RectangleDouble(mouseEvent.X + 1, mouseEvent.Y + 1, mouseEvent.X + 1, mouseEvent.Y + 1);
				}
				else
				{
					popupBounds = new RectangleDouble(this.Width - 32, this.Height - 32, this.Width, this.Height);
				}

				popupMenu.ShowMenu(this, mouseEvent);
			}
		}

		internal void ApplyFilter(string filterText)
		{
			this.filterText = filterText;
			this.Reload().ConfigureAwait(false);
		}

		internal void ClearFilter()
		{
			this.filterText = null;
			this.Reload().ConfigureAwait(false);
		}

		public override void OnClosed(EventArgs e)
		{
			if (this.libraryContext != null)
			{
				this.libraryContext.ContainerChanged -= this.ActiveContainer_Changed;
				this.libraryContext.ContentChanged -= this.ActiveContainer_ContentChanged;
			}

			base.OnClosed(e);
		}
	}
}
