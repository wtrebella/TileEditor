﻿using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;

[CustomEditor(typeof(TileMap))]
public class TileMapEditor : Editor
{
	enum State { Hover, BoxSelect }
	static Vector3[] rect = new Vector3[4];

	TileMap tileMap;
	FieldInfo undoCallback;
	bool editing;
	Matrix4x4 worldToLocal;

	State state;
	int cursorX;
	int cursorOtherAxis;
	int cursorClickX;
	int cursorClickOtherAxis;
	bool deleting;
	int direction;

	bool updateConnections = true;
	bool wireframeHidden;

	#region Inspector GUI

	public override void OnInspectorGUI()
	{
		//Get tilemap
		if (tileMap == null)
			tileMap = (TileMap)target;

		//Crazy hack to register undo
//		if (undoCallback == null)
//		{
//			undoCallback = typeof(EditorApplication).GetField("undoRedoPerformed", BindingFlags.NonPublic | BindingFlags.Static);
//			if (undoCallback != null)
//				undoCallback.SetValue(null, new EditorApplication.CallbackFunction(OnUndoRedo));
//		}

		//Toggle editing mode
		if (editing)
		{
			if (GUILayout.Button("Stop Editing"))
				editing = false;
			else
			{
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Update All"))
					UpdateAll();
				if (GUILayout.Button("Clear"))
					Clear();
				EditorGUILayout.EndHorizontal();
			}
		}
		else if (GUILayout.Button("Edit TileMap"))
			editing = true;

		//Tile Size
		EditorGUI.BeginChangeCheck();
		var newTileSize = EditorGUILayout.FloatField("Tile Size", tileMap.tileSize);
		if (EditorGUI.EndChangeCheck())
		{
			//RecordDeepUndo();
			tileMap.tileSize = newTileSize;
			UpdatePositions();
		}

		//Tile Prefab
		EditorGUI.BeginChangeCheck();
		var newTilePrefab = (Transform)EditorGUILayout.ObjectField("Tile Prefab", tileMap.tilePrefab, typeof(Transform), false);
		if (EditorGUI.EndChangeCheck())
		{
			//RecordUndo();
			tileMap.tilePrefab = newTilePrefab;
		}

		//Tile Map
		EditorGUI.BeginChangeCheck();
		var newTileSet = (TileSet)EditorGUILayout.ObjectField("Tile Set", tileMap.tileSet, typeof(TileSet), false);
		if (EditorGUI.EndChangeCheck())
		{
			//RecordUndo();
			tileMap.tileSet = newTileSet;
		}

		//Tile Prefab selector
		if (tileMap.tileSet != null)
		{
			EditorGUI.BeginChangeCheck();
			var names = new string[tileMap.tileSet.prefabs.Length + 1];
			var values = new int[names.Length + 1];
			names[0] = tileMap.tilePrefab != null ? tileMap.tilePrefab.name : "";
			values[0] = 0;
			for (int i = 1; i < names.Length; i++)
			{
				names[i] = tileMap.tileSet.prefabs[i - 1] != null ? tileMap.tileSet.prefabs[i - 1].name : "";
				//if (i < 10)
				//	names[i] = i + ". " + names[i];
				values[i] = i;
			}
			var index = EditorGUILayout.IntPopup("Select Tile", 0, names, values);
			if (EditorGUI.EndChangeCheck() && index > 0)
			{
				//RecordUndo();
				tileMap.tilePrefab = tileMap.tileSet.prefabs[index - 1];
			}
		}

		//Selecting direction
		EditorGUILayout.BeginHorizontal(GUILayout.Width(60));
		EditorGUILayout.PrefixLabel("Direction");
		EditorGUILayout.BeginVertical(GUILayout.Width(20));
		GUILayout.Space(20);
		if (direction == 3)
			GUILayout.Box("<", GUILayout.Width(20));
		else if (GUILayout.Button("<"))
			direction = 3;
		GUILayout.Space(20);
		EditorGUILayout.EndVertical();
		EditorGUILayout.BeginVertical(GUILayout.Width(20));
		if (direction == 0)
			GUILayout.Box("^", GUILayout.Width(20));
		else if (GUILayout.Button("^"))
			direction = 0;
		if (direction == -1)
			GUILayout.Box("?", GUILayout.Width(20));
		else if (GUILayout.Button("?"))
			direction = -1;
		if (direction == 2)
			GUILayout.Box("v", GUILayout.Width(20));
		else if (GUILayout.Button("v"))
			direction = 2;
		EditorGUILayout.EndVertical();
		EditorGUILayout.BeginVertical(GUILayout.Width(20));
		GUILayout.Space(20);
		if (direction == 1)
			GUILayout.Box(">", GUILayout.Width(20));
		else if (GUILayout.Button(">"))
			direction = 1;
		GUILayout.Space(20);
		EditorGUILayout.EndVertical();
		EditorGUILayout.EndHorizontal();

		//Connect diagonals
		EditorGUI.BeginChangeCheck();
		var newConnectDiagonals = EditorGUILayout.Toggle("Connect Diagonals", tileMap.connectDiagonals);
		if (EditorGUI.EndChangeCheck())
		{
			//RecordUndo();
			tileMap.connectDiagonals = newConnectDiagonals;
			updateConnections = true;
			SceneView.RepaintAll();
		}

		//Connect diagonals
		if (tileMap.connectDiagonals)
		{
			EditorGUI.BeginChangeCheck();
			var newCutCorners = EditorGUILayout.Toggle("Cut Corners", tileMap.cutCorners);
			if (EditorGUI.EndChangeCheck())
			{
				//RecordUndo();
				tileMap.cutCorners = newCutCorners;
				updateConnections = true;
				SceneView.RepaintAll();
			}
		}

		//Draw path tiles
		EditorGUI.BeginChangeCheck();
		drawPathMap = EditorGUILayout.Toggle("Draw Path Map", drawPathMap);
		if (EditorGUI.EndChangeCheck())
			SceneView.RepaintAll();
	}

	#endregion

	#region Scene GUI

	void OnSceneGUI()
	{
		//Get tilemap
		if (tileMap == null)
			tileMap = (TileMap)target;

		//Update paths
		if (updateConnections)
		{
			updateConnections = false;
			tileMap.UpdateConnections();
		}

		//Toggle editing
		if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab)
		{
			editing = !editing;
			EditorUtility.SetDirty(target);
		}

		//Toggle selected tile
		/*if (tileMap.tileSet != null)
		{
			if (e.type == EventType.KeyDown)
			{
				var code = (int)e.keyCode - (int)KeyCode.Alpha1;
				if (code >= 0 && code < tileMap.tileSet.prefabs.Length)
				{
					RecordUndo();
					tileMap.tilePrefab = tileMap.tileSet.prefabs[code];
					e.Use();
					return;
				}
			}
		}*/

		//Draw path nodes
		if (drawPathMap)
		{
			Handles.color = new Color(0, 0, 1, 0.5f);
			foreach (var instance in tileMap.instances)
			{
				var tile = instance.GetComponent<PathTile>();
				if (tile != null)
				{
					Handles.DotCap(0, tile.transform.localPosition, Quaternion.identity, tileMap.tileSize / 17);
					foreach (var other in tile.connections)
						if (other != null && tile.GetInstanceID() > other.GetInstanceID())
							Handles.DrawLine(tile.transform.localPosition, other.transform.localPosition);
				}
			}
		}

		if (editing)
		{
			//Hide mesh
			HideWireframe(true);

			//Quit on tool change
			if (e.type == EventType.KeyDown)
			{
				switch (e.keyCode)
				{
				case KeyCode.Q:
				case KeyCode.W:
				case KeyCode.E:
				case KeyCode.R:
					return;
				}
			}

			//Quit if panning or no camera exists
			if (Tools.current == Tool.View || (e.isMouse && e.button > 1) || Camera.current == null || e.type == EventType.ScrollWheel)
				return;
			
			//Quit if laying out
			if (e.type == EventType.Layout)
			{
				HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
				return;
			}

			//Update matrices
			Handles.matrix = tileMap.transform.localToWorldMatrix;
			worldToLocal = tileMap.transform.worldToLocalMatrix;

			//Draw axes
			Handles.color = Color.red;
			Handles.DrawLine(new Vector3(-tileMap.tileSize, 0, 0), new Vector3(tileMap.tileSize, 0, 0));
			if (tileMap.tileMapType == TileMapType.XZ_3D) Handles.DrawLine(new Vector3(0, 0, -tileMap.tileSize), new Vector3(0, 0, tileMap.tileSize));
			else if (tileMap.tileMapType == TileMapType.XY_2D) Handles.DrawLine(new Vector3(0, -tileMap.tileSize, 0), new Vector3(0, tileMap.tileSize, 0));

			//Update mouse position
			Plane plane = new Plane();
			if (tileMap.tileMapType == TileMapType.XZ_3D) plane = new Plane(tileMap.transform.up, tileMap.transform.position);
			else if (tileMap.tileMapType == TileMapType.XY_2D) plane = new Plane(tileMap.transform.forward, tileMap.transform.position);
			var ray = Camera.current.ScreenPointToRay(new Vector3(e.mousePosition.x, Camera.current.pixelHeight - e.mousePosition.y));
			float hit;
			if (!plane.Raycast(ray, out hit))
				return;
			var mousePosition = worldToLocal.MultiplyPoint(ray.GetPoint(hit));
			cursorX = Mathf.RoundToInt(mousePosition.x / tileMap.tileSize);
			if (tileMap.tileMapType == TileMapType.XZ_3D) cursorOtherAxis = Mathf.RoundToInt(mousePosition.z / tileMap.tileSize);
			else if (tileMap.tileMapType == TileMapType.XY_2D) cursorOtherAxis = Mathf.RoundToInt(mousePosition.y / tileMap.tileSize);

			//Update the state and repaint
			state = UpdateState();
			HandleUtility.Repaint();
			e.Use();
		}
		else
			HideWireframe(false);
	}

	void HideWireframe(bool hide)
	{
		if (wireframeHidden != hide)
		{
			wireframeHidden = hide;
			foreach (var renderer in tileMap.transform.GetComponentsInChildren<Renderer>())
				EditorUtility.SetSelectedWireframeHidden(renderer, hide);
		}
	}

	#endregion

	#region Update state

	State UpdateState()
	{
		switch (state)
		{
		//Hovering
		case State.Hover:
			DrawGrid();
			DrawRect(cursorX, cursorOtherAxis, 1, 1, Color.white, new Color(1, 1, 1, 0f));
			if (e.type == EventType.MouseDown && e.button < 2)
			{
				cursorClickX = cursorX;
				cursorClickOtherAxis = cursorOtherAxis;

				bool isMacControlClicking = Application.platform == RuntimePlatform.OSXEditor && e.control;
				deleting = e.button > 0 || isMacControlClicking;
				return State.BoxSelect;
			}
			break;

		//Placing
		case State.BoxSelect:

			//Get the drag selection
			var x = Mathf.Min(cursorX, cursorClickX);
			var otherAxis = Mathf.Min(cursorOtherAxis, cursorClickOtherAxis);
			var sizeX = Mathf.Abs(cursorX - cursorClickX) + 1;
			var sizeOtherAxis = Mathf.Abs(cursorOtherAxis - cursorClickOtherAxis) + 1;
			
			//Draw the drag selection
			DrawRect(x, otherAxis, sizeX, sizeOtherAxis, Color.white, deleting ? new Color(1, 0, 0, 0.2f) : new Color(0, 1, 0, 0.2f));

			//Finish the drag
			if (e.type == EventType.MouseUp && e.button < 2)
			{
				if (deleting)
				{
					bool isMacControlClicking = Application.platform == RuntimePlatform.OSXEditor && e.control;
					if (e.button > 0 || isMacControlClicking)
						SetRect(x, otherAxis, sizeX, sizeOtherAxis, null, direction);
				}
				else if (e.button == 0)
					SetRect(x, otherAxis, sizeX, sizeOtherAxis, tileMap.tilePrefab, direction);

				return State.Hover;
			}
			break;
		}
		return state;
	}

	void DrawGrid()
	{
		var gridSize = 5;
		var maxDist = Mathf.Sqrt(Mathf.Pow(gridSize - 1, 2) * 2) * 0.75f;
		for (int x = -gridSize; x <= gridSize; x++)
		{
			for (int otherAxis = -gridSize; otherAxis <= gridSize; otherAxis++)
			{
				Handles.color = new Color(1, 1, 1, 1 - Mathf.Sqrt(x * x + otherAxis * otherAxis) / maxDist);
				Vector3 p = Vector3.zero;
				if (tileMap.tileMapType == TileMapType.XZ_3D) p = new Vector3((cursorX + x) * tileMap.tileSize, 0, (cursorOtherAxis + otherAxis) * tileMap.tileSize);
				else if (tileMap.tileMapType == TileMapType.XY_2D) p = new Vector3((cursorX + x) * tileMap.tileSize, (cursorOtherAxis + otherAxis) * tileMap.tileSize, 0);
				Handles.DotCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.02f);
			}
		}
	}

	void DrawRect(int x, int otherAxis, int sizeX, int sizeOtherAxis, Color outline, Color fill)
	{
		Handles.color = Color.white;
		Vector3 min, max = Vector3.zero;

		if (tileMap.tileMapType == TileMapType.XZ_3D) {
			min = new Vector3(x * tileMap.tileSize - tileMap.tileSize / 2, 0, otherAxis * tileMap.tileSize - tileMap.tileSize / 2);
			max = min + new Vector3(sizeX * tileMap.tileSize, 0, sizeOtherAxis * tileMap.tileSize);
			
			rect[0].Set(min.x, 0, min.z);
			rect[1].Set(max.x, 0, min.z);
			rect[2].Set(max.x, 0, max.z);
			rect[3].Set(min.x, 0, max.z);
		}
		else if (tileMap.tileMapType == TileMapType.XY_2D) {
			min = new Vector3(x * tileMap.tileSize - tileMap.tileSize / 2, otherAxis * tileMap.tileSize - tileMap.tileSize / 2, 0);
			max = min + new Vector3(sizeX * tileMap.tileSize, sizeOtherAxis * tileMap.tileSize, 0);

			rect[0].Set(min.x, min.y, 0);
			rect[1].Set(max.x, min.y, 0);
			rect[2].Set(max.x, max.y, 0);
			rect[3].Set(min.x, max.y, 0);
		}

		Handles.DrawSolidRectangleWithOutline(rect, fill, outline);
	}

	#endregion

	#region Modifying TileMap

	bool UpdateTile(int index)
	{
		//Destroy existing tile
		if (tileMap.instances[index] != null)
		{
#if UNITY_4_3
			//Undo.SetTransformParent(tileMap.instances[index], null, "Undo new Transform parent");
			DestroyImmediate(tileMap.instances[index].gameObject);
#else
			DestroyImmediate(tileMap.instances[index].gameObject);
#endif
		}

		//Check if prefab is null
		if (tileMap.prefabs[index] != null)
		{
			//Place the tile
			var instance = (Transform)PrefabUtility.InstantiatePrefab(tileMap.prefabs[index]);
			instance.parent = tileMap.transform;
			instance.localPosition = tileMap.GetPosition(index);

			if (tileMap.tileMapType == TileMapType.XZ_3D) instance.localRotation = Quaternion.Euler(0, tileMap.directions[index] * 90, 0);
			else if (tileMap.tileMapType == TileMapType.XY_2D) instance.localRotation = Quaternion.Euler(0, 0, tileMap.directions[index] * -90);

			tileMap.instances[index] = instance;
			wireframeHidden = false;
			return true;
		}
		else
		{
			//Remove the tile
			tileMap.hashes.RemoveAt(index);
			tileMap.prefabs.RemoveAt(index);
			tileMap.directions.RemoveAt(index);
			tileMap.instances.RemoveAt(index);
			return false;
		}
	}

	void UpdatePositions()
	{
		for (int i = 0; i < tileMap.hashes.Count; i++)
			if (tileMap.instances[i] != null)
				tileMap.instances[i].localPosition = tileMap.GetPosition(i);
	}
	
	void UpdateAll()
	{
		int x, otherAxis;
		for (int i = 0; i < tileMap.hashes.Count; i++)
		{
			tileMap.GetPosition(i, out x, out otherAxis);
			SetTile(x, otherAxis, tileMap.prefabs[i], tileMap.directions[i]);
		}
	}

	void Clear()
	{
		//RecordDeepUndo();
		int x, otherAxis;
		while (tileMap.hashes.Count > 0)
		{
			tileMap.GetPosition(0, out x, out otherAxis);
			SetTile(x, otherAxis, null, 0);
		}
	}

	bool SetTile(int x, int otherAxis, Transform prefab, int direction)
	{
		var hash = tileMap.GetHash(x, otherAxis);
		var index = tileMap.hashes.IndexOf(hash);
		if (index >= 0)
		{
			//Replace existing tile
			tileMap.prefabs[index] = prefab;
			if (direction < 0)
				tileMap.directions[index] = Random.Range(0, 4);
			else
				tileMap.directions[index] = direction;
			return UpdateTile(index);
		}
		else if (prefab != null)
		{
			//Create new tile
			index = tileMap.prefabs.Count;
			tileMap.hashes.Add(hash);
			tileMap.prefabs.Add(prefab);
			if (direction < 0)
				tileMap.directions.Add(Random.Range(0, 4));
			else
				tileMap.directions.Add(direction);
			tileMap.instances.Add(null);
			return UpdateTile(index);
		}
		else 
			return false;
	}

	void SetRect(int x, int otherAxis, int sizeX, int sizeOtherAxis, Transform prefab, int direction)
	{
		//RecordDeepUndo();
		for (int xx = 0; xx < sizeX; xx++)
			for (int otherAxiss = 0; otherAxiss < sizeOtherAxis; otherAxiss++)
				SetTile(x + xx, otherAxis + otherAxiss, prefab, direction);
	}

	#endregion
	
	#region Undo handling

//	void OnUndoRedo()
//	{
//		UpdatePositions();
//		updateConnections = true;
//	}
//	
//	void RecordUndo()
//	{
//		updateConnections = true;
//#if UNITY_4_3
//		Undo.RecordObject(target, "TileMap Changed");
//#else
//		Undo.RegisterUndo(target, "TileMap Changed");
//#endif
//	}
//
//	void RecordDeepUndo()
//	{
//		updateConnections = true;
//#if UNITY_4_3
//		Undo.RegisterFullObjectHierarchyUndo(target);
//#else
//		Undo.RegisterSceneUndo("TileMap Changed");
//#endif
//	}
	
	#endregion

	#region Properties

	Event e
	{
		get { return Event.current; }
	}

	bool drawPathMap
	{
		get { return EditorPrefs.GetBool("TileMapEditor_drawPathMap", true); }
		set { EditorPrefs.SetBool("TileMapEditor_drawPathMap", value); }
	}

	#endregion

	#region Menu items

	[MenuItem("GameObject/Create Other/3D TileMap", false, 2000)]
	static void Create3DTileMap()
	{
		var obj = new GameObject("TileMap");
		TileMap tileMap = obj.AddComponent<TileMap>();
		tileMap.tileMapType = TileMapType.XZ_3D;
	}

	[MenuItem("GameObject/Create Other/2D TileMap", false, 2000)]
	static void Create2DTileMap()
	{
		var obj = new GameObject("TileMap");
		TileMap tileMap = obj.AddComponent<TileMap>();
		tileMap.tileMapType = TileMapType.XY_2D;
	}

	[MenuItem("Assets/Create/TileSet")]
	static void CreateTileSet()
	{
		var asset = ScriptableObject.CreateInstance<TileSet>();
		var path = AssetDatabase.GetAssetPath(Selection.activeObject);

		Debug.Log(path.ToString());

		if (string.IsNullOrEmpty(path))	{
			path = "Assets/";
		}
		else if (Path.GetExtension(path) != "")	{
			path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
		}
		else {
			path += "/";
		}
		
		var assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "TileSet.asset");
		Debug.Log(assetPathAndName);
		AssetDatabase.CreateAsset(asset, assetPathAndName);
		AssetDatabase.SaveAssets();
		EditorUtility.FocusProjectWindow();
		Selection.activeObject = asset;
		asset.hideFlags = HideFlags.DontSave;
	}

	#endregion
}
