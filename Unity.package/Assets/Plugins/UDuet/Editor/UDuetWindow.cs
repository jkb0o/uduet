using UnityEngine;
using UnityEditor;
using System.Collections;

public class UDuetWindow : EditorWindow {

    [MenuItem("Window/UDuet")]
    static void Init(){
        EditorWindow.GetWindow(typeof(UDuetWindow));
    }

    void OnGUI(){
        if (UDuet.instance.isSlave){
            title = "UDuet: Slave";
        } else {
            title = "UDuet: Master";
        }

    }
}
