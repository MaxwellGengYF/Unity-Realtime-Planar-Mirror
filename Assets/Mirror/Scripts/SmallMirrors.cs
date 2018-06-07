using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// Small mirrors component
/// </summary>
[RequireComponent(typeof(Renderer))]
public class SmallMirrors : MonoBehaviour {
	[System.NonSerialized]
	public Mirror manager;
	Renderer render;
	void Awake(){
		render = GetComponent<Renderer> ();
	}

	public Renderer GetRenderer(){
		if (!render) {
			render = GetComponent<Renderer> ();
		}
		return render;
	}

	void OnWillRenderObject(){
		manager.OnWillRenderObject ();
	}
}
