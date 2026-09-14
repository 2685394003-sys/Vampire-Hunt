using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

[ExecuteInEditMode]
public enum TopologyType
{
	Undefined,
	Variable,
	Fixed	
}

// Lightweight editor-only coroutine runner. The imported package used
// an external editor-coroutine package without declaring that dependency, which made a clean
// project fail to compile. EditorApplication.update keeps the same frame-based
// behaviour without adding a project-wide dependency.
internal sealed class PandaEditorCoroutine
{
	private readonly IEnumerator routine;
	private bool stopped;

	private PandaEditorCoroutine(IEnumerator routineToRun)
	{
		routine = routineToRun;
		EditorApplication.update += Tick;
	}

	public static PandaEditorCoroutine Start(IEnumerator routineToRun)
	{
		return routineToRun == null ? null : new PandaEditorCoroutine(routineToRun);
	}

	public void Stop()
	{
		if (stopped) return;
		stopped = true;
		EditorApplication.update -= Tick;
	}

	private void Tick()
	{
		if (stopped) return;
		try
		{
			if (!routine.MoveNext()) Stop();
		}
		catch (System.Exception exception)
		{
			Stop();
			Debug.LogException(exception);
		}
	}
}

public class PandaATV : EditorWindow
{  
	public string ExportPath = "Assets/VampireHunt/Art/PandaShaderV2.3/Assert/ATV/";
	public string ExportFilename = "VAT";
	public float StartTime = 0.0f;
	public float EndTime = 1.0f;
	public float SampleRate = 24.0f;
	public TopologyType VariableTopology = TopologyType.Undefined;

	public Transform MeshToBake;
	public Material ReferenceMaterial;
	

	[MenuItem("Window/PandaATV")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(PandaATV));
    }
    
	public void Awake()
    {
		ReferenceMaterial = Resources.Load<Material>("PandavfxVATMaterial");
	}

    void OnGUI()
    {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		GUILayout.Label ("输入", EditorStyles.boldLabel);
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUI.BeginChangeCheck();
 		MeshToBake = EditorGUILayout.ObjectField("目标", MeshToBake, typeof(Transform), true) as Transform;
		if (EditorGUI.EndChangeCheck())
		{
			Debug.Log("Object to bake has changed ... updating data");
			currentBaking?.Stop();
			currentBaking = PandaEditorCoroutine.Start(UpdateFromAlembic());
		}
	
		ReferenceMaterial = EditorGUILayout.ObjectField("关联材质", ReferenceMaterial, typeof(Material), true) as Material;
		EditorGUILayout.EndVertical();
		EditorGUILayout.EndVertical();
		//GUILayout.Space(10);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		GUILayout.Label ("输出", EditorStyles.boldLabel);
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		ExportPath = EditorGUILayout.TextField ("输出路径", ExportPath);
        ExportFilename = EditorGUILayout.TextField ("命名", ExportFilename);
        GUILayout.Space(2);
        GUILayout.Label ("路径 : "+ ExportPath + "Resources/"+ExportFilename+"_xxx.xxx");
		EditorGUILayout.EndVertical();
		EditorGUILayout.EndVertical();
		//	GUILayout.Space(10);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		GUILayout.Label ("动画", EditorStyles.boldLabel);
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		if (VariableTopology!=TopologyType.Undefined)
		{
			StartTime = EditorGUILayout.FloatField("开始时间(秒)", StartTime);
			EndTime = EditorGUILayout.FloatField("结束时间(秒)", EndTime);
			SampleRate = EditorGUILayout.FloatField("帧频", SampleRate);

		}
		switch (VariableTopology)
		{
			case TopologyType.Undefined:
		        GUILayout.Label ("目标错误,请尝试其他层级");
				break;
			case TopologyType.Fixed:
		     //   GUILayout.Label ("Fixed topology (morphing mesh)");
				break;
			case TopologyType.Variable:
		     //   GUILayout.Label ("Variable topology (mesh sequence)");
				break;
		}
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		GUILayout.Label("重要提示：");
		GUILayout.Label ("目标选择后，加载动画需要稍等片刻。");
		GUILayout.Label("再次渲染时请先删除之前的文件或者使用新的命名。");
			EditorGUILayout.EndVertical();
		EditorGUILayout.EndVertical();
		EditorGUILayout.EndVertical();
	//	GUILayout.Space(10);

		if (VariableTopology!=TopologyType.Undefined&& ReferenceMaterial!=null)
		{
			if (bakingInProgress)
			{
				if (GUILayout.Button("取消渲染"))
				{
					CancelBake();
				}
			}
			else
			{
				if (GUILayout.Button("渲染"))
				{
					BakeMesh();
				}
			}
		}
    }

	SerializedProperty timeProp = null;
	SerializedProperty startTimeProp = null;
	SerializedProperty endTimeProp = null;
	SerializedObject alembicObject = null;
	PandaEditorCoroutine currentBaking = null;

	bool bakingInProgress = false;
	int maxTriangleCount=0;
	int minTriangleCount=10000000;

	SerializedObject InitAlembic()
	{
		if (MeshToBake==null)
		{
			Debug.LogError("No mesh to bake!");
			return null;
		}

		if (MeshToBake.parent==null)
		{
			Debug.LogError("Transform should be under an Alembic player");
			return null;
		}

		GameObject parent = MeshToBake.parent.gameObject;
		if (parent==null)
		{
			Debug.LogError("No parent!");
			return null;
		}

        var alembicPlayer = parent.gameObject.GetComponent("AlembicStreamPlayer");
		if (alembicPlayer == null)
		{
			Debug.LogError("Alembic player!");
			return null;
		}
		alembicObject = new SerializedObject(alembicPlayer);

		timeProp = alembicObject.FindProperty("currentTime");
		startTimeProp = alembicObject.FindProperty("startTime");
		endTimeProp = alembicObject.FindProperty("endTime");

		return alembicObject;
	}

	private void BakeMesh()
	{
		Debug.Log("Start baking mesh!");
        currentBaking?.Stop();
        currentBaking = PandaEditorCoroutine.Start(ExportFrames());
	}

	IEnumerator UpdateFromAlembic()
	{
		Debug.Log("Get time from Alembic!");
		VariableTopology = TopologyType.Undefined;

		SerializedObject alembic = InitAlembic();
		if (alembic!=null)
		{

			maxTriangleCount=0;
			minTriangleCount=10000000;

			{
				Debug.Log("Checking max triangle count");
				int framesCount = Mathf.RoundToInt((EndTime-StartTime) * SampleRate + 0.5f);
				float maintime = EndTime - StartTime;

				for (int frame = 0;frame<framesCount;frame++)
				{
					float timing = StartTime + ((float)frame)/SampleRate;
					timeProp.floatValue = timing; 
					alembicObject.ApplyModifiedProperties();
					yield return null;

					int triangleCount = 0;
					for(int i=0;i<MeshToBake.childCount;i++)
					{
						MeshFilter localMeshFilter = MeshToBake.GetChild(i).GetComponent<MeshFilter>();
						if (localMeshFilter != null)
						{
							triangleCount += localMeshFilter.sharedMesh.triangles.Length/3;
						}
					}
					if (triangleCount>maxTriangleCount)
						maxTriangleCount = triangleCount;
					if (triangleCount<minTriangleCount)
						minTriangleCount = triangleCount;
				}
				Debug.Log("Max triangles count : " + maxTriangleCount);
				Debug.Log("Min triangles count : " + minTriangleCount);
			}
			yield return null;
			StartTime = 0.0f;
			SampleRate = 1.0f/startTimeProp.floatValue;
			if (SampleRate > 30f) {
				SampleRate = 30f;
			}
			EndTime = endTimeProp.floatValue;
			VariableTopology = (maxTriangleCount == minTriangleCount)?TopologyType.Fixed : TopologyType.Variable;
			yield return null;
		}
	}

	private void CancelBake()
	{
		Debug.Log("Cancel current baking!");
		currentBaking?.Stop();
		currentBaking = null;
	}

	Vector2 getUV(int Xpos, int Ypos, int Xsize, int Ysize)
	{
		Vector2 uv = new Vector2();

		uv.x = (0.5f + (float)Xpos)/(float)Xsize;
		uv.y = (0.5f + (float)Ypos)/(float)Ysize;

		return uv;
	}

	Vector2Int getCoord(int Xindex, int Yindex, int Xsize, int Ysize, int columnSize)
	{
		Vector2Int uv = new Vector2Int();
		
		int columnIndex = Yindex/Ysize;
		int verticalIndex = Yindex % Ysize;

		uv.x = Xindex + columnIndex*columnSize;
		uv.y = verticalIndex;

		return uv;
	}

	IEnumerator ExportFrames()
	{
		Mesh bakedMesh=null;
		Vector3[] vertices;
		Vector2[] uv;
		Vector3[] normals;
	//	Vector4[] tangents;
		Color[] colors;
		int[] triangles;
		int verticesCount = 0;
		int trianglesIndexCount = 0;

		string finalExportPath = ExportPath + "Resources/";

		SerializedObject alembic = InitAlembic();

		timeProp.floatValue = StartTime; 
		alembicObject.ApplyModifiedProperties();
		yield return null;


		bakedMesh = new Mesh();
		bakedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

		verticesCount = 0;
		trianglesIndexCount = 0;

		if ((MeshToBake != null) && (MeshToBake.childCount!=0))
		{
			bool hasNormal = false;
			bool hasUVs = false;
			bool hasColors = false;

			if (VariableTopology == TopologyType.Variable)
			{
				hasNormal = true;
				verticesCount = maxTriangleCount*3;
				trianglesIndexCount = maxTriangleCount*3;
			}
			else
			{
				for(int i=0;i<MeshToBake.childCount;i++)
				{
					MeshFilter localMeshFilter = MeshToBake.GetChild(i).GetComponent<MeshFilter>();

					if (localMeshFilter != null)
					{
						verticesCount += localMeshFilter.sharedMesh.vertices.Length;
						trianglesIndexCount += localMeshFilter.sharedMesh.triangles.Length;

						hasNormal |= (localMeshFilter.sharedMesh.normals.Length>0);
						hasColors |= (localMeshFilter.sharedMesh.colors.Length>0);
						hasUVs |= (localMeshFilter.sharedMesh.uv.Length>0);				
					}
				}		
			}

			vertices = new Vector3[verticesCount];
			uv = new Vector2[verticesCount];
			normals = new Vector3[verticesCount];
			colors = new Color[verticesCount];
			triangles = new int[trianglesIndexCount];

			int currentTrianglesIndex = 0;
			int verticesOffset = 0;

			if (VariableTopology == TopologyType.Variable)
			{
				for(int i=0;i<verticesCount;i++)	// everything is initialized to 0
				{
					triangles[i] = i;
					vertices[i] = Vector3.zero;
					normals[i] = Vector3.up;
				}
			}
			else
			{
				for(int i=0;i<MeshToBake.childCount;i++)
				{
					MeshFilter localMeshFilter = MeshToBake.GetChild(i).GetComponent<MeshFilter>();
					if (localMeshFilter != null)
					{
						float random = Random.value;				
						for(int j=0;j<localMeshFilter.sharedMesh.vertices.Length;j++)
						{
							if (hasUVs)
								uv[j + verticesOffset] = localMeshFilter.sharedMesh.uv[j];
							if (hasColors)
								colors[j + verticesOffset] = localMeshFilter.sharedMesh.colors[j];

							vertices[j + verticesOffset] = localMeshFilter.sharedMesh.vertices[j];
						}

						for(int j=0;j<localMeshFilter.sharedMesh.triangles.Length;j++)
						{
							triangles[currentTrianglesIndex++] = localMeshFilter.sharedMesh.triangles[j] + verticesOffset;
						}

						verticesOffset += localMeshFilter.sharedMesh.vertices.Length;
					}
				}
			}
			
			bakedMesh.vertices = vertices;
			if (hasUVs)
				bakedMesh.uv = uv;
			if (hasNormal)
				bakedMesh.normals = normals;
			if (hasColors)
				bakedMesh.colors = colors;
			bakedMesh.triangles = triangles;

			int[] textureSize = {32,64,128,256,512,1024,2048,4096,8192,16384};
			bakedMesh.RecalculateBounds();

			int columns = -1;
			int textureHeight=-1;
			int textureWidth=-1;

			int vertexCount = vertices.Length;
			int framesCount = Mathf.RoundToInt((EndTime-StartTime) * SampleRate + 0.5f);	
			int adjustedFramesCount = framesCount +2; // = space between columns

			Debug.Log("Frames count : "+framesCount);
			Debug.Log("Vertices count : "+vertexCount);

			bool exportVAT = true;

			columns = Mathf.CeilToInt(Mathf.Sqrt((float)vertexCount/(float)adjustedFramesCount));
			Debug.Log("Initial columns : "+columns);
			int textureHeightAdjusted =  Mathf.CeilToInt(((float)vertexCount/(float)columns));
			for(int i=0;i<textureSize.Length;i++)
			{
				if ((textureHeight==-1) && (textureHeightAdjusted<=textureSize[i]))
					textureHeight = textureSize[i];
			}
			Debug.Log("Wanted height : "+textureHeightAdjusted+" - next POW 2 : "+textureHeight);
			if (textureHeight==-1)
			{
				Debug.LogError("Alembic too big to be encoded in VAT format ... too high");
				exportVAT=false;
			}

			if (exportVAT)
			{
				columns = Mathf.CeilToInt(((float)vertexCount/(float)textureHeight));

				Debug.Log("Adjusted columns : "+columns);
				for(int i=0;i<textureSize.Length;i++)
				{
					if ((textureWidth==-1) && ((adjustedFramesCount*columns)<=textureSize[i]))
						textureWidth = textureSize[i];
				}
				Debug.Log("Wanted width : "+(adjustedFramesCount*columns)+" - next POW 2 : "+textureWidth);

				if (textureWidth==-1)
				{
					Debug.LogError("Alembic too big to be encoded in VAT format ... too wide");
					exportVAT=false;
				}
			}

			if (exportVAT)
			{
				Bounds newBounds = new Bounds();
				Vector3 minBounds = new Vector3(1e9f, 1e9f, 1e9f);
				Vector3 maxBounds = new Vector3(-1e9f, -1e9f, -1e9f);

				Vector2[] uv2 = new Vector2[verticesCount];

				Debug.Log("Texture size : "+textureWidth+" x "+textureHeight+" Vertices : "+vertexCount+" Frames : "+framesCount);

				Texture2D positionTexture= new Texture2D(textureWidth, textureHeight, TextureFormat.RGBAHalf, true, true);
				if (VariableTopology == TopologyType.Variable)
					positionTexture.filterMode = FilterMode.Point;
				positionTexture.wrapMode = TextureWrapMode.Clamp;
				Texture2D normalTexture= new Texture2D(textureWidth, textureHeight, TextureFormat.RGBAHalf, true, true);
				if (VariableTopology == TopologyType.Variable) 
					normalTexture.filterMode = FilterMode.Point;
				normalTexture.wrapMode = TextureWrapMode.Clamp;

				for(int frame = 0;frame<framesCount;frame++)
				{
					float timing = StartTime + ((float)frame)/SampleRate;
					Debug.Log("Encoding frame "+frame+" / "+framesCount+" ("+timing+")");
					timeProp.floatValue = timing; 
					alembicObject.ApplyModifiedProperties();
					yield return null;

					if (VariableTopology == TopologyType.Variable)
					{
						Debug.Log("Doing "+maxTriangleCount*3);
						MeshFilter localMeshFilter = MeshToBake.GetChild(0).GetComponent<MeshFilter>();
						if (localMeshFilter != null)
						{
							List<Vector3> local_vertices = new List<Vector3>(); 
							localMeshFilter.sharedMesh.GetVertices(local_vertices);
							List<Vector3> local_normals = new List<Vector3>(); 
							localMeshFilter.sharedMesh.GetNormals(local_normals);
							int[] local_index= localMeshFilter.sharedMesh.GetTriangles(0);

							for(int targetIndex=0 ; targetIndex<maxTriangleCount*3 ;targetIndex++)
							{
								Vector2Int coordinates = getCoord(frame, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
								Vector2Int coordinates_0 = getCoord(0, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
								Vector2 uvCoord = getUV(coordinates_0.x, coordinates_0.y, textureWidth, textureHeight);

								uv2[targetIndex] = uvCoord;

								Vector3 newVertexPos = Vector3.zero;
								Vector3 newVertexNrm  = Vector3.up;

								if (targetIndex<local_index.Length)
								{
									int vtxIndex = local_index[targetIndex];
									newVertexPos = local_vertices[vtxIndex];
									newVertexNrm = local_normals[vtxIndex];
								}

								minBounds = Vector3.Min(minBounds, newVertexPos);
								maxBounds = Vector3.Max(maxBounds, newVertexPos);

								positionTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexPos.x, newVertexPos.y, newVertexPos.z, 1.0f));	
								normalTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexNrm.x, newVertexNrm.y, newVertexNrm.z, 1.0f));	
							}

						}
					}
					else
					{
						Debug.Log("Doing animated solid meshes ");
						verticesOffset = 0;
						for(int i=0;i<MeshToBake.childCount;i++)
						{
							MeshFilter localMeshFilter = MeshToBake.GetChild(i).GetComponent<MeshFilter>();
							if (localMeshFilter != null)
							{
								List<Vector3> local_vertices = new List<Vector3>(); 
								localMeshFilter.sharedMesh.GetVertices(local_vertices);
								List<Vector3> local_normals = new List<Vector3>(); 
								localMeshFilter.sharedMesh.GetNormals(local_normals);
								int[] local_index= localMeshFilter.sharedMesh.GetTriangles(0);


								for(int j=0;j<local_vertices.Count;j++)
								{
									int targetIndex = j + verticesOffset;

									Vector2Int coordinates = getCoord(frame, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
									Vector2Int coordinates_0 = getCoord(0, targetIndex, textureWidth, textureHeight, adjustedFramesCount);
									Vector2 uvCoord = getUV(coordinates_0.x, coordinates_0.y, textureWidth, textureHeight);

									uv2[targetIndex] = uvCoord;

									Vector3 newVertexPos = local_vertices[j];
									Vector3 refVertexPos = vertices[targetIndex];
									newVertexPos -= refVertexPos;
									Vector3 newVertexNrm = local_normals[j];

									minBounds = Vector3.Min(minBounds, newVertexPos);
									maxBounds = Vector3.Max(maxBounds, newVertexPos);

									positionTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexPos.x, newVertexPos.y, newVertexPos.z, 1.0f));	
									normalTexture.SetPixel(coordinates.x, coordinates.y, new Color(newVertexNrm.x, newVertexNrm.y, newVertexNrm.z, 1.0f));	
								}
								verticesOffset += local_vertices.Count;
							}
						}
					}
				}

				newBounds.max = maxBounds;
				newBounds.min = minBounds;
				Debug.Log("Min bounds : "+minBounds.x+" , "+minBounds.y+" , "+minBounds.z);
				Debug.Log("Max bounds : "+maxBounds.x+" , "+maxBounds.y+" , "+maxBounds.z);

				bakedMesh.bounds = newBounds;

				positionTexture.Apply();
				normalTexture.Apply();

				Debug.Log("Saving positions texture asset at "+finalExportPath+ExportFilename+"_position.asset");
				AssetDatabase.CreateAsset(positionTexture,finalExportPath+ExportFilename+"_position.asset" );
				AssetDatabase.SaveAssets();
				Debug.Log("Saving normals texture asset at "+finalExportPath+ExportFilename+"_normal.asset");
				AssetDatabase.CreateAsset(normalTexture,finalExportPath+ExportFilename+"_normal.asset" );
				AssetDatabase.SaveAssets();

				bakedMesh.uv2 = uv2;
			}

			Debug.Log("Saving merged mesh asset at "+finalExportPath+ExportFilename+"_mesh.asset");
			AssetDatabase.CreateAsset(bakedMesh,finalExportPath+ExportFilename+"_mesh.asset" );
			AssetDatabase.SaveAssets();
			yield return null;			

			Debug.Log("Create prefab");

			Debug.Log("Saving material asset");
			Material newMaterial = new Material(ReferenceMaterial.shader);
			newMaterial.name = ExportFilename+"_material";
			newMaterial.SetFloat("_VATTime", (float)framesCount);
			newMaterial.SetFloat("_IfVAT", 1);
			newMaterial.SetColor("_MainColor", new Color(0,0,0,1));
			newMaterial.SetFloat("_fnl_sacle", 0.5f);

			Texture2D VATPositionTex1 = Resources.Load<Texture2D >(ExportFilename+"_position");
			Texture2D VATNormalTex1 = Resources.Load<Texture2D >(ExportFilename+"_normal");
		//	if (VATPositionTex1 == null)
		//		Debug.Log("Can't load position texture "+finalExportPath+ExportFilename+"_position.asset");
		//	if (VATNormalTex == null)
		//		Debug.Log("Can't load position texture "+finalExportPath+ExportFilename+"_normal.asset");
			
			newMaterial.SetTexture("_VATPositionTex", VATPositionTex1);
			newMaterial.SetTexture("_VATNormalTex", VATNormalTex1);

			AssetDatabase.CreateAsset(newMaterial,finalExportPath+ExportFilename+"_material.mat" );
			AssetDatabase.SaveAssets();

			GameObject newGameObject = new GameObject(ExportFilename+"_Object");

			Mesh resMesh = Resources.Load<Mesh>(ExportFilename+"_mesh");
			if (resMesh == null)
				Debug.Log("Unable to reload created mesh");

			Material resMaterial = Resources.Load<Material>(ExportFilename+"_material");
			if (resMaterial == null)
				Debug.Log("Unable to reload material");

			MeshFilter meshFilter = newGameObject.AddComponent<MeshFilter>();
			meshFilter.mesh = resMesh;
			MeshRenderer meshRenderer = newGameObject.AddComponent<MeshRenderer>();
			meshRenderer.material = resMaterial;

			PrefabUtility.SaveAsPrefabAsset(newGameObject, finalExportPath+ExportFilename+"_prefab.prefab");

			DestroyImmediate(newGameObject);
		}		
	}
}
#endif
