using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class MirrorEditorWindow : EditorWindow {
	public static MirrorEditorWindow current;
	public float[] layers = new float[32];
	public bool useLayer = true;
	public LayerMask mirrorMask;
	public GameObject rootObj;
	[MenuItem("Window/MirrorManager")]
	static void Init(){
		current = GetWindow<MirrorEditorWindow> ();

		current.Show ();
	}

	string[] allLayer = new string[32];

	void OnGUI(){
		EditorGUILayout.Space ();
		EditorGUILayout.LabelField ("Layer Set: ");
		EditorGUILayout.Space ();
		rootObj = EditorGUILayout.ObjectField ("Root Obj", rootObj, typeof(GameObject)) as GameObject;
		EditorGUILayout.Space ();
		useLayer = EditorGUILayout.Toggle ("Use Per Layer Culling:   ", useLayer);
		if (useLayer) {
			for (int i = 0; i < 32; i++) {
				string currentName = LayerMask.LayerToName (i);

				if (!string.IsNullOrEmpty (currentName)) {
					EditorGUILayout.BeginHorizontal ();
					EditorGUILayout.LabelField ("  " + currentName + ":");
					float currentValue = layers [i];
					layers [i] = EditorGUILayout.FloatField (currentValue);
					EditorGUILayout.EndHorizontal ();
					allLayer [i] = currentName;
				} else
					allLayer [i] = "Default";
			}
		}

		if (GUILayout.Button ("Update Culling distance", GUILayout.MinHeight(30))) {
			Iterate (rootObj.transform);
		}
		EditorGUILayout.Space ();
		mirrorMask = EditorGUILayout.MaskField ("LayerMask",mirrorMask, allLayer);
		if (GUILayout.Button ("Update Layer Culling", GUILayout.MinHeight(30))) {
			IterateMask (rootObj.transform);
		}
		Repaint ();
	}

	void Iterate(Transform target){
		Mirror m;

		if (m = target.GetComponent<Mirror> ()) {
			m.enableSelfCullingDistance = useLayer;
			for (int i = 0; i < 32; ++i) {
				m.layerCullingDistances[i] = layers[i];
			}
		}

		for(int i = 0, length = target.childCount; i < length; ++i){
			Iterate (target.GetChild (i));
		}
	}

	void IterateMask(Transform target){
		Mirror m;

		if (m = target.GetComponent<Mirror> ()) {
			m.m_ReflectLayers = mirrorMask;
		}
		for(int i = 0, length = target.childCount; i < length; ++i){
			IterateMask (target.GetChild (i));
		}
	}

}

[CustomEditor(typeof(Mirror))]
public class MirrorsManagerEditor : Editor{
	Mirror target;
	void OnEnable(){
		target = (Mirror)serializedObject.targetObject;
	}

	public override void OnInspectorGUI ()
	{
		target.m_ClipPlaneOffset = EditorGUILayout.FloatField ("Clip Plane Offset", Mathf.Max (0.01f, target.m_ClipPlaneOffset));
		target.textureSize = EditorGUILayout.IntField("Reflect Resolution", target.textureSize);
		base.OnInspectorGUI ();
		if (target.enableSelfCullingDistance) {
			for (int i = 0; i < 32; i++) {
				string currentName = LayerMask.LayerToName (i);

				if (!string.IsNullOrEmpty (currentName)) {
					float currentValue = target.layerCullingDistances [i];
					currentValue = EditorGUILayout.FloatField ("   " + currentName,currentValue);
					target.layerCullingDistances.SetValue (currentValue, i);
				}
			}
		}


		target.useDistanceCull = EditorGUILayout.Toggle ("Distance Limit", target.useDistanceCull);
		if (target.useDistanceCull) {
			target.maxDistance = EditorGUILayout.FloatField ("Max render distance", Mathf.Max (0.1f, target.maxDistance));
		}


		EditorGUILayout.Space ();

		Repaint ();
		if (GUI.changed) {
			EditorUtility.SetDirty (target);
		}
	}
}
