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

namespace DaocClientLib.Drawing
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Collections.Generic;
	using Niflib;
	using Niflib.Extensions;
	
	#if OpenTK
	using OpenTK;
	using Matrix = OpenTK.Matrix4;
	#elif SharpDX
	using SharpDX;
	#elif MonoGame
	using Microsoft.Xna.Framework;
	#endif

	/// <summary>
	/// ClientMesh extract Nif Mesh Rendering.
	/// </summary>
	public class ClientMesh
	{
		#region const Node Names
		/// <summary>
		/// Root Switch Names
		/// </summary>
		public static readonly string[] SwitchNames = { "collisionswitch", "dungswitch", };
		/// <summary>
		/// Pickee Switch Names
		/// </summary>
		public static readonly string[] PickeeNames = { "pickee", };
		/// <summary>
		/// Collidee Switch Names
		/// </summary>
		public static readonly string[] CollideeNames = { "collidee", };
		/// <summary>
		/// Climb Switch Names
		/// </summary>
		public static readonly string[] ClimbNames = { "climb", };
		/// <summary>
		/// Door Switch Names
		/// </summary>
		public static readonly string[] DoorNames = { "door", };
		/// <summary>
		/// Not Drawable Switch Names
		/// </summary>
		public static readonly string[] NotDrawableNames = { "anim", "portal", "bv", "bounding", "!lod_cullme", "!visible_damaged", "shadowcaster", };
		#endregion

		#region nifInfo
		/// <summary>
		/// Nif File Name being Loaded
		/// </summary>
		public string NifName { get; protected set; }
		/// <summary>
		/// Is this Node Root Switched
		/// </summary>
		public bool HasRootSwitch { get; protected set; }
		/// <summary>
		/// Is this Node holding Multiple Roots
		/// </summary>
		public bool HasMultipleRoot { get; protected set; }
		
		
		protected List<string> m_warnings = new List<string>();
		/// <summary>
		/// Warnings while loading this node
		/// </summary>
		public string[] Warnings { get { return m_warnings.ToArray(); } }
		#endregion

		#region geometry members
		/// <summary>
		/// Doors Collidee Mesh indexed by Door Node Name
		/// </summary>
		public Dictionary<string, TriangleCollection> DoorCollidee { get; protected set; }
		/// <summary>
		/// Climb Collidee Mesh indexed by Climb Node Name
		/// </summary>
		public Dictionary<string, TriangleCollection> ClimbCollidee { get; protected set; }
		
		protected TriangleCollection m_pickee = new TriangleCollection
		{ Vertices = new Vector3[0], Indices = new TriangleIndex[0], };
		/// <summary>
		/// Pickee Mesh
		/// </summary>
		public TriangleCollection Pickee { get { return m_pickee; } protected set { m_pickee = value; } }

		protected TriangleCollection m_collidee = new TriangleCollection
		{ Vertices = new Vector3[0], Indices = new TriangleIndex[0], };
		/// <summary>
		/// Collidee Mesh
		/// </summary>
		public TriangleCollection Collidee { get { return m_collidee; } protected set { m_collidee = value; } }

		protected TriangleCollection m_visible = new TriangleCollection
		{ Vertices = new Vector3[0], Indices = new TriangleIndex[0], };
		/// <summary>
		/// Visible Mesh
		/// </summary>
		public TriangleCollection Visible { get { return m_visible; } protected set { m_visible = value; } }
		#endregion
		
		/// <summary>
		/// Initializes a new instance of the <see cref="ClientMesh"/> class.
		/// </summary>
		/// <param name="nifName">Name of the Nif File</param>
		/// <param name="fileContent">Content of the Nif File</param>
		public ClientMesh(string nifName, byte[] fileContent)
			: this(nifName)
		{
			if (string.IsNullOrEmpty(nifName))
				throw new ArgumentNullException("nifName");
			if (fileContent == null || fileContent.Length < 1)
				throw new ArgumentNullException("fileContent");
			
			using (var stream = new MemoryStream(fileContent))
			{
				using (var reader = new BinaryReader(stream))
				{
					var nif = new NiFile(reader);
					LoadGeometry(nif);
				}
			}
		}
		
		/// <summary>
		/// Initializes a new instance of the <see cref="ClientMesh"/> class with predefined Mesh.
		/// </summary>
		/// <param name="nifName">Name of the Nif File</param>
		/// <param name="pickee">pickee Mesh</param>
		/// <param name="collidee">collidee Mesh</param>
		/// <param name="visible">visible Mesh</param>
		public ClientMesh(string nifName, TriangleCollection pickee, TriangleCollection collidee, TriangleCollection visible)
			: this(nifName)
		{
			Pickee = pickee;
			Collidee = collidee;
			Visible = visible;
		}
		
		/// <summary>
		/// Initializes a new "base" instance of the <see cref="ClientMesh"/> class.
		/// </summary>
		/// <param name="nifName">Name of the Nif File</param>
		protected ClientMesh(string nifName)
		{
			NifName = nifName;
			ClimbCollidee = new Dictionary<string, TriangleCollection>();
			DoorCollidee = new Dictionary<string, TriangleCollection>();
		}
		
		/// <summary>
		/// Load Client Geometry from NiFile Object
		/// </summary>
		/// <param name="nif"></param>
		protected void LoadGeometry(NiFile nif)
		{
			HasRootSwitch = true;
			int rootIndex = 0;
			foreach (var root in nif.GetRoots())
			{
				// Get Direct Children
				foreach (var child in root.GetChildren().OfType<NiNode>())
				{
					if (SwitchNames.Any(swi => child.Name.Value.Equals(swi, StringComparison.OrdinalIgnoreCase)))
					{
						// is Switch Node
						foreach (var switchNode in child.GetChildren().OfType<NiNode>())
						{
							if (DoorNames.Any(door => switchNode.Name.Value.StartsWith(door, StringComparison.OrdinalIgnoreCase)))
							{
								// Is Door (load only collidee)
								var collideeNode = switchNode.GetChildren().OfType<NiNode>().FirstOrDefault(doorNode => CollideeNames.Any(coll => doorNode.Name.Value.Equals(coll, StringComparison.OrdinalIgnoreCase)));
								if (collideeNode != null)
								{
									DoorCollidee.Add(switchNode.Name.Value, collideeNode.GetTrianglesFromNode());
								}
								else
								{
									m_warnings.Add(string.Format("Could not Load Collidee from {0} in Nif : {1}", switchNode.Name.Value, NifName));
								}
							}
							else if (ClimbNames.Any(door => switchNode.Name.Value.StartsWith(door, StringComparison.OrdinalIgnoreCase)))
							{
								// Is Climb (load only collidee)
								var collideeNode = switchNode.GetChildren().OfType<NiNode>().FirstOrDefault(climbNode => CollideeNames.Any(coll => climbNode.Name.Value.Equals(coll, StringComparison.OrdinalIgnoreCase)));
								if (collideeNode != null)
								{
									ClimbCollidee.Add(switchNode.Name.Value, collideeNode.GetTrianglesFromNode());
								}
								else
								{
									m_warnings.Add(string.Format("Could not Load Collidee from {0} in Nif : {1}", switchNode.Name.Value, NifName));
								}
							}
							else if (CollideeNames.Any(coll => switchNode.Name.Value.Equals(coll, StringComparison.OrdinalIgnoreCase)))
							{
								// Is Collidee
								var tris = switchNode.GetTrianglesFromNode();
								TriangleCollection result;
								TriangleWalker.Concat(ref m_collidee, ref tris, out result);
								Collidee = result;
							}
							else if (PickeeNames.Any(pick => switchNode.Name.Value.Equals(pick, StringComparison.OrdinalIgnoreCase)))
							{
								// Is Pickee
								var tris = switchNode.GetTrianglesFromNode();
								TriangleCollection result;
								TriangleWalker.Concat(ref m_pickee, ref tris, out result);
								Pickee = result;
							}
							else
							{
								// Handle Visible
								if (switchNode.IsInvisible() || NotDrawableNames.Any(not => switchNode.Name.Value.StartsWith(not, StringComparison.OrdinalIgnoreCase)))
								{
									m_warnings.Add(string.Format("Removed Invisible Node {0} in Nif : {1}", switchNode.Name.Value, NifName));
									continue;
								}
								
								// Remaining should be visible
								var tris = switchNode.GetTrianglesFromNode();
								TriangleCollection result;
								TriangleWalker.Concat(ref m_visible, ref tris, out result);
								Visible = result;
							}
						}
					}
					else
					{
						// Direct Drawed Mesh (Only Visible)
						HasRootSwitch = false;
						var tris = child.GetTrianglesFromNode();
						TriangleCollection result;
						TriangleWalker.Concat(ref m_visible, ref tris, out result);
						Visible = result;
					}
				}
				rootIndex++;
			}
			
			// Store Multiple Root Flag
			HasMultipleRoot = rootIndex > 1;
		}
	}
}
