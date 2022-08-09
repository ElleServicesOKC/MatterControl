﻿/*
Copyright (c) 2018, Lars Brubaker, John Lewin
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
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ClipperLib;
using MatterHackers.Agg.Transform;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.VertexSource;
using MatterHackers.DataConverters2D;
using MatterHackers.DataConverters3D;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.PolygonMesh.Csg;
using MatterHackers.PolygonMesh.Processors;
using MatterHackers.RenderOpenGl;
using MatterHackers.RenderOpenGl.OpenGl;
using MatterHackers.VectorMath;
using Newtonsoft.Json;
using Polygon = System.Collections.Generic.List<ClipperLib.IntPoint>;
using Polygons = System.Collections.Generic.List<System.Collections.Generic.List<ClipperLib.IntPoint>>;

namespace MatterHackers.MatterControl.DesignTools
{
	public enum BaseTypes
	{
		None,
		Rectangle,
		Circle,
		/* Oval, Frame,*/
		Outline
	}

	public class BaseObject3D : Object3D, IPropertyGridModifier, IEditorDraw
	{
		public enum CenteringTypes
		{
			Bounds,
			Weighted
		}

		private readonly double scalingForClipper = 1000;

		public BaseObject3D()
		{
			Name = "Base".Localize();
		}

		public override bool CanApply => true;

		[EnumDisplay(Mode = EnumDisplayAttribute.PresentationMode.Tabs)]
		public BaseTypes BaseType { get; set; } = BaseTypes.Circle;

		[Description("The height the base will be derived from (starting at the bottom).")]
		public DoubleOrExpression CalculationHeight { get; set; } = .1;

		[DisplayName("Expand")]
		[Slider(0, 30, Easing.EaseType.Quadratic, snapDistance: .5)]
		public DoubleOrExpression BaseSize { get; set; } = 3;

		[Slider(0, 10, Easing.EaseType.Quadratic, snapDistance: .1)]
		public DoubleOrExpression InfillAmount { get; set; } = 3;

		[DisplayName("Height")]
		[Slider(1, 50, Easing.EaseType.Quadratic, useSnappingGrid: true)]
		public DoubleOrExpression ExtrusionHeight { get; set; } = 5;

		[DisplayName("")]
		[ReadOnly(true)]
		public string NoBaseMessage { get; set; } = "No base is added under your part. Switch to a different base option to create a base.";

		[DisplayName("")]
		[ReadOnly(true)]
		public string SpaceHolder1 { get; set; } = "";

		[DisplayName("")]
		[ReadOnly(true)]
		public string SpaceHolder2 { get; set; } = "";

		[EnumDisplay(Mode = EnumDisplayAttribute.PresentationMode.Buttons)]
		public CenteringTypes Centering { get; set; } = CenteringTypes.Weighted;

		public override void Cancel(UndoBuffer undoBuffer)
		{
			using (RebuildLock())
			{
				using (new CenterAndHeightMaintainer(this))
				{
					var firstChild = this.Children.FirstOrDefault();

					// only keep the first object
					this.Children.Modify(list =>
					{
						list.Clear();
						// add back in the sourceContainer
						list.Add(firstChild);
					});
				}
			}

			base.Cancel(undoBuffer);
		}

		private (IVertexSource vertexSource, double height) meshVertexCache;

		public static IVertexSource GetSlicePaths(IObject3D source, Plane plane)
		{
			var totalSlice = new Polygons();
			foreach (var item in source.VisibleMeshes())
			{
				var cutPlane = Plane.Transform(plane, item.WorldMatrix(source).Inverted);
				// return the vertex source of the bottom of the mesh
				var slice = SliceLayer.CreateSlice(item.Mesh, cutPlane);
				totalSlice = totalSlice.Union(slice);
			}
		
			return totalSlice.CreateVertexStorage();
		}

		private bool OutlineIsFromMesh
		{
			get
			{
				var vertexSource = this.Descendants<IObject3D>().FirstOrDefault(i => i.VertexSource != null)?.VertexSource;
				var hasMesh = this.Descendants<IObject3D>().Where(m => m.Mesh != null).Any();

				return vertexSource == null && hasMesh;
			}
		}

		[JsonIgnore]
		public override IVertexSource VertexSource
		{
			get
			{
				if (OutlineIsFromMesh)
				{
					var calculationHeight = CalculationHeight.Value(this);
					if (meshVertexCache.vertexSource == null || meshVertexCache.height != calculationHeight)
					{
						var aabb = this.GetAxisAlignedBoundingBox();
						var cutPlane = new Plane(Vector3.UnitZ, new Vector3(0, 0, aabb.MinXYZ.Z + calculationHeight));
						meshVertexCache.vertexSource = GetSlicePaths(this, cutPlane);
						meshVertexCache.height = calculationHeight;
					}

					return meshVertexCache.vertexSource;
				}

				var vertexSource = this.Descendants<IObject3D>().FirstOrDefault((i) => i.VertexSource != null)?.VertexSource;
				return vertexSource;
			}

			set
			{
                var pathObject = this.Children.FirstOrDefault(i => i.VertexSource != null);
                if (pathObject != null)
				{
					pathObject.VertexSource = value;
				}
			}
		}

		public static async Task<BaseObject3D> Create()
		{
			var item = new BaseObject3D();
			await item.Rebuild();
			return item;
		}

		public override async void OnInvalidate(InvalidateArgs invalidateArgs)
		{
			if ((invalidateArgs.InvalidateType.HasFlag(InvalidateType.Children)
				|| invalidateArgs.InvalidateType.HasFlag(InvalidateType.Matrix)
				|| invalidateArgs.InvalidateType.HasFlag(InvalidateType.Path)
				|| invalidateArgs.InvalidateType.HasFlag(InvalidateType.Mesh))
				&& invalidateArgs.Source != this
				&& !RebuildLocked)
			{
				// make sure we clear our cache
				meshVertexCache.vertexSource = null;
				await Rebuild();
			}
			else if ((invalidateArgs.InvalidateType.HasFlag(InvalidateType.Properties) && invalidateArgs.Source == this))
			{
				await Rebuild();
			}
			else if (SheetObject3D.NeedsRebuild(this, invalidateArgs))
			{
				await Rebuild();
			}
			else
			{
				base.OnInvalidate(invalidateArgs);
			}
		}

		public override Task Rebuild()
		{
			this.DebugDepth("Rebuild");

			var rebuildLock = this.RebuildLock();

			return ApplicationController.Instance.Tasks.Execute(
				"Base".Localize(),
				null,
				(reporter, cancellationToken) =>
				{
					using (new CenterAndHeightMaintainer(this, MaintainFlags.Bottom))
					{
						var firstChild = this.Children.FirstOrDefault();

						// remove the base mesh we added
						this.Children.Modify(list =>
						{
							list.Clear();
							// add back in the sourceContainer
							list.Add(firstChild);
						});

						// and create the base
						var vertexSource = this.VertexSource;

						// Convert VertexSource into expected Polygons
						Polygons polygonShape = (vertexSource == null) ? null : vertexSource.CreatePolygons();
						GenerateBase(polygonShape, firstChild.GetAxisAlignedBoundingBox().MinXYZ.Z);
					}

					UiThread.RunOnIdle(() =>
					{
						rebuildLock.Dispose();
						Invalidate(InvalidateType.DisplayValues);
						this.CancelAllParentBuilding();
						Parent?.Invalidate(new InvalidateArgs(this, InvalidateType.Children));
					});

					return Task.CompletedTask;
				});
		}

		private static Polygon GetBoundingPolygon(Polygons basePolygons)
		{
			var min = new IntPoint(long.MaxValue, long.MaxValue);
			var max = new IntPoint(long.MinValue, long.MinValue);

			foreach (Polygon polygon in basePolygons)
			{
				foreach (IntPoint point in polygon)
				{
					min.X = Math.Min(point.X - 10, min.X);
					min.Y = Math.Min(point.Y - 10, min.Y);
					max.X = Math.Max(point.X + 10, max.X);
					max.Y = Math.Max(point.Y + 10, max.Y);
				}
			}

			var boundingPoly = new Polygon();
			boundingPoly.Add(min);
			boundingPoly.Add(new IntPoint(min.X, max.Y));
			boundingPoly.Add(max);
			boundingPoly.Add(new IntPoint(max.X, min.Y));

			return boundingPoly;
		}

		private Polygon GetBoundingCircle(Polygons basePolygons)
		{
			IntPoint center;
			double radius;

			if (Centering == CenteringTypes.Bounds)
			{
				IEnumerable<Vector2> GetVertices()
				{
					foreach (var polygon in basePolygons)
					{
						foreach (var positon in polygon)
						{
							yield return new Vector2(positon.X, positon.Y);
						}
					}
				}

				var circle = SmallestEnclosingCircle.MakeCircle(GetVertices());

				center = new IntPoint(circle.Center.X, circle.Center.Y);
				radius = (long)circle.Radius;
			}
			else
			{
				var outsidePolygons = new List<List<IntPoint>>();
				// remove all holes from the polygons so we only center the major outlines
				var polygons = VertexSource.CreatePolygons();

				foreach (var polygon in polygons)
				{
					if (polygon.GetWindingDirection() == 1)
					{
						outsidePolygons.Add(polygon);
					}
				}

				IVertexSource outsideSource = outsidePolygons.CreateVertexStorage();

				var polyCenter = outsideSource.GetWeightedCenter();

				center = new IntPoint(polyCenter.X * 1000, polyCenter.Y * 1000);
				radius = 0;

				foreach (Polygon polygon in basePolygons)
				{
					foreach (IntPoint point in polygon)
					{
						long length = (point - center).Length();
						if (length > radius)
						{
							radius = length;
						}
					}
				}
			}

			var boundingCircle = new Polygon();
			int numPoints = 100;

			for (int i = 0; i < numPoints; i++)
			{
				double angle = i / 100.0 * Math.PI * 2.0;
				IntPoint newPointOnCircle = new IntPoint(Math.Cos(angle) * radius, Math.Sin(angle) * radius) + center;
				boundingCircle.Add(newPointOnCircle);
			}

			return boundingCircle;
		}

		public void GenerateBase(Polygons polygonShape, double bottomWithoutBase)
		{
			if (polygonShape != null
				&& polygonShape.Select(p => p.Count).Sum() > 3)
			{
				Polygons polysToOffset = new Polygons();

				switch (BaseType)
				{
					case BaseTypes.Rectangle:
						polysToOffset.Add(GetBoundingPolygon(polygonShape));
						break;

					case BaseTypes.Circle:
						polysToOffset.Add(GetBoundingCircle(polygonShape));
						break;

					case BaseTypes.Outline:
						polysToOffset.AddRange(polygonShape);
						break;
				}

				if (polysToOffset.Count > 0)
				{
					Polygons basePolygons;

					var infillAmount = InfillAmount.Value(this);
					var baseSize = BaseSize.Value(this);
					var extrusionHeight = ExtrusionHeight.Value(this);
					if (BaseType == BaseTypes.Outline
						&& infillAmount > 0)
					{
						basePolygons = polysToOffset.Offset((baseSize + infillAmount) * scalingForClipper);
						basePolygons = basePolygons.Offset(-infillAmount * scalingForClipper);
					}
					else
					{
						basePolygons = polysToOffset.Offset(baseSize * scalingForClipper);
					}

					basePolygons = ClipperLib.Clipper.CleanPolygons(basePolygons, 10);

					VertexStorage rawVectorShape = basePolygons.PolygonToPathStorage();
					var vectorShape = new VertexSourceApplyTransform(rawVectorShape, Affine.NewScaling(1.0 / scalingForClipper));

					var mesh = VertexSourceToMesh.Extrude(vectorShape, zHeightTop: extrusionHeight);
					mesh.Translate(new Vector3(0, 0, -extrusionHeight + bottomWithoutBase));

					var baseObject = new Object3D()
					{
						Mesh = mesh
					};
					Children.Add(baseObject);
				}
				else
				{
					// clear the mesh
					Mesh = null;
				}
			}
		}


		public void UpdateControls(PublicPropertyChange change)
		{
			var changeSet = new Dictionary<string, bool>();
			changeSet.Clear();

			changeSet.Add(nameof(NoBaseMessage), BaseType == BaseTypes.None);
			changeSet.Add(nameof(SpaceHolder1), BaseType == BaseTypes.None || BaseType == BaseTypes.Rectangle);
			changeSet.Add(nameof(SpaceHolder2), BaseType == BaseTypes.None);
			changeSet.Add(nameof(BaseSize), BaseType != BaseTypes.None);
			changeSet.Add(nameof(InfillAmount), BaseType == BaseTypes.Outline);
			changeSet.Add(nameof(Centering), BaseType == BaseTypes.Circle);
			changeSet.Add(nameof(ExtrusionHeight), BaseType != BaseTypes.None);

			var vertexSource = this.Descendants<IObject3D>().FirstOrDefault((i) => i.VertexSource != null)?.VertexSource;
            var meshSource = this.Descendants<IObject3D>().Where((i) => i.Mesh != null);

			changeSet.Add(nameof(CalculationHeight), vertexSource == null && meshSource.Where(m => m.Mesh != null).Any());

			// first turn on all the settings we want to see
			foreach (var kvp in changeSet.Where(c => c.Value))
			{
				change.SetRowVisible(kvp.Key, () => kvp.Value);
			}

			// then turn off all the settings we want to hide
			foreach (var kvp in changeSet.Where(c => !c.Value))
			{
				change.SetRowVisible(kvp.Key, () => kvp.Value);
			}
		}

		Matrix4X4 CalcTransform()
		{
			var aabb = this.GetAxisAlignedBoundingBox(this.WorldMatrix());
			return this.WorldMatrix() * Matrix4X4.CreateTranslation(0, 0, CalculationHeight.Value(this) - aabb.MinXYZ.Z + ExtrusionHeight.Value(this));
		}

		public void DrawEditor(Object3DControlsLayer layer, DrawEventArgs e)
		{
			if (OutlineIsFromMesh)
			{
				layer.World.RenderPathOutline(CalcTransform(), VertexSource, Agg.Color.Red, 5);

				// turn the lighting back on
				GL.Enable(EnableCap.Lighting);
			}
		}

		public AxisAlignedBoundingBox GetEditorWorldspaceAABB(Object3DControlsLayer layer)
		{
			if (OutlineIsFromMesh)
			{
				// TODO: Untested.
				return layer.World.GetWorldspaceAabbOfRenderPathOutline(CalcTransform(), VertexSource, 5);
			}
			return AxisAlignedBoundingBox.Empty();
		}
	}
}