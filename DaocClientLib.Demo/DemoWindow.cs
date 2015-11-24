﻿/*
 * DaocClientLib - Dark Age of Camelot Setup Ressources Wrapper
 * 
 * The MIT License (MIT)
 * 
 * Copyright (c) 2015 dol-leodagan
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

// Don't Compile Demo if in an Unsupported Configuration
#if OpenTK || Debug

namespace DaocClientLib.Demo
{
	using System;
	using System.Diagnostics;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.CompilerServices;
	using System.Collections.Generic;
	
	using Gwen.Control;
	
	using OpenTK;
	using OpenTK.Graphics;
	using OpenTK.Graphics.OpenGL;
	using OpenTK.Input;
	
	using SharpNav;
	using SharpNav.Crowds;
	using SharpNav.Geometry;
	using SharpNav.IO.Json;
	using SharpNav.Pathfinding;
	
	using Key = OpenTK.Input.Key;
	
	/// <summary>
	/// Entry Point, Main Window for Demo Program
	/// </summary>
	public partial class DemoWindow : GameWindow
	{
		private enum DisplayMode
		{
			None,
			Heightfield,
			CompactHeightfield,
			DistanceField,
			Regions,
			Contours,
			PolyMesh,
			PolyMeshDetail,
			NavMesh,
			Pathfinding,
		}

		private bool interceptExceptions;

		private Camera cam;
		private float zoom = MathHelper.PiOver4;

		//Generate poly mesh
		private Heightfield heightfield;
		private CompactHeightfield compactHeightfield;
		private Color4[] regionColors;
		private ContourSet contourSet;
		private PolyMesh polyMesh;
		private PolyMeshDetail polyMeshDetail;
		
		//Pathfinding
		//private NavMeshCreateParams parameters;
		private NavMeshBuilder buildData;
		private TiledNavMesh tiledNavMesh;
		private NavMeshQuery navMeshQuery;

		//Smooth path for a single unit
		private NavPoint startPt;
		private NavPoint endPt;
		private List<PolyId> path;
		private List<Vector3> smoothPath;

		//A crowd is made up of multiple units, each with their own path
		private Crowd crowd;
		private const int MAX_AGENTS = 128;
		private const int AGENT_MAX_TRAIL = 64;
		private int numIterations = 50;
		private int numActiveAgents = 0;
		private AgentTrail[] trails = new AgentTrail[MAX_AGENTS];

		private struct AgentTrail
		{
			public Vector3[] Trail;
			public int HTrail;
		}

		private bool hasGenerated;
		private bool displayLevel;
		private DisplayMode displayMode;

		private KeyboardState prevK;
		private MouseState prevM;

		private Gwen.Input.OpenTK gwenInput;
		private Gwen.Renderer.OpenTK gwenRenderer;
		private Gwen.Skin.Base gwenSkin;
		private Gwen.Control.Canvas gwenCanvas;
		private Matrix4 gwenProjection;

		public DemoWindow()
			: base(1600, 900, new GraphicsMode(32, 8, 0, 4))
		{
			cam = new Camera();

			Keyboard.KeyDown += OnKeyboardKeyDown;
			Keyboard.KeyUp += OnKeyboardKeyUp;
			Mouse.ButtonDown += OnMouseButtonDown;
			Mouse.ButtonUp += OnMouseButtonUp;
			Mouse.Move += OnMouseMove;
			Mouse.WheelChanged += OnMouseWheel;

			this.Title = "DaocClientLib Demo";
			//this.Icon = SharpNav.Examples.Properties.Resources.Icon;
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			InitializeOpenGL();

			if (_level != null) LoadLevel();
			LoadDebugMeshes();

			gwenRenderer = new Gwen.Renderer.OpenTK();
			gwenSkin = new Gwen.Skin.TexturedBase(gwenRenderer, "GwenSkin.png");
			gwenCanvas = new Gwen.Control.Canvas(gwenSkin);
			gwenInput = new Gwen.Input.OpenTK(this);

			gwenInput.Initialize(gwenCanvas);
			gwenCanvas.SetSize(Width, Height);
			gwenCanvas.ShouldDrawBackground = false;

			gwenProjection = Matrix4.CreateOrthographicOffCenter(0, Width, Height, 0, -1, 1);

			InitializeUI();
			if (true && _level == null && clientData == null)
			{
				LoadClient(@"C:\Dark Age of Camelot_Latest");
				//_levelId = 221;
				_levelId = 207;
			}
		}

		protected override void OnUpdateFrame(FrameEventArgs e)
		{
			base.OnUpdateFrame(e);

			if (!Focused)
				return;

			KeyboardState k = OpenTK.Input.Keyboard.GetState();
			MouseState m = OpenTK.Input.Mouse.GetState();

			bool isShiftDown = false;
			if (k[Key.LShift] || k[Key.RShift])
				isShiftDown = true;

			//TODO make cam speed/shift speedup controllable from GUI
			float camSpeed = 10f * (float)e.Time * (isShiftDown ? 3f : 1f);
			float zoomSpeed = (float)Math.PI * (float)e.Time * (isShiftDown ? 0.2f : 0.1f);

			if (k[Key.W])
				cam.Move(-camSpeed);
			if (k[Key.A])
				cam.Strafe(-camSpeed);
			if (k[Key.S])
				cam.Move(camSpeed);
			if (k[Key.D])
				cam.Strafe(camSpeed);
			if (k[Key.Q])
				cam.Elevate(camSpeed);
			if (k[Key.E])
				cam.Elevate(-camSpeed);
			if (k[Key.Z])
			{
				zoom += zoomSpeed;
				if (zoom > MathHelper.PiOver2)
					zoom = MathHelper.PiOver2;
			}
			if (k[Key.C])
			{
				zoom -= zoomSpeed;
				if (zoom < 0.002f)
					zoom = 0.002f;
			}

			if (m[MouseButton.Right])
			{
				cam.RotatePitch((m.X - prevM.X) * (float)e.Time * 2f);
				cam.RotateHeading((prevM.Y - m.Y) * (float)e.Time * 2f);
			}

			float aspect = Width / (float)Height;
			Matrix4 persp = Matrix4.CreatePerspectiveFieldOfView(zoom, aspect, 0.1f, 1000f);
			GL.MatrixMode(MatrixMode.Projection);
			GL.LoadMatrix(ref persp);
			GL.MatrixMode(MatrixMode.Modelview);
			cam.LoadView();
			if (crowd != null)
				crowd.Update((float)e.Time);

			prevK = k;
			prevM = m;

			if (gwenRenderer.TextCacheSize > 1000)
				gwenRenderer.FlushTextCache();
		}

		protected void OnKeyboardKeyDown(object sender, KeyboardKeyEventArgs e)
		{
			if (!Focused)
				return;

			if (e.Key == Key.Escape)
				Exit();
			else if (e.Key == Key.F9)
				Gwen.Platform.Neutral.FileOpen("Load NavMesh from file", ".", "All SharpNav Files(.snb, .snx, .snj)|*.snb;*.snx;*.snj|SharpNav Binary(.snb)|*.snb|SharpNav XML(.snx)|*.snx|SharpNav JSON(.snj)|*.snj", LoadNavMeshFromFile);
			else if (e.Key == Key.F11)
				WindowState = OpenTK.WindowState.Normal;
			else if (e.Key == Key.F12)
				WindowState = OpenTK.WindowState.Fullscreen;

			gwenInput.ProcessKeyDown(e);

			base.OnKeyDown(e);
		}

		protected void OnKeyboardKeyUp(object sender, KeyboardKeyEventArgs e)
		{
			if (!Focused)
				return;

			gwenInput.ProcessKeyUp(e);
		}

		protected void OnMouseButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (!Focused)
				return;

			gwenInput.ProcessMouseMessage(e);
		}

		protected void OnMouseButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (!Focused)
				return;

			gwenInput.ProcessMouseMessage(e);
		}

		protected void OnMouseMove(object sender, MouseMoveEventArgs e)
		{
			gwenInput.ProcessMouseMessage(e);
		}

		protected void OnMouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (!Focused)
				return;

			gwenInput.ProcessMouseMessage(e);
		}

		protected override void OnRenderFrame(FrameEventArgs e)
		{
			base.OnRenderFrame(e);

			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

			if (displayLevel && _level != null)
			{
				_level.Draw();
			}
			
			//DrawLevelOutline();

			switch (displayMode)
			{
				case DisplayMode.Heightfield: 
					DrawHeightfield();
					break;
				case DisplayMode.CompactHeightfield:
					DrawCompactHeightfield();
					break;
				case DisplayMode.DistanceField:
					DrawDistanceField();
					break;
				case DisplayMode.Regions:
					DrawRegions();
					break;
				case DisplayMode.Contours:
					DrawContours();
					break;
				case DisplayMode.PolyMesh:
					DrawPolyMesh();
					break;
				case DisplayMode.PolyMeshDetail:
					DrawPolyMeshDetail();
					break;
				case DisplayMode.NavMesh:
					DrawNavMesh();
					break;
				case DisplayMode.Pathfinding:
					DrawPathfinding();
					break;
			}

			DrawCrowd();
			DrawAxes();
			DrawUI();

			SwapBuffers();
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);

			GL.Viewport(0, 0, Width, Height);
			float aspect = Width / (float)Height;

			Matrix4 persp = Matrix4.CreatePerspectiveFieldOfView(zoom, aspect, 0.1f, 1000f);
			GL.MatrixMode(MatrixMode.Projection);
			GL.LoadMatrix(ref persp);
			GL.MatrixMode(MatrixMode.Modelview);
			cam.LoadView();

			gwenProjection = Matrix4.CreateOrthographicOffCenter(0, Width, Height, 0, 0, 1);
			gwenCanvas.SetSize(Width, Height);
		}

		protected override void OnUnload(EventArgs e)
		{
			gwenCanvas.Dispose();
			gwenSkin.Dispose();
			gwenRenderer.Dispose();

			UnloadLevel();
			UnloadDebugMeshes();

			base.OnUnload(e);
		}

		private void LoadNavMeshFromFile(string path)
		{
			tiledNavMesh = new NavMeshJsonSerializer().Deserialize(path);
			displayMode = DisplayMode.NavMesh;
		}

		private void GenerateNavMesh()
		{
			Console.WriteLine("Generating NavMesh");

			Stopwatch sw = new Stopwatch();
			sw.Start();
			long prevMs = 0;
			try
			{
				//level.SetBoundingBoxOffset(new Vector3(settings.CellSize * 0.5f, settings.CellHeight * 0.5f, settings.CellSize * 0.5f));
				
				var levelTris = _level.Triangles().ToArray();
				var triEnumerable = TriangleEnumerable.FromTriangle(levelTris, 0, levelTris.Length);
				BBox3 bounds = triEnumerable.GetBoundingBox();
				
				heightfield = new Heightfield(bounds, settings);

				Console.WriteLine("Heightfield");
				Console.WriteLine(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				Area[] areas = AreaGenerator.From(triEnumerable, Area.Default)
					.MarkAboveHeight(areaSettings.MaxLevelHeight, Area.Null)
					.MarkBelowHeight(areaSettings.MinLevelHeight, Area.Null)
					.MarkBelowSlope(areaSettings.MaxTriSlope, Area.Null)
					.ToArray();
				heightfield.RasterizeTrianglesWithAreas(levelTris, areas);
				//heightfield.RasterizeTriangles(levelTris, Area.Default);

				Console.WriteLine(" + Rasterization\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				Console.WriteLine(" + Filtering");
				prevMs = sw.ElapsedMilliseconds;

				heightfield.FilterLedgeSpans(settings.VoxelAgentHeight, settings.VoxelMaxClimb);

				Console.WriteLine("   + Ledge Spans\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				heightfield.FilterLowHangingWalkableObstacles(settings.VoxelMaxClimb);

				Console.WriteLine("   + Low Hanging Obstacles\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				heightfield.FilterWalkableLowHeightSpans(settings.VoxelAgentHeight);

				Console.WriteLine("   + Low Height Spans\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				compactHeightfield = new CompactHeightfield(heightfield, settings);

				Console.WriteLine("CompactHeightfield");
				Console.WriteLine(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;
				 
				compactHeightfield.Erode(settings.VoxelAgentRadius);

				Console.WriteLine(" + Erosion\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				compactHeightfield.BuildDistanceField();

				Console.WriteLine(" + Distance Field\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				compactHeightfield.BuildRegions(0, settings.MinRegionSize, settings.MergedRegionSize);

				Console.WriteLine(" + Regions\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				Random r = new Random();
				regionColors = new Color4[compactHeightfield.MaxRegions];
				regionColors[0] = Color4.Black;
				for (int i = 1; i < regionColors.Length; i++)
					regionColors[i] = new Color4((byte)r.Next(0, 255), (byte)r.Next(0, 255), (byte)r.Next(0, 255), 255);

				Console.WriteLine(" + Colors\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				contourSet = compactHeightfield.BuildContourSet(settings);

				Console.WriteLine("ContourSet");
				Console.WriteLine(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				polyMesh = new PolyMesh(contourSet, settings);

				Console.WriteLine("PolyMesh");
				Console.WriteLine(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				polyMeshDetail = new PolyMeshDetail(polyMesh, compactHeightfield, settings);

				Console.WriteLine("PolyMeshDetail");
				Console.WriteLine(" + Ctor\t\t\t\t" + (sw.ElapsedMilliseconds - prevMs).ToString("D3") + " ms");
				prevMs = sw.ElapsedMilliseconds;

				hasGenerated = true;


			}
			catch (Exception e)
			{
				if (!interceptExceptions)
					throw;
				else
					Console.WriteLine("Navmesh generation failed with exception:" + Environment.NewLine + e.ToString());
			}
			finally
			{
				sw.Stop();
			}

			if (hasGenerated)
			{
				try
				{
					GeneratePathfinding();

					//Pathfinding with multiple units
					GenerateCrowd();
				}
				catch (Exception e)
				{
					Console.WriteLine("Pathfinding generation failed with exception" + Environment.NewLine + e.ToString());
				}

				Label l = (Label)statusBar.FindChildByName("GenTime");
				l.Text = "Generation Time: " + sw.ElapsedMilliseconds + "ms";

				Console.WriteLine("Navmesh generated successfully in " + sw.ElapsedMilliseconds + "ms.");
				Console.WriteLine("Rasterized " + _level.TriangleCount + " triangles.");
				Console.WriteLine("Generated " + contourSet.Count + " regions.");
				Console.WriteLine("PolyMesh contains " + polyMesh.VertCount + " vertices in " + polyMesh.PolyCount + " polys.");
				Console.WriteLine("PolyMeshDetail contains " + polyMeshDetail.VertCount + " vertices and " + polyMeshDetail.TrisCount + " tris in " + polyMeshDetail.MeshCount + " meshes.");
			}
		}

		private void GeneratePathfinding()
		{
			if (!hasGenerated)
				return;

			Random rand = new Random();

			buildData = new NavMeshBuilder(polyMesh, polyMeshDetail, new SharpNav.Pathfinding.OffMeshConnection[0], settings);

			tiledNavMesh = new TiledNavMesh(buildData);
			navMeshQuery = new NavMeshQuery(tiledNavMesh, 2048);

			//Find random start and end points on the poly mesh
			/*int startRef;
			navMeshQuery.FindRandomPoint(out startRef, out startPos);*/

			Vector3 c = new Vector3(0,0,0);
			Vector3 e = new Vector3(500f, 500f, 500f);
			navMeshQuery.FindNearestPoly(ref c, ref e, out startPt);

			navMeshQuery.FindRandomPointAroundCircle(startPt, 1000, out endPt);

			//calculate the overall path, which contains an array of polygon references
			int MAX_POLYS = 256;
			path = new List<PolyId>(MAX_POLYS);
			navMeshQuery.FindPath(ref startPt, ref endPt, path);

			//find a smooth path over the mesh surface
			int npolys = path.Count;
			PolyId[] polys = path.ToArray();
			Vector3 iterPos = new Vector3();
			Vector3 targetPos = new Vector3();
			navMeshQuery.ClosestPointOnPoly(startPt.Polygon, startPt.Position, ref iterPos);
			navMeshQuery.ClosestPointOnPoly(polys[npolys - 1], endPt.Position, ref targetPos);

			smoothPath = new List<Vector3>(2048);
			smoothPath.Add(iterPos);

			float STEP_SIZE = 0.5f;
			float SLOP = 0.01f;
			while (npolys > 0 && smoothPath.Count < smoothPath.Capacity)
			{
				//find location to steer towards
				Vector3 steerPos = new Vector3();
				int steerPosFlag = 0;
				PolyId steerPosRef = PolyId.Null;

				if (!GetSteerTarget(navMeshQuery, iterPos, targetPos, SLOP, polys, npolys, ref steerPos, ref steerPosFlag, ref steerPosRef))
					break;

				bool endOfPath = (steerPosFlag & PathfindingCommon.STRAIGHTPATH_END) != 0 ? true : false;
				bool offMeshConnection = (steerPosFlag & PathfindingCommon.STRAIGHTPATH_OFFMESH_CONNECTION) != 0 ? true : false;

				//find movement delta
				Vector3 delta = steerPos - iterPos;
				float len = (float)Math.Sqrt(Vector3.Dot(delta, delta));

				//if steer target is at end of path or off-mesh link
				//don't move past location
				if ((endOfPath || offMeshConnection) && len < STEP_SIZE)
					len = 1;
				else
					len = STEP_SIZE / len;

				Vector3 moveTgt = new Vector3();
				VMad(ref moveTgt, iterPos, delta, len);

				//move
				Vector3 result = new Vector3();
				List<PolyId> visited = new List<PolyId>(16);
				navMeshQuery.MoveAlongSurface(new NavPoint(polys[0], iterPos), moveTgt, ref result, visited);
				npolys = FixupCorridor(polys, npolys, MAX_POLYS, visited);
				float h = 0;
				navMeshQuery.GetPolyHeight(polys[0], result, ref h);
				result.Y = h;
				iterPos = result;

				//handle end of path when close enough
				if (endOfPath && InRange(iterPos, steerPos, SLOP, 1.0f))
				{
					//reached end of path
					iterPos = targetPos;
					if (smoothPath.Count < smoothPath.Capacity)
					{
						smoothPath.Add(iterPos);
					}
					break;
				}

				//store results
				if (smoothPath.Count < smoothPath.Capacity)
				{
					smoothPath.Add(iterPos);
				}
			}
		}

		/// <summary>
		/// Scaled vector addition
		/// </summary>
		/// <param name="dest">Result</param>
		/// <param name="v1">Vector 1</param>
		/// <param name="v2">Vector 2</param>
		/// <param name="s">Scalar</param>
		private void VMad(ref Vector3 dest, Vector3 v1, Vector3 v2, float s)
		{
			dest.X = v1.X + v2.X * s;
			dest.Y = v1.Y + v2.Y * s;
			dest.Z = v1.Z + v2.Z * s;
		}

		private bool GetSteerTarget(NavMeshQuery navMeshQuery, Vector3 startPos, Vector3 endPos, float minTargetDist, PolyId[] path, int pathSize,
			ref Vector3 steerPos, ref int steerPosFlag, ref PolyId steerPosRef)
		{
			int MAX_STEER_POINTS = 3;
			Vector3[] steerPath = new Vector3[MAX_STEER_POINTS];
			int[] steerPathFlags = new int[MAX_STEER_POINTS];
			PolyId[] steerPathPolys = new PolyId[MAX_STEER_POINTS];
			int nsteerPath = 0;
			navMeshQuery.FindStraightPath(startPos, endPos, path, pathSize,
				steerPath, steerPathFlags, steerPathPolys, ref nsteerPath, MAX_STEER_POINTS, 0);

			if (nsteerPath == 0)
				return false;

			//find vertex far enough to steer to
			int ns = 0;
			while (ns < nsteerPath)
			{
				if ((steerPathFlags[ns] & PathfindingCommon.STRAIGHTPATH_OFFMESH_CONNECTION) != 0 ||
					!InRange(steerPath[ns], startPos, minTargetDist, 1000.0f))
					break;

				ns++;
			}

			//failed to find good point to steer to
			if (ns >= nsteerPath)
				return false;

			steerPos = steerPath[ns];
			steerPos.Y = startPos.Y;
			steerPosFlag = steerPathFlags[ns];
			steerPosRef = steerPathPolys[ns];

			return true;
		}

		private bool InRange(Vector3 v1, Vector3 v2, float r, float h)
		{
			float dx = v2.X - v1.X;
			float dy = v2.Y - v1.Y;
			float dz = v2.Z - v1.Z;
			return (dx * dx + dz * dz) < (r * r) && Math.Abs(dy) < h;
		}

		private int FixupCorridor(PolyId[] path, int npath, int maxPath, List<PolyId> visited)
		{
			int furthestPath = -1;
			int furthestVisited = -1;

			//find furhtest common polygon
			for (int i = npath - 1; i >= 0; i--)
			{
				bool found = false;
				for (int j = visited.Count - 1; j >= 0; j--)
				{
					if (path[i] == visited[j])
					{
						furthestPath = i;
						furthestVisited = j;
						found = true;
					}
				}

				if (found)
					break;
			}

			//if no intersection found, return current path
			if (furthestPath == -1 || furthestVisited == -1)
				return npath;

			//concatenate paths
			//adjust beginning of the buffer to include the visited
			int req = visited.Count - furthestVisited;
			int orig = Math.Min(furthestPath + 1, npath);
			int size = Math.Max(0, npath - orig);
			if (req + size > maxPath)
				size = maxPath - req;
			for (int i = 0; i < size; i++)
				path[req + i] = path[orig + i];

			//store visited
			for (int i = 0; i < req; i++)
				path[i] = visited[(visited.Count - 1) - i];

			return req + size;
		}

		private void GenerateCrowd()
		{
			if (!hasGenerated)
				return;

			Random rand = new Random();
			crowd = new Crowd(MAX_AGENTS, 0.6f, ref tiledNavMesh);
	
			Vector3 c = new Vector3(0, 0, 0);
			Vector3 e = new Vector3(500f, 500f, 500f);

			AgentParams ap = new AgentParams();
			ap.Radius = 0.6f;
			ap.Height = 2.0f;
			ap.MaxAcceleration = 8.0f;
			ap.MaxSpeed = 3.5f;
			ap.CollisionQueryRange = ap.Radius * 12.0f;
			ap.PathOptimizationRange = ap.Radius * 30.0f;
			ap.UpdateFlags = new UpdateFlags();

			//initialize starting positions for each active agent
			for (int i = 0; i < numActiveAgents; i++)
			{
				//Get the polygon that the starting point is in
				NavPoint startPt;
				navMeshQuery.FindNearestPoly(ref c, ref e, out startPt);

				//Pick a new random point that is within a certain radius of the current point
				NavPoint newPt;
				navMeshQuery.FindRandomPointAroundCircle(startPt, 1000, out newPt);

				c = newPt.Position;

				//Save this random point as the starting position
				trails[i].Trail = new Vector3[AGENT_MAX_TRAIL];
				trails[i].Trail[0] = newPt.Position;
				trails[i].HTrail = 0;

				//add this agent to the crowd
				int idx = crowd.AddAgent(newPt.Position, ap);

				//Give this agent a target point
				NavPoint targetPt;
				navMeshQuery.FindRandomPointAroundCircle(newPt, 1000, out targetPt);

				crowd.GetAgent(idx).RequestMoveTarget(targetPt.Polygon, targetPt.Position);
				trails[i].Trail[AGENT_MAX_TRAIL - 1] = targetPt.Position;
			}
		}
	}
}

#endif