using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using IFix;
using IFix.Core;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        string text = Path.Combine(Application.streamingAssetsPath, "Assembly-CSharp.patch.bytes");
        bool flag = File.Exists(text);
        if (flag)
        {
            Debug.Log("Load HotFix, patchPath=" + text);
            PatchManager.Load(new FileStream(text, FileMode.Open), true);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnGUI()
    {
        if (GUI.Button(new Rect((Screen.width - 200) / 2, 20, 200, 100), "Call  FuncA"))
        {
            Debug.Log("Button, Call FuncA, result=" + FuncA());
        }
    }

    [Patch]
    public string FuncA()
    {
        return "New";
    }

    public void callFunc()
    {
        string obj = "instance";

        var method = typeof(object).GetMethod("ToString");
        var ftn = method.MethodHandle.GetFunctionPointer();
        var func = (Func<string>)Activator.CreateInstance(typeof(Func<string>), obj, ftn);

        var result = func();
        Debug.Log("result:" + result);
    }

    public void main(string[] args)
    {
        Console.WriteLine("Hello World!" + args);
    }
}
